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
    }

    private void SiteLink_Click(object? sender, PointerPressedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://github.com/erspicu/AprNes") { UseShellExecute = true }); }
        catch { }
    }

    private void BtnOK_Click(object? sender, RoutedEventArgs e) => Close();
}
