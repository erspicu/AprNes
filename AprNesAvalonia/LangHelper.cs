using System;
using System.Collections.Generic;
using System.IO;

namespace AprNesAvalonia;

/// <summary>Loads AprNesLang.ini and provides text lookup by language + key.</summary>
public static class LangHelper
{
    // lang → key → text
    private static readonly Dictionary<string, Dictionary<string, string>> _table = new(StringComparer.OrdinalIgnoreCase);

    public static bool Loaded { get; private set; }
    public static string CurrentLang { get; set; } = "zh-tw";

    public static void Init(string iniPath)
    {
        _table.Clear();
        if (!File.Exists(iniPath)) return;

        string currentSection = "";
        foreach (var raw in File.ReadAllLines(iniPath))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                if (!_table.ContainsKey(currentSection))
                    _table[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            else if (line.Contains('=') && !line.StartsWith(';'))
            {
                int eq = line.IndexOf('=');
                string key = line[..eq].Trim();
                string val = line[(eq + 1)..].Trim();
                if (!_table.ContainsKey(currentSection))
                    _table[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _table[currentSection][key] = val;
            }
        }
        Loaded = _table.Count > 0;
    }

    public static string Get(string lang, string key, string defaultValue = "")
    {
        if (_table.TryGetValue(lang, out var section) && section.TryGetValue(key, out var val))
            return val;
        // fallback to zh-tw
        if (_table.TryGetValue("zh-tw", out section) && section.TryGetValue(key, out val))
            return val;
        return defaultValue;
    }
}
