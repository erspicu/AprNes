using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AprNesAvalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Load language table once at startup
        string langPath = System.IO.Path.Combine(
            System.AppContext.BaseDirectory, "configure", "AprNesLang.ini");
        LangHelper.Init(langPath);

        // Auto-detect default language from system locale
        var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        if (culture.StartsWith("zh-TW", System.StringComparison.OrdinalIgnoreCase) ||
            culture.StartsWith("zh-Hant", System.StringComparison.OrdinalIgnoreCase))
            LangHelper.CurrentLang = "zh-tw";
        else if (culture.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase))
            LangHelper.CurrentLang = "zh-cn";
        else
            LangHelper.CurrentLang = "en-us";
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}