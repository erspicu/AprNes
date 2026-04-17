using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace AprNesAvalonia;

/// <summary>
/// File-based SkSL loader with in-memory cache.
/// Shaders live in {AppBaseDir}/Shaders/*.sksl and are copied by the csproj
/// (&lt;None CopyToOutputDirectory="PreserveNewest"&gt;).
/// </summary>
internal static class ShaderLoader
{
    static readonly Dictionary<string, SKRuntimeEffect> _cache = new();
    static readonly string _shaderDir = Path.Combine(AppContext.BaseDirectory, "Shaders");

    public static string ShaderDir => _shaderDir;

    /// <summary>
    /// Load (and compile) a .sksl by filename. Throws on compile error or missing file.
    /// </summary>
    public static SKRuntimeEffect Load(string fileName)
    {
        if (_cache.TryGetValue(fileName, out var cached)) return cached;

        string path = Path.Combine(_shaderDir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Shader not found: {path}");

        string src = File.ReadAllText(path);
        var eff = SKRuntimeEffect.CreateShader(src, out string? errors);
        if (eff == null)
            throw new InvalidOperationException(
                $"Shader compile failed [{fileName}]:\n{errors}");

        _cache[fileName] = eff;
        return eff;
    }

    /// <summary>Clear the cache (e.g. for hot-reload during development).</summary>
    public static void Reset()
    {
        foreach (var e in _cache.Values) e.Dispose();
        _cache.Clear();
    }
}
