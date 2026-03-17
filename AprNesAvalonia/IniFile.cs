using System;
using System.Collections.Generic;
using System.IO;

namespace AprNesAvalonia;

/// <summary>Simple INI file reader/writer (single section or key=value flat file).</summary>
public class IniFile
{
    private readonly string _path;
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    public IniFile(string path)
    {
        _path = path;
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        string currentSection = "";
        foreach (var raw in File.ReadAllLines(_path))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                if (!_sections.ContainsKey(currentSection))
                    _sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            else if (line.Contains('=') && !line.StartsWith(';') && !line.StartsWith('#'))
            {
                int eq = line.IndexOf('=');
                string key = line[..eq].Trim();
                string val = line[(eq + 1)..].Trim();
                if (!_sections.ContainsKey(currentSection))
                    _sections[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _sections[currentSection][key] = val;
            }
        }
    }

    /// <summary>Read from the root (no section header) or the first section.</summary>
    public string Get(string key, string defaultValue = "")
    {
        // try root ("") first, then any section
        if (_sections.TryGetValue("", out var root) && root.TryGetValue(key, out var v)) return v;
        foreach (var sec in _sections.Values)
            if (sec.TryGetValue(key, out v)) return v;
        return defaultValue;
    }

    public int GetInt(string key, int defaultValue = 0) =>
        int.TryParse(Get(key, defaultValue.ToString()), out var i) ? i : defaultValue;

    public bool GetBool(string key, bool defaultValue = false) =>
        Get(key, defaultValue ? "1" : "0") == "1";

    public void Set(string key, string value)
    {
        if (!_sections.ContainsKey("")) _sections[""] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _sections[""][key] = value;
    }

    public void Save()
    {
        using var sw = new StreamWriter(_path);
        foreach (var (section, kvp) in _sections)
        {
            if (!string.IsNullOrEmpty(section)) sw.WriteLine($"[{section}]");
            foreach (var (k, v) in kvp) sw.WriteLine($"{k}={v}");
            if (!string.IsNullOrEmpty(section)) sw.WriteLine();
        }
    }
}
