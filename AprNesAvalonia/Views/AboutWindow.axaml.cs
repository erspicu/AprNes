using System;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AprNesAvalonia.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionLabel.Text = $"Version : {GetBuildTimestamp()}";
        ApplyLanguage();
    }

    /// <summary>
    /// Read build timestamp from AssemblyInformationalVersion (embedded by MSBuild SourceRevisionId).
    /// Format: "1.0.0+build20260401-153045" → "2026/04/01 15:30:45"
    /// This is stored inside the PE metadata — immune to file copy/rename/touch.
    /// </summary>
    private static string GetBuildTimestamp()
    {
        var ver = typeof(AboutWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Extract "build20260401-153045" from "1.0.0+build20260401-153045"
        if (ver != null)
        {
            int idx = ver.IndexOf("+build", StringComparison.Ordinal);
            if (idx >= 0)
            {
                string stamp = ver[(idx + 6)..]; // "20260401-153045"
                if (stamp.Length >= 15)
                {
                    return $"{stamp[..4]}/{stamp[4..6]}/{stamp[6..8]} {stamp[9..11]}:{stamp[11..13]}:{stamp[13..15]}";
                }
            }
        }
        return ver ?? "unknown";
    }

    private void ApplyLanguage()
    {
        if (!LangHelper.Loaded) return;
        var L = (string key, string def) => LangHelper.Get(LangHelper.CurrentLang, key, def);
        DescLabel.Text    = L("about_desc", "April Nintendo Entertainment System Emulator");
        AuthorLabel.Text  = L("about_author", "Author : erspicu_brox");
        SiteLink.Text     = L("about_site", "Official Site");
        BtnOK.Content     = L("ok", "OK");
    }

    private void SiteLink_Click(object? sender, PointerPressedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://github.com/erspicu/AprNes") { UseShellExecute = true }); }
        catch { }
    }

    private void BtnOK_Click(object? sender, RoutedEventArgs e) => Close();
}
