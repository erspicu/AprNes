using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using LangTool;
using NativeTools;

namespace AprNes
{
    public partial class AprNesUI : Form
    {
        [DllImport("winmm.dll")] static extern int timeBeginPeriod(int uPeriod);
        [DllImport("winmm.dll")] static extern int timeEndPeriod(int uPeriod);

        bool _highResPeriodActive = false;
        void BeginHighResPeriod() { if (!_highResPeriodActive) { timeBeginPeriod(1); _highResPeriodActive = true; } }
        void EndHighResPeriod()   { if (_highResPeriodActive)  { timeEndPeriod(1);   _highResPeriodActive = false; } }

        Graphics grfx;
        public Dictionary<string, string> AppConfigure = new Dictionary<string, string>();
        public static string ConfigureDir = Application.StartupPath + @"\configure";
        string ConfigureFile = Application.StartupPath + @"\configure\AprNes.ini";
        string AudioPlusIniFile = Application.StartupPath + @"\configure\AprNesAudioPlus.ini";

        // 舊版 AprNes.ini 若殘留 analog key 則在 Configure_Write 時過濾掉（避免重複寫入）
        static readonly System.Collections.Generic.HashSet<string> s_analogKeys =
            new System.Collections.Generic.HashSet<string>(new[] {
                "RF_NoiseIntensity", "RF_SlewRate", "RF_ChromaBlur",
                "AV_NoiseIntensity", "AV_SlewRate", "AV_ChromaBlur",
                "SV_NoiseIntensity", "SV_SlewRate", "SV_ChromaBlur",
                "RF_BeamSigma", "RF_BloomStrength", "RF_BrightnessBoost",
                "AV_BeamSigma", "AV_BloomStrength", "AV_BrightnessBoost",
                "SV_BeamSigma", "SV_BloomStrength", "SV_BrightnessBoost",
            });

        // AudioPlus 效果參數已移至 AprNesAudioPlus.ini，從主 ini 過濾掉
        static readonly System.Collections.Generic.HashSet<string> s_audioPlusKeys =
            new System.Collections.Generic.HashSet<string>(new[] {
                "ConsoleModel", "RfCrosstalk", "CustomLpfCutoff", "CustomBuzz",
                "BuzzAmplitude", "BuzzFreq", "RfVolume",
                "StereoWidth", "HaasDelay", "HaasCrossfeed",
                "ReverbWet", "CombFeedback", "CombDamp",
                "BassBoostDb", "BassBoostFreq",
                // 舊版相容（從 AprNes.ini 遷移過來的舊 key）
                "Reverb", "BassBoost",
            });

        Dictionary<int, KeyMap> NES_KeyMAP = new Dictionary<int, KeyMap>();
        public Dictionary<string, KeyMap> NES_KeyMAP_joypad = new Dictionary<string, KeyMap>();

        // 最近開啟的 ROM 清單（最新在前，最多 10 筆）
        List<string> _recentROMs = new List<string>();
        const int MaxRecentROMs = 10;
        ToolStripMenuItem _recentROMsMenuItem;

        joystick _joystick = new joystick();

        /// <summary>舊版相容：若舊位置有 ini 而新位置沒有，自動搬移</summary>
        static void MigrateOldIni(string fileName)
        {
            string oldPath = System.IO.Path.Combine(Application.StartupPath, fileName);
            string newPath = System.IO.Path.Combine(ConfigureDir, fileName);
            if (System.IO.File.Exists(oldPath) && !System.IO.File.Exists(newPath))
                System.IO.File.Move(oldPath, newPath);
        }

        Stopwatch st = new Stopwatch();//test UI finish time

        public AprNesUI()
        {
            st.Restart();
            InitializeComponent();
            NesCore.OnError = msg => MessageBox.Show(msg);

            // 確保 configure 目錄存在
            if (!System.IO.Directory.Exists(ConfigureDir))
                System.IO.Directory.CreateDirectory(ConfigureDir);

            // 舊版相容：自動搬移舊位置的 ini 到 configure/
            MigrateOldIni("AprNes.ini");
            MigrateOldIni("AprNesLang.ini");
            MigrateOldIni("AprNesAnalog.ini");
            MigrateOldIni("AprNesAudioPlus.ini");

            LangINI.init();
            if (LangINI.LangFileMissing)
                MessageBox.Show("AprNesLang.ini not found.\nUI will use default text.",
                                "Language file missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadConfig();
            initUILang();
            InitRecentROMsMenu();
            grfx = panel1.CreateGraphics();

        }

        protected static AprNesUI instance;
        public static AprNesUI GetInstance()
        {
            if (instance == null || instance.IsDisposed)
                instance = new AprNesUI();
            return instance;
        }

        public void initUILang()
        {
            if (!LangINI.LangLoadOK) return;

            UIOpenRom.Text = fun1ToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["rom"];
            UIConfig.Text = fun3ToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["setting"];
            UIReset.Text = LangINI.lang_table[AppConfigure["Lang"]]["reset"];
            fun2ToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["reset"] + " (Soft)";
            fun7ToolStripMenuItem.Text = "Hard Reset";
            UIAbout.Text = fun6ToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["about"];
            fun5ToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["appclose"];
            fullScreeenToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["fullscreen"];
            normalToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["normal"];
            screenModeToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["screenmode"];
            RomInf.Text = fun4ToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["rominfo"]; //rominfo
            if (_recentROMsMenuItem != null)
                _recentROMsMenuItem.Text = LangINI.Get(AppConfigure["Lang"], "recent", "Recent");
        }

        int ScreenSize = 1;
        public void initUIsize()
        {
            // AnalogEnabled 時依 AnalogSize 決定（256×N × 210×N，8:7 AR）
            // 直接從 NesCore.AnalogSize 計算，避免依賴 NesCore.Crt_DstW/DstH（可能尚未 sync）
            int renderWidth  = NesCore.AnalogEnabled ? 256 * NesCore.AnalogSize : 256 * ScreenSize;
            int renderHeight = NesCore.AnalogEnabled ? 210 * NesCore.AnalogSize : 240 * ScreenSize;

            panel1.Visible = false;
            panel1.Width  = renderWidth;
            panel1.Height = renderHeight;

            if (!NesCore.AnalogEnabled && AppConfigure["filter"] == "scanline")
            {
                switch (ScreenSize)
                {
                    case 2: panel1.Width = 600; break;
                    case 4: panel1.Width = 1196; break;
                    case 6: panel1.Width = 1792; break;
                }
            }

            // panel 改變大小後重建 Graphics context
            grfx?.Dispose();
            grfx = panel1.CreateGraphics();

            // grfx 重建後，RenderObj 持有的舊 Graphics 已失效，需重新 init
            unsafe
            {
                if (RenderObj != null)
                    RenderObj.init(NesCore.ScreenBuf1x, grfx);
            }

            if (ScreenCenterFull)
            {
                UIAbout.Visible = RomInf.Visible = UIOpenRom.Visible = UIReset.Visible = UIConfig.Visible = label3.Visible = false;

                fullScreeenToolStripMenuItem_Click(null, null);
                panel1.Visible = true;
                return;
            }

            UIAbout.Visible = RomInf.Visible = UIOpenRom.Visible = UIReset.Visible = UIConfig.Visible = label3.Visible = true;
            panel1.Location = new Point(5, 35);
            this.Width  = renderWidth  + 26;   // panel + 左右邊框
            this.Height = renderHeight + 92;   // panel + 上工具列(35) + 下按鈕列(57)

            if (!NesCore.AnalogEnabled && AppConfigure["filter"] == "scanline")
            {
                switch (ScreenSize)
                {
                    case 2: Width = 600; break;
                    case 4: Width = 1196; break;
                    case 6: Width = 1792; break;
                }
                Width += 26;
            }
            UIAbout.Location = new Point(Width - 82, renderHeight + 37);
            RomInf.Location  = new Point(5,          renderHeight + 37);
            panel1.Visible = true;
        }

        public enum KeyMap
        {
            NES_btn_A = 0,
            NES_btn_B = 1,
            NES_btn_SELECT = 2,
            NES_btn_START = 3,
            NES_btn_UP = 4,
            NES_btn_DOWN = 5,
            NES_btn_LEFT = 6,
            NES_btn_RIGHT = 7
        }

        public int key_A = 90;
        public int key_B = 88;
        public int key_SELECT = 83;
        public int key_START = 65;
        public int key_RIGHT = 39;
        public int key_LEFT = 37;
        public int key_UP = 38;
        public int key_DOWN = 40;

        string joypad_A = "";
        string joypad_B = "";
        string joypad_SELECT = "";
        string joypad_START = "";
        string joypad_UP = "";
        string joypad_DOWN = "";
        string joypad_LEFT = "";
        string joypad_RIGHT = "";

        static string GetDefaultLang()
        {
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            string name = culture.Name.ToLowerInvariant(); // e.g. "zh-tw", "zh-cn", "zh-hant", "en-us"
            if (name.StartsWith("zh"))
            {
                // zh-TW, zh-Hant → 繁體; zh-CN, zh-Hans, zh-SG → 簡體
                if (name.Contains("tw") || name.Contains("hk") || name.Contains("mo") || name.Contains("hant"))
                    return "zh-tw";
                return "zh-cn";
            }
            return "en-us";
        }

        public void LoadConfig()
        {
            if (!File.Exists(ConfigureFile))
            {
                //建立預設
                AppConfigure["key_A"] = key_A.ToString();
                AppConfigure["key_B"] = key_B.ToString();
                AppConfigure["key_SELECT"] = key_SELECT.ToString();
                AppConfigure["key_START"] = key_START.ToString();
                AppConfigure["key_UP"] = key_UP.ToString();
                AppConfigure["key_DOWN"] = key_DOWN.ToString();
                AppConfigure["key_LEFT"] = key_LEFT.ToString();
                AppConfigure["key_RIGHT"] = key_RIGHT.ToString();
                AppConfigure["LimitFPS"] = "1";
                AppConfigure["ScreenSize"] = "1";
                AppConfigure["CaptureScreenPath"] = Application.StartupPath+ @"\Captures\Screenshots";
                AppConfigure["joypad_A"] = "";
                AppConfigure["joypad_B"] = "";
                AppConfigure["joypad_SELECT"] = "";
                AppConfigure["joypad_START"] = "";
                AppConfigure["joypad_UP"] = "";
                AppConfigure["joypad_DOWN"] = "";
                AppConfigure["joypad_LEFT"] = "";
                AppConfigure["joypad_RIGHT"] = "";
                AppConfigure["Lang"] = GetDefaultLang();
                AppConfigure["filter"] = "xbrz";
                AppConfigure["Sound"] = "1";
                AppConfigure["Volume"] = "70";
                AppConfigure["AnalogMode"] = "0";
                AppConfigure["AnalogOutput"] = "AV";
                AppConfigure["UltraAnalog"] = "0";
                AppConfigure["crt"] = "1";
                Configure_Write();
            }

            List<string> lines = File.ReadAllLines(ConfigureFile).ToList();
            foreach (string i in lines)
            {
                // 跳過注解行（; 或 # 開頭）
                string trimmed = i.TrimStart();
                if (trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;

                string[] keyvalue = i.Split(new char[] { '=' }, 2);
                if (keyvalue.Length == 2)
                    AppConfigure[keyvalue[0]] = keyvalue[1];
            }

            LimitFPS = false;
            if (AppConfigure["LimitFPS"] == "1")
                LimitFPS = true;

            key_A = int.Parse(AppConfigure["key_A"]);
            key_B = int.Parse(AppConfigure["key_B"]);
            key_SELECT = int.Parse(AppConfigure["key_SELECT"]);
            key_START = int.Parse(AppConfigure["key_START"]);
            key_RIGHT = int.Parse(AppConfigure["key_RIGHT"]);
            key_LEFT = int.Parse(AppConfigure["key_LEFT"]);
            key_UP = int.Parse(AppConfigure["key_UP"]);
            key_DOWN = int.Parse(AppConfigure["key_DOWN"]);

            joypad_A = AppConfigure["joypad_A"];
            joypad_B = AppConfigure["joypad_B"];
            joypad_SELECT = AppConfigure["joypad_SELECT"];
            joypad_START = AppConfigure["joypad_START"];
            joypad_UP = AppConfigure["joypad_UP"];
            joypad_DOWN = AppConfigure["joypad_DOWN"];
            joypad_LEFT = AppConfigure["joypad_LEFT"];
            joypad_RIGHT = AppConfigure["joypad_RIGHT"];

            ScreenSize = int.Parse(AppConfigure["ScreenSize"]);
            Console.WriteLine("UI size : " + ScreenSize);
            ScreenCenterFull = bool.Parse(AppConfigure["ScreenFull"]);

            // 讀取音效開關設定
            if (AppConfigure.ContainsKey("Sound"))
                NesCore.AudioEnabled = AppConfigure["Sound"] == "1";
            else
                NesCore.AudioEnabled = true;

            // 讀取音量設定
            int vol;
            if (AppConfigure.ContainsKey("Volume") && int.TryParse(AppConfigure["Volume"], out vol))
                NesCore.Volume = Math.Max(0, Math.Min(100, vol));
            else
                NesCore.Volume = 70;

            // ── AudioPlus 設定（AudioMode 留在主 ini，其餘效果參數在 AprNesAudioPlus.ini）──
            int am;
            NesCore.AudioMode = (AppConfigure.ContainsKey("AudioMode") && int.TryParse(AppConfigure["AudioMode"], out am) && am >= 0 && am <= 2) ? am : 0;
            LoadAudioPlusIni();

            // 讀取 Accuracy 選項設定 (預設全開)
            NesCore.AccuracyOptA = !AppConfigure.ContainsKey("AccuracyOptA") || AppConfigure["AccuracyOptA"] != "0";

            // 讀取類比訊號模擬設定 (預設關閉)
            NesCore.AnalogEnabled = AppConfigure.ContainsKey("AnalogMode") && AppConfigure["AnalogMode"] == "1";

            // 讀取類比輸出尺寸（2-6，預設 4）
            if (AppConfigure.ContainsKey("AnalogSize"))
            {
                int sz;
                int[] valid = { 2, 4, 6, 8 };
                NesCore.AnalogSize = (int.TryParse(AppConfigure["AnalogSize"], out sz) && System.Array.IndexOf(valid, sz) >= 0) ? sz : 4;
            }
            else
                NesCore.AnalogSize = 4;

            // 讀取 Ultra 類比設定（預設關閉）
            NesCore.UltraAnalog = AppConfigure.ContainsKey("UltraAnalog") && AppConfigure["UltraAnalog"] == "1";

            // 讀取 CRT 電子束光學模擬開關（預設開啟，UltraAnalog=1 時有效）
            NesCore.CrtEnabled = !AppConfigure.ContainsKey("crt") || AppConfigure["crt"] != "0";

            // 讀取類比端子模式 (AnalogMode=1 時有效，預設 AV)
            if (AppConfigure.ContainsKey("AnalogOutput"))
            {
                switch (AppConfigure["AnalogOutput"].ToUpper())
                {
                    case "RF":     NesCore.AnalogOutput = AnalogOutputMode.RF;     break;
                    case "SVIDEO": NesCore.AnalogOutput = AnalogOutputMode.SVideo; break;
                    default:       NesCore.AnalogOutput = AnalogOutputMode.AV;     break;
                }
            }
            else
            {
                NesCore.AnalogOutput = AnalogOutputMode.AV;
            }

            LoadAnalogConfig(); // 讀取 AprNesAnalog.ini（開機一次）
            // 注意：不在此處呼叫 SyncAnalogConfig()，避免在模擬執行緒尚未暫停時
            // 改變 CrtScreen._analogSize，導致 DemodulateRow 使用新 dstW 存取舊 buffer 而 crash。
            // 正確的 sync 由 ApplyRenderSettings() 負責（已確保模擬執行緒完全停止）。

            NES_init_KeyMap();

        }

        private void NES_init_KeyMap()
        {
            NES_KeyMAP_joypad[joypad_A] = KeyMap.NES_btn_A;
            NES_KeyMAP_joypad[joypad_B] = KeyMap.NES_btn_B;
            NES_KeyMAP_joypad[joypad_SELECT] = KeyMap.NES_btn_SELECT;
            NES_KeyMAP_joypad[joypad_START] = KeyMap.NES_btn_START;
            NES_KeyMAP_joypad[joypad_UP] = KeyMap.NES_btn_UP;
            NES_KeyMAP_joypad[joypad_DOWN] = KeyMap.NES_btn_DOWN;
            NES_KeyMAP_joypad[joypad_LEFT] = KeyMap.NES_btn_LEFT;
            NES_KeyMAP_joypad[joypad_RIGHT] = KeyMap.NES_btn_RIGHT;

            NES_KeyMAP.Clear();
            NES_KeyMAP[key_A] = KeyMap.NES_btn_A;
            NES_KeyMAP[key_B] = KeyMap.NES_btn_B;
            NES_KeyMAP[key_SELECT] = KeyMap.NES_btn_SELECT;
            NES_KeyMAP[key_START] = KeyMap.NES_btn_START;
            NES_KeyMAP[key_RIGHT] = KeyMap.NES_btn_RIGHT;
            NES_KeyMAP[key_LEFT] = KeyMap.NES_btn_LEFT;
            NES_KeyMAP[key_UP] = KeyMap.NES_btn_UP;
            NES_KeyMAP[key_DOWN] = KeyMap.NES_btn_DOWN;
        }

        public void Configure_Write()
        {

            AppConfigure["key_A"] = key_A.ToString();
            AppConfigure["key_B"] = key_B.ToString();
            AppConfigure["key_SELECT"] = key_SELECT.ToString();
            AppConfigure["key_START"] = key_START.ToString();
            AppConfigure["key_UP"] = key_UP.ToString();
            AppConfigure["key_DOWN"] = key_DOWN.ToString();
            AppConfigure["key_LEFT"] = key_LEFT.ToString();
            AppConfigure["key_RIGHT"] = key_RIGHT.ToString();


            AppConfigure["joypad_A"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_A))
                AppConfigure["joypad_A"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_A).Key;

            AppConfigure["joypad_B"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_B))
                AppConfigure["joypad_B"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_B).Key;

            AppConfigure["joypad_SELECT"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_SELECT))
                AppConfigure["joypad_SELECT"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_SELECT).Key;

            AppConfigure["joypad_START"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_START))
                AppConfigure["joypad_START"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_START).Key;

            AppConfigure["joypad_UP"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_UP))
                AppConfigure["joypad_UP"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_UP).Key;

            AppConfigure["joypad_DOWN"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_DOWN))
                AppConfigure["joypad_DOWN"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_DOWN).Key;

            AppConfigure["joypad_LEFT"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_LEFT))
                AppConfigure["joypad_LEFT"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_LEFT).Key;

            AppConfigure["joypad_RIGHT"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_RIGHT))
                AppConfigure["joypad_RIGHT"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_RIGHT).Key;

            AppConfigure["ScreenFull"] = ScreenCenterFull.ToString();
            AppConfigure["Sound"] = NesCore.AudioEnabled ? "1" : "0";
            AppConfigure["Volume"] = NesCore.Volume.ToString();
            AppConfigure["AccuracyOptA"] = NesCore.AccuracyOptA ? "1" : "0";
            AppConfigure["AudioMode"] = NesCore.AudioMode.ToString();
            SaveAudioPlusIni();
            AppConfigure["crt"] = NesCore.CrtEnabled ? "1" : "0";
            AppConfigure["AnalogSize"] = NesCore.AnalogSize.ToString();

            string conf = "";
            foreach (string i in AppConfigure.Keys)
            {
                if (s_analogKeys.Contains(i)) continue;          // 過濾殘留的舊版 analog key
                if (s_audioPlusKeys.Contains(i)) continue;       // 已移至 AprNesAudioPlus.ini
                if (string.IsNullOrWhiteSpace(i)) continue;      // 防呆：跳過空白 key
                if (i.Contains("=") || i.Contains("\n")) continue; // 防呆：key 不得含 = 或換行
                string val = AppConfigure[i] ?? "";
                if (val.Contains("\n")) val = val.Replace("\n", "").Replace("\r", ""); // 防呆：value 去換行
                conf += i + "=" + val + "\r\n";
            }

            FileWriteAllText(ConfigureFile, conf);
        }

        // ────────────────────────────────────────────────────────────────────
        // Recent ROMs — 最近開啟的 ROM 紀錄（最多 10 筆，存在 AprNes.ini）
        // ────────────────────────────────────────────────────────────────────

        void InitRecentROMsMenu()
        {
            string langKey = AppConfigure.ContainsKey("Lang") ? AppConfigure["Lang"] : "en-us";
            string text = LangINI.Get(langKey, "recent", "Recent");
            _recentROMsMenuItem = new ToolStripMenuItem(text);

            // 插入到 fun1（Open）的下方（index 1）
            int idx = contextMenuStrip1.Items.IndexOf(fun1ToolStripMenuItem);
            contextMenuStrip1.Items.Insert(idx + 1, _recentROMsMenuItem);

            // 從 INI 讀取
            if (AppConfigure.ContainsKey("RecentROMs") && !string.IsNullOrEmpty(AppConfigure["RecentROMs"]))
            {
                string[] paths = AppConfigure["RecentROMs"].Split('|');
                foreach (string p in paths)
                    if (!string.IsNullOrEmpty(p))
                        _recentROMs.Add(p);
            }
            BuildRecentROMsMenu();
        }

        void BuildRecentROMsMenu()
        {
            _recentROMsMenuItem.DropDownItems.Clear();
            if (_recentROMs.Count == 0)
            {
                _recentROMsMenuItem.Enabled = false;
                return;
            }
            _recentROMsMenuItem.Enabled = true;
            foreach (string path in _recentROMs)
            {
                var item = new ToolStripMenuItem(Path.GetFileName(path));
                item.ToolTipText = path;
                item.Tag = path;
                item.Click += RecentROM_Click;
                _recentROMsMenuItem.DropDownItems.Add(item);
            }
        }

        void AddRecentROM(string fullPath)
        {
            // 移除重複（不區分大小寫）
            _recentROMs.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
            // 插入最前
            _recentROMs.Insert(0, fullPath);
            // 限制數量
            if (_recentROMs.Count > MaxRecentROMs)
                _recentROMs.RemoveRange(MaxRecentROMs, _recentROMs.Count - MaxRecentROMs);
            // 更新選單
            BuildRecentROMsMenu();
            // 寫入 INI
            AppConfigure["RecentROMs"] = string.Join("|", _recentROMs);
            Configure_Write();
        }

        void RemoveRecentROM(string fullPath)
        {
            _recentROMs.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
            BuildRecentROMsMenu();
            AppConfigure["RecentROMs"] = string.Join("|", _recentROMs);
            Configure_Write();
        }

        unsafe void RecentROM_Click(object sender, EventArgs e)
        {
            var item = sender as ToolStripMenuItem;
            if (item == null) return;
            string path = item.Tag as string;
            if (string.IsNullOrEmpty(path)) return;

            if (!File.Exists(path))
            {
                string langKey = AppConfigure.ContainsKey("Lang") ? AppConfigure["Lang"] : "en-us";
                string msg = LangINI.Get(langKey, "file_not_found", "File not found, removed from list.");
                MessageBox.Show(msg, "AprNes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RemoveRecentROM(path);
                return;
            }
            LoadRomFromPath(path);
        }

        // ────────────────────────────────────────────────────────────────────
        // AprNesAnalog.ini — 類比訊號模擬端子參數（開機讀一次，不隨 Configure_Write 重寫）
        // ────────────────────────────────────────────────────────────────────
        void LoadAnalogConfig()
        {
            string analogFile = ConfigureDir + @"\AprNesAnalog.ini";
            string Fmt(float v) => v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);

            if (!File.Exists(analogFile))
            {
                // 首次建立：寫出預設值 + 詳細注解，供使用者手動調整
                string c = "";
                c += "; AprNesAnalog.ini  --  類比訊號模擬端子參數\r\n";
                c += "; 此檔案僅在 AprNes 啟動時讀入一次（修改後需重啟才生效）\r\n";
                c += "; AprNes.ini 的 AnalogMode=1 啟用類比模擬，AnalogOutput=RF/AV/SVideo 選擇端子\r\n";
                c += "; UltraAnalog=0：Stage 1 參數有效；UltraAnalog=1：Stage 1 + Stage 2 皆有效\r\n";
                c += ";\r\n";
                c += "; ── Stage 1（Ntsc 訊號解碼）────────────────────────────────────────────\r\n";
                c += "; NoiseIntensity  熱雜訊振幅（0=無雜訊，0.04=輕微 RF 效果，0.10=明顯）\r\n";
                c += "; SlewRate        電容響應速率 IIR alpha（0=完全模糊，1=完全清晰）\r\n";
                c += "; ChromaBlur      色彩暈染程度 IIR alpha（0=完全暈染，1=無暈染）\r\n";
                c += "RF_NoiseIntensity=" + Fmt(NesCore.RF_NoiseIntensity) + "\r\n";
                c += "RF_SlewRate="       + Fmt(NesCore.RF_SlewRate)       + "\r\n";
                c += "RF_ChromaBlur="     + Fmt(NesCore.RF_ChromaBlur)     + "\r\n";
                c += "AV_NoiseIntensity=" + Fmt(NesCore.AV_NoiseIntensity) + "\r\n";
                c += "AV_SlewRate="       + Fmt(NesCore.AV_SlewRate)       + "\r\n";
                c += "AV_ChromaBlur="     + Fmt(NesCore.AV_ChromaBlur)     + "\r\n";
                c += "SV_NoiseIntensity=" + Fmt(NesCore.SV_NoiseIntensity) + "\r\n";
                c += "SV_SlewRate="       + Fmt(NesCore.SV_SlewRate)       + "\r\n";
                c += "SV_ChromaBlur="     + Fmt(NesCore.SV_ChromaBlur)     + "\r\n";
                c += ";\r\n";
                c += "; ── Stage 2（CrtScreen 電子束光學，UltraAnalog=1 有效）──────────────────\r\n";
                c += "; BeamSigma       電子束擴散半徑 sigma（值越大掃描線越寬，越模糊）\r\n";
                c += "; BloomStrength   高光溢出強度（0=無 Bloom，1=強烈溢出）\r\n";
                c += "; BrightnessBoost 亮度補償倍率（補償掃描線黑溝造成的平均亮度損失）\r\n";
                c += "RF_BeamSigma="       + Fmt(NesCore.RF_BeamSigma)       + "\r\n";
                c += "RF_BloomStrength="   + Fmt(NesCore.RF_BloomStrength)   + "\r\n";
                c += "RF_BrightnessBoost=" + Fmt(NesCore.RF_BrightnessBoost) + "\r\n";
                c += "AV_BeamSigma="       + Fmt(NesCore.AV_BeamSigma)       + "\r\n";
                c += "AV_BloomStrength="   + Fmt(NesCore.AV_BloomStrength)   + "\r\n";
                c += "AV_BrightnessBoost=" + Fmt(NesCore.AV_BrightnessBoost) + "\r\n";
                c += "SV_BeamSigma="       + Fmt(NesCore.SV_BeamSigma)       + "\r\n";
                c += "SV_BloomStrength="   + Fmt(NesCore.SV_BloomStrength)   + "\r\n";
                c += "SV_BrightnessBoost=" + Fmt(NesCore.SV_BrightnessBoost) + "\r\n";
                FileWriteAllText(analogFile, c);
                return; // 使用已設定的預設 static 值，不需再解析
            }

            // 解析既有檔案
            var dict = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(analogFile))
            {
                string t = line.TrimStart();
                if (t.StartsWith(";") || t.StartsWith("#")) continue;
                var kv = line.Split(new char[] { '=' }, 2);
                if (kv.Length == 2) dict[kv[0].Trim()] = kv[1].Trim();
            }

            float Get(string key, float def)
            {
                float v;
                return dict.ContainsKey(key) &&
                       float.TryParse(dict[key], System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out v) ? v : def;
            }

            bool GetBool(string key, bool def)
            {
                if (!dict.ContainsKey(key)) return def;
                string s = dict[key];
                return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            // Effect toggles
            NesCore.HbiSimulation     = GetBool("HbiSimulation", true);
            NesCore.ColorBurstJitter  = GetBool("ColorBurstJitter", true);
            NesCore.SymmetricIQ       = GetBool("SymmetricIQ", true);
            NesCore.InterlaceJitter = GetBool("InterlaceJitter", true);
            bool ringingOn     = GetBool("RingingEnabled", true);
            bool vignetteOn    = GetBool("VignetteEnabled", true);
            bool shadowMaskOn  = GetBool("ShadowMaskEnabled", true);
            bool curvatureOn   = GetBool("CurvatureEnabled", true);
            bool phosphorOn    = GetBool("PhosphorEnabled", true);
            bool hbeamOn       = GetBool("HBeamEnabled", true);
            bool convergenceOn = GetBool("ConvergenceEnabled", true);

            // Effect values
            NesCore.RingStrength  = ringingOn ? Get("RingStrength", 0.3f) : 0f;
            NesCore.GammaCoeff    = Get("GammaCoeff", 0.229f);
            NesCore.ColorTempR    = Get("ColorTempR", 1.0f);
            NesCore.ColorTempG    = Get("ColorTempG", 1.0f);
            NesCore.ColorTempB    = Get("ColorTempB", 1.0f);
            NesCore.VignetteStrength   = vignetteOn ? Get("VignetteStrength", 0.15f) : 0f;
            NesCore.ShadowMaskMode     = shadowMaskOn
                ? (NesCore.CrtMaskType)(int)Get("ShadowMaskMode", 1f)
                : NesCore.CrtMaskType.None;
            NesCore.ShadowMaskStrength = Get("ShadowMaskStrength", 0.3f);
            NesCore.CurvatureStrength  = curvatureOn ? Get("CurvatureStrength", 0.12f) : 0f;
            NesCore.PhosphorDecay      = phosphorOn ? Get("PhosphorDecay", 0.15f) : 0f;
            NesCore.HBeamSpread        = hbeamOn ? Get("HBeamSpread", 0.4f) : 0f;
            NesCore.ConvergenceStrength = convergenceOn ? Get("ConvergenceStrength", 2.0f) : 0f;

            // Stage 1 connector (Ntsc.cs)
            NesCore.RF_NoiseIntensity = Get("RF_NoiseIntensity", 0.04f);
            NesCore.RF_SlewRate       = Get("RF_SlewRate",       0.60f);
            NesCore.RF_ChromaBlur     = Get("RF_ChromaBlur",     0.10f);
            NesCore.AV_NoiseIntensity = Get("AV_NoiseIntensity", 0.003f);
            NesCore.AV_SlewRate       = Get("AV_SlewRate",       0.80f);
            NesCore.AV_ChromaBlur     = Get("AV_ChromaBlur",     0.35f);
            NesCore.SV_NoiseIntensity = Get("SV_NoiseIntensity", 0.00f);
            NesCore.SV_SlewRate       = Get("SV_SlewRate",       0.90f);
            NesCore.SV_ChromaBlur     = Get("SV_ChromaBlur",     0.45f);
            // Stage 2 connector (CrtScreen.cs)
            NesCore.RF_BeamSigma       = Get("RF_BeamSigma",       1.10f);
            NesCore.RF_BloomStrength   = Get("RF_BloomStrength",   0.50f);
            NesCore.RF_BrightnessBoost = Get("RF_BrightnessBoost", 1.10f);
            NesCore.AV_BeamSigma       = Get("AV_BeamSigma",       0.85f);
            NesCore.AV_BloomStrength   = Get("AV_BloomStrength",   0.25f);
            NesCore.AV_BrightnessBoost = Get("AV_BrightnessBoost", 1.25f);
            NesCore.SV_BeamSigma       = Get("SV_BeamSigma",       0.65f);
            NesCore.SV_BloomStrength   = Get("SV_BloomStrength",   0.10f);
            NesCore.SV_BrightnessBoost = Get("SV_BrightnessBoost", 1.40f);

            // Update gamma LUT with loaded GammaCoeff
            NesCore.UpdateGammaLUT();
        }

        public void FileWriteAllText(string path, string str)
        {
            Console.WriteLine("Configure save !");
            Stream s = File.OpenWrite(path);
            StreamWriter sw = new StreamWriter(s);
            sw.WriteLine(str);
            sw.Close();
        }

        // ────────────────────────────────────────────────────────────────────
        // AprNesAudioPlus.ini — 音效處理參數獨立設定檔
        // ────────────────────────────────────────────────────────────────────

        void LoadAudioPlusIni()
        {
            var cfg = new Dictionary<string, string>();

            if (File.Exists(AudioPlusIniFile))
            {
                foreach (string line in File.ReadAllLines(AudioPlusIniFile))
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith(";") || trimmed.StartsWith("#")) continue;
                    string[] kv = line.Split(new char[] { '=' }, 2);
                    if (kv.Length == 2) cfg[kv[0].Trim()] = kv[1].Trim();
                }
            }

            // 輔助：讀取整數，帶預設值和 clamp
            int ReadInt(string key, int def, int min, int max)
            {
                int v;
                return (cfg.ContainsKey(key) && int.TryParse(cfg[key], out v)) ? Math.Max(min, Math.Min(max, v)) : def;
            }

            // ── Authentic ──
            NesCore.ConsoleModel = ReadInt("ConsoleModel", 0, 0, 6);
            NesCore.RfCrosstalk = cfg.ContainsKey("RfCrosstalk") && cfg["RfCrosstalk"] == "1";
            NesCore.CustomLpfCutoff = ReadInt("CustomLpfCutoff", 14000, 1000, 22000);
            NesCore.CustomBuzz = cfg.ContainsKey("CustomBuzz") && cfg["CustomBuzz"] == "1";
            NesCore.BuzzAmplitude = ReadInt("BuzzAmplitude", 30, 0, 100);
            NesCore.BuzzFreq = ReadInt("BuzzFreq", 60, 50, 60);
            NesCore.RfVolume = ReadInt("RfVolume", 50, 0, 200);

            // ── Modern ──
            NesCore.StereoWidth = ReadInt("StereoWidth", 50, 0, 100);
            NesCore.HaasDelay = ReadInt("HaasDelay", 20, 10, 30);
            NesCore.HaasCrossfeed = ReadInt("HaasCrossfeed", 40, 0, 80);
            NesCore.ReverbWet = ReadInt("ReverbWet", 0, 0, 30);
            NesCore.CombFeedback = ReadInt("CombFeedback", 70, 30, 90);
            NesCore.CombDamp = ReadInt("CombDamp", 30, 10, 70);
            NesCore.BassBoostDb = ReadInt("BassBoostDb", 0, 0, 12);
            NesCore.BassBoostFreq = ReadInt("BassBoostFreq", 150, 80, 300);
        }

        public void SaveAudioPlusIniPublic() { SaveAudioPlusIni(); }

        void SaveAudioPlusIni()
        {
            string content =
                "; ═══════════════════════════════════════════════════════════════\r\n" +
                "; AprNesAudioPlus.ini — AudioPlus 音效處理設定\r\n" +
                "; ═══════════════════════════════════════════════════════════════\r\n" +
                ";\r\n" +
                "; ══════════════════════════════════════════════════════════════\r\n" +
                ";  Authentic 模式 (AudioMode=1) — 主機型號 + 類比特性\r\n" +
                "; ══════════════════════════════════════════════════════════════\r\n" +
                ";\r\n" +
                "; ConsoleModel    主機型號 (0-6)\r\n" +
                ";                 0 = Famicom (HVC-001)        ~14kHz 明亮清晰\r\n" +
                ";                 1 = Front-Loader (NES-001)    ~4.7kHz 溫暖厚實\r\n" +
                ";                 2 = Top-Loader (NES-101)     ~20kHz + 60Hz buzz\r\n" +
                ";                 3 = AV Famicom (HVC-101)     ~19kHz 乾淨 AV 直出\r\n" +
                ";                 4 = Sharp Twin Famicom        ~12kHz 略暗\r\n" +
                ";                 5 = Sharp Famicom Titler      ~16kHz S-Video\r\n" +
                ";                 6 = Custom                   自訂所有參數\r\n" +
                ";\r\n" +
                "; RfCrosstalk     RF 音視串擾 (0=Off, 1=On)\r\n" +
                ";\r\n" +
                "; ── Custom 模式專用 (ConsoleModel=6) ──\r\n" +
                "; CustomLpfCutoff 自訂 LPF 截止頻率 (1000-22000 Hz)\r\n" +
                "; CustomBuzz      自訂 buzz 開關 (0=Off, 1=On)\r\n" +
                ";\r\n" +
                "; ── 通用微調（所有主機型號皆適用）──\r\n" +
                "; BuzzAmplitude   Buzz 振幅 (0-100, 0=無聲 30=預設 100=最大)\r\n" +
                "; BuzzFreq        Buzz 頻率 (50=歐規, 60=美規)\r\n" +
                "; RfVolume        RF 串擾音量 (0-200, 0=無聲 50=預設 200=最大)\r\n" +
                ";\r\n" +
                "; ══════════════════════════════════════════════════════════════\r\n" +
                ";  Modern 模式 (AudioMode=2) — 立體聲 + 空間效果\r\n" +
                "; ══════════════════════════════════════════════════════════════\r\n" +
                ";\r\n" +
                "; ── 立體聲混音 ──\r\n" +
                "; StereoWidth     立體聲寬度 (0-100, 0=mono 50=預設 100=最大分離)\r\n" +
                ";\r\n" +
                "; ── Haas Effect（空間感）──\r\n" +
                "; HaasDelay       右聲道延遲 (10-30 ms, 20=預設)\r\n" +
                "; HaasCrossfeed   延遲信號回饋比例 (0-80%, 40=預設)\r\n" +
                ";\r\n" +
                "; ── Reverb（殘響）──\r\n" +
                "; ReverbWet       殘響濕度 (0-30%, 0=Off 10=Light 15=Medium)\r\n" +
                "; CombFeedback    殘響長度 (30-90%, 70=預設, 越高越長)\r\n" +
                "; CombDamp        高頻阻尼 (10-70%, 30=預設, 越高越暗)\r\n" +
                ";\r\n" +
                "; ── Bass Boost（Triangle 低音增強）──\r\n" +
                "; BassBoostDb     增強量 (0-12 dB, 0=Off)\r\n" +
                "; BassBoostFreq   中心頻率 (80-300 Hz, 150=預設)\r\n" +
                ";\r\n" +
                "; ═══════════════════════════════════════════════════════════════\r\n" +
                "\r\n" +
                "; ── Authentic ──\r\n" +
                "ConsoleModel=" + NesCore.ConsoleModel + "\r\n" +
                "RfCrosstalk=" + (NesCore.RfCrosstalk ? "1" : "0") + "\r\n" +
                "CustomLpfCutoff=" + NesCore.CustomLpfCutoff + "\r\n" +
                "CustomBuzz=" + (NesCore.CustomBuzz ? "1" : "0") + "\r\n" +
                "BuzzAmplitude=" + NesCore.BuzzAmplitude + "\r\n" +
                "BuzzFreq=" + NesCore.BuzzFreq + "\r\n" +
                "RfVolume=" + NesCore.RfVolume + "\r\n" +
                "\r\n" +
                "; ── Modern ──\r\n" +
                "StereoWidth=" + NesCore.StereoWidth + "\r\n" +
                "HaasDelay=" + NesCore.HaasDelay + "\r\n" +
                "HaasCrossfeed=" + NesCore.HaasCrossfeed + "\r\n" +
                "ReverbWet=" + NesCore.ReverbWet + "\r\n" +
                "CombFeedback=" + NesCore.CombFeedback + "\r\n" +
                "CombDamp=" + NesCore.CombDamp + "\r\n" +
                "BassBoostDb=" + NesCore.BassBoostDb + "\r\n" +
                "BassBoostFreq=" + NesCore.BassBoostFreq + "\r\n";

            FileWriteAllText(AudioPlusIniFile, content);
        }

        string JoyPadWayName(string xy_name, int value)
        {
            string tmp = "";

            if (xy_name == "X")
            {
                if (value == 0)
                    return "LEFT";

                if (value == 65535)
                    return "RIGHT";
            }

            if (xy_name == "Y")
            {
                if (value == 0)
                    return "UP";

                if (value == 65535)
                    return "DOWN";
            }

            return tmp;
        }
        bool app_running = true;
        void polling_listener()
        {
            while (app_running)
            {
                Thread.Sleep(_joystick.PeriodMin);
                List<joystickEvent> event_list = _joystick.joy_event_captur();
                foreach (joystickEvent joy_event in event_list)
                {
                    //for configure
                    if (configure)
                    {
                        bool handled = false;
                        AprNesUI.GetInstance().Invoke(new MethodInvoker(() =>
                        {
                            var cfg = AprNes_ConfigureUI.GetInstance();
                            if (cfg == null) return;
                            int expectedType = cfg.ExpectedJoyInputType();
                            if (expectedType == -1) return;               // no control focused
                            // expectedType 2 = direction controls: accept axis(0) OR button(1)
                            if (expectedType != 2 && joy_event.event_type != expectedType) return;

                            if (joy_event.event_type == 0) //方向鍵觸發
                            {
                                if (joy_event.way_type == 0)
                                    cfg.Setup_JoyPad_define(joy_event.joystick_id.ToString(), "X", 0, joy_event.way_value);
                                else
                                    cfg.Setup_JoyPad_define(joy_event.joystick_id.ToString(), "Y", 0, joy_event.way_value);
                            }
                            else //一般按鈕觸發
                                cfg.Setup_JoyPad_define(joy_event.joystick_id.ToString(), "Button " + joy_event.button_id.ToString(), joy_event.button_id, 128);

                            handled = true;
                        }));
                        if (handled) break;
                        continue;
                    }

                    //for gaming..
                    if (running)
                    {
                        KeyMap joy = KeyMap.NES_btn_A;
                        if (joy_event.event_type == 1)
                        {
                            string key = joy_event.joystick_id.ToString() + "," + "Button " + joy_event.button_id.ToString() + "," + joy_event.button_id.ToString();
                            if (AprNesUI.GetInstance().NES_KeyMAP_joypad.ContainsKey(key))
                                joy = AprNesUI.GetInstance().NES_KeyMAP_joypad[key];
                            else
                                continue;
                        }
                        else
                        {
                            string XY = (joy_event.way_type == 0) ? "X" : "Y";
                            string key = joy_event.joystick_id.ToString() + "," + JoyPadWayName(XY, joy_event.way_value) + "," + "0" + "," + joy_event.way_value;

                            if (AprNesUI.GetInstance().NES_KeyMAP_joypad.ContainsKey(key))
                                joy = AprNesUI.GetInstance().NES_KeyMAP_joypad[key];
                            else
                            {
                                string key_a = joy_event.joystick_id.ToString() + "," + JoyPadWayName(XY, 0) + "," + "0" + "," + "0";
                                string key_b = joy_event.joystick_id.ToString() + "," + JoyPadWayName(XY, 65535) + "," + "0" + "," + "65535";

                                if (NES_KeyMAP_joypad.ContainsKey(key_a) || (AprNesUI.GetInstance().NES_KeyMAP_joypad.ContainsKey(key_b)))
                                {
                                    if (XY == "X")
                                    {
                                        NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_LEFT);
                                        NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_RIGHT);
                                    }

                                    if (XY == "Y")
                                    {
                                        NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_UP);
                                        NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_DOWN);
                                    }
                                }
                                continue;
                            }

                        }

                        switch (joy)
                        {
                            case KeyMap.NES_btn_A:
                                {
                                    if (joy_event.button_event == 1)
                                        NesCore.P1_ButtonPress((byte)KeyMap.NES_btn_A);
                                    else
                                        NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_A);

                                }
                                break;
                            case KeyMap.NES_btn_B:
                                {
                                    if (joy_event.button_event == 1)
                                        NesCore.P1_ButtonPress((byte)KeyMap.NES_btn_B);
                                    else
                                        NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_B);
                                }
                                break;

                            case KeyMap.NES_btn_SELECT:
                                {
                                    if (joy_event.button_event == 1)
                                        NesCore.P1_ButtonPress((byte)KeyMap.NES_btn_SELECT);
                                    else
                                        NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_SELECT);
                                }
                                break;
                            case KeyMap.NES_btn_START:
                                {
                                    if (joy_event.button_event == 1)
                                        NesCore.P1_ButtonPress((byte)KeyMap.NES_btn_START);
                                    else
                                        NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_START);
                                }
                                break;

                            case KeyMap.NES_btn_UP:
                                if (joy_event.event_type == 0 || joy_event.button_event == 1)
                                    NesCore.P1_ButtonPress((byte)KeyMap.NES_btn_UP);
                                else
                                    NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_UP);
                                break;

                            case KeyMap.NES_btn_DOWN:
                                if (joy_event.event_type == 0 || joy_event.button_event == 1)
                                    NesCore.P1_ButtonPress((byte)KeyMap.NES_btn_DOWN);
                                else
                                    NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_DOWN);
                                break;
                            case KeyMap.NES_btn_LEFT:
                                if (joy_event.event_type == 0 || joy_event.button_event == 1)
                                    NesCore.P1_ButtonPress((byte)KeyMap.NES_btn_LEFT);
                                else
                                    NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_LEFT);
                                break;

                            case KeyMap.NES_btn_RIGHT:
                                if (joy_event.event_type == 0 || joy_event.button_event == 1)
                                    NesCore.P1_ButtonPress((byte)KeyMap.NES_btn_RIGHT);
                                else
                                    NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_RIGHT);
                                break;
                        }
                    }
                }
            }
        }

        Thread nes_t = null;
        bool running = false;
        public bool IsRunning => running;
        public string rom_file = "";
        public byte[] rom_bytes;
        byte[] current_rom_bytes;  // 保存已解壓的 ROM 資料供 Hard Reset 使用

public string GetRomInfo()
        {
            try
            {
                string info = "";
                if (rom_bytes == null || rom_bytes.Count() == 0) return "No load Rom !";
                if (!(rom_bytes[0] == 'N' && rom_bytes[1] == 'E' && rom_bytes[2] == 'S' && rom_bytes[3] == 0x1a)) return "Bad Magic Number ! (maybe no intro header ?)";
                info = "FileName : " + nes_name + "\r\n";
                info += "iNes Header\r\n";
                byte PRG_ROM_count = rom_bytes[4];
                info += "PRG-ROM count : " + PRG_ROM_count + "\r\n";
                byte CHR_ROM_count = rom_bytes[5];
                info += "CHR-ROM count : " + CHR_ROM_count + "\r\n";
                byte ROM_Control_1 = rom_bytes[6];
                if ((ROM_Control_1 & 1) != 0) info += "vertical mirroring\r\n";
                else info += "horizontal mirroring\r\n";
                if ((ROM_Control_1 & 2) != 0) info += "battery-backed RAM : yes\r\n";
                else info += "battery-backed RAM : no\r\n";
                if ((ROM_Control_1 & 4) != 0) info += "trainer : yes\r\n";
                else info += "trainer : no\r\n";
                if ((ROM_Control_1 & 8) != 0) info += "fourscreen mirroring : yes\r\n";
                else info += "fourscreen mirroring : no\r\n";
                byte ROM_Control_2 = rom_bytes[7];
                int mapper;
                bool iNesV2 = false;
                if ((ROM_Control_2 & 0xf) != 0)
                {
                    mapper = (ROM_Control_1 & 0xf0) >> 4;

                    if ((ROM_Control_2 & 0xc) == 8)
                    {
                        iNesV2 = true;
                        mapper = (byte)(((ROM_Control_1 & 0xf0) >> 4) | (ROM_Control_2 & 0xf0));
                        info += "Nes header 2.0 version !\r\n";
                    }
                    else
                    {
                        mapper = (ROM_Control_1 & 0xf0) >> 4;
                        info += "Old style Mapper info !\r\n";
                    }
                }
                else
                    mapper = (byte)(((ROM_Control_1 & 0xf0) >> 4) | (ROM_Control_2 & 0xf0));

                string mapper_name = MapperRegistry.GetName(mapper);

                info += "Mapper number : " + mapper + " " + mapper_name + "\r\n";

                if (iNesV2)
                {
                    byte RAM_banks_count = rom_bytes[8];
                    info += "RAM banks count : " + RAM_banks_count + "\r\n";
                }
                return info;
            }
            catch
            {
                return "parse error !";
            }
        }

        void UpdateSoundMenuText()
        {
            if (_soundMenuItem == null || !LangINI.LangLoadOK) return;
            string lang = AppConfigure["Lang"];
            _soundMenuItem.Text = NesCore.AudioEnabled
                ? LangINI.Get(lang, "SoundON",  "Sound ON")
                : LangINI.Get(lang, "SoundOFF", "Sound OFF");
        }

        void UpdateUltraAnalogMenuText()
        {
            if (_ultraAnalogMenuItem == null) return;
            _ultraAnalogMenuItem.Text = NesCore.UltraAnalog
                ? "Ultra Analog: ON"
                : "Ultra Analog: OFF";
        }

        public string rom_file_name = "";
        public string nes_name = "";

        string SRamPath() => rom_file_name + ".sav";

        void SaveSRam()
        {
            if (!NesCore.HasBattery) return;
            File.WriteAllBytes(SRamPath(), NesCore.DumpSRam());
        }

        void LoadSRam()
        {
            if (!NesCore.HasBattery) return;
            string path = SRamPath();
            if (File.Exists(path))
                NesCore.LoadSRam(File.ReadAllBytes(path));
            else
                File.WriteAllBytes(path, new byte[0x2000]);
        }

        InterfaceGraphic RenderObj;

        unsafe private void button1_Click(object sender, EventArgs e)
        {
            OpenFileDialog fd = new OpenFileDialog();
            fd.Filter = "nes file (*.nes *.zip)|*.nes;*.zip";
            if (fd.ShowDialog() != DialogResult.OK) return;
            LoadRomFromPath(fd.FileName);
        }

        unsafe void LoadRomFromPath(string filePath)
        {
            FileInfo fi = new FileInfo(filePath);
            if (fi.Extension.ToLower() == ".zip")
            {
                ZipArchive archive = ZipFile.OpenRead(fi.FullName);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.FullName.ToLower().EndsWith(".nes"))
                    {
                        nes_name = entry.Name;
                        Stream fs = entry.Open();
                        long length = entry.Length;
                        rom_bytes = new byte[length];
                        fs.Read(rom_bytes, 0, (int)length);
                        fs.Close();
                    }
                }
            }
            else
            {
                nes_name = fi.Name;
                rom_bytes = File.ReadAllBytes(filePath);
            }

            rom_file_name = filePath.Remove(filePath.Length - 4, 4);
            current_rom_bytes = rom_bytes;

            StopRecordingIfActive();

            if (nes_t != null)
            {
                try
                {
                    EndHighResPeriod();
                    NesCore.exit = true;
                    NesCore._event.Set();
                    nes_t.Join(500);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

            SaveSRam();
            NesCore.exit = false;
            NesCore.rom_file_name = rom_file_name;

            bool init_result = NesCore.init(rom_bytes);

            if (RenderObj != null) RenderObj.freeMem();
            RenderObj = (InterfaceGraphic)Activator.CreateInstance(Type.GetType(NesCore.AnalogEnabled ? "AprNes.Render_Analog" : "AprNes.Render_" + AppConfigure["filter"] + "_" + ScreenSize + "x"));
            RenderObj.init(NesCore.ScreenBuf1x, grfx);

            NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
            NesCore.VideoOutput += new EventHandler(VideoOutputDeal);

            if (!init_result)
            {
                fps_count_timer.Enabled = false;
                running = false;
                label3.Text = "fps : ";
                MessageBox.Show("fail !");
                return;
            }
            LoadSRam();
            _fpsDeadline = 0;
            _fpsStopWatch.Restart();
            if (NesCore.AudioEnabled) WaveOutPlayer.OpenAudio();
            BeginHighResPeriod();
            nes_t = new Thread(NesCore.run);
            nes_t.IsBackground = true;
            nes_t.Start();
            fps_count_timer.Enabled = true;
            running = true;

            // 記錄到最近開啟清單
            AddRecentROM(filePath);
        }
        int fps = 0;
        readonly Stopwatch _fpsSw = Stopwatch.StartNew();
        private void fps_count_timer_Tick(object sender, EventArgs e)
        {
            this.Invoke((MethodInvoker)delegate
            {
                double elapsed = _fpsSw.Elapsed.TotalSeconds;
                _fpsSw.Restart();
                int count = System.Threading.Interlocked.Exchange(ref NesCore.frame_count, 0);
                fps = count;
                double actualFps = elapsed > 0 ? count / elapsed : 0;
                label3.Text = "fps : " + actualFps.ToString("F1");
            });
        }

        private void AprNesUI_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopRecordingIfActive();
            app_running = false;
            EndHighResPeriod();
            NesCore.exit = true;
            SaveSRam();
            WaveOutPlayer.CloseAudio();
            Thread.Sleep(10);
        }

        //http://stackoverflow.com/questions/11754874/keydown-not-firing-for-up-down-left-and-right
        protected override bool ProcessCmdKey(ref System.Windows.Forms.Message msg, System.Windows.Forms.Keys keyData)
        {
            // ── 全域快捷鍵（不需要 ROM 在執行） ──
            switch (keyData)
            {
                case Keys.Control | Keys.O:
                    button1_Click(null, null);
                    return true;
                case Keys.F11:
                    if (ScreenCenterFull || analogFullScreen)
                        fun8ToolStripMenuItem_Click(null, null);
                    else
                        fullScreeenToolStripMenuItem_Click(null, null);
                    return true;
                case Keys.Escape:
                    if (ScreenCenterFull || analogFullScreen)
                        fun8ToolStripMenuItem_Click(null, null);
                    return true;
                case Keys.Control | Keys.R:
                    if (running) label4_Click(null, null); // Reset
                    return true;
            }

            //for KeyDown check
            if (!running) return true;
            int keyboard_key = (int)keyData;

            if (keyboard_key == 65616)
            {
                NESCaptureScreen();
                return true;
            }
            if (NES_KeyMAP.ContainsKey(keyboard_key))
                NesCore.P1_ButtonPress((byte)NES_KeyMAP[keyboard_key]);
            return true;
        }

        bool writing = false;
        public void NESCaptureScreen()
        {

            if (!running) return;
            if (writing == true)
                return;
            writing = true;
            while (NesCore.screen_lock)
                Thread.Sleep(0);

            NesCore._event.Reset();

            DateTime dt = DateTime.Now;
            string stamp = (dt.ToLongDateString() + " " + dt.ToLongTimeString()).Replace(":", "-");
            try
            {
                if (!System.IO.Directory.Exists(AppConfigure["CaptureScreenPath"]))
                    System.IO.Directory.CreateDirectory(AppConfigure["CaptureScreenPath"]);
                RenderObj.GetOutput().Save(AppConfigure["CaptureScreenPath"] + @"\Screen-" + stamp + ".png", System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception e) { Console.WriteLine("i:" + e.Message); }

            NesCore._event.Set();

            Console.WriteLine("Screen-" + stamp + ".png" + " write finish !");
            writing = false;

            MessageBox.Show(AppConfigure["CaptureScreenPath"] + @"\Screen-" + stamp + ".png" + " " + "save!");
        }

        private void AprNesUI_KeyUp(object sender, KeyEventArgs e)
        {
            if (!running) return;
            if (NES_KeyMAP.ContainsKey(e.KeyValue))
                NesCore.P1_ButtonUnPress((byte)NES_KeyMAP[e.KeyValue]);
        }

        bool LimitFPS = true;
        const double NES_FRAME_SECONDS = 1.0 / 60.0988;
        readonly Stopwatch _fpsStopWatch = new Stopwatch();
        double _fpsDeadline = 0;

        unsafe void VideoOutputDeal(object sender, EventArgs e)
        {
            RenderObj.Render();
            if (VideoRecorder.IsRecording)
            {
                // Analog mode: always use fresh AnalogScreenBuf pointer (can be reallocated)
                // Non-analog: use cached RenderOutputPtr (stable, owned by RenderObj)
                uint* capturePtr = NesCore.AnalogEnabled && NesCore.AnalogScreenBuf != null
                    ? NesCore.AnalogScreenBuf : NesCore.RenderOutputPtr;
                if (capturePtr != null)
                    VideoRecorder.PushFrame(capturePtr);
            }
            if (LimitFPS)
            {
                if (!_fpsStopWatch.IsRunning) _fpsStopWatch.Restart();
                double now = _fpsStopWatch.Elapsed.TotalSeconds;
                if (_fpsDeadline < now)
                    _fpsDeadline = now + NES_FRAME_SECONDS;
                while (_fpsDeadline - _fpsStopWatch.Elapsed.TotalSeconds > 0.001)
                    Thread.Sleep(1);
                while (_fpsStopWatch.Elapsed.TotalSeconds < _fpsDeadline) { }
                _fpsDeadline += NES_FRAME_SECONDS;
            }
        }

        private void label1_MouseEnter(object sender, EventArgs e)
        {
            this.Cursor = Cursors.Hand;
            (sender as Label).BackColor = Color.LightGray;
        }

        private void label1_MouseLeave(object sender, EventArgs e)
        {
            this.Cursor = Cursors.Default;
            (sender as Label).BackColor = Color.WhiteSmoke;
        }

        bool configure = false;
        private void label2_Click_1(object sender, EventArgs e)
        {
            configure = true;
            AprNes_ConfigureUI.GetInstance().StartPosition = FormStartPosition.CenterParent;
            AprNes_ConfigureUI.GetInstance().init();
            AprNes_ConfigureUI.GetInstance().ShowDialog(this);
            configure = false;
        }
        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            AprNes_Infocs AprNesInf = new AprNes_Infocs();
            AprNesInf.StartPosition = FormStartPosition.CenterParent;
            AprNesInf.ShowDialog(this);
        }

        private void label4_Click(object sender, EventArgs e)
        {
            Reset();
        }

        unsafe public void Reset()
        {
            if (!running) return;

            SaveSRam();
            NesCore.rom_file_name = rom_file_name;

            NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
            NesCore._event.Reset();
            while (!NesCore.emuWaiting) Thread.Sleep(1);
            if (RenderObj != null) RenderObj.freeMem();
            RenderObj = (InterfaceGraphic)Activator.CreateInstance(Type.GetType(NesCore.AnalogEnabled ? "AprNes.Render_Analog" : "AprNes.Render_" + AppConfigure["filter"] + "_" + ScreenSize + "x"));
            RenderObj.init(NesCore.ScreenBuf1x, grfx);
            NesCore.VideoOutput += new EventHandler(VideoOutputDeal);

            NesCore.SoftReset();   // 設 flag（模擬線程暫停中，無 race condition）
            NesCore._event.Set();  // 恢復模擬線程，cpu_step 中偵測 softreset flag
        }

        /// <summary>
        /// 全螢幕中 AnalogMode 切換時的安全過渡：
        /// 先退出目前全螢幕 → 套用設定 → 重新進入正確的全螢幕模式
        /// </summary>
        public void FullScreenModeTransition(bool prevAnalog)
        {
            bool wasAnalogFS = analogFullScreen;
            bool wasNormalFS = ScreenCenterFull && !analogFullScreen;
            bool nowAnalog   = NesCore.AnalogEnabled;

            if (!wasAnalogFS && !wasNormalFS) return; // 不在全螢幕中，不需處理

            // 1. 先退出目前全螢幕（回到 windowed）
            if (wasAnalogFS)
                ExitAnalogFullScreen();
            else
                fun8ToolStripMenuItem_Click(null, null); // 退出一般全螢幕

            // 2. 在 windowed 模式下套用設定
            initUIsize();
            ApplyRenderSettings();

            // 3. 重新進入正確的全螢幕
            fullScreeenToolStripMenuItem_Click(null, null);
        }

        public bool IsInFullScreen => ScreenCenterFull || analogFullScreen;

        // 僅重建 RenderObj（filter/尺寸變更），不重置遊戲狀態
        unsafe public void ApplyRenderSettings()
        {
            if (!running) return;

            NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
            NesCore._event.Reset();
            // 等待模擬執行緒完成當前整幀並阻塞於 _event.WaitOne()
            // screen_lock 僅覆蓋 RenderScreen()，不足以確保 DemodulateRow 等掃描線處理已結束
            while (!NesCore.emuWaiting) Thread.Sleep(1);

            // AnalogEnabled 時：重建 CrtScreen 快取，並在必要時重新分配 AnalogScreenBuf
            // 注意：僅在 buf 尺寸改變（AnalogSize 切換）或 buf 不存在時才重新分配。
            // 若尺寸不變（例如只切換 VideoInput），保留現有 buf，避免渲染執行緒寫入時發生 race condition。
            if (NesCore.AnalogEnabled)
            {
                NesCore.SyncAnalogConfig();  // 先同步 AnalogSize 等參數，確保 NesCore.Crt_DstW/DstH 正確
                int needed = NesCore.Crt_DstW * NesCore.Crt_DstH;
                unsafe
                {
                    if (NesCore.AnalogScreenBuf == null || NesCore.AnalogBufSize != needed)
                    {
                        if (NesCore.AnalogScreenBuf != null)
                        {
                            System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)NesCore.AnalogScreenBuf);
                            NesCore.AnalogScreenBuf = null;
                        }
                        NesCore.AnalogBufSize   = needed;
                        NesCore.AnalogScreenBuf = (uint*)System.Runtime.InteropServices.Marshal.AllocHGlobal(
                            sizeof(uint) * needed);
                    }
                }
                NesCore.SyncAnalogConfig();  // buffer 重新分配後再同步指標
                NesCore.Ntsc_Init();
                NesCore.Crt_Init();
            }

            if (RenderObj != null) RenderObj.freeMem();
            RenderObj = (InterfaceGraphic)Activator.CreateInstance(Type.GetType(NesCore.AnalogEnabled ? "AprNes.Render_Analog" : "AprNes.Render_" + AppConfigure["filter"] + "_" + ScreenSize + "x"));
            RenderObj.init(NesCore.ScreenBuf1x, grfx);
            NesCore.VideoOutput += new EventHandler(VideoOutputDeal);
            NesCore._event.Set();
        }

        unsafe public void HardReset()
        {
            if (!running || current_rom_bytes == null) return;

            // 停止模擬線程
            EndHighResPeriod();
            NesCore.exit = true;
            NesCore._event.Set();
            if (nes_t != null)
            {
                nes_t.Join(500);
                if (nes_t.IsAlive) nes_t.Join(500);
            }

            SaveSRam();
            WaveOutPlayer.CloseAudio();

            // 完整重新初始化（等同 power cycle）
            NesCore.exit = false;
            NesCore.rom_file_name = rom_file_name;

            bool init_result = NesCore.init(current_rom_bytes);

            if (RenderObj != null) RenderObj.freeMem();
            RenderObj = (InterfaceGraphic)Activator.CreateInstance(Type.GetType(NesCore.AnalogEnabled ? "AprNes.Render_Analog" : "AprNes.Render_" + AppConfigure["filter"] + "_" + ScreenSize + "x"));
            RenderObj.init(NesCore.ScreenBuf1x, grfx);

            NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
            NesCore.VideoOutput += new EventHandler(VideoOutputDeal);

            if (!init_result)
            {
                fps_count_timer.Enabled = false;
                running = false;
                label3.Text = "fps : ";
                MessageBox.Show("Hard Reset fail !");
                return;
            }

            LoadSRam();
            _fpsDeadline = 0;
            _fpsStopWatch.Restart();
            if (NesCore.AudioEnabled) WaveOutPlayer.OpenAudio();
            BeginHighResPeriod();
            nes_t = new Thread(NesCore.run);
            nes_t.IsBackground = true;
            nes_t.Start();
        }

        private void fun7ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            HardReset();
        }

        private void _soundMenuItem_Click(object sender, EventArgs e)
        {
            NesCore.AudioEnabled = !NesCore.AudioEnabled;
            if (!NesCore.AudioEnabled)
                WaveOutPlayer.CloseAudio();
            else if (running)
                WaveOutPlayer.OpenAudio();
            UpdateSoundMenuText();
            // 儲存設定到 ini
            AppConfigure["Sound"] = NesCore.AudioEnabled ? "1" : "0";
            Configure_Write();
        }

        private unsafe void _ultraAnalogMenuItem_Click(object sender, EventArgs e)
        {
            NesCore.UltraAnalog = !NesCore.UltraAnalog;
            UpdateUltraAnalogMenuText();
            AppConfigure["UltraAnalog"] = NesCore.UltraAnalog ? "1" : "0";
            Configure_Write();

            // 同步渲染管線（Ntsc._ultraAnalog 需要與 NesCore.UltraAnalog 一致）
            if (running && NesCore.AnalogEnabled)
            {
                NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
                NesCore._event.Reset();
                while (!NesCore.emuWaiting) Thread.Sleep(1);

                NesCore.SyncAnalogConfig();
                NesCore.Ntsc_Init();
                NesCore.Crt_Init();

                NesCore.VideoOutput += new EventHandler(VideoOutputDeal);
                NesCore._event.Set();
            }
        }

        // ── Recording ──
        static string _ffmpegPath;

        static string GetFfmpegPath()
        {
            if (_ffmpegPath != null) return _ffmpegPath;
            string path = Path.Combine(Application.StartupPath, "tools", "ffmpeg", "ffmpeg.exe");
            _ffmpegPath = File.Exists(path) ? path : "";
            return _ffmpegPath;
        }

        void UpdateRecordMenuVisibility()
        {
            bool hasFfmpeg = !string.IsNullOrEmpty(GetFfmpegPath());
            _recordMenuItem.Visible = hasFfmpeg;
            if (hasFfmpeg)
            {
                _recordVideoMenuItem.Text = VideoRecorder.IsRecording
                    ? "■ Stop Recording" : "● Record Video";
                _recordVideoMenuItem.Enabled = running || VideoRecorder.IsRecording;
            }
        }

        unsafe void _recordVideoMenuItem_Click(object sender, EventArgs e)
        {
            if (VideoRecorder.IsRecording)
            {
                VideoRecorder.Stop();
                UpdateRecordMenuVisibility();
                this.Text = "AprNes";
                return;
            }

            if (!running) return;

            string capturesDir = Path.Combine(Application.StartupPath, "Captures", "Video");
            // Analog mode: read dimensions fresh from CrtScreen (buffer can be reallocated)
            int recW = NesCore.AnalogEnabled ? NesCore.Crt_DstW : NesCore.RenderOutputW;
            int recH = NesCore.AnalogEnabled ? NesCore.Crt_DstH : NesCore.RenderOutputH;
            bool ok = VideoRecorder.Start(GetFfmpegPath(), capturesDir, recW, recH);
            if (ok)
            {
                UpdateRecordMenuVisibility();
                this.Text = "AprNes [REC]";
            }
            else
            {
                string err = VideoRecorder.LastError ?? "Unknown error";
                MessageBox.Show("Failed to start recording.\n\n" + err,
                    "Recording Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void StopRecordingIfActive()
        {
            if (VideoRecorder.IsRecording)
            {
                VideoRecorder.Stop();
                try { this.Text = "AprNes"; } catch { }
            }
        }

        private void AprNesUI_Shown(object sender, EventArgs e)
        {
            initUIsize();
            UpdateSoundMenuText();
            UpdateUltraAnalogMenuText();
            UpdateRecordMenuVisibility();
            contextMenuStrip1.Opening += (s, ev) =>
            {
                UpdateRecordMenuVisibility();
                bool inFS = ScreenCenterFull || analogFullScreen;
                _ultraAnalogMenuItem.Enabled = !inFS;
                fun3ToolStripMenuItem.Enabled = !inFS;  // Config
            };

            _joystick.Init(this.Handle);
            new Thread(polling_listener).Start();
            new Thread(() =>
            {
                Thread.Sleep(50);
                Invoke(new MethodInvoker(() =>
                {
                    Opacity = 100;
                }));
                st.Stop();
                Console.WriteLine("UI Finish : " + st.ElapsedMilliseconds);
                Debug.WriteLine("UI Finish : " + st.ElapsedMilliseconds);
            }).Start();
        }

        private void RomInf_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            AprNes_RomInfoUI RomInfo = new AprNes_RomInfoUI();
            RomInfo.StartPosition = FormStartPosition.CenterParent;
            RomInfo.ShowDialog(this);
        }

        private void fun1ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            button1_Click(null, null);
        }

        private void fun5ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void fun2ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            label4_Click(null, null);
        }

        private void fun3ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            label2_Click_1(null, null);
        }

        private void fun6ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            linkLabel1_LinkClicked(null, null);
        }

        private void fun4ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RomInf_LinkClicked(null, null);
        }

        bool ScreenCenterFull = false;
        bool analogFullScreen = false;

        // 類比全螢幕進入前保存的狀態，退出時還原
        int savedPanelW, savedPanelH, savedPanelX, savedPanelY;
        int savedFormW, savedFormH;

        // 8:7 PAR content aspect ratio: (256 × 8/7) / 210 ≈ 1.3933
        const double AnalogContentAR = (256.0 * 8.0 / 7.0) / 210.0;

        unsafe void EnterAnalogFullScreen()
        {
            if (this.WindowState != FormWindowState.Maximized) Opacity = 0;

            // 保存原始狀態
            savedPanelW = panel1.Width;
            savedPanelH = panel1.Height;
            savedPanelX = panel1.Left;
            savedPanelY = panel1.Top;
            savedFormW  = this.Width;
            savedFormH  = this.Height;

            // 動態計算顯示區域（8:7 PAR，適應任何螢幕比例）
            int screenW = Screen.PrimaryScreen.Bounds.Width;
            int screenH = Screen.PrimaryScreen.Bounds.Height;
            double screenAR = (double)screenW / screenH;

            int displayW, displayH;
            if (screenAR > AnalogContentAR)
            {
                displayH = screenH;
                displayW = (int)(screenH * AnalogContentAR);
            }
            else
            {
                displayW = screenW;
                displayH = (int)(screenW / AnalogContentAR);
            }
            int padX = (screenW - displayW) / 2;
            int padY = (screenH - displayH) / 2;

            // 暫停模擬執行緒，安全重新分配 buffer
            if (running)
            {
                NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
                NesCore._event.Reset();
                while (!NesCore.emuWaiting) Thread.Sleep(1);
            }

            // 設定全螢幕覆寫解析度
            NesCore.Crt_SetFullscreenSize(displayW, displayH);

            // 重新分配 AnalogScreenBuf
            if (NesCore.AnalogEnabled)
            {
                NesCore.SyncAnalogConfig();
                int needed = NesCore.Crt_DstW * NesCore.Crt_DstH;
                if (NesCore.AnalogScreenBuf == null || NesCore.AnalogBufSize != needed)
                {
                    if (NesCore.AnalogScreenBuf != null)
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)NesCore.AnalogScreenBuf);
                        NesCore.AnalogScreenBuf = null;
                    }
                    NesCore.AnalogBufSize   = needed;
                    NesCore.AnalogScreenBuf = (uint*)System.Runtime.InteropServices.Marshal.AllocHGlobal(
                        sizeof(uint) * needed);
                }
                NesCore.SyncAnalogConfig();
                NesCore.Ntsc_Init();
                NesCore.Crt_Init();
            }

            // UI 全螢幕
            panel1.Visible = false;
            panel1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            UIAbout.Visible = RomInf.Visible = UIOpenRom.Visible = UIReset.Visible = UIConfig.Visible = label3.Visible = false;
            this.BackColor = Color.Black;
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;

            panel1.Size = new Size(displayW, displayH);
            panel1.Location = new Point(padX, padY);

            // 重建 Graphics + RenderObj
            grfx?.Dispose();
            grfx = panel1.CreateGraphics();
            if (RenderObj != null) RenderObj.freeMem();
            RenderObj = (InterfaceGraphic)Activator.CreateInstance(Type.GetType("AprNes.Render_Analog"));
            RenderObj.init(NesCore.ScreenBuf1x, grfx);

            label3.Location = new Point(0, 0);
            panel1.Visible = true;
            label3.Visible = true;
            this.Refresh();
            Opacity = 100;
            ScreenCenterFull = true;
            analogFullScreen = true;

            // 恢復模擬執行緒
            if (running)
            {
                NesCore.VideoOutput += new EventHandler(VideoOutputDeal);
                NesCore._event.Set();
            }

            Configure_Write();
        }

        unsafe void ExitAnalogFullScreen()
        {
            // 暫停模擬執行緒
            if (running)
            {
                NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
                NesCore._event.Reset();
                while (!NesCore.emuWaiting) Thread.Sleep(1);
            }

            // 清除全螢幕覆寫，恢復 AnalogSize 驅動的 DstW/DstH
            NesCore.Crt_ClearFullscreenSize();

            // 重新分配 AnalogScreenBuf 回原始大小
            if (NesCore.AnalogEnabled)
            {
                NesCore.SyncAnalogConfig();
                int needed = NesCore.Crt_DstW * NesCore.Crt_DstH;
                if (NesCore.AnalogScreenBuf == null || NesCore.AnalogBufSize != needed)
                {
                    if (NesCore.AnalogScreenBuf != null)
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)NesCore.AnalogScreenBuf);
                        NesCore.AnalogScreenBuf = null;
                    }
                    NesCore.AnalogBufSize   = needed;
                    NesCore.AnalogScreenBuf = (uint*)System.Runtime.InteropServices.Marshal.AllocHGlobal(
                        sizeof(uint) * needed);
                }
                NesCore.SyncAnalogConfig();
                NesCore.Ntsc_Init();
                NesCore.Crt_Init();
            }

            // 還原 UI 狀態
            panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.BackColor = SystemColors.Menu;
            this.WindowState = FormWindowState.Normal;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            ScreenCenterFull = false;
            analogFullScreen = false;

            // 透過 initUIsize() 統一處理排版、grfx/RenderObj 重建
            label3.Location = new Point(208, 8);
            initUIsize();

            // 恢復模擬執行緒
            if (running)
            {
                NesCore.VideoOutput += new EventHandler(VideoOutputDeal);
                NesCore._event.Set();
            }

            Configure_Write();
        }

        private void fun8ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (analogFullScreen) { ExitAnalogFullScreen(); return; }
            panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.BackColor = SystemColors.Menu;
            this.WindowState = FormWindowState.Normal;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            ScreenCenterFull = false;
            initUIsize();
        }

        private void fullScreeenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (NesCore.AnalogEnabled) { EnterAnalogFullScreen(); return; }

            if (this.WindowState != FormWindowState.Maximized) Opacity = 0;
            panel1.Visible = false;
            panel1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            UIAbout.Visible = RomInf.Visible = UIOpenRom.Visible = UIReset.Visible = UIConfig.Visible = label3.Visible = false;
            this.BackColor = Color.Black;
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.CenterToScreen();
            panel1.Left = (this.ClientSize.Width - panel1.Width) / 2;
            panel1.Top = (this.ClientSize.Height - panel1.Height) / 2;
            label3.Location = new Point(0, 0);
            panel1.Visible = true;
            label3.Visible = true;
            this.Refresh();
            Opacity = 100;
            ScreenCenterFull = true;
            Configure_Write();
        }

        private void normalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (analogFullScreen) { ExitAnalogFullScreen(); return; }
            panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.BackColor = SystemColors.Menu;
            this.WindowState = FormWindowState.Normal;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            label3.Location = new Point(208, 8);
            ScreenCenterFull = false;
            Configure_Write();
            initUIsize();
        }

    }
}
