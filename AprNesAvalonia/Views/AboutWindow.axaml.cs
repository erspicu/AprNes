using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AprNesAvalonia.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionLabel.Text = $"Version : {DateTime.Now:yyyy年M月d日 tt hh:mm:ss}";
        ApplyLanguage();
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
