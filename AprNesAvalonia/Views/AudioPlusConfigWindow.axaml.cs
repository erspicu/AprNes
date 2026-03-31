using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AprNesAvalonia.Views;

public partial class AudioPlusConfigWindow : Window
{
    private readonly IniFile _ini;

    public AudioPlusConfigWindow(IniFile ini)
    {
        InitializeComponent();
        _ini = ini;
        ApplyLanguage();
        // TODO: load audio+ settings from AprNesAudioPlus.ini
    }

    private string L(string key, string def) => LangHelper.Get(LangHelper.CurrentLang, key, def);

    private void ApplyLanguage()
    {
        if (!LangHelper.Loaded) return;

        Title = L("ap_title", "Advanced Audio Settings");

        // Tabs
        TabNes.Header       = L("ap_tab_nes", "NES Channels");
        TabExpansion.Header  = L("ap_tab_expansion", "Expansion Chips");
        TabPostProc.Header   = L("ap_tab_postproc", "Post-Processing");

        // NES Channels
        LblConsoleModel.Text = L("ap_console_model", "Console Model");
        LblPulse1Name.Text   = L("ap_pulse1", "Pulse 1");
        LblPulse2Name.Text   = L("ap_pulse2", "Pulse 2");
        LblTriangleName.Text = L("ap_triangle", "Triangle");
        LblNoiseName.Text    = L("ap_noise", "Noise");
        LblDMCName.Text      = L("ap_dmc", "DMC");

        // Expansion Chips
        LblChip.Text         = L("ap_chip", "Chip");
        LblSelectChip.Text   = L("ap_select_chip", "Select a chip to show its channels.");

        // Post-Processing
        LblStereoWidth.Text   = L("ap_stereo_width", "Stereo Width");
        LblHaasDelay.Text     = L("ap_haas_delay", "Haas Delay");
        LblHaasCrossfeed.Text = L("ap_haas_crossfeed", "Haas Crossfeed");
        LblReverbWet.Text     = L("ap_reverb_wet", "Reverb Wet");
        LblCombFeedback.Text  = L("ap_comb_feedback", "Comb Feedback");
        LblCombDamp.Text      = L("ap_comb_damp", "Comb Damp");
        LblBassBoostDb.Text   = L("ap_bass_boost_db", "Bass Boost dB");
        LblBassBoostFreq.Text = L("ap_bass_boost_freq", "Bass Boost Freq");

        BtnOK.Content = L("ok", "OK");
    }

    private void BtnOK_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: save audio+ settings to AprNesAudioPlus.ini
        Close();
    }
}
