using System;
using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AprNes;

namespace AprNesAvalonia.Views;

public partial class AnalogConfigWindow : Window
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public AnalogConfigWindow()
    {
        InitializeComponent();
        LoadFromFields();
        WireEvents();
        ApplyLanguage();
    }

    private string L(string key, string def) => LangHelper.Get(LangHelper.CurrentLang, key, def);

    // ── Language ────────────────────────────────────────────────────────
    private void ApplyLanguage()
    {
        if (!LangHelper.Loaded) return;

        Title = L("analog_setting", "Analog Video Settings");
        LblPreset.Text = L("analog_feature_profile", "Feature Profile");
        CmbPresetChoose.Content = L("analog_choose_feature", "-- Choose --");
        CmbPresetNtsc.Content   = L("analog_ntsc_only", "NTSC Only");
        CmbPresetCrt.Content    = L("analog_crt_only", "CRT Only");
        CmbPresetBoth.Content   = L("analog_ntsc_crt", "NTSC + CRT");

        TabNtsc.Header = L("analog_tab_ntsc", "NTSC");
        TabCrt.Header  = L("analog_tab_crt", "CRT");
        TabConnector.Header = L("analog_grp_connector", "Connector");

        // NTSC
        ChkHBI.Content             = L("analog_hbi", "HBI Simulation");
        ChkColorBurstJitter.Content = L("analog_colorburst", "Color Burst Jitter");
        ChkSymmetricIQ.Content     = L("analog_symmetric_iq", "Symmetric I/Q");
        ChkRinging.Content         = L("analog_ringing", "Ringing");
        LblGammaLabel.Text         = L("analog_gamma", "Gamma");
        LblColorTemp.Text          = L("analog_colortemp", "Color Temp");

        // CRT
        ChkInterlaceJitter.Content  = L("analog_interlace", "Interlace Jitter");
        ChkVignette.Content         = L("analog_vignette", "Vignette");
        ChkShadowMask.Content       = L("analog_shadowmask", "Shadow Mask");
        LblMaskStrength.Text        = L("analog_strength", "Strength");
        ChkCurvature.Content        = L("analog_curvature", "Curvature");
        ChkPhosphor.Content         = L("analog_phosphor", "Phosphor Persistence");
        ChkHorizontalBeam.Content   = L("analog_hbeam", "H-Beam");
        ChkConvergence.Content      = L("analog_convergence", "Convergence");

        BtnOK.Content = L("ok", "OK");
    }

    // ── Load NesCore → UI ──────────────────────────────────────────────
    private void LoadFromFields()
    {
        // NTSC checkboxes
        ChkHBI.IsChecked             = NesCore.HbiSimulation;
        ChkColorBurstJitter.IsChecked = NesCore.ColorBurstJitter;
        ChkSymmetricIQ.IsChecked     = NesCore.SymmetricIQ;
        ChkRinging.IsChecked         = NesCore.RingStrength > 0f;

        // NTSC sliders
        SliderRinging.Value = Clamp((int)(NesCore.RingStrength * 100f), 0, 100);
        SliderGamma.Value   = Clamp((int)(NesCore.GammaCoeff * 100f), 0, 100);
        SliderTempR.Value   = Clamp((int)(NesCore.ColorTempR * 100f), 0, 200);
        SliderTempG.Value   = Clamp((int)(NesCore.ColorTempG * 100f), 0, 200);
        SliderTempB.Value   = Clamp((int)(NesCore.ColorTempB * 100f), 0, 200);

        // CRT checkboxes
        ChkInterlaceJitter.IsChecked = NesCore.InterlaceJitter;
        ChkVignette.IsChecked        = NesCore.VignetteStrength > 0f;
        ChkShadowMask.IsChecked      = NesCore.ShadowMaskMode != NesCore.CrtMaskType.None;
        ChkCurvature.IsChecked       = NesCore.CurvatureStrength > 0f;
        ChkPhosphor.IsChecked        = NesCore.PhosphorDecay > 0f;
        ChkHorizontalBeam.IsChecked  = NesCore.HBeamSpread > 0f;
        ChkConvergence.IsChecked     = NesCore.ConvergenceStrength > 0f;

        // CRT sliders
        SliderVignette.Value    = Clamp((int)(NesCore.VignetteStrength * 100f), 0, 100);
        CmbShadowMaskMode.SelectedIndex = (int)NesCore.ShadowMaskMode;
        SliderShadowMask.Value  = Clamp((int)(NesCore.ShadowMaskStrength * 100f), 0, 100);
        SliderCurvature.Value   = Clamp((int)(NesCore.CurvatureStrength * 100f), 0, 100);
        SliderPhosphor.Value    = Clamp((int)(NesCore.PhosphorDecay * 100f), 0, 100);
        SliderHBeam.Value       = Clamp((int)(NesCore.HBeamSpread * 100f), 0, 100);
        SliderConvergence.Value = Clamp((int)(NesCore.ConvergenceStrength * 10f), 0, 100);

        // Connector — RF
        SlRfNoise.Value  = Clamp((int)(NesCore.RF_NoiseIntensity * 100f), 0, 100);
        SlRfSlew.Value   = Clamp((int)(NesCore.RF_SlewRate * 100f), 0, 100);
        SlRfChroma.Value = Clamp((int)(NesCore.RF_ChromaBlur * 100f), 0, 100);
        SlRfBeam.Value   = Clamp((int)(NesCore.RF_BeamSigma * 100f), 0, 200);
        SlRfBloom.Value  = Clamp((int)(NesCore.RF_BloomStrength * 100f), 0, 100);
        SlRfBright.Value = Clamp((int)(NesCore.RF_BrightnessBoost * 100f), 0, 200);

        // Connector — AV
        SlAvNoise.Value  = Clamp((int)(NesCore.AV_NoiseIntensity * 100f), 0, 100);
        SlAvSlew.Value   = Clamp((int)(NesCore.AV_SlewRate * 100f), 0, 100);
        SlAvChroma.Value = Clamp((int)(NesCore.AV_ChromaBlur * 100f), 0, 100);
        SlAvBeam.Value   = Clamp((int)(NesCore.AV_BeamSigma * 100f), 0, 200);
        SlAvBloom.Value  = Clamp((int)(NesCore.AV_BloomStrength * 100f), 0, 100);
        SlAvBright.Value = Clamp((int)(NesCore.AV_BrightnessBoost * 100f), 0, 200);

        // Connector — S-Video
        SlSvNoise.Value  = Clamp((int)(NesCore.SV_NoiseIntensity * 100f), 0, 100);
        SlSvSlew.Value   = Clamp((int)(NesCore.SV_SlewRate * 100f), 0, 100);
        SlSvChroma.Value = Clamp((int)(NesCore.SV_ChromaBlur * 100f), 0, 100);
        SlSvBeam.Value   = Clamp((int)(NesCore.SV_BeamSigma * 100f), 0, 200);
        SlSvBloom.Value  = Clamp((int)(NesCore.SV_BloomStrength * 100f), 0, 100);
        SlSvBright.Value = Clamp((int)(NesCore.SV_BrightnessBoost * 100f), 0, 200);

        CmbPreset.SelectedIndex = 0;
        UpdateAllLabels();
    }

    // ── Apply UI → NesCore ─────────────────────────────────────────────
    private void ApplyToFields()
    {
        // NTSC booleans
        NesCore.HbiSimulation    = ChkHBI.IsChecked == true;
        NesCore.ColorBurstJitter = ChkColorBurstJitter.IsChecked == true;
        NesCore.SymmetricIQ      = ChkSymmetricIQ.IsChecked == true;
        NesCore.UpdateIQMode();

        // NTSC values
        NesCore.RingStrength = (ChkRinging.IsChecked == true) ? (float)(SliderRinging.Value / 100.0) : 0f;
        NesCore.GammaCoeff   = (float)(SliderGamma.Value / 100.0);
        NesCore.ColorTempR   = (float)(SliderTempR.Value / 100.0);
        NesCore.ColorTempG   = (float)(SliderTempG.Value / 100.0);
        NesCore.ColorTempB   = (float)(SliderTempB.Value / 100.0);
        NesCore.UpdateGammaLUT();

        // CRT booleans
        NesCore.InterlaceJitter = ChkInterlaceJitter.IsChecked == true;

        // CRT values
        NesCore.VignetteStrength = (ChkVignette.IsChecked == true) ? (float)(SliderVignette.Value / 100.0) : 0f;
        NesCore.ShadowMaskMode   = (ChkShadowMask.IsChecked == true)
            ? (NesCore.CrtMaskType)CmbShadowMaskMode.SelectedIndex
            : NesCore.CrtMaskType.None;
        NesCore.ShadowMaskStrength  = (float)(SliderShadowMask.Value / 100.0);
        NesCore.CurvatureStrength   = (ChkCurvature.IsChecked == true) ? (float)(SliderCurvature.Value / 100.0) : 0f;
        NesCore.PhosphorDecay       = (ChkPhosphor.IsChecked == true) ? (float)(SliderPhosphor.Value / 100.0) : 0f;
        NesCore.HBeamSpread         = (ChkHorizontalBeam.IsChecked == true) ? (float)(SliderHBeam.Value / 100.0) : 0f;
        NesCore.ConvergenceStrength = (ChkConvergence.IsChecked == true) ? (float)(SliderConvergence.Value / 10.0) : 0f;

        // Connector — RF
        NesCore.RF_NoiseIntensity  = (float)(SlRfNoise.Value / 100.0);
        NesCore.RF_SlewRate        = (float)(SlRfSlew.Value / 100.0);
        NesCore.RF_ChromaBlur      = (float)(SlRfChroma.Value / 100.0);
        NesCore.RF_BeamSigma       = (float)(SlRfBeam.Value / 100.0);
        NesCore.RF_BloomStrength   = (float)(SlRfBloom.Value / 100.0);
        NesCore.RF_BrightnessBoost = (float)(SlRfBright.Value / 100.0);

        // Connector — AV
        NesCore.AV_NoiseIntensity  = (float)(SlAvNoise.Value / 100.0);
        NesCore.AV_SlewRate        = (float)(SlAvSlew.Value / 100.0);
        NesCore.AV_ChromaBlur      = (float)(SlAvChroma.Value / 100.0);
        NesCore.AV_BeamSigma       = (float)(SlAvBeam.Value / 100.0);
        NesCore.AV_BloomStrength   = (float)(SlAvBloom.Value / 100.0);
        NesCore.AV_BrightnessBoost = (float)(SlAvBright.Value / 100.0);

        // Connector — S-Video
        NesCore.SV_NoiseIntensity  = (float)(SlSvNoise.Value / 100.0);
        NesCore.SV_SlewRate        = (float)(SlSvSlew.Value / 100.0);
        NesCore.SV_ChromaBlur      = (float)(SlSvChroma.Value / 100.0);
        NesCore.SV_BeamSigma       = (float)(SlSvBeam.Value / 100.0);
        NesCore.SV_BloomStrength   = (float)(SlSvBloom.Value / 100.0);
        NesCore.SV_BrightnessBoost = (float)(SlSvBright.Value / 100.0);

        // Reinitialize CRT pipeline
        NesCore.SyncAnalogConfig();
        NesCore.Crt_Init();
    }

    // ── Save INI ───────────────────────────────────────────────────────
    private void SaveIni()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "configure", "AprNesAnalog.ini");
        string F(float v) => v.ToString("F4", Inv);
        string B(bool v) => v ? "1" : "0";

        string c = "";
        c += "; AprNesAnalog.ini  --  Analog simulation parameters\r\n;\r\n";
        c += "; ── Effect Toggles ─────────────────────────────────\r\n";
        c += "HbiSimulation="     + B(ChkHBI.IsChecked == true) + "\r\n";
        c += "ColorBurstJitter="  + B(ChkColorBurstJitter.IsChecked == true) + "\r\n";
        c += "SymmetricIQ="       + B(ChkSymmetricIQ.IsChecked == true) + "\r\n";
        c += "RingingEnabled="    + B(ChkRinging.IsChecked == true) + "\r\n";
        c += "InterlaceJitter="   + B(ChkInterlaceJitter.IsChecked == true) + "\r\n";
        c += "VignetteEnabled="   + B(ChkVignette.IsChecked == true) + "\r\n";
        c += "ShadowMaskEnabled=" + B(ChkShadowMask.IsChecked == true) + "\r\n";
        c += "CurvatureEnabled="  + B(ChkCurvature.IsChecked == true) + "\r\n";
        c += "PhosphorEnabled="   + B(ChkPhosphor.IsChecked == true) + "\r\n";
        c += "HBeamEnabled="      + B(ChkHorizontalBeam.IsChecked == true) + "\r\n";
        c += "ConvergenceEnabled=" + B(ChkConvergence.IsChecked == true) + "\r\n";
        c += ";\r\n; ── Effect Values ─────────────────────────────────\r\n";
        c += "RingStrength="       + F((float)(SliderRinging.Value / 100.0)) + "\r\n";
        c += "GammaCoeff="         + F((float)(SliderGamma.Value / 100.0)) + "\r\n";
        c += "ColorTempR="         + F((float)(SliderTempR.Value / 100.0)) + "\r\n";
        c += "ColorTempG="         + F((float)(SliderTempG.Value / 100.0)) + "\r\n";
        c += "ColorTempB="         + F((float)(SliderTempB.Value / 100.0)) + "\r\n";
        c += "VignetteStrength="   + F((float)(SliderVignette.Value / 100.0)) + "\r\n";
        c += "ShadowMaskMode="     + CmbShadowMaskMode.SelectedIndex + "\r\n";
        c += "ShadowMaskStrength=" + F((float)(SliderShadowMask.Value / 100.0)) + "\r\n";
        c += "CurvatureStrength="  + F((float)(SliderCurvature.Value / 100.0)) + "\r\n";
        c += "PhosphorDecay="      + F((float)(SliderPhosphor.Value / 100.0)) + "\r\n";
        c += "HBeamSpread="        + F((float)(SliderHBeam.Value / 100.0)) + "\r\n";
        c += "ConvergenceStrength=" + F((float)(SliderConvergence.Value / 10.0)) + "\r\n";
        c += ";\r\n; ── Stage 1 Connector ──────────────────────────────\r\n";
        c += "RF_NoiseIntensity=" + F((float)(SlRfNoise.Value / 100.0)) + "\r\n";
        c += "RF_SlewRate="       + F((float)(SlRfSlew.Value / 100.0)) + "\r\n";
        c += "RF_ChromaBlur="     + F((float)(SlRfChroma.Value / 100.0)) + "\r\n";
        c += "AV_NoiseIntensity=" + F((float)(SlAvNoise.Value / 100.0)) + "\r\n";
        c += "AV_SlewRate="       + F((float)(SlAvSlew.Value / 100.0)) + "\r\n";
        c += "AV_ChromaBlur="     + F((float)(SlAvChroma.Value / 100.0)) + "\r\n";
        c += "SV_NoiseIntensity=" + F((float)(SlSvNoise.Value / 100.0)) + "\r\n";
        c += "SV_SlewRate="       + F((float)(SlSvSlew.Value / 100.0)) + "\r\n";
        c += "SV_ChromaBlur="     + F((float)(SlSvChroma.Value / 100.0)) + "\r\n";
        c += ";\r\n; ── Stage 2 Connector ──────────────────────────────\r\n";
        c += "RF_BeamSigma="       + F((float)(SlRfBeam.Value / 100.0)) + "\r\n";
        c += "RF_BloomStrength="   + F((float)(SlRfBloom.Value / 100.0)) + "\r\n";
        c += "RF_BrightnessBoost=" + F((float)(SlRfBright.Value / 100.0)) + "\r\n";
        c += "AV_BeamSigma="       + F((float)(SlAvBeam.Value / 100.0)) + "\r\n";
        c += "AV_BloomStrength="   + F((float)(SlAvBloom.Value / 100.0)) + "\r\n";
        c += "AV_BrightnessBoost=" + F((float)(SlAvBright.Value / 100.0)) + "\r\n";
        c += "SV_BeamSigma="       + F((float)(SlSvBeam.Value / 100.0)) + "\r\n";
        c += "SV_BloomStrength="   + F((float)(SlSvBloom.Value / 100.0)) + "\r\n";
        c += "SV_BrightnessBoost=" + F((float)(SlSvBright.Value / 100.0)) + "\r\n";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, c);
        }
        catch { }
    }

    // ── Wire events ────────────────────────────────────────────────────
    private void WireEvents()
    {
        CmbPreset.SelectionChanged += (_, _) => ComboPreset_Changed();

        // NTSC sliders
        SliderRinging.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") LblRinging.Text = (SliderRinging.Value / 100.0).ToString("F2"); };
        SliderGamma.PropertyChanged   += (_, e) => { if (e.Property.Name == "Value") LblGamma.Text = (SliderGamma.Value / 100.0).ToString("F3"); };
        SliderTempR.PropertyChanged   += (_, e) => { if (e.Property.Name == "Value") LblTempR.Text = (SliderTempR.Value / 100.0).ToString("F2"); };
        SliderTempG.PropertyChanged   += (_, e) => { if (e.Property.Name == "Value") LblTempG.Text = (SliderTempG.Value / 100.0).ToString("F2"); };
        SliderTempB.PropertyChanged   += (_, e) => { if (e.Property.Name == "Value") LblTempB.Text = (SliderTempB.Value / 100.0).ToString("F2"); };

        // CRT sliders
        SliderVignette.PropertyChanged    += (_, e) => { if (e.Property.Name == "Value") LblVignette.Text = (SliderVignette.Value / 100.0).ToString("F2"); };
        SliderShadowMask.PropertyChanged  += (_, e) => { if (e.Property.Name == "Value") LblShadowMask.Text = (SliderShadowMask.Value / 100.0).ToString("F2"); };
        SliderCurvature.PropertyChanged   += (_, e) => { if (e.Property.Name == "Value") LblCurvature.Text = (SliderCurvature.Value / 100.0).ToString("F2"); };
        SliderPhosphor.PropertyChanged    += (_, e) => { if (e.Property.Name == "Value") LblPhosphor.Text = (SliderPhosphor.Value / 100.0).ToString("F2"); };
        SliderHBeam.PropertyChanged       += (_, e) => { if (e.Property.Name == "Value") LblHBeam.Text = (SliderHBeam.Value / 100.0).ToString("F2"); };
        SliderConvergence.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") LblConvergence.Text = (SliderConvergence.Value / 10.0).ToString("F1"); };

        // Connector sliders
        SlRfNoise.PropertyChanged  += (_, e) => { if (e.Property.Name == "Value") LblRfNoise.Text = (SlRfNoise.Value / 100.0).ToString("F2"); };
        SlRfSlew.PropertyChanged   += (_, e) => { if (e.Property.Name == "Value") LblRfSlew.Text = (SlRfSlew.Value / 100.0).ToString("F2"); };
        SlRfChroma.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") LblRfChroma.Text = (SlRfChroma.Value / 100.0).ToString("F2"); };
        SlRfBeam.PropertyChanged   += (_, e) => { if (e.Property.Name == "Value") LblRfBeam.Text = (SlRfBeam.Value / 100.0).ToString("F2"); };
        SlRfBloom.PropertyChanged  += (_, e) => { if (e.Property.Name == "Value") LblRfBloom.Text = (SlRfBloom.Value / 100.0).ToString("F2"); };
        SlRfBright.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") LblRfBright.Text = (SlRfBright.Value / 100.0).ToString("F2"); };

        SlAvNoise.PropertyChanged  += (_, e) => { if (e.Property.Name == "Value") LblAvNoise.Text = (SlAvNoise.Value / 100.0).ToString("F2"); };
        SlAvSlew.PropertyChanged   += (_, e) => { if (e.Property.Name == "Value") LblAvSlew.Text = (SlAvSlew.Value / 100.0).ToString("F2"); };
        SlAvChroma.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") LblAvChroma.Text = (SlAvChroma.Value / 100.0).ToString("F2"); };
        SlAvBeam.PropertyChanged   += (_, e) => { if (e.Property.Name == "Value") LblAvBeam.Text = (SlAvBeam.Value / 100.0).ToString("F2"); };
        SlAvBloom.PropertyChanged  += (_, e) => { if (e.Property.Name == "Value") LblAvBloom.Text = (SlAvBloom.Value / 100.0).ToString("F2"); };
        SlAvBright.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") LblAvBright.Text = (SlAvBright.Value / 100.0).ToString("F2"); };

        SlSvNoise.PropertyChanged  += (_, e) => { if (e.Property.Name == "Value") LblSvNoise.Text = (SlSvNoise.Value / 100.0).ToString("F2"); };
        SlSvSlew.PropertyChanged   += (_, e) => { if (e.Property.Name == "Value") LblSvSlew.Text = (SlSvSlew.Value / 100.0).ToString("F2"); };
        SlSvChroma.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") LblSvChroma.Text = (SlSvChroma.Value / 100.0).ToString("F2"); };
        SlSvBeam.PropertyChanged   += (_, e) => { if (e.Property.Name == "Value") LblSvBeam.Text = (SlSvBeam.Value / 100.0).ToString("F2"); };
        SlSvBloom.PropertyChanged  += (_, e) => { if (e.Property.Name == "Value") LblSvBloom.Text = (SlSvBloom.Value / 100.0).ToString("F2"); };
        SlSvBright.PropertyChanged += (_, e) => { if (e.Property.Name == "Value") LblSvBright.Text = (SlSvBright.Value / 100.0).ToString("F2"); };
    }

    // ── Preset profiles ────────────────────────────────────────────────
    private void ComboPreset_Changed()
    {
        switch (CmbPreset.SelectedIndex)
        {
            case 1: SetNtsc(true);  SetCrt(false); break;
            case 2: SetNtsc(false); SetCrt(true);  break;
            case 3: SetNtsc(true);  SetCrt(true);  break;
            default: return;
        }
        UpdateAllLabels();
    }

    private void SetNtsc(bool on)
    {
        ChkHBI.IsChecked = on;
        ChkColorBurstJitter.IsChecked = on;
        ChkRinging.IsChecked = on;
        if (on)
        {
            SliderRinging.Value = 30;
            SliderGamma.Value   = 23;
            SliderTempR.Value   = 100;
            SliderTempG.Value   = 100;
            SliderTempB.Value   = 100;
        }
    }

    private void SetCrt(bool on)
    {
        ChkInterlaceJitter.IsChecked = on;
        ChkVignette.IsChecked        = on;
        ChkShadowMask.IsChecked      = on;
        ChkCurvature.IsChecked       = on;
        ChkPhosphor.IsChecked        = on;
        ChkHorizontalBeam.IsChecked  = on;
        ChkConvergence.IsChecked     = on;
        if (on)
        {
            SliderVignette.Value    = 15;
            CmbShadowMaskMode.SelectedIndex = 1;
            SliderShadowMask.Value  = 30;
            SliderCurvature.Value   = 12;
            SliderPhosphor.Value    = 60;
            SliderHBeam.Value       = 40;
            SliderConvergence.Value = 20;
        }
    }

    // ── Update all labels ──────────────────────────────────────────────
    private void UpdateAllLabels()
    {
        LblRinging.Text     = (SliderRinging.Value / 100.0).ToString("F2");
        LblGamma.Text       = (SliderGamma.Value / 100.0).ToString("F3");
        LblTempR.Text       = (SliderTempR.Value / 100.0).ToString("F2");
        LblTempG.Text       = (SliderTempG.Value / 100.0).ToString("F2");
        LblTempB.Text       = (SliderTempB.Value / 100.0).ToString("F2");
        LblVignette.Text    = (SliderVignette.Value / 100.0).ToString("F2");
        LblShadowMask.Text  = (SliderShadowMask.Value / 100.0).ToString("F2");
        LblCurvature.Text   = (SliderCurvature.Value / 100.0).ToString("F2");
        LblPhosphor.Text    = (SliderPhosphor.Value / 100.0).ToString("F2");
        LblHBeam.Text       = (SliderHBeam.Value / 100.0).ToString("F2");
        LblConvergence.Text = (SliderConvergence.Value / 10.0).ToString("F1");

        LblRfNoise.Text  = (SlRfNoise.Value / 100.0).ToString("F2");
        LblRfSlew.Text   = (SlRfSlew.Value / 100.0).ToString("F2");
        LblRfChroma.Text = (SlRfChroma.Value / 100.0).ToString("F2");
        LblRfBeam.Text   = (SlRfBeam.Value / 100.0).ToString("F2");
        LblRfBloom.Text  = (SlRfBloom.Value / 100.0).ToString("F2");
        LblRfBright.Text = (SlRfBright.Value / 100.0).ToString("F2");

        LblAvNoise.Text  = (SlAvNoise.Value / 100.0).ToString("F2");
        LblAvSlew.Text   = (SlAvSlew.Value / 100.0).ToString("F2");
        LblAvChroma.Text = (SlAvChroma.Value / 100.0).ToString("F2");
        LblAvBeam.Text   = (SlAvBeam.Value / 100.0).ToString("F2");
        LblAvBloom.Text  = (SlAvBloom.Value / 100.0).ToString("F2");
        LblAvBright.Text = (SlAvBright.Value / 100.0).ToString("F2");

        LblSvNoise.Text  = (SlSvNoise.Value / 100.0).ToString("F2");
        LblSvSlew.Text   = (SlSvSlew.Value / 100.0).ToString("F2");
        LblSvChroma.Text = (SlSvChroma.Value / 100.0).ToString("F2");
        LblSvBeam.Text   = (SlSvBeam.Value / 100.0).ToString("F2");
        LblSvBloom.Text  = (SlSvBloom.Value / 100.0).ToString("F2");
        LblSvBright.Text = (SlSvBright.Value / 100.0).ToString("F2");
    }

    // ── OK button ──────────────────────────────────────────────────────
    private void BtnOK_Click(object? sender, RoutedEventArgs e)
    {
        ApplyToFields();
        SaveIni();
        Close();
    }

    private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
}
