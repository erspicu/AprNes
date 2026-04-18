using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace AprNesAvalonia;

/// <summary>
/// File-based SkSL loader with in-memory cache. Shaders live in
/// {AppBaseDir}/Shaders/*.sksl and are copied by the csproj
/// (&lt;None CopyToOutputDirectory="PreserveNewest"&gt;).
///
/// Supports versioned filenames of the form:
///   crt_core_YYYYMMDDHHMMSS_author.sksl   (author optional)
/// LoadLatest(prefix) picks the newest matching file by timestamp.
/// </summary>
internal static class ShaderLoader
{
    static readonly Dictionary<string, SKRuntimeEffect> _cache = new();
    static readonly string _shaderDir = Path.Combine(AppContext.BaseDirectory, "Shaders");

    /// <summary>
    /// CLI override: when set, LoadLatest ignores filename discovery and loads
    /// this specific file. Set via --crt-shader &lt;filename&gt; in Program.cs.
    /// </summary>
    public static string? CliOverride { get; set; }

    public static string ShaderDir => _shaderDir;

    /// <summary>
    /// Load (and compile) a specific .sksl by filename. Throws on compile error
    /// or missing file. Cached by full path.
    /// </summary>
    public static SKRuntimeEffect Load(string fileName)
    {
        string path = Path.Combine(_shaderDir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Shader not found: {path}");
        return LoadFromPath(path);
    }

    /// <summary>
    /// Pick the newest versioned shader matching {prefix}YYYYMMDDHHMMSS[_author].sksl,
    /// compile and return it.
    ///
    /// Selection priority:
    ///   1. CliOverride (if set) — explicit filename wins
    ///   2. Among files matching {prefix}{14-digit-timestamp}[_author].sksl:
    ///      - Order by timestamp descending
    ///      - Tiebreaker: author name ascending
    ///      - Final tiebreaker: file mtime descending
    ///   3. Fallback to <paramref name="fallbackFile"/> if provided
    ///   4. Throw FileNotFoundException otherwise
    /// </summary>
    public static SKRuntimeEffect LoadLatest(string prefix, string? fallbackFile = null)
    {
        // (1) CLI override takes absolute precedence
        if (!string.IsNullOrEmpty(CliOverride))
        {
            Console.WriteLine($"[Shader] CLI override: {CliOverride}");
            return Load(CliOverride);
        }

        // (2) Discover versioned files
        string[] files = Array.Empty<string>();
        try { files = Directory.GetFiles(_shaderDir, prefix + "*.sksl"); }
        catch (DirectoryNotFoundException) { /* fallthrough to fallback */ }

        var rx = new Regex(
            $@"^{Regex.Escape(prefix)}(\d{{14}})(?:_([\w\-]+))?\.sksl$",
            RegexOptions.IgnoreCase);

        var candidates = new List<(string path, string ts, string author, DateTime mtime)>();
        foreach (var f in files)
        {
            var m = rx.Match(Path.GetFileName(f));
            if (!m.Success) continue;
            candidates.Add((
                f,
                m.Groups[1].Value,
                m.Groups[2].Success ? m.Groups[2].Value : "",
                File.GetLastWriteTime(f)
            ));
        }

        if (candidates.Count > 0)
        {
            var ordered = candidates
                .OrderByDescending(c => c.ts)
                .ThenBy(c => c.author, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(c => c.mtime)
                .ToList();

            var pick = ordered[0];
            int skipped = ordered.Count - 1;
            string name = Path.GetFileName(pick.path);
            Console.WriteLine(skipped > 0
                ? $"[Shader] latest = {name} (skipping {skipped} older)"
                : $"[Shader] latest = {name}");
            return LoadFromPath(pick.path);
        }

        // (3) Fallback
        if (fallbackFile != null)
        {
            Console.WriteLine($"[Shader] no versioned match for '{prefix}*.sksl'; using fallback {fallbackFile}");
            return Load(fallbackFile);
        }

        // (4) Nothing to load
        throw new FileNotFoundException(
            $"No shader matching {prefix}YYYYMMDDHHMMSS[_author].sksl in {_shaderDir}");
    }

    /// <summary>Clear the cache (e.g. for hot-reload during development).</summary>
    public static void Reset()
    {
        foreach (var e in _cache.Values) e.Dispose();
        _cache.Clear();
    }

    // ─── internal ─────────────────────────────────────────────────────────

    static SKRuntimeEffect LoadFromPath(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;

        string src = File.ReadAllText(path);
        var eff = SKRuntimeEffect.CreateShader(src, out string? errors);
        if (eff == null)
            throw new InvalidOperationException(
                $"Shader compile failed [{Path.GetFileName(path)}]:\n{errors}");

        _cache[path] = eff;
        return eff;
    }
}
