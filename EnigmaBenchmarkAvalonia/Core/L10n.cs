namespace EnigmaBenchmark.Core;

using System.Globalization;

/// <summary>
/// Minimal runtime localisation. Picks one of three buckets based on
/// <see cref="CultureInfo.CurrentUICulture"/>:
///
///   zh-Hant / zh-TW / zh-HK / zh-MO  → Traditional Chinese (原文)
///   zh-Hans / zh-CN / zh-SG          → Simplified Chinese
///   everything else                   → English
///
/// Falls through to English on any probe failure. No .resx files, no
/// satellite assemblies — the localised strings are inline below because
/// there are only a few of them and they're UI copy that rarely changes.
/// </summary>
public static class L10n
{
    public enum Lang { English, TraditionalChinese, SimplifiedChinese }

    public static readonly Lang Current = DetectLanguage();

    private static Lang DetectLanguage()
    {
        try
        {
            var name = CultureInfo.CurrentUICulture.Name ?? "";
            // Avalonia sometimes reports "zh" without a region — look at the
            // resolved neutral parent first.
            string parent = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (!string.Equals(parent, "zh", StringComparison.OrdinalIgnoreCase))
                return Lang.English;

            // Check variants. IetfLanguageTag forms we care about:
            //   zh-Hant, zh-Hant-TW, zh-TW, zh-HK, zh-MO  → Traditional
            //   zh-Hans, zh-Hans-CN, zh-CN, zh-SG          → Simplified
            if (name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
             || name.EndsWith("TW", StringComparison.OrdinalIgnoreCase)
             || name.EndsWith("HK", StringComparison.OrdinalIgnoreCase)
             || name.EndsWith("MO", StringComparison.OrdinalIgnoreCase))
                return Lang.TraditionalChinese;

            // Default zh branch is Simplified (PRC-style zh, zh-Hans, zh-CN, zh-SG).
            return Lang.SimplifiedChinese;
        }
        catch
        {
            return Lang.English;
        }
    }

    // ── Tagline under the "EnigmaBenchmark" title ───────────────────────
    public static string Tagline => Current switch
    {
        Lang.TraditionalChinese => "二戰德國頂級密碼 × 你書桌上這顆 GPU",
        Lang.SimplifiedChinese  => "二战德国顶级密码 × 你桌上这颗 GPU",
        _                       => "WWII Germany's Top Ciphers × The GPU on Your Desk",
    };

    // ── Historical-comparison "NOW" column label, with dynamic year ─────
    public static string NowLabel(int year) => Current switch
    {
        Lang.TraditionalChinese => $"你這張 GPU（{year}）",
        Lang.SimplifiedChinese  => $"你这张 GPU（{year}）",
        _                       => $"Your GPU ({year})",
    };
}
