using System;
using System.Windows.Forms;
using LangTool;

namespace AprNes.UI
{
    public partial class AprNes_AudioPlusConfigureUI : Form
    {
        // 安全讀取語系字串（key 不存在時 fallback 為 key 名稱本身）
        static string L(string key)
        {
            var tbl = LangINI.lang_table[AprNesUI.GetInstance().AppConfigure["Lang"]];
            return tbl.ContainsKey(key) ? tbl[key] : key;
        }

        public AprNes_AudioPlusConfigureUI()
        {
            InitializeComponent();

            // 填入 ComboBox 選項
            cboConsoleModel.Items.AddRange(new object[] {
                "0 - Famicom (HVC-001)  ~14kHz",
                "1 - Front-Loader (NES-001)  ~4.7kHz",
                "2 - Top-Loader (NES-101)  ~20kHz +buzz",
                "3 - AV Famicom (HVC-101)  ~19kHz",
                "4 - Sharp Twin Famicom  ~12kHz",
                "5 - Sharp Famicom Titler  ~16kHz",
                "6 - Custom",
            });

            cboBuzzFreq.Items.AddRange(new object[] { "60 Hz", "50 Hz" });

            ApplyLang();
            LoadFromNesCore();
        }

        // ─────────────────────────────────────────────────────────
        // ApplyLang — 套用多語化字串到所有 UI 控制項
        // ─────────────────────────────────────────────────────────
        void ApplyLang()
        {
            if (!LangINI.LangLoadOK) return;

            this.Text              = L("ap_title");
            grpAuthentic.Text      = L("ap_grp_authentic");
            grpModern.Text         = L("ap_grp_modern");
            lblConsoleModel.Text   = L("ap_console_model");
            chkRfCrosstalk.Text    = L("ap_rf_crosstalk");
            lblCustomCutoff.Text   = L("ap_lpf_cutoff");
            chkCustomBuzz.Text     = L("ap_custom_buzz");
            lblBuzzFreq.Text       = L("ap_buzz_freq");
            lblBuzzAmp.Text        = L("ap_buzz_amp");
            lblRfVol.Text          = L("ap_rf_volume");
            lblStereoWidth.Text    = L("ap_stereo_width");
            lblHaasDelay.Text      = L("ap_haas_delay");
            lblHaasCrossfeed.Text  = L("ap_haas_crossfeed");
            lblReverbWet.Text      = L("ap_reverb_wet");
            lblCombFeedback.Text   = L("ap_reverb_length");
            lblCombDamp.Text       = L("ap_reverb_damping");
            lblBassDb.Text         = L("ap_bass_boost");
            lblBassFreq.Text       = L("ap_bass_freq");
            btnOK.Text             = L("ap_ok");
            btnCancel.Text         = L("ap_cancel");
        }

        // ─────────────────────────────────────────────────────────
        // LoadFromNesCore — 從 NesCore 靜態欄位讀取設定到 UI 控制項
        // ─────────────────────────────────────────────────────────
        void LoadFromNesCore()
        {
            // Authentic
            cboConsoleModel.SelectedIndex = Math.Max(0, Math.Min(6, NesCore.ConsoleModel));
            chkRfCrosstalk.Checked = NesCore.RfCrosstalk;
            trkCustomCutoff.Value = Math.Max(1000, Math.Min(22000, NesCore.CustomLpfCutoff));
            chkCustomBuzz.Checked = NesCore.CustomBuzz;
            trkBuzzAmp.Value = Math.Max(0, Math.Min(100, NesCore.BuzzAmplitude));
            cboBuzzFreq.SelectedIndex = (NesCore.BuzzFreq == 50) ? 1 : 0;
            trkRfVol.Value = Math.Max(0, Math.Min(200, NesCore.RfVolume));

            // Modern
            trkStereoWidth.Value = Math.Max(0, Math.Min(100, NesCore.StereoWidth));
            trkHaasDelay.Value = Math.Max(10, Math.Min(30, NesCore.HaasDelay));
            trkHaasCrossfeed.Value = Math.Max(0, Math.Min(80, NesCore.HaasCrossfeed));
            trkReverbWet.Value = Math.Max(0, Math.Min(30, NesCore.ReverbWet));
            trkCombFeedback.Value = Math.Max(30, Math.Min(90, NesCore.CombFeedback));
            trkCombDamp.Value = Math.Max(10, Math.Min(70, NesCore.CombDamp));
            trkBassDb.Value = Math.Max(0, Math.Min(12, NesCore.BassBoostDb));
            trkBassFreq.Value = Math.Max(80, Math.Min(300, NesCore.BassBoostFreq));

            UpdateCustomEnableState();
            UpdateAllValueLabels(null, EventArgs.Empty);
        }

        // ─────────────────────────────────────────────────────────
        // SaveToNesCore — 將 UI 控制項的值寫回 NesCore 靜態欄位
        // ─────────────────────────────────────────────────────────
        void SaveToNesCore()
        {
            // Authentic
            NesCore.ConsoleModel = cboConsoleModel.SelectedIndex;
            NesCore.RfCrosstalk = chkRfCrosstalk.Checked;
            NesCore.CustomLpfCutoff = trkCustomCutoff.Value;
            NesCore.CustomBuzz = chkCustomBuzz.Checked;
            NesCore.BuzzAmplitude = trkBuzzAmp.Value;
            NesCore.BuzzFreq = (cboBuzzFreq.SelectedIndex == 1) ? 50 : 60;
            NesCore.RfVolume = trkRfVol.Value;

            // Modern
            NesCore.StereoWidth = trkStereoWidth.Value;
            NesCore.HaasDelay = trkHaasDelay.Value;
            NesCore.HaasCrossfeed = trkHaasCrossfeed.Value;
            NesCore.ReverbWet = trkReverbWet.Value;
            NesCore.CombFeedback = trkCombFeedback.Value;
            NesCore.CombDamp = trkCombDamp.Value;
            NesCore.BassBoostDb = trkBassDb.Value;
            NesCore.BassBoostFreq = trkBassFreq.Value;
        }

        // ─────────────────────────────────────────────────────────
        // UpdateCustomEnableState — Custom 模式專用控制項的啟用/停用
        // ─────────────────────────────────────────────────────────
        void UpdateCustomEnableState()
        {
            bool isCustom = (cboConsoleModel.SelectedIndex == 6);
            lblCustomCutoff.Enabled = isCustom;
            trkCustomCutoff.Enabled = isCustom;
            lblCustomCutoffVal.Enabled = isCustom;
            chkCustomBuzz.Enabled = isCustom;
        }

        // ─────────────────────────────────────────────────────────
        // UpdateAllValueLabels — 更新所有 TrackBar 旁邊的數值顯示
        // ─────────────────────────────────────────────────────────
        void UpdateAllValueLabels(object sender, EventArgs e)
        {
            lblCustomCutoffVal.Text = trkCustomCutoff.Value + " Hz";
            lblBuzzAmpVal.Text = trkBuzzAmp.Value + "%";
            lblRfVolVal.Text = trkRfVol.Value.ToString();
            lblStereoWidthVal.Text = trkStereoWidth.Value + "%";
            lblHaasDelayVal.Text = trkHaasDelay.Value + " ms";
            lblHaasCrossfeedVal.Text = trkHaasCrossfeed.Value + "%";
            lblReverbWetVal.Text = trkReverbWet.Value + "%";
            lblCombFeedbackVal.Text = trkCombFeedback.Value + "%";
            lblCombDampVal.Text = trkCombDamp.Value + "%";
            lblBassDbVal.Text = (trkBassDb.Value == 0) ? "Off" : "+" + trkBassDb.Value + " dB";
            lblBassFreqVal.Text = trkBassFreq.Value + " Hz";
        }

        // ─────────────────────────────────────────────────────────
        // ConsoleModel 切換 → 更新 Custom 控制項啟用狀態
        // ─────────────────────────────────────────────────────────
        void cboConsoleModel_Changed(object sender, EventArgs e)
        {
            UpdateCustomEnableState();
        }

        // ─────────────────────────────────────────────────────────
        // OK — 儲存設定並關閉
        // ─────────────────────────────────────────────────────────
        void btnOK_Click(object sender, EventArgs e)
        {
            SaveToNesCore();

            // 寫入 AprNesAudioPlus.ini
            AprNesUI.GetInstance().SaveAudioPlusIniPublic();

            // 立即套用到音訊管線
            NesCore.AudioPlus_ApplySettings();

            DialogResult = DialogResult.OK;
            Close();
        }

        // ─────────────────────────────────────────────────────────
        // Cancel — 不儲存，直接關閉
        // ─────────────────────────────────────────────────────────
        void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
