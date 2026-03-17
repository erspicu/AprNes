using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AprNesAvalonia.Views;

public partial class ConfigWindow : Window
{
    public ConfigWindow()
    {
        InitializeComponent();
    }

    private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "選擇截圖存放目錄" };
        var result = await dialog.ShowAsync(this);
        if (result != null)
            TxtScreenshotPath.Text = result;
    }

    private void BtnOK_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
