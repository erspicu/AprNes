using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

class Program
{
    static void Main(string[] args)
    {
        string etlFile = args.Length > 0 ? args[0] : @"C:\ai_project\AprNes\temp\aprnes_jit.etl";
        string processName = args.Length > 1 ? args[1] : "AprNes";
        string outputFile = args.Length > 2 ? args[2] : @"C:\ai_project\AprNes\temp\profile_report.txt";

        Console.WriteLine($"Parsing ETL: {etlFile}");
        Console.WriteLine($"Process: {processName}");

        var report = new System.Text.StringBuilder();
        report.AppendLine(new string('=', 80));
        report.AppendLine($"  AprNes Profiling Report — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"  ETL: {etlFile}");
        report.AppendLine($"  Build: Debug x64 (.NET Framework 4.8.1)");
        report.AppendLine(new string('=', 80));
        report.AppendLine();

        // ════════════════════════════════════════════════════════
        // Phase 1: JIT + Inlining via ETWTraceEventSource (raw)
        // ════════════════════════════════════════════════════════
        var jitMethods = new List<(string FullName, double ILSize)>();
        var inlineOk = new Dictionary<string, int>();
        var inlineFail = new Dictionary<string, (int Count, string Reason)>();

        using (var source = new ETWTraceEventSource(etlFile))
        {
            var clr = new ClrTraceEventParser(source);

            clr.MethodJittingStarted += ev =>
            {
                if (!ev.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)) return;
                jitMethods.Add(($"{ev.MethodNamespace}.{ev.MethodName}", ev.MethodILSize));
            };

            clr.MethodInliningSucceeded += ev =>
            {
                if (!ev.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)) return;
                string name = $"{ev.InlineeNamespace}.{ev.InlineeName}";
                inlineOk[name] = inlineOk.GetValueOrDefault(name) + 1;
            };

            clr.MethodInliningFailed += ev =>
            {
                if (!ev.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)) return;
                string name = $"{ev.InlineeNamespace}.{ev.InlineeName}";
                var cur = inlineFail.GetValueOrDefault(name, (0, ""));
                inlineFail[name] = (cur.Item1 + 1, ev.FailReason);
            };

            Console.WriteLine("Phase 1: Processing JIT/Inlining events...");
            source.Process();
        }

        // ════════════════════════════════════════════════════════
        // Phase 2: CPU Sampling via TraceLog (resolved stacks)
        // ════════════════════════════════════════════════════════
        Console.WriteLine("Phase 2: Converting ETL → ETLX for stack resolution...");
        var inclusiveSamples = new Dictionary<string, int>();
        var exclusiveSamples = new Dictionary<string, int>();
        int totalSamples = 0;
        double cpuMSec = 0;
        int pid = 0;

        try
        {
            string etlxFile = TraceLog.CreateFromEventTraceLogFile(etlFile);
            using var traceLog = TraceLog.OpenOrConvert(etlxFile);

            var proc = traceLog.Processes.FirstOrDefault(p =>
                p.Name.Equals(processName, StringComparison.OrdinalIgnoreCase));

            if (proc != null)
            {
                pid = proc.ProcessID;
                cpuMSec = proc.CPUMSec;
                Console.WriteLine($"  Found process PID={pid}, CPU={cpuMSec:F0}ms");

                // Walk all CPU sampling events for this process
                Console.WriteLine("  Resolving CPU stacks...");
                foreach (var ev in traceLog.Events)
                {
                    if (ev.ProcessID != pid) continue;
                    if (ev.EventName != "PerfInfoSample" &&
                        ev.EventName != "PerfInfo/Sample" &&
                        !ev.EventName.Contains("SampledProfile") &&
                        !ev.EventName.Contains("PMCCounterProf"))
                        continue;

                    totalSamples++;
                    var cs = ev.CallStack();
                    if (cs == null) continue;

                    // Exclusive: top of stack only
                    string topName = cs.CodeAddress?.FullMethodName;
                    if (!string.IsNullOrEmpty(topName))
                        exclusiveSamples[topName] = exclusiveSamples.GetValueOrDefault(topName) + 1;

                    // Inclusive: all frames
                    var frame = cs;
                    var seen = new HashSet<string>();
                    while (frame != null)
                    {
                        string fname = frame.CodeAddress?.FullMethodName;
                        if (!string.IsNullOrEmpty(fname) && seen.Add(fname))
                            inclusiveSamples[fname] = inclusiveSamples.GetValueOrDefault(fname) + 1;
                        frame = frame.Caller;
                    }
                }
                Console.WriteLine($"  Processed {totalSamples} CPU samples");
            }

            try { File.Delete(etlxFile); } catch { }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  CPU stack resolution failed: {ex.Message}");
        }

        // ════════════════════════════════════════════════════════
        // Build Report
        // ════════════════════════════════════════════════════════

        // ── CPU Sampling ──
        if (totalSamples > 0)
        {
            report.AppendLine($"== CPU SAMPLING (PID={pid}, CPU={cpuMSec:F0}ms, {totalSamples} samples) ==");
            report.AppendLine();

            // Exclusive (self time)
            report.AppendLine("-- TOP 40 METHODS BY EXCLUSIVE CPU (self time) --");
            report.AppendLine($"{"Samples",-9} {"Excl%",-7} {"Method"}");
            report.AppendLine(new string('-', 90));
            foreach (var kv in exclusiveSamples.OrderByDescending(kv => kv.Value).Take(40))
            {
                double pct = 100.0 * kv.Value / totalSamples;
                report.AppendLine($"{kv.Value,-9} {pct,-7:F1} {kv.Key}");
            }

            report.AppendLine();

            // Inclusive (total time including callees)
            report.AppendLine("-- TOP 30 METHODS BY INCLUSIVE CPU (self + callees) --");
            report.AppendLine($"{"Samples",-9} {"Incl%",-7} {"Method"}");
            report.AppendLine(new string('-', 90));
            foreach (var kv in inclusiveSamples.OrderByDescending(kv => kv.Value).Take(30))
            {
                double pct = 100.0 * kv.Value / totalSamples;
                report.AppendLine($"{kv.Value,-9} {pct,-7:F1} {kv.Key}");
            }

            // NesCore-only exclusive
            report.AppendLine();
            report.AppendLine("-- NESCORE-ONLY EXCLUSIVE CPU --");
            report.AppendLine($"{"Samples",-9} {"Excl%",-7} {"Method"}");
            report.AppendLine(new string('-', 90));
            int nesTotal = 0;
            foreach (var kv in exclusiveSamples
                .Where(kv => kv.Key.Contains("NesCore") || kv.Key.Contains("AprNes"))
                .OrderByDescending(kv => kv.Value))
            {
                double pct = 100.0 * kv.Value / totalSamples;
                report.AppendLine($"{kv.Value,-9} {pct,-7:F1} {kv.Key}");
                nesTotal += kv.Value;
            }
            report.AppendLine($"{"---",-9} {"---",-7}");
            report.AppendLine($"{nesTotal,-9} {100.0 * nesTotal / totalSamples,-7:F1} TOTAL NesCore");
        }
        else
        {
            report.AppendLine("== CPU SAMPLING ==");
            report.AppendLine("No CPU samples resolved. Open ETL in PerfView GUI for analysis:");
            report.AppendLine($"  C:\\ai_project\\AprNes\\temp\\PerfView.exe \"{etlFile}\"");
        }

        report.AppendLine();

        // ── JIT Report ──
        report.AppendLine($"== JIT COMPILATION ({jitMethods.Count} methods total) ==");
        report.AppendLine();

        var nesJit = jitMethods.Where(m => m.FullName.Contains("NesCore") || m.FullName.Contains("AprNes"))
                               .OrderByDescending(m => m.ILSize).ToList();
        report.AppendLine($"-- NesCore/AprNes JIT'd Methods ({nesJit.Count}) sorted by IL size --");
        report.AppendLine($"{"IL bytes",10}  {"Method"}");
        report.AppendLine(new string('-', 82));
        foreach (var m in nesJit)
            report.AppendLine($"{m.ILSize,10:F0}  {m.FullName}");

        // ── Inlining ──
        report.AppendLine();
        int totalInlOk = inlineOk.Values.Sum();
        int totalInlFail = inlineFail.Values.Sum(v => v.Count);
        report.AppendLine($"== INLINING (Success: {totalInlOk} | Failed: {totalInlFail}) ==");
        report.AppendLine();

        var nesInlineFail = inlineFail.Where(kv => kv.Key.Contains("NesCore") || kv.Key.Contains("AprNes"))
                                       .OrderByDescending(kv => kv.Value.Count).ToList();
        if (nesInlineFail.Any())
        {
            report.AppendLine("-- NesCore INLINING FAILURES --");
            report.AppendLine($"{"Count",-6} {"Method",-55} {"Reason"}");
            report.AppendLine(new string('-', 110));
            foreach (var f in nesInlineFail)
                report.AppendLine($"{f.Value.Count,-6} {f.Key,-55} {f.Value.Reason}");
            report.AppendLine();
        }

        var nesInlineOk = inlineOk.Where(kv => kv.Key.Contains("NesCore") || kv.Key.Contains("AprNes"))
                                   .OrderByDescending(kv => kv.Value).ToList();
        if (nesInlineOk.Any())
        {
            report.AppendLine("-- NesCore INLINING SUCCESSES (sorted by frequency) --");
            report.AppendLine($"{"Count",-6} {"Method"}");
            report.AppendLine(new string('-', 70));
            foreach (var s in nesInlineOk)
                report.AppendLine($"{s.Value,-6} {s.Key}");
        }

        // ── Cross-reference: hot methods inline status ──
        if (totalSamples > 0 && nesInlineOk.Any())
        {
            report.AppendLine();
            report.AppendLine("== HOT PATH INLINE STATUS ==");
            report.AppendLine("(Cross-reference: top exclusive CPU methods vs inline status)");
            report.AppendLine($"{"Excl%",-7} {"Inlined?",-10} {"Method"}");
            report.AppendLine(new string('-', 80));

            var inlinedSet = new HashSet<string>(inlineOk.Keys);
            var failedSet = new HashSet<string>(inlineFail.Keys);

            foreach (var kv in exclusiveSamples
                .Where(kv => kv.Key.Contains("NesCore") || kv.Key.Contains("AprNes"))
                .OrderByDescending(kv => kv.Value).Take(20))
            {
                double pct = 100.0 * kv.Value / totalSamples;
                // Check short name match
                string shortName = kv.Key.Split(new[] { '!' }, 2).Last();
                string status = inlinedSet.Any(k => shortName.Contains(k.Split('.').Last())) ? "YES"
                    : failedSet.Any(k => shortName.Contains(k.Split('.').Last())) ? "FAILED"
                    : "NO (standalone)";
                report.AppendLine($"{pct,-7:F1} {status,-10} {kv.Key}");
            }
        }

        string text = report.ToString();
        File.WriteAllText(outputFile, text, System.Text.Encoding.UTF8);
        Console.WriteLine();
        Console.WriteLine(text);
        Console.WriteLine($"\nReport saved to: {outputFile}");
    }
}
