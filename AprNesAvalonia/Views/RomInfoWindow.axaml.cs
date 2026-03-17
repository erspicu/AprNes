using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace AprNesAvalonia.Views;

public partial class RomInfoWindow : Window
{
    public RomInfoWindow()
    {
        InitializeComponent();
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
