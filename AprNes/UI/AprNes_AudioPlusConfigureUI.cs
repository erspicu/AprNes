using System;
using System.Drawing;
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

        // ── Channel Volume UI 控制項 (動態建立) ──
        const int NES_CH = 5;
        const int EXP_CH = 8;
        CheckBox[] chkNes = new CheckBox[NES_CH];
        Label[]    lblNes = new Label[NES_CH];
        TrackBar[] trkNes = new TrackBar[NES_CH];
        Label[]    lblNesVal = new Label[NES_CH];

        CheckBox[] chkExp = new CheckBox[EXP_CH];
        Label[]    lblExp = new Label[EXP_CH];
        TrackBar[] trkExp = new TrackBar[EXP_CH];
        Label[]    lblExpVal = new Label[EXP_CH];

        ComboBox   cboMapperChip;
        Label      lblNesHeader;           // "NES Channel" header
        Label      lblMapperChipHeader;    // "Mapper Sound Chip" header
        Label      lblNesChInfo;           // NES channel 說明 (生效模式)
        Label      lblExpChInfo;           // Expansion channel 說明 (生效模式 + mapper 編號)

        static readonly string[] NesChNames = { "Pulse 1", "Pulse 2", "Triangle", "Noise", "DMC" };

        // Per-chip channel names (index matches ExpansionChipType enum)
        static readonly string[][] ExpChNames = {
            new string[0],                                                  // 0: None
            new string[] { "VRC6 Pulse 1", "VRC6 Pulse 2", "VRC6 Saw" },   // 1: VRC6
            new string[] { "VRC7 FM" },                                     // 2: VRC7
            new string[] { "N163 Ch1", "N163 Ch2", "N163 Ch3", "N163 Ch4",
                           "N163 Ch5", "N163 Ch6", "N163 Ch7", "N163 Ch8" }, // 3: N163
            new string[] { "5B Ch A", "5B Ch B", "5B Ch C" },               // 4: 5B
            new string[] { "MMC5 Pulse 1", "MMC5 Pulse 2" },               // 5: MMC5
            new string[] { "FDS Wave" },                                    // 6: FDS
        };

        static readonly string[] ChipNames = { "(None)", "VRC6", "VRC7", "Namco 163", "Sunsoft 5B", "MMC5", "FDS" };

        // Per-chip INI key prefixes
        static readonly string[] ChipIniPrefix = { "", "VRC6", "VRC7", "N163", "S5B", "MMC5", "FDS" };

        // Per-chip mapper number descriptions
        static readonly string[] ChipMapperDesc = {
            "",                                              // 0: None
            "Mapper 024 (VRC6a), 026 (VRC6b)",               // 1: VRC6
            "Mapper 085 (VRC7)",                              // 2: VRC7
            "Mapper 019 (Namco 163)",                         // 3: N163
            "Mapper 069 (Sunsoft FME-7 / 5B)",               // 4: 5B
            "Mapper 005 (MMC5)",                              // 5: MMC5
            "FDS (Famicom Disk System)",                      // 6: FDS
        };

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

            BuildChannelVolumeUI();
            ApplyLang();
            LoadFromNesCore();
        }

        // ── Layout 常數 ──
        const int CV_ROW_H = 38;       // row spacing
        const int CV_TRK_H = 30;       // trackbar height (compact, no ticks)
        const int CV_Y0 = 48;          // first row Y
        const int CV_LBL_W = 95;       // channel name label width
        const int CV_TRK_W = 150;      // trackbar width
        const int CV_VAL_W = 45;       // value label width
        const int CV_EXP_X = 400;      // expansion column X base
        const int CV_GRP_Y = 581;      // GroupBox top Y (matches Designer)
        const int CV_BTN_PAD = 12;     // padding below GroupBox to buttons

        // ─────────────────────────────────────────────────────────
        // BuildChannelVolumeUI — 動態建立 Channel Volume 控制項
        // ─────────────────────────────────────────────────────────
        void BuildChannelVolumeUI()
        {
            // ── NES Channels (left column) ──
            int xBase = 10;
            lblNesHeader = new Label {
                Text = "NES Channel", AutoSize = true,
                Location = new Point(xBase, 22), Font = new Font(Font, FontStyle.Bold)
            };
            ChannelVol.Controls.Add(lblNesHeader);

            // NES channel info (生效模式)
            lblNesChInfo = new Label {
                Text = "Volume / Enable : All modes    |    70% = calibrated (1.0x)",
                AutoSize = false,
                Size = new Size(370, 16),
                Location = new Point(xBase, CV_Y0 + NES_CH * CV_ROW_H + 4),
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font.FontFamily, 8f)
            };
            ChannelVol.Controls.Add(lblNesChInfo);

            for (int i = 0; i < NES_CH; i++)
            {
                int y = CV_Y0 + i * CV_ROW_H;
                CreateChannelRow(xBase, y, i, true);
            }

            // ── Mapper Sound Chip header + ComboBox ──
            lblMapperChipHeader = new Label {
                Text = "Mapper Sound Chip", AutoSize = true,
                Location = new Point(CV_EXP_X, 22), Font = new Font(Font, FontStyle.Bold)
            };
            ChannelVol.Controls.Add(lblMapperChipHeader);

            cboMapperChip = new ComboBox {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(CV_EXP_X + 170, 18),
                Size = new Size(150, 26)
            };
            cboMapperChip.Items.AddRange(ChipNames);
            cboMapperChip.SelectedIndexChanged += cboMapperChip_Changed;
            ChannelVol.Controls.Add(cboMapperChip);

            // Expansion channel info (生效模式 + mapper 編號)
            lblExpChInfo = new Label {
                Text = "",
                AutoSize = false,
                Size = new Size(380, 32),
                Location = new Point(CV_EXP_X, CV_Y0),
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font.FontFamily, 8f)
            };
            ChannelVol.Controls.Add(lblExpChInfo);

            // ── Expansion Channels (single column, repositioned dynamically) ──
            for (int i = 0; i < EXP_CH; i++)
            {
                int y = CV_Y0 + i * CV_ROW_H;
                CreateChannelRow(CV_EXP_X, y, i, false);
            }
        }

        void CreateChannelRow(int xBase, int y, int idx, bool isNes)
        {
            var chk = new CheckBox {
                Checked = true, AutoSize = true,
                Location = new Point(xBase, y + 4), Text = ""
            };
            var lbl = new Label {
                Text = isNes ? NesChNames[idx] : "Ch " + (idx + 1),
                AutoSize = false,
                Size = new Size(CV_LBL_W, 18),
                Location = new Point(xBase + 20, y + 4)
            };
            var trk = new TrackBar {
                Minimum = 0, Maximum = 100, Value = 100,
                TickStyle = TickStyle.None,
                Size = new Size(CV_TRK_W, CV_TRK_H),
                Location = new Point(xBase + 20 + CV_LBL_W + 5, y),
                LargeChange = 10, SmallChange = 1
            };
            var val = new Label {
                Text = "100%", AutoSize = false,
                Size = new Size(CV_VAL_W, 18),
                Location = new Point(xBase + 20 + CV_LBL_W + 5 + CV_TRK_W + 5, y + 4),
                TextAlign = ContentAlignment.MiddleRight
            };

            if (isNes)
            {
                chkNes[idx] = chk; lblNes[idx] = lbl; trkNes[idx] = trk; lblNesVal[idx] = val;
                int ci = idx;
                trk.Scroll += (s, e) => { lblNesVal[ci].Text = trkNes[ci].Value + "%"; };
            }
            else
            {
                chkExp[idx] = chk; lblExp[idx] = lbl; trkExp[idx] = trk; lblExpVal[idx] = val;
                int ci = idx;
                trk.Scroll += (s, e) => { lblExpVal[ci].Text = trkExp[ci].Value + "%"; };
            }

            ChannelVol.Controls.AddRange(new Control[] { chk, lbl, trk, val });
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
            ChannelVol.Text        = L("ap_grp_channel");
            lblNesHeader.Text      = L("ap_nes_channel");
            lblMapperChipHeader.Text = L("ap_mapper_chip");
            lblNesChInfo.Text      = L("ap_nes_ch_info");
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

            // Channel Volume — NES channels
            for (int i = 0; i < NES_CH; i++)
            {
                trkNes[i].Value = Math.Max(0, Math.Min(100, NesCore.ChannelVolume[i]));
                chkNes[i].Checked = NesCore.ChannelEnabled[i];
                lblNesVal[i].Text = trkNes[i].Value + "%";
            }

            // Channel Volume — Expansion channels
            for (int i = 0; i < EXP_CH; i++)
            {
                trkExp[i].Value = Math.Max(0, Math.Min(100, NesCore.ChannelVolume[NES_CH + i]));
                chkExp[i].Checked = NesCore.ChannelEnabled[NES_CH + i];
                lblExpVal[i].Text = trkExp[i].Value + "%";
            }

            // Auto-select current game's mapper chip
            int ct = (int)NesCore.expansionChipType;
            cboMapperChip.SelectedIndex = (ct >= 0 && ct < ChipNames.Length) ? ct : 0;
            UpdateExpChannelVisibility();

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

            // Channel Volume — NES channels
            for (int i = 0; i < NES_CH; i++)
            {
                NesCore.ChannelVolume[i] = trkNes[i].Value;
                NesCore.ChannelEnabled[i] = chkNes[i].Checked;
            }

            // Channel Volume — Expansion channels
            for (int i = 0; i < EXP_CH; i++)
            {
                NesCore.ChannelVolume[NES_CH + i] = trkExp[i].Value;
                NesCore.ChannelEnabled[NES_CH + i] = chkExp[i].Checked;
            }
        }

        // ─────────────────────────────────────────────────────────
        // UpdateExpChannelVisibility — 根據 ComboBox 選擇顯示/隱藏擴展聲道
        //   並動態調整 GroupBox / Form / Button 的大小與位置
        // ─────────────────────────────────────────────────────────
        void UpdateExpChannelVisibility()
        {
            int chipIdx = cboMapperChip.SelectedIndex;
            string[] names = (chipIdx > 0 && chipIdx < ExpChNames.Length)
                ? ExpChNames[chipIdx] : new string[0];
            int visibleCount = names.Length;

            // Update expansion info label
            if (chipIdx > 0 && chipIdx < ChipMapperDesc.Length)
            {
                lblExpChInfo.Text = ChipMapperDesc[chipIdx] + "\r\n"
                    + L("ap_exp_ch_info");
                lblExpChInfo.Visible = true;
            }
            else
            {
                lblExpChInfo.Text = "";
                lblExpChInfo.Visible = false;
            }

            // Info label sits right below the ComboBox row
            int infoY = CV_Y0;
            int infoH = (chipIdx > 0) ? 32 : 0;
            lblExpChInfo.Location = new Point(CV_EXP_X, infoY);

            // Channel rows start after info label
            int chRowY0 = CV_Y0 + infoH + 4;

            // Reposition expansion channel rows (single column, vertical)
            for (int i = 0; i < EXP_CH; i++)
            {
                bool visible = (i < visibleCount);
                chkExp[i].Visible = visible;
                lblExp[i].Visible = visible;
                trkExp[i].Visible = visible;
                lblExpVal[i].Visible = visible;

                if (visible)
                {
                    lblExp[i].Text = names[i];
                    int y = chRowY0 + i * CV_ROW_H;
                    chkExp[i].Location  = new Point(CV_EXP_X, y + 4);
                    lblExp[i].Location  = new Point(CV_EXP_X + 20, y + 4);
                    trkExp[i].Location  = new Point(CV_EXP_X + 20 + CV_LBL_W + 5, y);
                    lblExpVal[i].Location = new Point(CV_EXP_X + 20 + CV_LBL_W + 5 + CV_TRK_W + 5, y + 4);
                }
            }

            // Resize GroupBox: height = max(NES info row, expansion rows) + header + padding
            int nesBottomY = CV_Y0 + NES_CH * CV_ROW_H + 20; // +20 for NES info label
            int expBottomY = chRowY0 + visibleCount * CV_ROW_H;
            int contentBottom = Math.Max(nesBottomY, expBottomY);
            int grpH = contentBottom + 15; // bottom padding

            ChannelVol.Size = new Size(ChannelVol.Width, grpH);

            // Resize form and reposition buttons
            int formH = CV_GRP_Y + grpH + CV_BTN_PAD + btnOK.Height + CV_BTN_PAD;
            this.ClientSize = new Size(this.ClientSize.Width, formH);
            btnOK.Location     = new Point(btnOK.Location.X, CV_GRP_Y + grpH + CV_BTN_PAD);
            btnCancel.Location = new Point(btnCancel.Location.X, CV_GRP_Y + grpH + CV_BTN_PAD);
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
        // Mapper Chip ComboBox 切換 → 更新擴展聲道顯示
        // ─────────────────────────────────────────────────────────
        void cboMapperChip_Changed(object sender, EventArgs e)
        {
            UpdateExpChannelVisibility();
        }

        // ─────────────────────────────────────────────────────────
        // OK — 儲存設定並關閉
        // ─────────────────────────────────────────────────────────
        void btnOK_Click(object sender, EventArgs e)
        {
            SaveToNesCore();

            // 寫入 AprNesAudioPlus.ini
            AprNesUI.GetInstance().SaveAudioPlusIniPublic();

            // 立即套用到音訊管線 (更新 gain + 重建管線)
            NesCore.mmix_UpdateChannelGains();
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
