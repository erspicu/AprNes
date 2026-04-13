using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

class Program
{
    static int Main(string[] args)
    {
        var etlFile  = args.Length >= 1 ? args[0] : @"C:\ai_project\AprNes\temp\aprnes_pmu.etl";
        var procName = args.Length >= 2 ? args[1] : "AprNes";
        var outFile  = args.Length >= 3 ? args[2] : @"C:\ai_project\AprNes\temp\pmu_report.txt";

        Console.WriteLine($"Analyzing {etlFile} for process {procName}");
        var etlxFile = Path.ChangeExtension(etlFile, ".etlx");
        if (File.Exists(etlxFile)) File.Delete(etlxFile);

        // Counter ID → Name (must match what PerfView /CpuCounters set)
        var counterNames = new Dictionary<int, string>
        {
            { 0, "Timer" },
            { 9, "IcacheMisses" },
            { 19, "TotalCycles" },
            { 20, "IcacheIssues" },
        };

        // Per-counter method histograms (method → sample count)
        var exclByCounter = new Dictionary<int, Dictionary<string, long>>();
        var totalByCounter = new Dictionary<int, long>();
        foreach (var id in counterNames.Keys)
        {
            exclByCounter[id] = new Dictionary<string, long>();
            totalByCounter[id] = 0;
        }

        var opts = new TraceLogOptions { ContinueOnError = true };
        // Let OpenOrConvert handle both creation and loading (same path EtlAnalyzer uses)
        using var traceLog = TraceLog.OpenOrConvert(etlFile, opts);

        var proc = traceLog.Processes.FirstOrDefault(p => p.Name.Equals(procName, StringComparison.OrdinalIgnoreCase));
        if (proc == null) { Console.WriteLine($"Process {procName} not found"); return 2; }
        var pid = proc.ProcessID;
        Console.WriteLine($"PID={pid}, CPU={proc.CPUMSec:F0}ms");

        long total = 0, withStack = 0, withSource = 0;
        var eventNames = new Dictionary<string, int>();

        foreach (var ev in traceLog.Events)
        {
            if (ev.ProcessID != pid) continue;

            // We look at both PerfInfo/SampleProfile (timer) and PerfInfo/PMCSample (counter)
            string en = ev.EventName ?? "";
            if (!en.Contains("Sample") && !en.Contains("PMC")) continue;
            eventNames[en] = eventNames.GetValueOrDefault(en) + 1;

            int sourceId = -1;
            // Try cast to PMCCounterProfTraceData (PMU sample)
            if (ev is PMCCounterProfTraceData pmc)
            {
                sourceId = pmc.ProfileSource;
                withSource++;
            }
            else if (ev is SampledProfileTraceData timer)
            {
                sourceId = 0; // Timer
                withSource++;
            }
            else continue;

            if (!counterNames.ContainsKey(sourceId)) continue;

            total++;
            totalByCounter[sourceId]++;
            var cs = ev.CallStack();
            if (cs == null) continue;
            withStack++;

            // Symbol name with fallback to module+address
            var ca = cs.CodeAddress;
            string topName = ca?.FullMethodName ?? "";
            if (string.IsNullOrEmpty(topName) && ca != null)
            {
                string mod = ca.ModuleFile?.Name ?? "?";
                topName = $"[{mod}!0x{ca.Address:X}]";
            }
            if (!string.IsNullOrEmpty(topName))
                exclByCounter[sourceId][topName] = exclByCounter[sourceId].GetValueOrDefault(topName) + 1;
        }

        Console.WriteLine($"total samples={total}, with stack={withStack}");
        Console.WriteLine($"Event types seen:");
        foreach (var kv in eventNames.OrderByDescending(kv => kv.Value).Take(10))
            Console.WriteLine($"  {kv.Value,10}  {kv.Key}");

        var report = new StringBuilder();
        report.AppendLine("=".PadRight(80, '='));
        report.AppendLine("  AprNes PMU Profiling Report");
        report.AppendLine($"  ETL: {etlFile}");
        report.AppendLine($"  PID: {pid}, CPU: {proc.CPUMSec:F0} ms");
        report.AppendLine("=".PadRight(80, '='));
        report.AppendLine();

        foreach (var kv in counterNames)
        {
            int id = kv.Key; string name = kv.Value;
            long tot = totalByCounter[id];
            report.AppendLine($"== {name} (ID {id}): {tot} samples ==");
            if (tot == 0) { report.AppendLine("  (no samples)"); report.AppendLine(); continue; }
            report.AppendLine($"{"Samples",-9} {"Pct%",-7} Method");
            report.AppendLine(new string('-', 90));
            foreach (var mk in exclByCounter[id].OrderByDescending(x => x.Value).Take(30))
            {
                double pct = 100.0 * mk.Value / tot;
                report.AppendLine($"{mk.Value,-9} {pct,-7:F1} {mk.Key}");
            }
            report.AppendLine();
        }

        // Cross-derive: IcacheMisses% (miss rate per method) if both IcacheMisses and IcacheIssues captured
        if (totalByCounter.TryGetValue(9, out long _misses) && totalByCounter.TryGetValue(20, out long _issues)
            && _misses > 0 && _issues > 0)
        {
            report.AppendLine("== TOP METHODS BY L1 I-CACHE MISS RATE ==");
            report.AppendLine("  (miss rate = IcacheMisses samples / IcacheIssues samples per method)");
            report.AppendLine($"{"Miss%",-8} {"Miss",-8} {"Fetch",-8} Method");
            report.AppendLine(new string('-', 90));
            var methods = new HashSet<string>(exclByCounter[9].Keys);
            methods.UnionWith(exclByCounter[20].Keys);
            var ratios = methods
                .Select(m =>
                {
                    long m9 = exclByCounter[9].GetValueOrDefault(m);
                    long m20 = exclByCounter[20].GetValueOrDefault(m);
                    double rate = m20 > 0 ? 100.0 * m9 / m20 : 0.0;
                    return (method: m, miss: m9, fetch: m20, rate);
                })
                .Where(x => x.fetch >= 50) // filter noise
                .OrderByDescending(x => x.rate)
                .Take(25);
            foreach (var (method, miss, fetch, rate) in ratios)
                report.AppendLine($"{rate,-8:F2} {miss,-8} {fetch,-8} {method}");
            report.AppendLine();

            double globalMissRate = 100.0 * _misses / _issues;
            report.AppendLine($"Global miss rate: {_misses} / {_issues} = {globalMissRate:F2}%");
        }

        File.WriteAllText(outFile, report.ToString());
        Console.WriteLine($"Report saved to: {outFile}");
        try { File.Delete(etlxFile); } catch { }
        return 0;
    }
}
