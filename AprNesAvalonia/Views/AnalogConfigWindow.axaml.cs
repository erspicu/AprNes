using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AprNesAvalonia.Views;

public partial class AnalogConfigWindow : Window
{
    private readonly IniFile _ini;

    public AnalogConfigWindow(IniFile ini)
    {
        InitializeComponent();
        _ini = ini;
        ApplyLanguage();
        // TODO: load analog settings from AprNesAnalog.ini
    }

    private string L(string key, string def) => LangHelper.Get(LangHelper.CurrentLang, key, def);

    private void ApplyLanguage()
    {
        if (!LangHelper.Loaded) return;

        Title = L("analog_setting", "Analog Video Settings");
        TabNtsc.Header = L("analog_tab_ntsc", "NTSC");
        TabCrt.Header  = L("analog_tab_crt", "CRT");

        // NTSC tab
        ChkHBI.Content             = L("analog_hbi", "HBI Simulation");
        ChkColorBurstJitter.Content = L("analog_colorburst", "Color Burst Jitter");
        ChkSymmetricIQ.Content     = L("analog_symmetric_iq", "Symmetric I/Q");
        ChkRinging.Content         = L("analog_ringing", "Ringing");
        LblGammaLabel.Text         = L("analog_gamma", "Gamma");
        LblColorTemp.Text          = L("analog_colortemp", "Color Temp");

        // CRT tab
        ChkInterlaceJitter.Content  = L("analog_interlace", "Interlace Jitter");
        ChkVignette.Content         = L("analog_vignette", "Vignette");
        ChkShadowMask.Content       = L("analog_shadowmask", "Shadow Mask");
        ChkCurvature.Content        = L("analog_curvature", "Curvature");
        ChkPhosphor.Content         = L("analog_phosphor", "Phosphor Persistence");
        ChkHorizontalBeam.Content   = L("analog_hbeam", "H-Beam");
        ChkConvergence.Content      = L("analog_convergence", "Convergence");

        BtnOK.Content = L("ok", "OK");
    }

    private void BtnOK_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: save analog settings to AprNesAnalog.ini
        Close();
    }
}
