using System;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using LangTool;

namespace AprNes.UI
{
    public partial class AprNes_AnalogConfigureUI : Form
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public AprNes_AnalogConfigureUI()
        {
            InitializeComponent();
            LoadFromFields();
            WireEvents();
            InitLang();
        }

        // ── Apply multi-language text to controls ─────────────────────────
        static string L(string key)
        {
            var tbl = LangINI.lang_table[AprNesUI.GetInstance().AppConfigure["Lang"]];
            return tbl.ContainsKey(key) ? tbl[key] : key;
        }

        void InitLang()
        {
            if (!LangINI.LangLoadOK) return;

            this.Text = L("analog_setting");
            Analog_Ok_btn.Text = L("ok");
            lblPreset.Text = L("analog_feature_profile");

            // Preset combo items
            comboPreset.Items.Clear();
            comboPreset.Items.Add(L("analog_choose_feature"));
            comboPreset.Items.Add(L("analog_ntsc_only"));
            comboPreset.Items.Add(L("analog_crt_only"));
            comboPreset.Items.Add(L("analog_ntsc_crt"));
            comboPreset.SelectedIndex = 0;

            // Group boxes
            grpNtsc.Text = L("analog_grp_ntsc");
            grpCrt.Text = L("analog_grp_crt");
            grpConnector.Text = L("analog_grp_connector");

            // NTSC checkboxes + descriptions
            chkHBI.Text = L("analog_hbi");
            lblHBIDesc.Text = L("analog_hbi_desc");
            chkColorBurstJitter.Text = L("analog_colorburst");
            lblColorBurstJitterDesc.Text = L("analog_colorburst_desc");
            chkRinging.Text = L("analog_ringing");
            lblRingingDesc.Text = L("analog_ringing_desc");
            lblGamma.Text = L("analog_gamma");
            lblGammaDesc.Text = L("analog_gamma_desc");
            lblColorTemp.Text = L("analog_colortemp");
            lblColorTempDesc.Text = L("analog_colortemp_desc");

            // CRT checkboxes + descriptions
            chkInterlaceJitter.Text = L("analog_interlace");
            lblInterlaceJitterDesc.Text = L("analog_interlace_desc");
            chkVignette.Text = L("analog_vignette");
            lblVignetteDesc.Text = L("analog_vignette_desc");
            chkShadowMask.Text = L("analog_shadowmask");
            lblShadowMaskDesc.Text = L("analog_shadowmask_desc");
            lblMaskStrength.Text = L("analog_strength");
            chkCurvature.Text = L("analog_curvature");
            lblCurvatureDesc.Text = L("analog_curvature_desc");
            chkPhosphor.Text = L("analog_phosphor");
            lblPhosphorDesc.Text = L("analog_phosphor_desc");
            chkHBeam.Text = L("analog_hbeam");
            lblHBeamDesc.Text = L("analog_hbeam_desc");
            chkConvergence.Text = L("analog_convergence");
            lblConvergenceDesc.Text = L("analog_convergence_desc");
        }

        // ── Load current NesCore static fields → UI controls ──────────────
        void LoadFromFields()
        {
            // NTSC checkboxes
            chkHBI.Checked = Ntsc.HbiSimulation;
            chkColorBurstJitter.Checked = Ntsc.ColorBurstJitter;
            chkRinging.Checked = Ntsc.RingStrength > 0f;

            // NTSC sliders
            trkRinging.Value = Clamp((int)(Ntsc.RingStrength * 100f), trkRinging.Minimum, trkRinging.Maximum);
            trkGamma.Value = Clamp((int)(Ntsc.GammaCoeff * 100f), trkGamma.Minimum, trkGamma.Maximum);
            trkCTR.Value = Clamp((int)(Ntsc.ColorTempR * 100f), trkCTR.Minimum, trkCTR.Maximum);
            trkCTG.Value = Clamp((int)(Ntsc.ColorTempG * 100f), trkCTG.Minimum, trkCTG.Maximum);
            trkCTB.Value = Clamp((int)(Ntsc.ColorTempB * 100f), trkCTB.Minimum, trkCTB.Maximum);

            // CRT checkboxes
            chkInterlaceJitter.Checked = CrtScreen.InterlaceJitter;
            chkVignette.Checked = CrtScreen.VignetteStrength > 0f;
            chkShadowMask.Checked = CrtScreen.ShadowMaskMode != CrtScreen.MaskType.None;
            chkCurvature.Checked = CrtScreen.CurvatureStrength > 0f;
            chkPhosphor.Checked = CrtScreen.PhosphorDecay > 0f;
            chkHBeam.Checked = CrtScreen.HBeamSpread > 0f;
            chkConvergence.Checked = CrtScreen.ConvergenceStrength > 0f;

            // CRT sliders
            trkVignette.Value = Clamp((int)(CrtScreen.VignetteStrength * 100f), trkVignette.Minimum, trkVignette.Maximum);
            cmbShadowMask.SelectedIndex = (int)CrtScreen.ShadowMaskMode;
            trkMaskStrength.Value = Clamp((int)(CrtScreen.ShadowMaskStrength * 100f), trkMaskStrength.Minimum, trkMaskStrength.Maximum);
            trkCurvature.Value = Clamp((int)(CrtScreen.CurvatureStrength * 100f), trkCurvature.Minimum, trkCurvature.Maximum);
            trkPhosphor.Value = Clamp((int)(CrtScreen.PhosphorDecay * 100f), trkPhosphor.Minimum, trkPhosphor.Maximum);
            trkHBeam.Value = Clamp((int)(CrtScreen.HBeamSpread * 100f), trkHBeam.Minimum, trkHBeam.Maximum);
            trkConvergence.Value = Clamp((int)(CrtScreen.ConvergenceStrength * 10f), trkConvergence.Minimum, trkConvergence.Maximum);

            // Connector — RF (Stage 1)
            trkRfNoise.Value = Clamp((int)(Ntsc.RF_NoiseIntensity * 100f), trkRfNoise.Minimum, trkRfNoise.Maximum);
            trkRfSlew.Value = Clamp((int)(Ntsc.RF_SlewRate * 100f), trkRfSlew.Minimum, trkRfSlew.Maximum);
            trkRfChroma.Value = Clamp((int)(Ntsc.RF_ChromaBlur * 100f), trkRfChroma.Minimum, trkRfChroma.Maximum);
            // Connector — RF (Stage 2)
            trkRfBeam.Value = Clamp((int)(CrtScreen.RF_BeamSigma * 100f), trkRfBeam.Minimum, trkRfBeam.Maximum);
            trkRfBloom.Value = Clamp((int)(CrtScreen.RF_BloomStrength * 100f), trkRfBloom.Minimum, trkRfBloom.Maximum);
            trkRfBright.Value = Clamp((int)(CrtScreen.RF_BrightnessBoost * 100f), trkRfBright.Minimum, trkRfBright.Maximum);

            // Connector — AV (Stage 1)
            trkAvNoise.Value = Clamp((int)(Ntsc.AV_NoiseIntensity * 100f), trkAvNoise.Minimum, trkAvNoise.Maximum);
            trkAvSlew.Value = Clamp((int)(Ntsc.AV_SlewRate * 100f), trkAvSlew.Minimum, trkAvSlew.Maximum);
            trkAvChroma.Value = Clamp((int)(Ntsc.AV_ChromaBlur * 100f), trkAvChroma.Minimum, trkAvChroma.Maximum);
            // Connector — AV (Stage 2)
            trkAvBeam.Value = Clamp((int)(CrtScreen.AV_BeamSigma * 100f), trkAvBeam.Minimum, trkAvBeam.Maximum);
            trkAvBloom.Value = Clamp((int)(CrtScreen.AV_BloomStrength * 100f), trkAvBloom.Minimum, trkAvBloom.Maximum);
            trkAvBright.Value = Clamp((int)(CrtScreen.AV_BrightnessBoost * 100f), trkAvBright.Minimum, trkAvBright.Maximum);

            // Connector — S-Video (Stage 1)
            trkSvNoise.Value = Clamp((int)(Ntsc.SV_NoiseIntensity * 100f), trkSvNoise.Minimum, trkSvNoise.Maximum);
            trkSvSlew.Value = Clamp((int)(Ntsc.SV_SlewRate * 100f), trkSvSlew.Minimum, trkSvSlew.Maximum);
            trkSvChroma.Value = Clamp((int)(Ntsc.SV_ChromaBlur * 100f), trkSvChroma.Minimum, trkSvChroma.Maximum);
            // Connector — S-Video (Stage 2)
            trkSvBeam.Value = Clamp((int)(CrtScreen.SV_BeamSigma * 100f), trkSvBeam.Minimum, trkSvBeam.Maximum);
            trkSvBloom.Value = Clamp((int)(CrtScreen.SV_BloomStrength * 100f), trkSvBloom.Minimum, trkSvBloom.Maximum);
            trkSvBright.Value = Clamp((int)(CrtScreen.SV_BrightnessBoost * 100f), trkSvBright.Minimum, trkSvBright.Maximum);

            // Preset combo
            comboPreset.SelectedIndex = 0;

            // Refresh all value labels
            UpdateAllLabels();
        }

        // ── Apply UI controls → NesCore static fields + save INI ──────────
        void ApplyToFields()
        {
            // NTSC booleans
            Ntsc.HbiSimulation = chkHBI.Checked;
            Ntsc.ColorBurstJitter = chkColorBurstJitter.Checked;

            // NTSC values (checkbox controls whether effect is active)
            Ntsc.RingStrength = chkRinging.Checked ? trkRinging.Value / 100f : 0f;
            Ntsc.GammaCoeff = trkGamma.Value / 100f;
            Ntsc.ColorTempR = trkCTR.Value / 100f;
            Ntsc.ColorTempG = trkCTG.Value / 100f;
            Ntsc.ColorTempB = trkCTB.Value / 100f;

            // Rebuild gamma LUT after changing GammaCoeff
            Ntsc.UpdateGammaLUT();

            // CRT booleans
            CrtScreen.InterlaceJitter = chkInterlaceJitter.Checked;

            // CRT values (checkbox controls whether effect is active)
            CrtScreen.VignetteStrength = chkVignette.Checked ? trkVignette.Value / 100f : 0f;
            CrtScreen.ShadowMaskMode = chkShadowMask.Checked
                ? (CrtScreen.MaskType)cmbShadowMask.SelectedIndex
                : CrtScreen.MaskType.None;
            CrtScreen.ShadowMaskStrength = trkMaskStrength.Value / 100f;
            CrtScreen.CurvatureStrength = chkCurvature.Checked ? trkCurvature.Value / 100f : 0f;
            CrtScreen.PhosphorDecay = chkPhosphor.Checked ? trkPhosphor.Value / 100f : 0f;
            CrtScreen.HBeamSpread = chkHBeam.Checked ? trkHBeam.Value / 100f : 0f;
            CrtScreen.ConvergenceStrength = chkConvergence.Checked ? trkConvergence.Value / 10f : 0f;

            // Connector — RF
            Ntsc.RF_NoiseIntensity = trkRfNoise.Value / 100f;
            Ntsc.RF_SlewRate = trkRfSlew.Value / 100f;
            Ntsc.RF_ChromaBlur = trkRfChroma.Value / 100f;
            CrtScreen.RF_BeamSigma = trkRfBeam.Value / 100f;
            CrtScreen.RF_BloomStrength = trkRfBloom.Value / 100f;
            CrtScreen.RF_BrightnessBoost = trkRfBright.Value / 100f;

            // Connector — AV
            Ntsc.AV_NoiseIntensity = trkAvNoise.Value / 100f;
            Ntsc.AV_SlewRate = trkAvSlew.Value / 100f;
            Ntsc.AV_ChromaBlur = trkAvChroma.Value / 100f;
            CrtScreen.AV_BeamSigma = trkAvBeam.Value / 100f;
            CrtScreen.AV_BloomStrength = trkAvBloom.Value / 100f;
            CrtScreen.AV_BrightnessBoost = trkAvBright.Value / 100f;

            // Connector — S-Video
            Ntsc.SV_NoiseIntensity = trkSvNoise.Value / 100f;
            Ntsc.SV_SlewRate = trkSvSlew.Value / 100f;
            Ntsc.SV_ChromaBlur = trkSvChroma.Value / 100f;
            CrtScreen.SV_BeamSigma = trkSvBeam.Value / 100f;
            CrtScreen.SV_BloomStrength = trkSvBloom.Value / 100f;
            CrtScreen.SV_BrightnessBoost = trkSvBright.Value / 100f;

            // Reinitialize CRT with updated parameters
            CrtScreen.Init();
        }

        void SaveIni()
        {
            string path = Application.StartupPath + @"\AprNesAnalog.ini";
            string F(float v) => v.ToString("F4", Inv);
            string B(bool v) => v ? "1" : "0";

            string c = "";
            c += "; AprNesAnalog.ini  --  Analog simulation parameters\r\n";
            c += ";\r\n";
            c += "; ── Effect Toggles ─────────────────────────────────────────────────\r\n";
            c += "HbiSimulation=" + B(chkHBI.Checked) + "\r\n";
            c += "ColorBurstJitter=" + B(chkColorBurstJitter.Checked) + "\r\n";
            c += "RingingEnabled=" + B(chkRinging.Checked) + "\r\n";
            c += "InterlaceJitter=" + B(chkInterlaceJitter.Checked) + "\r\n";
            c += "VignetteEnabled=" + B(chkVignette.Checked) + "\r\n";
            c += "ShadowMaskEnabled=" + B(chkShadowMask.Checked) + "\r\n";
            c += "CurvatureEnabled=" + B(chkCurvature.Checked) + "\r\n";
            c += "PhosphorEnabled=" + B(chkPhosphor.Checked) + "\r\n";
            c += "HBeamEnabled=" + B(chkHBeam.Checked) + "\r\n";
            c += "ConvergenceEnabled=" + B(chkConvergence.Checked) + "\r\n";
            c += ";\r\n";
            c += "; ── Effect Values ──────────────────────────────────────────────────\r\n";
            c += "RingStrength=" + F(trkRinging.Value / 100f) + "\r\n";
            c += "GammaCoeff=" + F(trkGamma.Value / 100f) + "\r\n";
            c += "ColorTempR=" + F(trkCTR.Value / 100f) + "\r\n";
            c += "ColorTempG=" + F(trkCTG.Value / 100f) + "\r\n";
            c += "ColorTempB=" + F(trkCTB.Value / 100f) + "\r\n";
            c += "VignetteStrength=" + F(trkVignette.Value / 100f) + "\r\n";
            c += "ShadowMaskMode=" + cmbShadowMask.SelectedIndex + "\r\n";
            c += "ShadowMaskStrength=" + F(trkMaskStrength.Value / 100f) + "\r\n";
            c += "CurvatureStrength=" + F(trkCurvature.Value / 100f) + "\r\n";
            c += "PhosphorDecay=" + F(trkPhosphor.Value / 100f) + "\r\n";
            c += "HBeamSpread=" + F(trkHBeam.Value / 100f) + "\r\n";
            c += "ConvergenceStrength=" + F(trkConvergence.Value / 10f) + "\r\n";
            c += ";\r\n";
            c += "; ── Stage 1 Connector (Ntsc) ──────────────────────────────────────\r\n";
            c += "RF_NoiseIntensity=" + F(trkRfNoise.Value / 100f) + "\r\n";
            c += "RF_SlewRate=" + F(trkRfSlew.Value / 100f) + "\r\n";
            c += "RF_ChromaBlur=" + F(trkRfChroma.Value / 100f) + "\r\n";
            c += "AV_NoiseIntensity=" + F(trkAvNoise.Value / 100f) + "\r\n";
            c += "AV_SlewRate=" + F(trkAvSlew.Value / 100f) + "\r\n";
            c += "AV_ChromaBlur=" + F(trkAvChroma.Value / 100f) + "\r\n";
            c += "SV_NoiseIntensity=" + F(trkSvNoise.Value / 100f) + "\r\n";
            c += "SV_SlewRate=" + F(trkSvSlew.Value / 100f) + "\r\n";
            c += "SV_ChromaBlur=" + F(trkSvChroma.Value / 100f) + "\r\n";
            c += ";\r\n";
            c += "; ── Stage 2 Connector (CrtScreen) ───────────────────────────────────\r\n";
            c += "RF_BeamSigma=" + F(trkRfBeam.Value / 100f) + "\r\n";
            c += "RF_BloomStrength=" + F(trkRfBloom.Value / 100f) + "\r\n";
            c += "RF_BrightnessBoost=" + F(trkRfBright.Value / 100f) + "\r\n";
            c += "AV_BeamSigma=" + F(trkAvBeam.Value / 100f) + "\r\n";
            c += "AV_BloomStrength=" + F(trkAvBloom.Value / 100f) + "\r\n";
            c += "AV_BrightnessBoost=" + F(trkAvBright.Value / 100f) + "\r\n";
            c += "SV_BeamSigma=" + F(trkSvBeam.Value / 100f) + "\r\n";
            c += "SV_BloomStrength=" + F(trkSvBloom.Value / 100f) + "\r\n";
            c += "SV_BrightnessBoost=" + F(trkSvBright.Value / 100f) + "\r\n";

            File.WriteAllText(path, c);
        }

        // ── Wire all events ───────────────────────────────────────────────
        void WireEvents()
        {
            // OK button
            Analog_Ok_btn.Click += (s, e) =>
            {
                ApplyToFields();
                SaveIni();
                DialogResult = DialogResult.OK;
                Close();
            };

            // Preset combo
            comboPreset.SelectedIndexChanged += ComboPreset_Changed;

            // TrackBar scroll → update value labels
            trkRinging.Scroll += (s, e) => lblRingingVal.Text = (trkRinging.Value / 100f).ToString("F2");
            trkGamma.Scroll += (s, e) => lblGammaVal.Text = (trkGamma.Value / 100f).ToString("F3");
            trkCTR.Scroll += (s, e) => lblCTRVal.Text = (trkCTR.Value / 100f).ToString("F2");
            trkCTG.Scroll += (s, e) => lblCTGVal.Text = (trkCTG.Value / 100f).ToString("F2");
            trkCTB.Scroll += (s, e) => lblCTBVal.Text = (trkCTB.Value / 100f).ToString("F2");
            trkVignette.Scroll += (s, e) => lblVignetteVal.Text = (trkVignette.Value / 100f).ToString("F2");
            trkMaskStrength.Scroll += (s, e) => lblMaskStrengthVal.Text = (trkMaskStrength.Value / 100f).ToString("F2");
            trkCurvature.Scroll += (s, e) => lblCurvatureVal.Text = (trkCurvature.Value / 100f).ToString("F2");
            trkPhosphor.Scroll += (s, e) => lblPhosphorVal.Text = (trkPhosphor.Value / 100f).ToString("F2");
            trkHBeam.Scroll += (s, e) => lblHBeamVal.Text = (trkHBeam.Value / 100f).ToString("F2");
            trkConvergence.Scroll += (s, e) => lblConvergenceVal.Text = (trkConvergence.Value / 10f).ToString("F1");

            // Connector trackbar scroll
            trkRfNoise.Scroll += (s, e) => lblRfNoiseVal.Text = (trkRfNoise.Value / 100f).ToString("F2");
            trkRfSlew.Scroll += (s, e) => lblRfSlewVal.Text = (trkRfSlew.Value / 100f).ToString("F2");
            trkRfChroma.Scroll += (s, e) => lblRfChromaVal.Text = (trkRfChroma.Value / 100f).ToString("F2");
            trkRfBeam.Scroll += (s, e) => lblRfBeamVal.Text = (trkRfBeam.Value / 100f).ToString("F2");
            trkRfBloom.Scroll += (s, e) => lblRfBloomVal.Text = (trkRfBloom.Value / 100f).ToString("F2");
            trkRfBright.Scroll += (s, e) => lblRfBrightVal.Text = (trkRfBright.Value / 100f).ToString("F2");
            trkAvNoise.Scroll += (s, e) => lblAvNoiseVal.Text = (trkAvNoise.Value / 100f).ToString("F2");
            trkAvSlew.Scroll += (s, e) => lblAvSlewVal.Text = (trkAvSlew.Value / 100f).ToString("F2");
            trkAvChroma.Scroll += (s, e) => lblAvChromaVal.Text = (trkAvChroma.Value / 100f).ToString("F2");
            trkAvBeam.Scroll += (s, e) => lblAvBeamVal.Text = (trkAvBeam.Value / 100f).ToString("F2");
            trkAvBloom.Scroll += (s, e) => lblAvBloomVal.Text = (trkAvBloom.Value / 100f).ToString("F2");
            trkAvBright.Scroll += (s, e) => lblAvBrightVal.Text = (trkAvBright.Value / 100f).ToString("F2");
            trkSvNoise.Scroll += (s, e) => lblSvNoiseVal.Text = (trkSvNoise.Value / 100f).ToString("F2");
            trkSvSlew.Scroll += (s, e) => lblSvSlewVal.Text = (trkSvSlew.Value / 100f).ToString("F2");
            trkSvChroma.Scroll += (s, e) => lblSvChromaVal.Text = (trkSvChroma.Value / 100f).ToString("F2");
            trkSvBeam.Scroll += (s, e) => lblSvBeamVal.Text = (trkSvBeam.Value / 100f).ToString("F2");
            trkSvBloom.Scroll += (s, e) => lblSvBloomVal.Text = (trkSvBloom.Value / 100f).ToString("F2");
            trkSvBright.Scroll += (s, e) => lblSvBrightVal.Text = (trkSvBright.Value / 100f).ToString("F2");
        }

        // ── Preset profiles ──────────────────────────────────────────────
        void ComboPreset_Changed(object sender, EventArgs e)
        {
            switch (comboPreset.SelectedIndex)
            {
                case 1: SetNtsc(true);  SetCrt(false); break; // NTSC Only
                case 2: SetNtsc(false); SetCrt(true);  break; // CRT Only
                case 3: SetNtsc(true);  SetCrt(true);  break; // NTSC + CRT
                default: return; // "Choose Feature" — do nothing
            }
            UpdateAllLabels();
        }

        void SetNtsc(bool on)
        {
            chkHBI.Checked = on;
            chkColorBurstJitter.Checked = on;
            chkRinging.Checked = on;
            if (on)
            {
                trkRinging.Value = 30;
                trkGamma.Value = 23;
                trkCTR.Value = 100;
                trkCTG.Value = 100;
                trkCTB.Value = 100;
            }
        }

        void SetCrt(bool on)
        {
            chkInterlaceJitter.Checked = on;
            chkVignette.Checked = on;
            chkShadowMask.Checked = on;
            chkCurvature.Checked = on;
            chkPhosphor.Checked = on;
            chkHBeam.Checked = on;
            chkConvergence.Checked = on;
            if (on)
            {
                trkVignette.Value = 15;
                cmbShadowMask.SelectedIndex = 1; // Aperture Grille
                trkMaskStrength.Value = 30;
                trkCurvature.Value = 12;
                trkPhosphor.Value = 60;
                trkHBeam.Value = 40;
                trkConvergence.Value = 20;
            }
        }

        // ── Update all value labels from current trackbar positions ──────
        void UpdateAllLabels()
        {
            lblRingingVal.Text = (trkRinging.Value / 100f).ToString("F2");
            lblGammaVal.Text = (trkGamma.Value / 100f).ToString("F3");
            lblCTRVal.Text = (trkCTR.Value / 100f).ToString("F2");
            lblCTGVal.Text = (trkCTG.Value / 100f).ToString("F2");
            lblCTBVal.Text = (trkCTB.Value / 100f).ToString("F2");
            lblVignetteVal.Text = (trkVignette.Value / 100f).ToString("F2");
            lblMaskStrengthVal.Text = (trkMaskStrength.Value / 100f).ToString("F2");
            lblCurvatureVal.Text = (trkCurvature.Value / 100f).ToString("F2");
            lblPhosphorVal.Text = (trkPhosphor.Value / 100f).ToString("F2");
            lblHBeamVal.Text = (trkHBeam.Value / 100f).ToString("F2");
            lblConvergenceVal.Text = (trkConvergence.Value / 10f).ToString("F1");

            lblRfNoiseVal.Text = (trkRfNoise.Value / 100f).ToString("F2");
            lblRfSlewVal.Text = (trkRfSlew.Value / 100f).ToString("F2");
            lblRfChromaVal.Text = (trkRfChroma.Value / 100f).ToString("F2");
            lblRfBeamVal.Text = (trkRfBeam.Value / 100f).ToString("F2");
            lblRfBloomVal.Text = (trkRfBloom.Value / 100f).ToString("F2");
            lblRfBrightVal.Text = (trkRfBright.Value / 100f).ToString("F2");
            lblAvNoiseVal.Text = (trkAvNoise.Value / 100f).ToString("F2");
            lblAvSlewVal.Text = (trkAvSlew.Value / 100f).ToString("F2");
            lblAvChromaVal.Text = (trkAvChroma.Value / 100f).ToString("F2");
            lblAvBeamVal.Text = (trkAvBeam.Value / 100f).ToString("F2");
            lblAvBloomVal.Text = (trkAvBloom.Value / 100f).ToString("F2");
            lblAvBrightVal.Text = (trkAvBright.Value / 100f).ToString("F2");
            lblSvNoiseVal.Text = (trkSvNoise.Value / 100f).ToString("F2");
            lblSvSlewVal.Text = (trkSvSlew.Value / 100f).ToString("F2");
            lblSvChromaVal.Text = (trkSvChroma.Value / 100f).ToString("F2");
            lblSvBeamVal.Text = (trkSvBeam.Value / 100f).ToString("F2");
            lblSvBloomVal.Text = (trkSvBloom.Value / 100f).ToString("F2");
            lblSvBrightVal.Text = (trkSvBright.Value / 100f).ToString("F2");
        }

        static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
