using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using AprNes;

namespace AprNesAvalonia.Views;

public partial class AudioPlusConfigWindow : Window
{
    // ── Constants ───────────────────────────────────────────────────────
    const int NES_CH = 5;
    const int EXP_CH = 8;

    static readonly string[] NesChNames = { "Pulse 1", "Pulse 2", "Triangle", "Noise", "DMC" };

    static readonly string[][] ExpChNames = {
        Array.Empty<string>(),                                               // 0: None
        new[] { "VRC6 Pulse 1", "VRC6 Pulse 2", "VRC6 Saw" },               // 1: VRC6
        new[] { "VRC7 FM" },                                                 // 2: VRC7
        new[] { "N163 Ch1","N163 Ch2","N163 Ch3","N163 Ch4",
                "N163 Ch5","N163 Ch6","N163 Ch7","N163 Ch8" },               // 3: N163
        new[] { "5B Ch A", "5B Ch B", "5B Ch C" },                           // 4: 5B
        new[] { "MMC5 Pulse 1", "MMC5 Pulse 2" },                           // 5: MMC5
        new[] { "FDS Wave" },                                                // 6: FDS
    };

    static readonly string[] ChipMapperDesc = {
        "",
        "Mapper 024 (VRC6a), 026 (VRC6b)",
        "Mapper 085 (VRC7)",
        "Mapper 019 (Namco 163)",
        "Mapper 069 (Sunsoft FME-7 / 5B)",
        "Mapper 005 (MMC5)",
        "FDS (Famicom Disk System)",
    };

    static readonly string[] ChipIniPrefix = { "", "VRC6", "VRC7", "N163", "S5B", "MMC5", "FDS" };

    // ── Dynamic expansion channel controls ─────────────────────────────
    private readonly CheckBox[] _chkExp  = new CheckBox[EXP_CH];
    private readonly TextBlock[] _lblExp = new TextBlock[EXP_CH];
    private readonly Slider[] _trkExp    = new Slider[EXP_CH];
    private readonly TextBlock[] _lblExpVal = new TextBlock[EXP_CH];

    // Per-chip volume/enable storage
    private readonly int[,]  _chipChVol = new int[7, 8];
    private readonly bool[,] _chipChEn  = new bool[7, 8];

    // ── NES channel control refs (for array-based access) ──────────────
    private CheckBox[] _chkNes = null!;
    private Slider[]   _slNes  = null!;
    private TextBlock[] _lblNesVal = null!;

    public AudioPlusConfigWindow()
    {
        InitializeComponent();
        InitChipDefaults();
        InitNesChannelArrays();
        BuildExpChannelUI();
        WireEvents();
        ApplyLanguage();
        LoadFromNesCore();
    }

    private string L(string key, string def) => LangHelper.Get(LangHelper.CurrentLang, key, def);

    private void InitNesChannelArrays()
    {
        _chkNes   = new[] { ChkPulse1, ChkPulse2, ChkTriangle, ChkNoise, ChkDMC };
        _slNes    = new[] { SlPulse1, SlPulse2, SlTriangle, SlNoise, SlDMC };
        _lblNesVal = new[] { LblPulse1, LblPulse2, LblTriangle, LblNoise, LblDMC };
    }

    private void InitChipDefaults()
    {
        for (int c = 0; c < 7; c++)
            for (int i = 0; i < 8; i++)
            { _chipChVol[c, i] = 70; _chipChEn[c, i] = true; }
    }

    // ── Build expansion channel rows dynamically ───────────────────────
    private void BuildExpChannelUI()
    {
        for (int i = 0; i < EXP_CH; i++)
        {
            var chk = new CheckBox { IsChecked = true, IsVisible = false };
            var lbl = new TextBlock { Text = "Ch " + (i + 1), VerticalAlignment = VerticalAlignment.Center, Width = 90, IsVisible = false };
            var trk = new Slider { Minimum = 0, Maximum = 100, Value = 70, Width = 200, IsVisible = false,
                                   TickFrequency = 1, IsSnapToTickEnabled = true };
            var val = new TextBlock { Text = "70%", VerticalAlignment = VerticalAlignment.Center, MinWidth = 40, IsVisible = false };

            int ci = i;
            trk.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") val.Text = (int)trk.Value + "%"; };

            _chkExp[i] = chk; _lblExp[i] = lbl; _trkExp[i] = trk; _lblExpVal[i] = val;

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(chk);
            row.Children.Add(lbl);
            row.Children.Add(trk);
            row.Children.Add(val);
            ExpChannelPanel.Children.Add(row);
        }
    }

    // ── Language ────────────────────────────────────────────────────────
    private void ApplyLanguage()
    {
        if (!LangHelper.Loaded) return;

        Title = L("ap_title", "Advanced Audio Settings");
        TabNes.Header       = L("ap_tab_nes", "NES Channels");
        TabExpansion.Header  = L("ap_tab_expansion", "Expansion Chips");
        TabPostProc.Header   = L("ap_tab_postproc", "Post-Processing");

        LblConsoleModel.Text = L("ap_console_model", "Console Model");
        ChkRfCrosstalk.Content = L("ap_rf_crosstalk", "RF Crosstalk");
        LblCustomCutoff.Text = L("ap_lpf_cutoff", "LPF Cutoff");
        ChkCustomBuzz.Content = L("ap_custom_buzz", "Custom Buzz");
        LblBuzzAmp.Text      = L("ap_buzz_amp", "Buzz Amp");
        LblBuzzFreq.Text     = L("ap_buzz_freq", "Buzz Freq");
        LblRfVol.Text        = L("ap_rf_volume", "RF Volume");

        LblPulse1Name.Text   = L("ap_pulse1", "Pulse 1");
        LblPulse2Name.Text   = L("ap_pulse2", "Pulse 2");
        LblTriangleName.Text = L("ap_triangle", "Triangle");
        LblNoiseName.Text    = L("ap_noise", "Noise");
        LblDMCName.Text      = L("ap_dmc", "DMC");

        LblChip.Text         = L("ap_chip", "Chip");
        LblSelectChip.Text   = L("ap_select_chip", "Select a chip to show its channels.");

        LblStereoWidth.Text   = L("ap_stereo_width", "Stereo Width");
        LblHaasDelay.Text     = L("ap_haas_delay", "Haas Delay");
        LblHaasCrossfeed.Text = L("ap_haas_crossfeed", "Haas Crossfeed");
        LblReverbWet.Text     = L("ap_reverb_wet", "Reverb Wet");
        LblCombFeedback.Text  = L("ap_reverb_length", "Comb Feedback");
        LblCombDamp.Text      = L("ap_reverb_damping", "Comb Damp");
        LblBassBoostDb.Text   = L("ap_bass_boost", "Bass Boost dB");
        LblBassBoostFreq.Text = L("ap_bass_freq", "Bass Boost Freq");

        BtnOK.Content     = L("ap_ok", "OK");
        BtnCancel.Content = L("ap_cancel", "Cancel");
    }

    // ── Load NesCore → UI ──────────────────────────────────────────────
    private void LoadFromNesCore()
    {
        // Authentic
        CmbConsoleModel.SelectedIndex = Math.Clamp(NesCore.ConsoleModel, 0, 6);
        ChkRfCrosstalk.IsChecked = NesCore.RfCrosstalk;
        SlCustomCutoff.Value = Math.Clamp(NesCore.CustomLpfCutoff, 1000, 22000);
        ChkCustomBuzz.IsChecked = NesCore.CustomBuzz;
        SlBuzzAmp.Value = Math.Clamp(NesCore.BuzzAmplitude, 0, 100);
        CmbBuzzFreq.SelectedIndex = (NesCore.BuzzFreq == 50) ? 1 : 0;
        SlRfVol.Value = Math.Clamp(NesCore.RfVolume, 0, 200);

        // Modern
        SlStereoWidth.Value  = Math.Clamp(NesCore.StereoWidth, 0, 100);
        SlHaasDelay.Value    = Math.Clamp(NesCore.HaasDelay, 10, 30);
        SlHaasCrossfeed.Value = Math.Clamp(NesCore.HaasCrossfeed, 0, 80);
        SlReverbWet.Value    = Math.Clamp(NesCore.ReverbWet, 0, 30);
        SlCombFeedback.Value = Math.Clamp(NesCore.CombFeedback, 30, 90);
        SlCombDamp.Value     = Math.Clamp(NesCore.CombDamp, 10, 70);
        SlBassBoostDb.Value  = Math.Clamp(NesCore.BassBoostDb, 0, 12);
        SlBassBoostFreq.Value = Math.Clamp(NesCore.BassBoostFreq, 80, 300);

        // NES channels
        for (int i = 0; i < NES_CH; i++)
        {
            _slNes[i].Value = Math.Clamp(NesCore.ChannelVolume[i], 0, 100);
            _chkNes[i].IsChecked = NesCore.ChannelEnabled[i];
            _lblNesVal[i].Text = (int)_slNes[i].Value + "%";
        }

        // Expansion channels — load per-chip stored settings
        LoadExpChipSettings();

        // Auto-select current game's mapper chip
        int ct = (int)NesCore.expansionChipType;
        CmbExpChip.SelectedIndex = (ct >= 0 && ct < 7) ? ct : 0;
        UpdateExpChannelVisibility();
        UpdateCustomEnableState();
        UpdateAllValueLabels();
    }

    // ── Load per-chip expansion data from AprNesAudioPlus.ini ──────────
    private void LoadExpChipSettings()
    {
        string iniPath = Path.Combine(AppContext.BaseDirectory, "configure", "AprNesAudioPlus.ini");
        if (!File.Exists(iniPath)) return;

        var cfg = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadAllLines(iniPath))
        {
            string t = line.TrimStart();
            if (t.StartsWith(';') || t.StartsWith('#')) continue;
            var kv = line.Split(new[] { '=' }, 2);
            if (kv.Length == 2) cfg[kv[0].Trim()] = kv[1].Trim();
        }

        for (int chip = 1; chip < ChipIniPrefix.Length; chip++)
        {
            for (int ch = 0; ch < 8; ch++)
            {
                string volKey = "ChVol_" + ChipIniPrefix[chip] + "_" + ch;
                string enKey  = "ChEn_"  + ChipIniPrefix[chip] + "_" + ch;
                if (cfg.TryGetValue(volKey, out var vs) && int.TryParse(vs, out int v))
                    _chipChVol[chip, ch] = Math.Clamp(v, 0, 100);
                if (cfg.TryGetValue(enKey, out var es))
                    _chipChEn[chip, ch] = es != "0";
            }
        }
    }

    // ── Save UI → NesCore ──────────────────────────────────────────────
    private void SaveToNesCore()
    {
        // Authentic
        NesCore.ConsoleModel    = CmbConsoleModel.SelectedIndex;
        NesCore.RfCrosstalk     = ChkRfCrosstalk.IsChecked == true;
        NesCore.CustomLpfCutoff = (int)SlCustomCutoff.Value;
        NesCore.CustomBuzz      = ChkCustomBuzz.IsChecked == true;
        NesCore.BuzzAmplitude   = (int)SlBuzzAmp.Value;
        NesCore.BuzzFreq        = (CmbBuzzFreq.SelectedIndex == 1) ? 50 : 60;
        NesCore.RfVolume        = (int)SlRfVol.Value;

        // Modern
        NesCore.StereoWidth   = (int)SlStereoWidth.Value;
        NesCore.HaasDelay     = (int)SlHaasDelay.Value;
        NesCore.HaasCrossfeed = (int)SlHaasCrossfeed.Value;
        NesCore.ReverbWet     = (int)SlReverbWet.Value;
        NesCore.CombFeedback  = (int)SlCombFeedback.Value;
        NesCore.CombDamp      = (int)SlCombDamp.Value;
        NesCore.BassBoostDb   = (int)SlBassBoostDb.Value;
        NesCore.BassBoostFreq = (int)SlBassBoostFreq.Value;

        // NES channels
        for (int i = 0; i < NES_CH; i++)
        {
            NesCore.ChannelVolume[i]  = (int)_slNes[i].Value;
            NesCore.ChannelEnabled[i] = _chkNes[i].IsChecked == true;
        }

        // Save current chip's expansion channel settings back
        int chipIdx = CmbExpChip.SelectedIndex;
        if (chipIdx > 0 && chipIdx < 7)
        {
            for (int i = 0; i < EXP_CH; i++)
            {
                _chipChVol[chipIdx, i] = (int)_trkExp[i].Value;
                _chipChEn[chipIdx, i]  = _chkExp[i].IsChecked == true;
            }
        }

        // Apply current chip to NesCore
        ApplyChipChannelSettings((int)NesCore.expansionChipType);
    }

    private void ApplyChipChannelSettings(int chipIdx)
    {
        if (chipIdx < 0 || chipIdx >= 7) chipIdx = 0;
        for (int i = 0; i < 8; i++)
        {
            NesCore.ChannelVolume[NES_CH + i]  = _chipChVol[chipIdx, i];
            NesCore.ChannelEnabled[NES_CH + i] = _chipChEn[chipIdx, i];
        }
    }

    // ── Save INI ───────────────────────────────────────────────────────
    private void SaveAudioPlusIni()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "configure", "AprNesAudioPlus.ini");

        string content =
            "; AprNesAudioPlus.ini — AudioPlus settings\r\n\r\n" +
            "; ── Authentic ──\r\n" +
            "ConsoleModel=" + NesCore.ConsoleModel + "\r\n" +
            "RfCrosstalk=" + (NesCore.RfCrosstalk ? "1" : "0") + "\r\n" +
            "CustomLpfCutoff=" + NesCore.CustomLpfCutoff + "\r\n" +
            "CustomBuzz=" + (NesCore.CustomBuzz ? "1" : "0") + "\r\n" +
            "BuzzAmplitude=" + NesCore.BuzzAmplitude + "\r\n" +
            "BuzzFreq=" + NesCore.BuzzFreq + "\r\n" +
            "RfVolume=" + NesCore.RfVolume + "\r\n" +
            "\r\n; ── Modern ──\r\n" +
            "StereoWidth=" + NesCore.StereoWidth + "\r\n" +
            "HaasDelay=" + NesCore.HaasDelay + "\r\n" +
            "HaasCrossfeed=" + NesCore.HaasCrossfeed + "\r\n" +
            "ReverbWet=" + NesCore.ReverbWet + "\r\n" +
            "CombFeedback=" + NesCore.CombFeedback + "\r\n" +
            "CombDamp=" + NesCore.CombDamp + "\r\n" +
            "BassBoostDb=" + NesCore.BassBoostDb + "\r\n" +
            "BassBoostFreq=" + NesCore.BassBoostFreq + "\r\n" +
            "\r\n; ── Channel Volume ──\r\n";

        // NES channels
        string[] nesChKeys = { "Pulse1", "Pulse2", "Triangle", "Noise", "DMC" };
        for (int i = 0; i < NES_CH; i++)
        {
            content += "ChVol_" + nesChKeys[i] + "=" + NesCore.ChannelVolume[i] + "\r\n";
            content += "ChEn_"  + nesChKeys[i] + "=" + (NesCore.ChannelEnabled[i] ? "1" : "0") + "\r\n";
        }

        // Per-chip expansion channels
        for (int chip = 1; chip < ChipIniPrefix.Length; chip++)
        {
            for (int ch = 0; ch < 8; ch++)
            {
                content += "ChVol_" + ChipIniPrefix[chip] + "_" + ch + "=" + _chipChVol[chip, ch] + "\r\n";
                content += "ChEn_"  + ChipIniPrefix[chip] + "_" + ch + "=" + (_chipChEn[chip, ch] ? "1" : "0") + "\r\n";
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        catch { }
    }

    // ── Wire events ────────────────────────────────────────────────────
    private void WireEvents()
    {
        CmbConsoleModel.SelectionChanged += (_, _) => UpdateCustomEnableState();
        CmbExpChip.SelectionChanged += (_, _) => OnExpChipChanged();

        // NES channel slider labels
        for (int i = 0; i < NES_CH; i++)
        {
            int ci = i;
            _slNes[ci].PropertyChanged += (_, e) => { if (e.Property.Name == "Value") _lblNesVal[ci].Text = (int)_slNes[ci].Value + "%"; };
        }

        // Post-processing slider labels
        SlCustomCutoff.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") LblCustomCutoffVal.Text = (int)SlCustomCutoff.Value + " Hz"; };
        SlBuzzAmp.PropertyChanged      += (_, e) => { if (e.Property.Name == "Value") LblBuzzAmpVal.Text = (int)SlBuzzAmp.Value + "%"; };
        SlRfVol.PropertyChanged        += (_, e) => { if (e.Property.Name == "Value") LblRfVolVal.Text = ((int)SlRfVol.Value).ToString(); };
        SlStereoWidth.PropertyChanged  += (_, e) => { if (e.Property.Name == "Value") LblStereoWidthVal.Text = (int)SlStereoWidth.Value + "%"; };
        SlHaasDelay.PropertyChanged    += (_, e) => { if (e.Property.Name == "Value") LblHaasDelayVal.Text = (int)SlHaasDelay.Value + " ms"; };
        SlHaasCrossfeed.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") LblHaasCrossfeedVal.Text = (int)SlHaasCrossfeed.Value + "%"; };
        SlReverbWet.PropertyChanged    += (_, e) => { if (e.Property.Name == "Value") LblReverbWetVal.Text = (int)SlReverbWet.Value + "%"; };
        SlCombFeedback.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") LblCombFeedbackVal.Text = (int)SlCombFeedback.Value + "%"; };
        SlCombDamp.PropertyChanged     += (_, e) => { if (e.Property.Name == "Value") LblCombDampVal.Text = (int)SlCombDamp.Value + "%"; };
        SlBassBoostDb.PropertyChanged  += (_, e) => { if (e.Property.Name == "Value") LblBassBoostDbVal.Text = ((int)SlBassBoostDb.Value == 0) ? "Off" : "+" + (int)SlBassBoostDb.Value + " dB"; };
        SlBassBoostFreq.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") LblBassBoostFreqVal.Text = (int)SlBassBoostFreq.Value + " Hz"; };
    }

    // ── Expansion chip changed ─────────────────────────────────────────
    private void OnExpChipChanged()
    {
        // Save current chip's settings before switching
        int prevChip = -1;
        // We don't track prev easily, just update visibility
        UpdateExpChannelVisibility();
    }

    private void UpdateExpChannelVisibility()
    {
        int chipIdx = CmbExpChip.SelectedIndex;
        string[] names = (chipIdx > 0 && chipIdx < ExpChNames.Length) ? ExpChNames[chipIdx] : Array.Empty<string>();
        int visibleCount = names.Length;

        // Info label
        if (chipIdx > 0 && chipIdx < ChipMapperDesc.Length)
        {
            LblExpInfo.Text = ChipMapperDesc[chipIdx];
            LblExpInfo.IsVisible = true;
            LblSelectChip.IsVisible = false;
        }
        else
        {
            LblExpInfo.IsVisible = false;
            LblSelectChip.IsVisible = true;
        }

        // Load this chip's stored settings into UI
        for (int i = 0; i < EXP_CH; i++)
        {
            bool visible = (i < visibleCount);
            _chkExp[i].IsVisible = visible;
            _lblExp[i].IsVisible = visible;
            _trkExp[i].IsVisible = visible;
            _lblExpVal[i].IsVisible = visible;

            // The parent StackPanel row also needs visibility
            if (_chkExp[i].Parent is StackPanel sp)
                sp.IsVisible = visible;

            if (visible)
            {
                _lblExp[i].Text = names[i];
                _trkExp[i].Value = _chipChVol[chipIdx, i];
                _chkExp[i].IsChecked = _chipChEn[chipIdx, i];
                _lblExpVal[i].Text = _chipChVol[chipIdx, i] + "%";
            }
        }
    }

    // ── Custom console model enable state ──────────────────────────────
    private void UpdateCustomEnableState()
    {
        bool isCustom = (CmbConsoleModel.SelectedIndex == 6);
        LblCustomCutoff.IsEnabled = isCustom;
        SlCustomCutoff.IsEnabled = isCustom;
        LblCustomCutoffVal.IsEnabled = isCustom;
        ChkCustomBuzz.IsEnabled = isCustom;
    }

    // ── Update all value labels ────────────────────────────────────────
    private void UpdateAllValueLabels()
    {
        LblCustomCutoffVal.Text = (int)SlCustomCutoff.Value + " Hz";
        LblBuzzAmpVal.Text      = (int)SlBuzzAmp.Value + "%";
        LblRfVolVal.Text        = ((int)SlRfVol.Value).ToString();
        LblStereoWidthVal.Text  = (int)SlStereoWidth.Value + "%";
        LblHaasDelayVal.Text    = (int)SlHaasDelay.Value + " ms";
        LblHaasCrossfeedVal.Text = (int)SlHaasCrossfeed.Value + "%";
        LblReverbWetVal.Text    = (int)SlReverbWet.Value + "%";
        LblCombFeedbackVal.Text = (int)SlCombFeedback.Value + "%";
        LblCombDampVal.Text     = (int)SlCombDamp.Value + "%";
        LblBassBoostDbVal.Text  = ((int)SlBassBoostDb.Value == 0) ? "Off" : "+" + (int)SlBassBoostDb.Value + " dB";
        LblBassBoostFreqVal.Text = (int)SlBassBoostFreq.Value + " Hz";

        for (int i = 0; i < NES_CH; i++)
            _lblNesVal[i].Text = (int)_slNes[i].Value + "%";
    }

    // ── OK / Cancel ────────────────────────────────────────────────────
    private void BtnOK_Click(object? sender, RoutedEventArgs e)
    {
        SaveToNesCore();
        SaveAudioPlusIni();

        // Apply to audio pipeline
        NesCore.mmix_UpdateChannelGains();
        NesCore.AudioPlus_ApplySettings();

        Close();
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
