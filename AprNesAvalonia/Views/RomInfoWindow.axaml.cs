using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace AprNesAvalonia.Views;

public partial class RomInfoWindow : Window
{
    public RomInfoWindow()
    {
        InitializeComponent();
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        if (!LangHelper.Loaded) return;
        var L = (string key, string def) => LangHelper.Get(LangHelper.CurrentLang, key, def);
        Title = L("rominfo_title", "ROM File information");
        TxtInfo.Text = L("rominfo_no_rom", "No load Rom !");
        BtnCopy.Content = L("copy", "Copy");
        BtnOK.Content = L("ok", "OK");
    }

    public void SetInfo(string info) => TxtInfo.Text = info;

    private async void BtnCopy_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(TxtInfo.Text ?? string.Empty);
    }

    private void BtnOK_Click(object? sender, RoutedEventArgs e) => Close();
}
