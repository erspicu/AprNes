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
            init();
        }

        // 安全讀取語系字串（key 不存在時 fallback 為 key 名稱本身）
        static string LangStr(string key)
        {
            var tbl = LangINI.lang_table[AprNesUI.GetInstance().AppConfigure["Lang"]];
            return tbl.ContainsKey(key) ? tbl[key] : key;
        }

        public void init()
        {

            if (!LangINI.LangLoadOK) return;

            Ok_btn.Text      = LangStr("ok");
            this.Text        = LangStr("setting");
            choose_dir.Text  = LangStr("selectfolder");
            groupBox1.Text   = LangStr("keypad");
            groupBox2.Text   = LangStr("joypad");
            groupBox4.Text   = LangStr("screen");
            LimitFPS_checkBox.Text = LangStr("limitfps");
            perdotFSM.Text   = LangStr("perdotFSM");
            label18.Text     = LangStr("langchoose");
            label9.Text      = "Shift + p " + LangStr("capture_path");
            groupBox3.Text   = LangStr("scanline");

            // 音效
            groupBox5.Text      = LangStr("sound_group");
            UpdateSoundUI(); // 套用語系至 SoundcheckBox.Text

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

            comboBox1.Items.Clear();

            int ch = 0;
            foreach (string i in LangINI.lang_map.Keys)
            {
                comboBox1.Items.Add(i + " " + LangINI.lang_map[i]);
                if (i == AprNesUI.GetInstance().AppConfigure["Lang"])
                    comboBox1.SelectedIndex = ch;
                ch++;
            }
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
            foreach (TextBox tb in new[] { joypad_A, joypad_B, joypad_START, joypad_SELECT,
                                           joypad_UP, joypad_DOWN, joypad_LEFT, joypad_RIGHT })
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

            // Analog 開啟時，停用原有 Screen / Scanline 設定
            groupBox3.Enabled = !analogOn;
            groupBox4.Enabled = !analogOn;

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

            if (radioButtonX2s.Checked)
            {
                AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "2";
                AprNesUI.GetInstance().AppConfigure["filter"] = "scanline";
            }
            else if (radioButtonX4s.Checked)
            {
                AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "4";
                AprNesUI.GetInstance().AppConfigure["filter"] = "scanline";
            }
            else if (radioButtonX6s.Checked)
            {
                AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "6";
                AprNesUI.GetInstance().AppConfigure["filter"] = "scanline";
            }

            if (radioButtonX1.Checked)
            {
                AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "1";
                AprNesUI.GetInstance().AppConfigure["filter"] = "xbrz";
            }
            else if (radioButtonX2.Checked)
            {
                AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "2";
                AprNesUI.GetInstance().AppConfigure["filter"] = "xbrz";
            }
            else if (radioButtonX3.Checked)
            {
                AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "3";
                AprNesUI.GetInstance().AppConfigure["filter"] = "xbrz";
            }
            else if (radioButtonX4.Checked)
            {
                AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "4";
                AprNesUI.GetInstance().AppConfigure["filter"] = "xbrz";
            }
            else if (radioButtonX5.Checked)
            {
                AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "5";
                AprNesUI.GetInstance().AppConfigure["filter"] = "xbrz";
            }
            else if (radioButtonX6.Checked)
            {
                AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "6";
                AprNesUI.GetInstance().AppConfigure["filter"] = "xbrz";
            }
            else if (radioButtonX8.Checked)
            {
                AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "8";
                AprNesUI.GetInstance().AppConfigure["filter"] = "xbrz";
            }
            else if (radioButtonX9.Checked)
            {
                AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "9";
                AprNesUI.GetInstance().AppConfigure["filter"] = "xbrz";
            }
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
            AprNesUI.GetInstance().AppConfigure["Lang"] = (comboBox1.SelectedItem as string).Split(new char[] { ' ' })[0];
            AprNesUI.GetInstance().Configure_Write();

            AprNesUI.GetInstance().LoadConfig();
            AprNesUI.GetInstance().initUILang();
            AprNesUI.GetInstance().initUIsize();

            AprNesUI.GetInstance().ApplyRenderSettings();

            // AnalogMode 切換（OFF→ON 或 ON→OFF）時，若 AnalogScreenBuf 狀態與新設定不符
            // 則主動補齊：OFF→ON 已由 ApplyRenderSettings 處理；ON→OFF 需手動釋放緩衝區
            bool newAnalogEnabled = NesCore.AnalogEnabled; // LoadConfig 已更新
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
            if (_activeJoyControl == joypad_A || _activeJoyControl == joypad_B ||
                _activeJoyControl == joypad_START || _activeJoyControl == joypad_SELECT)
                return 1;
            if (_activeJoyControl == joypad_UP || _activeJoyControl == joypad_DOWN ||
                _activeJoyControl == joypad_LEFT || _activeJoyControl == joypad_RIGHT)
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
        private void GBEMU_ConfigureUI_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Visible = false;
        }
        Dictionary<string, AprNesUI.KeyMap> NES_KeyMAP_joypad_config = new Dictionary<string, AprNesUI.KeyMap>();
        private void GBEMU_ConfigureUI_Shown(object sender, EventArgs e)
        {
            _activeJoyControl = null;
            NES_KeyMAP_joypad_config.Clear();
            foreach (string key in AprNesUI.GetInstance().NES_KeyMAP_joypad.Keys)
                NES_KeyMAP_joypad_config[key] = AprNesUI.GetInstance().NES_KeyMAP_joypad[key];

            joypad_A.Text = joypad_B.Text = joypad_SELECT.Text = joypad_START.Text = joypad_UP.Text = joypad_DOWN.Text = joypad_LEFT.Text = joypad_RIGHT.Text = "";

            foreach (string key in NES_KeyMAP_joypad_config.Keys)
            {
                if (key == "") continue;

                if (NES_KeyMAP_joypad_config[key] == AprNesUI.KeyMap.NES_btn_A)
                {
                    List<string> tmp = key.Split(new char[] { ',' }).ToList();
                    joypad_A.Text = tmp[1];
                }
                else if (NES_KeyMAP_joypad_config[key] == AprNesUI.KeyMap.NES_btn_B)
                {
                    List<string> tmp = key.Split(new char[] { ',' }).ToList();
                    joypad_B.Text = tmp[1];
                }
                else if (NES_KeyMAP_joypad_config[key] == AprNesUI.KeyMap.NES_btn_SELECT)
                {
                    List<string> tmp = key.Split(new char[] { ',' }).ToList();
                    joypad_SELECT.Text = tmp[1];
                }
                else if (NES_KeyMAP_joypad_config[key] == AprNesUI.KeyMap.NES_btn_START)
                {
                    List<string> tmp = key.Split(new char[] { ',' }).ToList();
                    joypad_START.Text = tmp[1];
                }
                else if (NES_KeyMAP_joypad_config[key] == AprNesUI.KeyMap.NES_btn_UP)
                {
                    List<string> tmp = key.Split(new char[] { ',' }).ToList();
                    joypad_UP.Text = tmp[1];
                }
                else if (NES_KeyMAP_joypad_config[key] == AprNesUI.KeyMap.NES_btn_DOWN)
                {
                    List<string> tmp = key.Split(new char[] { ',' }).ToList();
                    joypad_DOWN.Text = tmp[1];
                }
                else if (NES_KeyMAP_joypad_config[key] == AprNesUI.KeyMap.NES_btn_LEFT)
                {
                    List<string> tmp = key.Split(new char[] { ',' }).ToList();
                    joypad_LEFT.Text = tmp[1];
                }
                else if (NES_KeyMAP_joypad_config[key] == AprNesUI.KeyMap.NES_btn_RIGHT)
                {
                    List<string> tmp = key.Split(new char[] { ',' }).ToList();
                    joypad_RIGHT.Text = tmp[1];
                }
            }


            switch (AprNesUI.GetInstance().AppConfigure["filter"])
            {
                case "xbrz":
                    (groupBox4.Controls.Find("radioButtonX" + AprNesUI.GetInstance().AppConfigure["ScreenSize"] , true)[0] as RadioButton).Checked = true;
                    break;

                case "scanline":
                    (groupBox3.Controls.Find("radioButtonX" + AprNesUI.GetInstance().AppConfigure["ScreenSize"] + "s", true)[0] as RadioButton).Checked = true;
                    break;
            }



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


        private void radioButtonX_CheckedChanged(object sender, EventArgs e)
        {

            if ((sender as RadioButton).Checked == false) return;

            radioButtonX2s.CheckedChanged += null;
            radioButtonX4s.CheckedChanged += null;
            radioButtonX6s.CheckedChanged += null;

            radioButtonX2s.Checked = false;
            radioButtonX4s.Checked = false;
            radioButtonX6s.Checked = false;

            radioButtonX2s.CheckedChanged += new System.EventHandler(this.radioButtonXs_CheckedChanged);
            radioButtonX4s.CheckedChanged += new System.EventHandler(this.radioButtonXs_CheckedChanged);
            radioButtonX6s.CheckedChanged += new System.EventHandler(this.radioButtonXs_CheckedChanged);
        }

        private void radioButtonXs_CheckedChanged(object sender, EventArgs e)
        {

            if ((sender as RadioButton).Checked == false) return;

            radioButtonX1.CheckedChanged += null;
            radioButtonX2.CheckedChanged += null;
            radioButtonX3.CheckedChanged += null;
            radioButtonX4.CheckedChanged += null;
            radioButtonX5.CheckedChanged += null;
            radioButtonX6.CheckedChanged += null;
            radioButtonX8.CheckedChanged += null;
            radioButtonX9.CheckedChanged += null;


            radioButtonX1.Checked = false;
            radioButtonX2.Checked = false;
            radioButtonX3.Checked = false;
            radioButtonX4.Checked = false;
            radioButtonX5.Checked = false;
            radioButtonX6.Checked = false;
            radioButtonX8.Checked = false;
            radioButtonX9.Checked = false;


            radioButtonX1.CheckedChanged += new System.EventHandler(this.radioButtonX_CheckedChanged);
            radioButtonX2.CheckedChanged += new System.EventHandler(this.radioButtonX_CheckedChanged);
            radioButtonX3.CheckedChanged += new System.EventHandler(this.radioButtonX_CheckedChanged);
            radioButtonX4.CheckedChanged += new System.EventHandler(this.radioButtonX_CheckedChanged);
            radioButtonX5.CheckedChanged += new System.EventHandler(this.radioButtonX_CheckedChanged);
            radioButtonX6.CheckedChanged += new System.EventHandler(this.radioButtonX_CheckedChanged);
            radioButtonX8.CheckedChanged += new System.EventHandler(this.radioButtonX_CheckedChanged);
            radioButtonX9.CheckedChanged += new System.EventHandler(this.radioButtonX_CheckedChanged);

        }

        private void textBox_KeyConfig_KeyUp(object sender, KeyEventArgs e)
        {

            (sender as TextBox).Text = e.KeyData.ToString();
            (sender as TextBox).ReadOnly = true;

            string name = (sender as TextBox).Name.Remove(0, 8);
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
