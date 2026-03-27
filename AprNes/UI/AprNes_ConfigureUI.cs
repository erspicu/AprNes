using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using LangTool;


namespace AprNes
{
    public partial class AprNes_ConfigureUI : Form
    {
        public AprNes_ConfigureUI()
        {
            InitializeComponent();
            RegisterJoyActivation();
            RegisterP2KeyboardEvents();
            init();
        }

        void RegisterP2KeyboardEvents()
        {
            // Wire P2 keyboard textboxes (textBox9-16) to same handlers as P1 keyboard
            foreach (TextBox tb in new[] { textBox9, textBox10, textBox11, textBox12,
                                           textBox13, textBox14, textBox15, textBox16 })
            {
                tb.KeyUp += textBox_KeyConfig_KeyUp;
                tb.MouseClick += textBox_KeyConfig_MouseClick;
                tb.Leave += textBox_A_Leave;
                tb.ReadOnly = true;
            }
        }

        // 安全讀取語系字串（key 不存在時 fallback 為 key 名稱本身）
        static string LangStr(string key)
        {
            var tbl = LangINI.lang_table[AprNesUI.GetInstance().AppConfigure["Lang"]];
            return tbl.ContainsKey(key) ? tbl[key] : key;
        }

        // ── Resize filter combobox items ──────────────────────────────────
        // Display text → INI value mapping
        // Stage 1: all filters (xBRZ works on fixed 256×240 NES input)
        static readonly string[][] filterItemsS1 = {
            new[] { "無",          "none" },
            new[] { "xBRZ 2x",    "xbrz_2" },
            new[] { "xBRZ 3x",    "xbrz_3" },
            new[] { "xBRZ 4x",    "xbrz_4" },
            new[] { "xBRZ 5x",    "xbrz_5" },
            new[] { "xBRZ 6x",    "xbrz_6" },
            new[] { "ScaleX 2x",  "scalex_2" },
            new[] { "ScaleX 3x",  "scalex_3" },
            new[] { "NN 2x",      "nn_2" },
            new[] { "NN 3x",      "nn_3" },
            new[] { "NN 4x",      "nn_4" },
        };
        // Stage 2: no xBRZ (internal buffers hardcoded to 256×240)
        static readonly string[][] filterItemsS2 = {
            new[] { "無",          "none" },
            new[] { "ScaleX 2x",  "scalex_2" },
            new[] { "ScaleX 3x",  "scalex_3" },
            new[] { "NN 2x",      "nn_2" },
            new[] { "NN 3x",      "nn_3" },
            new[] { "NN 4x",      "nn_4" },
        };

        static int GetFilterScale(string iniVal)
        {
            if (iniVal == "none") return 1;
            string[] parts = iniVal.Split('_');
            return parts.Length == 2 ? int.Parse(parts[1]) : 1;
        }

        void InitResizeComboBoxes()
        {
            sizelevel1.SelectedIndexChanged -= sizelevel_Changed;
            sizelevel2.SelectedIndexChanged -= sizelevel_Changed;

            sizelevel1.Items.Clear();
            sizelevel2.Items.Clear();
            foreach (var item in filterItemsS1)
                sizelevel1.Items.Add(item[0]);
            foreach (var item in filterItemsS2)
                sizelevel2.Items.Add(item[0]);

            // Restore from INI
            string s1 = AprNesUI.GetInstance().AppConfigure.ContainsKey("ResizeStage1")
                       ? AprNesUI.GetInstance().AppConfigure["ResizeStage1"] : "none";
            string s2 = AprNesUI.GetInstance().AppConfigure.ContainsKey("ResizeStage2")
                       ? AprNesUI.GetInstance().AppConfigure["ResizeStage2"] : "none";

            sizelevel1.SelectedIndex = FindFilterIndex(filterItemsS1, s1);
            sizelevel2.SelectedIndex = FindFilterIndex(filterItemsS2, s2);

            bool s1None = (sizelevel1.SelectedIndex == 0);
            sizelevel2.Enabled = !s1None;
            if (s1None) sizelevel2.SelectedIndex = 0;

            UpdateResolutionLabel();

            sizelevel1.SelectedIndexChanged += sizelevel_Changed;
            sizelevel2.SelectedIndexChanged += sizelevel_Changed;
        }

        int FindFilterIndex(string[][] items, string iniVal)
        {
            for (int i = 0; i < items.Length; i++)
                if (items[i][1] == iniVal) return i;
            return 0; // default to "無"
        }

        void sizelevel_Changed(object sender, EventArgs e)
        {
            if (sender == sizelevel1)
            {
                bool s1None = (sizelevel1.SelectedIndex == 0);
                sizelevel2.Enabled = !s1None;
                if (s1None) sizelevel2.SelectedIndex = 0;
            }
            UpdateResolutionLabel();
        }

        void UpdateResolutionLabel()
        {
            int s1 = GetFilterScale(filterItemsS1[sizelevel1.SelectedIndex][1]);
            int s2 = GetFilterScale(filterItemsS2[Math.Max(0, sizelevel2.SelectedIndex)][1]);
            int w = 256 * s1 * s2;
            int h = 240 * s1 * s2;
            label19.Text = w + " × " + h;
        }

        public void init()
        {

            if (!LangINI.LangLoadOK) return;

            Ok_btn.Text      = LangStr("ok");
            this.Text        = LangStr("setting");
            choose_dir.Text  = LangStr("selectfolder");
            groupBox1.Text   = LangStr("keypad");
            groupBox2.Text   = LangStr("joypad");
            LimitFPS_checkBox.Text = LangStr("limitfps");
            perdotFSM.Text   = LangStr("perdotFSM");
            label18.Text     = LangStr("langchoose");
            label9.Text      = "Shift + p " + LangStr("capture_path");

            // 畫面輸出
            resize.Text      = LangStr("resize_group");
            sizel1lab.Text   = LangStr("resize_stage1");
            sizel2lab.Text   = LangStr("resize_stage2");
            saneline.Text    = LangStr("resize_scanline");

            // 音效
            groupBox5.Text      = LangStr("sound_group");
            UpdateSoundUI(); // 套用語系至 SoundcheckBox.Text

            // AudioMode ComboBox 初始化
            AudioMode.Items.Clear();
            AudioMode.Items.AddRange(new object[] {
                LangStr("audio_mode_pure"),
                LangStr("audio_mode_authentic"),
                LangStr("audio_mode_modern")
            });
            AudioMode.SelectedIndex = Math.Max(0, Math.Min(2, NesCore.AudioMode));
            AudioModeLab.Text = LangStr("audio_mode");
            AudioAdvanceSetting.Text = LangStr("audio_advance_setting");
            AudioAdvanceSetting.Click -= AudioAdvanceSetting_Click;
            AudioAdvanceSetting.Click += AudioAdvanceSetting_Click;

            // Analog 相關控制項語系
            groupBoxAnalog.Text = LangStr("analog_group");
            useAnalog.Text      = LangStr("analog_mode");
            ultraAnalog.Text    = LangStr("ultra_analog");
            crtuse.Text         = LangStr("crt_effect");
            VideoInputLab.Text  = LangStr("video_input");
            label_analogSize.Text = LangStr("analog_size");
            AnalogSetting.Text   = LangStr("analog_advance_setting");

            // 同步 Analog Size 選擇（index 0=2x,1=4x,2=6x,3=8x）
            int[] analogSizeMap = { 2, 4, 6, 8 };
            int analogIdx = System.Array.IndexOf(analogSizeMap, NesCore.AnalogSize);
            if (analogIdx < 0) analogIdx = 1; // 預設 4x (index=1)
            comboBox_analogSize.SelectedIndex = analogIdx;

            comboBox1.SelectedIndexChanged -= comboBox1_LangChanged;
            comboBox1.Items.Clear();

            int ch = 0;
            foreach (string i in LangINI.lang_map.Keys)
            {
                comboBox1.Items.Add(i + " " + LangINI.lang_map[i]);
                if (i == AprNesUI.GetInstance().AppConfigure["Lang"])
                    comboBox1.SelectedIndex = ch;
                ch++;
            }

            comboBox1.SelectedIndexChanged += comboBox1_LangChanged;
        }

        // ─────────────────────────────────────────────────────────
        // 語系選擇立即生效 — 切換後即時重新套用所有 UI 字串
        // ─────────────────────────────────────────────────────────
        bool _langChanging;
        void comboBox1_LangChanged(object sender, EventArgs e)
        {
            if (_langChanging || comboBox1.SelectedItem == null) return;
            _langChanging = true;
            string newLang = (comboBox1.SelectedItem as string).Split(new char[] { ' ' })[0];
            AprNesUI.GetInstance().AppConfigure["Lang"] = newLang;
            AprNesUI.GetInstance().initUILang();
            init();
            _langChanging = false;
        }

        protected static AprNes_ConfigureUI instance;
        public static AprNes_ConfigureUI GetInstance()
        {
            if (instance == null || instance.IsDisposed)
                instance = new AprNes_ConfigureUI();
            return instance;
        }

        // Track which joypad TextBox the user last clicked/entered, instead of relying on .Focused
        // (which is unreliable across threads / modal dialog Invoke).
        TextBox _activeJoyControl = null;
        void RegisterJoyActivation()
        {
            // P1 joypad
            foreach (TextBox tb in new[] { joypad_A, joypad_B, joypad_START, joypad_SELECT,
                                           joypad_UP, joypad_DOWN, joypad_LEFT, joypad_RIGHT })
            {
                TextBox captured = tb;
                captured.Click += (s, e) => _activeJoyControl = captured;
                captured.Enter += (s, e) => _activeJoyControl = captured;
            }
            // P2 joypad (textBox1-8)
            foreach (TextBox tb in new[] { textBox8, textBox7, textBox6, textBox5,
                                           textBox4, textBox3, textBox2, textBox1 })
            {
                TextBox captured = tb;
                captured.Click += (s, e) => _activeJoyControl = captured;
                captured.Enter += (s, e) => _activeJoyControl = captured;
            }
        }

        // Analog 模式切換：決定哪些 UI 啟用 / 停用
        void UpdateAnalogEnableState()
        {
            bool analogOn = useAnalog.Checked;
            bool ultraOn  = analogOn && ultraAnalog.Checked;

            // Analog 開啟時，停用畫面輸出設定
            resize.Enabled  = !analogOn;
            saneline.Enabled = !analogOn;

            // Analog 子選項只在 AnalogMode 啟用時才可操作
            ultraAnalog.Enabled         = analogOn;
            VideoInput.Enabled          = analogOn;
            VideoInputLab.Enabled       = analogOn;
            label_analogSize.Enabled    = analogOn;
            comboBox_analogSize.Enabled = analogOn;

            // CRT 效果需要 UltraAnalog 才有效
            crtuse.Enabled = ultraOn;
        }

        private void AnalogSetting_Click(object sender, EventArgs e)
        {
            using (var dlg = new UI.AprNes_AnalogConfigureUI())
            {
                dlg.ShowDialog(this);
            }
        }

        private void AudioAdvanceSetting_Click(object sender, EventArgs e)
        {
            using (var dlg = new UI.AprNes_AudioPlusConfigureUI())
            {
                dlg.ShowDialog(this);
            }
        }

        private void useAnalog_CheckedChanged(object sender, EventArgs e)
        {
            UpdateAnalogEnableState();
        }

        private void ultraAnalog_CheckedChanged(object sender, EventArgs e)
        {
            UpdateAnalogEnableState();
        }

        void UpdateSoundUI()
        {
            SoundtrackBar.Enabled = SoundcheckBox.Checked;
            string prefix = LangStr("sound_prefix");   // "音效 - " / "Sound - "
            string off    = LangStr("sound_off");       // "關閉" / "OFF"
            SoundcheckBox.Text = prefix + (SoundcheckBox.Checked ? SoundtrackBar.Value + "%" : off);
        }

        private void SoundcheckBox_CheckedChanged(object sender, EventArgs e)
        {
            UpdateSoundUI();
        }

        private void SoundtrackBar_Scroll(object sender, EventArgs e)
        {
            UpdateSoundUI();
        }

        public void BeforClose()
        {
            // ── 先暫停模擬執行緒，再修改任何 NesCore 欄位 ──────────────────
            // 防止 CrtScreen.Render() 在欄位已改、緩衝區未重建時存取越界記憶體
            bool isRunning = AprNesUI.GetInstance().IsRunning;
            if (isRunning)
            {
                NesCore._event.Reset();
                // 等待模擬執行緒完成當前整幀並阻塞於 _event.WaitOne()
                while (!NesCore.emuWaiting) System.Threading.Thread.Sleep(1);
            }

            // ── Resize stage settings ──────────────────────────────────────
            AprNesUI.GetInstance().AppConfigure["ResizeStage1"] = filterItemsS1[sizelevel1.SelectedIndex][1];
            AprNesUI.GetInstance().AppConfigure["ResizeStage2"] = filterItemsS2[Math.Max(0, sizelevel2.SelectedIndex)][1];
            AprNesUI.GetInstance().AppConfigure["Scanline"] = saneline.Checked ? "1" : "0";

            // Compute ScreenSize from stage multipliers (for initUIsize)
            int s1scale = GetFilterScale(filterItemsS1[sizelevel1.SelectedIndex][1]);
            int s2scale = GetFilterScale(filterItemsS2[Math.Max(0, sizelevel2.SelectedIndex)][1]);
            AprNesUI.GetInstance().AppConfigure["ScreenSize"] = (s1scale * s2scale).ToString();
            AprNesUI.GetInstance().AppConfigure["filter"] = "resize";
            // ── Analog 相關設定寫回 AppConfigure ──────────────────────────────
            bool prevAnalogEnabled = NesCore.AnalogEnabled;

            AprNesUI.GetInstance().AppConfigure["AnalogMode"]   = useAnalog.Checked ? "1" : "0";
            AprNesUI.GetInstance().AppConfigure["UltraAnalog"]  = ultraAnalog.Checked ? "1" : "0";
            AprNesUI.GetInstance().AppConfigure["crt"]          = crtuse.Checked ? "1" : "0";
            // Configure_Write() 從 NesCore.CrtEnabled 讀值寫入 ini，必須先同步
            NesCore.CrtEnabled = crtuse.Checked;

            switch (VideoInput.SelectedIndex)
            {
                case 0: AprNesUI.GetInstance().AppConfigure["AnalogOutput"] = "RF";     break;
                case 1: AprNesUI.GetInstance().AppConfigure["AnalogOutput"] = "SVideo"; break;
                default: AprNesUI.GetInstance().AppConfigure["AnalogOutput"] = "AV";    break;
            }

            // Analog Size：comboBox index 0=2x,1=4x,2=6x,3=8x
            int[] analogSizeValues = { 2, 4, 6, 8 };
            int selIdx = comboBox_analogSize.SelectedIndex;
            NesCore.AnalogSize = (selIdx >= 0 && selIdx < analogSizeValues.Length) ? analogSizeValues[selIdx] : 4;
            AprNesUI.GetInstance().AppConfigure["AnalogSize"] = NesCore.AnalogSize.ToString();

            AprNesUI.GetInstance().NES_KeyMAP_joypad.Clear();

            foreach (string key in NES_KeyMAP_joypad_config.Keys)
                AprNesUI.GetInstance().NES_KeyMAP_joypad[key] = NES_KeyMAP_joypad_config[key];

            AprNesUI.GetInstance().AppConfigure["LimitFPS"] = "0";
            if (LimitFPS_checkBox.Checked) AprNesUI.GetInstance().AppConfigure["LimitFPS"] = "1";

            // 音效設定寫入並立即生效
            NesCore.AudioEnabled = SoundcheckBox.Checked;
            NesCore.Volume = SoundtrackBar.Value;
            NesCore.AudioMode = AudioMode.SelectedIndex;

            // Accuracy 設定寫入並立即生效
            NesCore.AccuracyOptA = perdotFSM.Checked;

            AprNesUI.GetInstance().AppConfigure["CaptureScreenPath"] = screen_path.Text;
            AprNesUI.GetInstance().key_A = key_A;
            AprNesUI.GetInstance().key_B = key_B;
            AprNesUI.GetInstance().key_SELECT = key_SELECT;
            AprNesUI.GetInstance().key_START = key_START;
            AprNesUI.GetInstance().key_RIGHT = key_RIGHT;
            AprNesUI.GetInstance().key_LEFT = key_LEFT;
            AprNesUI.GetInstance().key_UP = key_UP;
            AprNesUI.GetInstance().key_DOWN = key_DOWN;
            // P2 keyboard
            AprNesUI.GetInstance().key_P2_A = key_P2_A;
            AprNesUI.GetInstance().key_P2_B = key_P2_B;
            AprNesUI.GetInstance().key_P2_SELECT = key_P2_SELECT;
            AprNesUI.GetInstance().key_P2_START = key_P2_START;
            AprNesUI.GetInstance().key_P2_RIGHT = key_P2_RIGHT;
            AprNesUI.GetInstance().key_P2_LEFT = key_P2_LEFT;
            AprNesUI.GetInstance().key_P2_UP = key_P2_UP;
            AprNesUI.GetInstance().key_P2_DOWN = key_P2_DOWN;
            AprNesUI.GetInstance().AppConfigure["Lang"] = (comboBox1.SelectedItem as string).Split(new char[] { ' ' })[0];
            AprNesUI.GetInstance().Configure_Write();

            AprNesUI.GetInstance().LoadConfig();
            AprNesUI.GetInstance().initUILang();

            bool newAnalogEnabled = NesCore.AnalogEnabled; // LoadConfig 已更新

            // 全螢幕中切換 AnalogMode 時，需安全過渡（退出→套用→重新進入）
            if (AprNesUI.GetInstance().IsInFullScreen && prevAnalogEnabled != newAnalogEnabled)
            {
                AprNesUI.GetInstance().FullScreenModeTransition(prevAnalogEnabled);
            }
            else
            {
                AprNesUI.GetInstance().initUIsize();
                AprNesUI.GetInstance().ApplyRenderSettings();
            }

            // AnalogMode 切換（OFF→ON 或 ON→OFF）時，若 AnalogScreenBuf 狀態與新設定不符
            // 則主動補齊：OFF→ON 已由 ApplyRenderSettings 處理；ON→OFF 需手動釋放緩衝區
            if (prevAnalogEnabled && !newAnalogEnabled)
            {
                unsafe
                {
                    if (NesCore.AnalogScreenBuf != null)
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)NesCore.AnalogScreenBuf);
                        NesCore.AnalogScreenBuf = null;
                        NesCore.AnalogBufSize   = 0;
                    }
                }
            }

            // Sync WaveOutPlayer to the new AudioEnabled state
            WaveOutPlayer.CloseAudio();
            if (NesCore.AudioEnabled && AprNesUI.GetInstance().IsRunning)
                WaveOutPlayer.OpenAudio();
        }

        private void OK(object sender, EventArgs e)
        {
            BeforClose();
            Close();
        }

        // Returns the event_type the currently active joypad control expects:
        //   1  = button only (A/B/Start/Select)
        //   2  = axis OR button (UP/DOWN/LEFT/RIGHT — D-pad may fire as either)
        //  -1  = nothing active
        public int ExpectedJoyInputType()
        {
            // P1 joypad
            if (_activeJoyControl == joypad_A || _activeJoyControl == joypad_B ||
                _activeJoyControl == joypad_START || _activeJoyControl == joypad_SELECT)
                return 1;
            if (_activeJoyControl == joypad_UP || _activeJoyControl == joypad_DOWN ||
                _activeJoyControl == joypad_LEFT || _activeJoyControl == joypad_RIGHT)
                return 2;
            // P2 joypad: textBox8=A, textBox7=B, textBox6=SELECT, textBox5=START
            if (_activeJoyControl == textBox8 || _activeJoyControl == textBox7 ||
                _activeJoyControl == textBox5 || _activeJoyControl == textBox6)
                return 1;
            // P2 joypad: textBox4=UP, textBox3=DOWN, textBox2=LEFT, textBox1=RIGHT
            if (_activeJoyControl == textBox4 || _activeJoyControl == textBox3 ||
                _activeJoyControl == textBox2 || _activeJoyControl == textBox1)
                return 2;
            return -1;
        }

        public void Setup_JoyPad_define(string uid, string btn_name, int raw_id, int value)
        {
            TextBox target = _activeJoyControl;
            if (target == null) return;

            if (target == joypad_A)
            {
                if (value != 128) return;
                if (!btn_name.StartsWith("Button"))
                {
                    MessageBox.Show("非Button類型輸入!");
                    return;
                }
                joypad_A.Text = btn_name;

                if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_A))
                {
                    string key = NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_A).Key;
                    NES_KeyMAP_joypad_config.Remove(key);
                }

                NES_KeyMAP_joypad_config[uid + "," + btn_name + "," + raw_id] = AprNesUI.KeyMap.NES_btn_A;

            }
            else if (target == joypad_B)
            {
                if (!btn_name.StartsWith("Button"))
                {
                    MessageBox.Show("非Button類型輸入!");
                    return;
                }
                if (value != 128) return;
                joypad_B.Text = btn_name;

                if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_B))
                {
                    string key = NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_B).Key;
                    NES_KeyMAP_joypad_config.Remove(key);
                }

                NES_KeyMAP_joypad_config[uid + "," + btn_name + "," + raw_id] = AprNesUI.KeyMap.NES_btn_B;
            }
            else if (target == joypad_START)
            {
                if (!btn_name.StartsWith("Button"))
                {
                    MessageBox.Show("非Button類型輸入!");
                    return;
                }
                if (value != 128) return;
                joypad_START.Text = btn_name;

                if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_START))
                {
                    string key = NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_START).Key;
                    NES_KeyMAP_joypad_config.Remove(key);
                }

                NES_KeyMAP_joypad_config[uid + "," + btn_name + "," + raw_id] = AprNesUI.KeyMap.NES_btn_START;
            }
            else if (target == joypad_SELECT)
            {

                if (!btn_name.StartsWith("Button"))
                {
                    MessageBox.Show("非Button類型輸入!");
                    return;
                }
                if (value != 128) return;
                joypad_SELECT.Text = btn_name;

                if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_SELECT))
                {
                    string key = NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_SELECT).Key;
                    NES_KeyMAP_joypad_config.Remove(key);
                }

                NES_KeyMAP_joypad_config[uid + "," + btn_name + "," + raw_id] = AprNesUI.KeyMap.NES_btn_SELECT;
            }
            else if (target == joypad_UP)
            {
                if (btn_name.StartsWith("Button"))
                {
                    if (value == 0) return; // button release, ignore
                    joypad_UP.Text = btn_name;
                    if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_UP))
                        NES_KeyMAP_joypad_config.Remove(NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_UP).Key);
                    NES_KeyMAP_joypad_config[uid + "," + btn_name + "," + raw_id] = AprNesUI.KeyMap.NES_btn_UP;
                    return;
                }
                if (!btn_name.StartsWith("X") && !btn_name.StartsWith("Y")) return;
                if (value == 32511 || value == 32767) return;
                string name = JoyPadWayName(btn_name, value);
                if (name == "") return;
                joypad_UP.Text = name;
                if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_UP))
                    NES_KeyMAP_joypad_config.Remove(NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_UP).Key);
                NES_KeyMAP_joypad_config[uid + "," + joypad_UP.Text + "," + raw_id + "," + value] = AprNesUI.KeyMap.NES_btn_UP;
            }
            else if (target == joypad_DOWN)
            {
                if (btn_name.StartsWith("Button"))
                {
                    if (value == 0) return;
                    joypad_DOWN.Text = btn_name;
                    if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_DOWN))
                        NES_KeyMAP_joypad_config.Remove(NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_DOWN).Key);
                    NES_KeyMAP_joypad_config[uid + "," + btn_name + "," + raw_id] = AprNesUI.KeyMap.NES_btn_DOWN;
                    return;
                }
                if (!btn_name.StartsWith("X") && !btn_name.StartsWith("Y")) return;
                if (value == 32511 || value == 32767) return;
                string name = JoyPadWayName(btn_name, value);
                if (name == "") return;
                joypad_DOWN.Text = name;
                if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_DOWN))
                    NES_KeyMAP_joypad_config.Remove(NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_DOWN).Key);
                NES_KeyMAP_joypad_config[uid + "," + joypad_DOWN.Text + "," + raw_id + "," + value] = AprNesUI.KeyMap.NES_btn_DOWN;
            }
            else if (target == joypad_LEFT)
            {
                if (btn_name.StartsWith("Button"))
                {
                    if (value == 0) return;
                    joypad_LEFT.Text = btn_name;
                    if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_LEFT))
                        NES_KeyMAP_joypad_config.Remove(NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_LEFT).Key);
                    NES_KeyMAP_joypad_config[uid + "," + btn_name + "," + raw_id] = AprNesUI.KeyMap.NES_btn_LEFT;
                    return;
                }
                if (!btn_name.StartsWith("X") && !btn_name.StartsWith("Y")) return;
                if (value == 32511 || value == 32767) return;
                string name = JoyPadWayName(btn_name, value);
                if (name == "") return;
                joypad_LEFT.Text = name;
                if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_LEFT))
                    NES_KeyMAP_joypad_config.Remove(NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_LEFT).Key);
                NES_KeyMAP_joypad_config[uid + "," + joypad_LEFT.Text + "," + raw_id + "," + value] = AprNesUI.KeyMap.NES_btn_LEFT;
            }
            else if (target == joypad_RIGHT)
            {
                if (btn_name.StartsWith("Button"))
                {
                    if (value == 0) return;
                    joypad_RIGHT.Text = btn_name;
                    if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_RIGHT))
                        NES_KeyMAP_joypad_config.Remove(NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_RIGHT).Key);
                    NES_KeyMAP_joypad_config[uid + "," + btn_name + "," + raw_id] = AprNesUI.KeyMap.NES_btn_RIGHT;
                    return;
                }
                if (!btn_name.StartsWith("X") && !btn_name.StartsWith("Y")) return;
                if (value == 32511 || value == 32767) return;
                string name = JoyPadWayName(btn_name, value);
                if (name == "") return;
                joypad_RIGHT.Text = name;
                if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_RIGHT))
                    NES_KeyMAP_joypad_config.Remove(NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_RIGHT).Key);
                NES_KeyMAP_joypad_config[uid + "," + joypad_RIGHT.Text + "," + raw_id + "," + value] = AprNesUI.KeyMap.NES_btn_RIGHT;
            }
            // ── P2 Joypad controls ──────────────────────────────────────────
            else
            {
                SetupP2JoyPad(target, uid, btn_name, raw_id, value);
            }
        }

        // P2 joypad configuration — maps textBox1-8 to P2 NES buttons
        void SetupP2JoyPad(TextBox target, string uid, string btn_name, int raw_id, int value)
        {
            // Button controls: textBox8=A, textBox7=B, textBox6=SELECT, textBox5=START
            TextBox[] p2BtnControls = { textBox8, textBox7, textBox6, textBox5 };
            AprNesUI.KeyMap[] p2BtnMaps = { AprNesUI.KeyMap.NES_btn_P2_A, AprNesUI.KeyMap.NES_btn_P2_B,
                                            AprNesUI.KeyMap.NES_btn_P2_SELECT, AprNesUI.KeyMap.NES_btn_P2_START };
            for (int i = 0; i < p2BtnControls.Length; i++)
            {
                if (target == p2BtnControls[i])
                {
                    if (!btn_name.StartsWith("Button")) { MessageBox.Show("非Button類型輸入!"); return; }
                    if (value != 128) return;
                    target.Text = btn_name;
                    if (NES_KeyMAP_joypad_config.Values.Contains(p2BtnMaps[i]))
                        NES_KeyMAP_joypad_config.Remove(NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == p2BtnMaps[i]).Key);
                    NES_KeyMAP_joypad_config[uid + "," + btn_name + "," + raw_id] = p2BtnMaps[i];
                    return;
                }
            }

            // Direction controls: textBox4=UP, textBox3=DOWN, textBox2=LEFT, textBox1=RIGHT
            TextBox[] p2DirControls = { textBox4, textBox3, textBox2, textBox1 };
            AprNesUI.KeyMap[] p2DirMaps = { AprNesUI.KeyMap.NES_btn_P2_UP, AprNesUI.KeyMap.NES_btn_P2_DOWN,
                                            AprNesUI.KeyMap.NES_btn_P2_LEFT, AprNesUI.KeyMap.NES_btn_P2_RIGHT };
            for (int i = 0; i < p2DirControls.Length; i++)
            {
                if (target == p2DirControls[i])
                {
                    if (btn_name.StartsWith("Button"))
                    {
                        if (value == 0) return;
                        target.Text = btn_name;
                        if (NES_KeyMAP_joypad_config.Values.Contains(p2DirMaps[i]))
                            NES_KeyMAP_joypad_config.Remove(NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == p2DirMaps[i]).Key);
                        NES_KeyMAP_joypad_config[uid + "," + btn_name + "," + raw_id] = p2DirMaps[i];
                        return;
                    }
                    if (!btn_name.StartsWith("X") && !btn_name.StartsWith("Y")) return;
                    if (value == 32511 || value == 32767) return;
                    string name = JoyPadWayName(btn_name, value);
                    if (name == "") return;
                    target.Text = name;
                    if (NES_KeyMAP_joypad_config.Values.Contains(p2DirMaps[i]))
                        NES_KeyMAP_joypad_config.Remove(NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == p2DirMaps[i]).Key);
                    NES_KeyMAP_joypad_config[uid + "," + target.Text + "," + raw_id + "," + value] = p2DirMaps[i];
                    return;
                }
            }
        }
        private string JoyPadWayName(string xy_name, int value)
        {
            string tmp = "";

            if (xy_name == "X")
            {
                if (value == 0) return "LEFT";
                if (value == 65535) return "RIGHT";
            }

            if (xy_name == "Y")
            {
                if (value == 0) return "UP";
                if (value == 65535) return "DOWN";
            }

            return tmp;
        }
        private void AprNes_ConfigureUI_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Visible = false;
        }
        Dictionary<string, AprNesUI.KeyMap> NES_KeyMAP_joypad_config = new Dictionary<string, AprNesUI.KeyMap>();
        private void AprNes_ConfigureUI_Shown(object sender, EventArgs e)
        {
            _activeJoyControl = null;
            NES_KeyMAP_joypad_config.Clear();
            foreach (string key in AprNesUI.GetInstance().NES_KeyMAP_joypad.Keys)
                NES_KeyMAP_joypad_config[key] = AprNesUI.GetInstance().NES_KeyMAP_joypad[key];

            // P1 joypad
            joypad_A.Text = joypad_B.Text = joypad_SELECT.Text = joypad_START.Text = joypad_UP.Text = joypad_DOWN.Text = joypad_LEFT.Text = joypad_RIGHT.Text = "";
            // P2 joypad
            textBox8.Text = textBox7.Text = textBox6.Text = textBox5.Text = textBox4.Text = textBox3.Text = textBox2.Text = textBox1.Text = "";

            foreach (string key in NES_KeyMAP_joypad_config.Keys)
            {
                if (key == "") continue;
                List<string> tmp = key.Split(new char[] { ',' }).ToList();
                string displayName = tmp[1];

                switch (NES_KeyMAP_joypad_config[key])
                {
                    // P1
                    case AprNesUI.KeyMap.NES_btn_A:      joypad_A.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_B:      joypad_B.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_SELECT:  joypad_SELECT.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_START:   joypad_START.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_UP:      joypad_UP.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_DOWN:    joypad_DOWN.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_LEFT:    joypad_LEFT.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_RIGHT:   joypad_RIGHT.Text = displayName; break;
                    // P2
                    case AprNesUI.KeyMap.NES_btn_P2_A:      textBox8.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_P2_B:      textBox7.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_P2_SELECT:  textBox6.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_P2_START:   textBox5.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_P2_UP:      textBox4.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_P2_DOWN:    textBox3.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_P2_LEFT:    textBox2.Text = displayName; break;
                    case AprNesUI.KeyMap.NES_btn_P2_RIGHT:   textBox1.Text = displayName; break;
                }
            }


            // ── Resize combobox + scanline checkbox 載入 ──────────────────
            InitResizeComboBoxes();
            saneline.Checked = AprNesUI.GetInstance().AppConfigure.ContainsKey("Scanline")
                             && AprNesUI.GetInstance().AppConfigure["Scanline"] == "1";



            if (AprNesUI.GetInstance().AppConfigure["LimitFPS"] == "1") LimitFPS_checkBox.Checked = true;
            else LimitFPS_checkBox.Checked = false;

            screen_path.Text = AprNesUI.GetInstance().AppConfigure["CaptureScreenPath"];

            textBox_A.Text = ((Keys)int.Parse(AprNesUI.GetInstance().AppConfigure["key_A"])).ToString();
            textBox_B.Text = ((Keys)int.Parse(AprNesUI.GetInstance().AppConfigure["key_B"])).ToString();
            textBox_SELECT.Text = ((Keys)int.Parse(AprNesUI.GetInstance().AppConfigure["key_SELECT"])).ToString();
            textBox_START.Text = ((Keys)int.Parse(AprNesUI.GetInstance().AppConfigure["key_START"])).ToString();
            textBox_UP.Text = ((Keys)int.Parse(AprNesUI.GetInstance().AppConfigure["key_UP"])).ToString();
            textBox_DOWN.Text = ((Keys)int.Parse(AprNesUI.GetInstance().AppConfigure["key_DOWN"])).ToString();
            textBox_LEFT.Text = ((Keys)int.Parse(AprNesUI.GetInstance().AppConfigure["key_LEFT"])).ToString();
            textBox_RIGHT.Text = ((Keys)int.Parse(AprNesUI.GetInstance().AppConfigure["key_RIGHT"])).ToString();

            key_A = int.Parse(AprNesUI.GetInstance().AppConfigure["key_A"]);
            key_B = int.Parse(AprNesUI.GetInstance().AppConfigure["key_B"]);
            key_SELECT = int.Parse(AprNesUI.GetInstance().AppConfigure["key_SELECT"]);
            key_START = int.Parse(AprNesUI.GetInstance().AppConfigure["key_START"]);
            key_UP = int.Parse(AprNesUI.GetInstance().AppConfigure["key_UP"]);
            key_DOWN = int.Parse(AprNesUI.GetInstance().AppConfigure["key_DOWN"]);
            key_LEFT = int.Parse(AprNesUI.GetInstance().AppConfigure["key_LEFT"]);
            key_RIGHT = int.Parse(AprNesUI.GetInstance().AppConfigure["key_RIGHT"]);

            // P2 keyboard
            var cfg = AprNesUI.GetInstance().AppConfigure;
            key_P2_A = cfg.ContainsKey("key_P2_A") ? int.Parse(cfg["key_P2_A"]) : 0;
            key_P2_B = cfg.ContainsKey("key_P2_B") ? int.Parse(cfg["key_P2_B"]) : 0;
            key_P2_SELECT = cfg.ContainsKey("key_P2_SELECT") ? int.Parse(cfg["key_P2_SELECT"]) : 0;
            key_P2_START = cfg.ContainsKey("key_P2_START") ? int.Parse(cfg["key_P2_START"]) : 0;
            key_P2_UP = cfg.ContainsKey("key_P2_UP") ? int.Parse(cfg["key_P2_UP"]) : 0;
            key_P2_DOWN = cfg.ContainsKey("key_P2_DOWN") ? int.Parse(cfg["key_P2_DOWN"]) : 0;
            key_P2_LEFT = cfg.ContainsKey("key_P2_LEFT") ? int.Parse(cfg["key_P2_LEFT"]) : 0;
            key_P2_RIGHT = cfg.ContainsKey("key_P2_RIGHT") ? int.Parse(cfg["key_P2_RIGHT"]) : 0;

            textBox16.Text = key_P2_A != 0 ? ((Keys)key_P2_A).ToString() : "";
            textBox15.Text = key_P2_B != 0 ? ((Keys)key_P2_B).ToString() : "";
            textBox14.Text = key_P2_SELECT != 0 ? ((Keys)key_P2_SELECT).ToString() : "";
            textBox13.Text = key_P2_START != 0 ? ((Keys)key_P2_START).ToString() : "";
            textBox12.Text = key_P2_UP != 0 ? ((Keys)key_P2_UP).ToString() : "";
            textBox11.Text = key_P2_DOWN != 0 ? ((Keys)key_P2_DOWN).ToString() : "";
            textBox10.Text = key_P2_LEFT != 0 ? ((Keys)key_P2_LEFT).ToString() : "";
            textBox9.Text = key_P2_RIGHT != 0 ? ((Keys)key_P2_RIGHT).ToString() : "";

            LimitFPS_checkBox.Focus();

            // 音效設定載入
            SoundcheckBox.Checked = NesCore.AudioEnabled;
            SoundtrackBar.Value = Math.Max(0, Math.Min(100, NesCore.Volume));
            UpdateSoundUI();

            // Accuracy 設定載入
            perdotFSM.Checked = NesCore.AccuracyOptA;

            // ── Analog 設定載入 ──────────────────────────────────────────────
            useAnalog.Checked   = NesCore.AnalogEnabled;
            ultraAnalog.Checked = NesCore.UltraAnalog;
            crtuse.Checked      = NesCore.CrtEnabled;

            switch (NesCore.AnalogOutput)
            {
                case AnalogOutputMode.RF:     VideoInput.SelectedIndex = 0; break;
                case AnalogOutputMode.SVideo: VideoInput.SelectedIndex = 1; break;
                default:                              VideoInput.SelectedIndex = 2; break;
            }

            // Analog Size
            int[] sizeMap = { 2, 4, 6, 8 };
            int aidx = System.Array.IndexOf(sizeMap, NesCore.AnalogSize);
            comboBox_analogSize.SelectedIndex = aidx >= 0 ? aidx : 1;

            // 根據 AnalogMode 決定哪些控制項啟用
            UpdateAnalogEnableState();
        }

        private void choose_dir_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fd = new FolderBrowserDialog();
            if (fd.ShowDialog() != DialogResult.OK) return;
            screen_path.Text = fd.SelectedPath;
        }

        int key_A = 0, key_B = 0, key_SELECT = 0, key_START = 0, key_RIGHT = 0, key_LEFT = 0, key_UP = 0, key_DOWN = 0;
        int key_P2_A = 0, key_P2_B = 0, key_P2_SELECT = 0, key_P2_START = 0, key_P2_RIGHT = 0, key_P2_LEFT = 0, key_P2_UP = 0, key_P2_DOWN = 0;

        private void AprNes_ConfigureUI_Load(object sender, EventArgs e)
        {

        }

        // Old radio button handlers removed — replaced by combobox logic

        private void textBox_KeyConfig_KeyUp(object sender, KeyEventArgs e)
        {
            TextBox tb = sender as TextBox;
            tb.Text = e.KeyData.ToString();
            tb.ReadOnly = true;

            // P1 keyboard: textBox_A ~ textBox_RIGHT (name starts with "textBox_")
            if (tb.Name.StartsWith("textBox_"))
            {
                string name = tb.Name.Remove(0, 8);
                switch (name)
                {
                    case "A": key_A = e.KeyValue; break;
                    case "B": key_B = e.KeyValue; break;
                    case "START": key_START = e.KeyValue; break;
                    case "SELECT": key_SELECT = e.KeyValue; break;
                    case "UP": key_UP = e.KeyValue; break;
                    case "DOWN": key_DOWN = e.KeyValue; break;
                    case "LEFT": key_LEFT = e.KeyValue; break;
                    case "RIGHT": key_RIGHT = e.KeyValue; break;
                }
                return;
            }

            // P2 keyboard: textBox16=A, textBox15=B, textBox14=SELECT, textBox13=START,
            //              textBox12=UP, textBox11=DOWN, textBox10=LEFT, textBox9=RIGHT
            if (tb == textBox16)     key_P2_A = e.KeyValue;
            else if (tb == textBox15) key_P2_B = e.KeyValue;
            else if (tb == textBox14) key_P2_SELECT = e.KeyValue;
            else if (tb == textBox13) key_P2_START = e.KeyValue;
            else if (tb == textBox12) key_P2_UP = e.KeyValue;
            else if (tb == textBox11) key_P2_DOWN = e.KeyValue;
            else if (tb == textBox10) key_P2_LEFT = e.KeyValue;
            else if (tb == textBox9)  key_P2_RIGHT = e.KeyValue;
        }

        private void textBox_KeyConfig_MouseClick(object sender, MouseEventArgs e)
        {
            (sender as TextBox).ReadOnly = false;
        }

        private void textBox_A_Leave(object sender, EventArgs e)
        {
            (sender as TextBox).ReadOnly = true;
        }
    }
}
