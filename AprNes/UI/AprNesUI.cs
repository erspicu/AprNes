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

        static readonly string LogFile = System.IO.Path.Combine(Application.StartupPath, "AprNes.log");

        static void LogError(string msg)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine;
                System.IO.File.AppendAllText(LogFile, line);
            }
            catch { }
        }

        Stopwatch st = new Stopwatch();//test UI finish time

        public AprNesUI()
        {
            st.Restart();
            InitializeComponent();
            NesCore.OnError = msg => { LogError(msg); MessageBox.Show(msg); };

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

            fun1ToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["rom"];
            fun3ToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["setting"];
            fun2ToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["reset"] + " (Soft)";
            fun7ToolStripMenuItem.Text = "Hard Reset";
            fun6ToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["about"];
            fun5ToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["appclose"];
            fullScreeenToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["fullscreen"];
            normalToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["normal"];
            screenModeToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["screenmode"];
            fun4ToolStripMenuItem.Text = LangINI.lang_table[AppConfigure["Lang"]]["rominfo"];
            if (_recentROMsMenuItem != null)
                _recentROMsMenuItem.Text = LangINI.Get(AppConfigure["Lang"], "recent", "Recent");

            // MenuStrip i18n
            string lang = AppConfigure["Lang"];
            _menuFile.Text = LangINI.Get(lang, "menu_file", "File");
            _menuFileOpen.Text = LangINI.lang_table[lang]["rom"];
            _menuFileRecent.Text = LangINI.Get(lang, "recent", "Recent");
            _menuFileExit.Text = LangINI.lang_table[lang]["appclose"];
            _menuEmulation.Text = LangINI.Get(lang, "menu_emulation", "Emulation");
            _menuEmulationSoftReset.Text = LangINI.lang_table[lang]["reset"] + " (Soft)";
            _menuEmulationHardReset.Text = "Hard Reset";
            _menuEmulationRegion.Text = LangINI.Get(lang, "region", "Region");
            _menuEmulationLimitFps.Text = LangINI.lang_table[lang]["limitfps"];
            _menuEmulationPerdotFSM.Text = LangINI.lang_table[lang]["perdotFSM"];
            _menuView.Text = LangINI.Get(lang, "menu_view", "View");
            _menuViewToggleFullScreen.Text = LangINI.lang_table[lang]["fullscreen"];
            _menuViewSound.Text = NesCore.AudioEnabled
                ? LangINI.lang_table[lang]["SoundON"]
                : LangINI.lang_table[lang]["SoundOFF"];
            _menuViewUltraAnalog.Text = LangINI.Get(lang, "ultra_analog", "Ultra Analog")
                + (NesCore.UltraAnalog ? ": ON" : ": OFF");
            _menuTools.Text = LangINI.Get(lang, "menu_tools", "Tools");
            _menuToolsScreenshot.Text = LangINI.Get(lang, "screenshot", "Screenshot");
            _menuToolsRomInfo.Text = LangINI.lang_table[lang]["rominfo"];
            _menuToolsConfig.Text = LangINI.lang_table[lang]["setting"];
            _menuHelp.Text = LangINI.Get(lang, "menu_help", "Help");
            _menuHelpShortcuts.Text = LangINI.Get(lang, "shortcuts", "Keyboard Shortcuts");
            _menuHelpAbout.Text = LangINI.lang_table[lang]["about"];
        }

        int ScreenSize = 1;

        // Parse scale multiplier from INI value like "xbrz_4" → 4, "none" → 1
        static int GetResizeScale(string iniVal)
        {
            if (string.IsNullOrEmpty(iniVal) || iniVal == "none") return 1;
            string[] parts = iniVal.Split('_');
            int s;
            return (parts.Length == 2 && int.TryParse(parts[1], out s)) ? s : 1;
        }

        // Parse filter type from INI value like "xbrz_4" → XBRz, "nn_3" → NN
        static ResizeFilter GetResizeFilter(string iniVal)
        {
            if (string.IsNullOrEmpty(iniVal) || iniVal == "none") return ResizeFilter.None;
            string prefix = iniVal.Split('_')[0];
            switch (prefix)
            {
                case "xbrz":   return ResizeFilter.XBRz;
                case "scalex": return ResizeFilter.ScaleX;
                case "nn":     return ResizeFilter.NN;
                default:       return ResizeFilter.None;
            }
        }

        // Create and configure a Render_resize from current AppConfigure
        unsafe Render_resize CreateRenderResize()
        {
            string s1Val = AppConfigure.ContainsKey("ResizeStage1") ? AppConfigure["ResizeStage1"] : "none";
            string s2Val = AppConfigure.ContainsKey("ResizeStage2") ? AppConfigure["ResizeStage2"] : "none";
            bool scanline = AppConfigure.ContainsKey("Scanline") && AppConfigure["Scanline"] == "1";

            var r = new Render_resize();
            r.Configure(GetResizeFilter(s1Val), GetResizeScale(s1Val),
                         GetResizeFilter(s2Val), GetResizeScale(s2Val),
                         scanline);
            return r;
        }

        public void initUIsize()
        {
            // 停止 async 渲染執行緒：initUIsize 會 dispose grfx / resize panel，
            // 若渲染執行緒正在用同一個 HDC 做 SetDIBitsToDevice 會死鎖
            StopAnalogRenderThread();

            // AnalogEnabled 時依 AnalogSize 決定（256×N × 210×N，8:7 AR）
            // 直接從 NesCore.AnalogSize 計算，避免依賴 NesCore.Crt_DstW/DstH（可能尚未 sync）
            int renderWidth  = NesCore.AnalogEnabled ? 256 * NesCore.AnalogSize : 256 * ScreenSize;
            int renderHeight = NesCore.AnalogEnabled ? 210 * NesCore.AnalogSize : 240 * ScreenSize;

            panel1.Visible = false;
            panel1.Width  = renderWidth;
            panel1.Height = renderHeight;

            // panel 改變大小後重建 Graphics context
            grfx?.Dispose();
            grfx = panel1.CreateGraphics();

            // grfx 重建後，RenderObj 持有的舊 Graphics 已失效，需釋放舊 buffer 再重新 init
            unsafe
            {
                if (RenderObj != null)
                {
                    RenderObj.freeMem();
                    RenderObj.init(NesCore.ScreenBuf1x, grfx);
                }
            }

            if (ScreenCenterFull)
            {
                label3.Visible = false;

                fullScreeenToolStripMenuItem_Click(null, null);
                panel1.Visible = true;
                return;
            }

            label3.Visible = true;
            panel1.Location = new Point(5, 35);
            this.Width  = renderWidth  + 26;   // panel + 左右邊框
            this.Height = renderHeight + 92;   // panel + 上工具列(35) + 下按鈕列(57)

            label3.Location = new Point(5, renderHeight + 37);
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
            NES_btn_RIGHT = 7,
            // P2
            NES_btn_P2_A = 8,
            NES_btn_P2_B = 9,
            NES_btn_P2_SELECT = 10,
            NES_btn_P2_START = 11,
            NES_btn_P2_UP = 12,
            NES_btn_P2_DOWN = 13,
            NES_btn_P2_LEFT = 14,
            NES_btn_P2_RIGHT = 15
        }

        public int key_A = 90;
        public int key_B = 88;
        public int key_SELECT = 83;
        public int key_START = 65;
        public int key_RIGHT = 39;
        public int key_LEFT = 37;
        public int key_UP = 38;
        public int key_DOWN = 40;

        // P2 keyboard (default: no binding)
        public int key_P2_A = 0;
        public int key_P2_B = 0;
        public int key_P2_SELECT = 0;
        public int key_P2_START = 0;
        public int key_P2_RIGHT = 0;
        public int key_P2_LEFT = 0;
        public int key_P2_UP = 0;
        public int key_P2_DOWN = 0;

        string joypad_A = "";
        string joypad_B = "";
        string joypad_SELECT = "";
        string joypad_START = "";
        string joypad_UP = "";
        string joypad_DOWN = "";
        string joypad_LEFT = "";
        string joypad_RIGHT = "";

        // P2 joypad
        string joypad_P2_A = "";
        string joypad_P2_B = "";
        string joypad_P2_SELECT = "";
        string joypad_P2_START = "";
        string joypad_P2_UP = "";
        string joypad_P2_DOWN = "";
        string joypad_P2_LEFT = "";
        string joypad_P2_RIGHT = "";

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
                AppConfigure["ResizeStage1"] = "none";
                AppConfigure["ResizeStage2"] = "none";
                AppConfigure["Scanline"] = "0";
                AppConfigure["CaptureScreenPath"] = Path.Combine(Application.StartupPath, "Captures", "Screenshots");
                AppConfigure["CaptureVideoPath"] = Path.Combine(Application.StartupPath, "Captures", "Video");
                AppConfigure["CaptureAudioPath"] = Path.Combine(Application.StartupPath, "Captures", "Audio");
                AppConfigure["VideoQuality"] = "90";
                AppConfigure["AudioBitrate"] = "160";
                AppConfigure["joypad_A"] = "";
                AppConfigure["joypad_B"] = "";
                AppConfigure["joypad_SELECT"] = "";
                AppConfigure["joypad_START"] = "";
                AppConfigure["joypad_UP"] = "";
                AppConfigure["joypad_DOWN"] = "";
                AppConfigure["joypad_LEFT"] = "";
                AppConfigure["joypad_RIGHT"] = "";
                // P2 keyboard
                AppConfigure["key_P2_A"] = "0";
                AppConfigure["key_P2_B"] = "0";
                AppConfigure["key_P2_SELECT"] = "0";
                AppConfigure["key_P2_START"] = "0";
                AppConfigure["key_P2_UP"] = "0";
                AppConfigure["key_P2_DOWN"] = "0";
                AppConfigure["key_P2_LEFT"] = "0";
                AppConfigure["key_P2_RIGHT"] = "0";
                // P2 joypad
                AppConfigure["joypad_P2_A"] = "";
                AppConfigure["joypad_P2_B"] = "";
                AppConfigure["joypad_P2_SELECT"] = "";
                AppConfigure["joypad_P2_START"] = "";
                AppConfigure["joypad_P2_UP"] = "";
                AppConfigure["joypad_P2_DOWN"] = "";
                AppConfigure["joypad_P2_LEFT"] = "";
                AppConfigure["joypad_P2_RIGHT"] = "";
                AppConfigure["Lang"] = GetDefaultLang();
                AppConfigure["filter"] = "resize";
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
            _menuEmulationLimitFps.Checked = LimitFPS;

            key_A = int.Parse(AppConfigure["key_A"]);
            key_B = int.Parse(AppConfigure["key_B"]);
            key_SELECT = int.Parse(AppConfigure["key_SELECT"]);
            key_START = int.Parse(AppConfigure["key_START"]);
            key_RIGHT = int.Parse(AppConfigure["key_RIGHT"]);
            key_LEFT = int.Parse(AppConfigure["key_LEFT"]);
            key_UP = int.Parse(AppConfigure["key_UP"]);
            key_DOWN = int.Parse(AppConfigure["key_DOWN"]);

            joypad_A = AppConfigure.ContainsKey("joypad_A") ? AppConfigure["joypad_A"] : "";
            joypad_B = AppConfigure.ContainsKey("joypad_B") ? AppConfigure["joypad_B"] : "";
            joypad_SELECT = AppConfigure.ContainsKey("joypad_SELECT") ? AppConfigure["joypad_SELECT"] : "";
            joypad_START = AppConfigure.ContainsKey("joypad_START") ? AppConfigure["joypad_START"] : "";
            joypad_UP = AppConfigure.ContainsKey("joypad_UP") ? AppConfigure["joypad_UP"] : "";
            joypad_DOWN = AppConfigure.ContainsKey("joypad_DOWN") ? AppConfigure["joypad_DOWN"] : "";
            joypad_LEFT = AppConfigure.ContainsKey("joypad_LEFT") ? AppConfigure["joypad_LEFT"] : "";
            joypad_RIGHT = AppConfigure.ContainsKey("joypad_RIGHT") ? AppConfigure["joypad_RIGHT"] : "";

            // P2 keyboard
            key_P2_A = AppConfigure.ContainsKey("key_P2_A") ? int.Parse(AppConfigure["key_P2_A"]) : 0;
            key_P2_B = AppConfigure.ContainsKey("key_P2_B") ? int.Parse(AppConfigure["key_P2_B"]) : 0;
            key_P2_SELECT = AppConfigure.ContainsKey("key_P2_SELECT") ? int.Parse(AppConfigure["key_P2_SELECT"]) : 0;
            key_P2_START = AppConfigure.ContainsKey("key_P2_START") ? int.Parse(AppConfigure["key_P2_START"]) : 0;
            key_P2_UP = AppConfigure.ContainsKey("key_P2_UP") ? int.Parse(AppConfigure["key_P2_UP"]) : 0;
            key_P2_DOWN = AppConfigure.ContainsKey("key_P2_DOWN") ? int.Parse(AppConfigure["key_P2_DOWN"]) : 0;
            key_P2_LEFT = AppConfigure.ContainsKey("key_P2_LEFT") ? int.Parse(AppConfigure["key_P2_LEFT"]) : 0;
            key_P2_RIGHT = AppConfigure.ContainsKey("key_P2_RIGHT") ? int.Parse(AppConfigure["key_P2_RIGHT"]) : 0;

            // P2 joypad
            joypad_P2_A = AppConfigure.ContainsKey("joypad_P2_A") ? AppConfigure["joypad_P2_A"] : "";
            joypad_P2_B = AppConfigure.ContainsKey("joypad_P2_B") ? AppConfigure["joypad_P2_B"] : "";
            joypad_P2_SELECT = AppConfigure.ContainsKey("joypad_P2_SELECT") ? AppConfigure["joypad_P2_SELECT"] : "";
            joypad_P2_START = AppConfigure.ContainsKey("joypad_P2_START") ? AppConfigure["joypad_P2_START"] : "";
            joypad_P2_UP = AppConfigure.ContainsKey("joypad_P2_UP") ? AppConfigure["joypad_P2_UP"] : "";
            joypad_P2_DOWN = AppConfigure.ContainsKey("joypad_P2_DOWN") ? AppConfigure["joypad_P2_DOWN"] : "";
            joypad_P2_LEFT = AppConfigure.ContainsKey("joypad_P2_LEFT") ? AppConfigure["joypad_P2_LEFT"] : "";
            joypad_P2_RIGHT = AppConfigure.ContainsKey("joypad_P2_RIGHT") ? AppConfigure["joypad_P2_RIGHT"] : "";

            // ── Resize pipeline: compute ScreenSize from stage multipliers ──
            if (!AppConfigure.ContainsKey("ResizeStage1")) AppConfigure["ResizeStage1"] = "none";
            if (!AppConfigure.ContainsKey("ResizeStage2")) AppConfigure["ResizeStage2"] = "none";
            if (!AppConfigure.ContainsKey("Scanline"))     AppConfigure["Scanline"]     = "0";
            if (!AppConfigure.ContainsKey("filter"))       AppConfigure["filter"]       = "resize";

            // Migrate old INI: convert old filter+ScreenSize to new ResizeStage system
            if (AppConfigure["filter"] == "xbrz" || AppConfigure["filter"] == "scanline")
            {
                int oldSize = 1;
                int.TryParse(AppConfigure["ScreenSize"], out oldSize);
                if (oldSize <= 1)
                {
                    AppConfigure["ResizeStage1"] = "none";
                    AppConfigure["ResizeStage2"] = "none";
                }
                else if (oldSize <= 6)
                {
                    AppConfigure["ResizeStage1"] = "xbrz_" + oldSize;
                    AppConfigure["ResizeStage2"] = "none";
                }
                else if (oldSize == 8)
                {
                    AppConfigure["ResizeStage1"] = "xbrz_4";
                    AppConfigure["ResizeStage2"] = "nn_2";
                }
                else if (oldSize == 9)
                {
                    AppConfigure["ResizeStage1"] = "xbrz_3";
                    AppConfigure["ResizeStage2"] = "nn_3";
                }
                if (AppConfigure["filter"] == "scanline")
                    AppConfigure["Scanline"] = "1";
                AppConfigure["filter"] = "resize";
            }

            int s1 = GetResizeScale(AppConfigure["ResizeStage1"]);
            int s2 = GetResizeScale(AppConfigure["ResizeStage2"]);
            ScreenSize = s1 * s2;
            if (ScreenSize < 1) ScreenSize = 1;
            AppConfigure["ScreenSize"] = ScreenSize.ToString();
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
            _menuEmulationPerdotFSM.Checked = NesCore.AccuracyOptA;

            // 讀取 Region 設定 (預設 NTSC)
            if (AppConfigure.ContainsKey("Region"))
            {
                NesCore.RegionType r;
                if (System.Enum.TryParse(AppConfigure["Region"], out r))
                    NesCore.Region = r;
            }
            UpdateRegionCheckmarks();

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

            // 驗證並確保 Capture 路徑存在（INI 缺失或路徑無效時使用預設值）
            EnsureCapturePath("CaptureScreenPath", Path.Combine(Application.StartupPath, "Captures", "Screenshots"));
            EnsureCapturePath("CaptureVideoPath",  Path.Combine(Application.StartupPath, "Captures", "Video"));
            EnsureCapturePath("CaptureAudioPath",  Path.Combine(Application.StartupPath, "Captures", "Audio"));

            // 錄影/錄音品質設定
            int vq;
            int[] validVQ = { 90, 80, 70, 60 };
            VideoRecorder.VideoQuality = (AppConfigure.ContainsKey("VideoQuality") &&
                int.TryParse(AppConfigure["VideoQuality"], out vq) && System.Array.IndexOf(validVQ, vq) >= 0) ? vq : 90;
            int ab;
            int[] validAB = { 192, 160, 128 };
            AudioRecorder.AudioBitrate = (AppConfigure.ContainsKey("AudioBitrate") &&
                int.TryParse(AppConfigure["AudioBitrate"], out ab) && System.Array.IndexOf(validAB, ab) >= 0) ? ab : 160;

            NES_init_KeyMap();

        }

        void EnsureCapturePath(string key, string defaultPath)
        {
            if (!AppConfigure.ContainsKey(key) || string.IsNullOrWhiteSpace(AppConfigure[key]))
                AppConfigure[key] = defaultPath;
            try
            {
                if (!Directory.Exists(AppConfigure[key]))
                    Directory.CreateDirectory(AppConfigure[key]);
            }
            catch
            {
                AppConfigure[key] = defaultPath;
                if (!Directory.Exists(defaultPath))
                    Directory.CreateDirectory(defaultPath);
            }
        }

        private void NES_init_KeyMap()
        {
            NES_KeyMAP_joypad.Clear();
            // P1 joypad
            if (joypad_A != "") NES_KeyMAP_joypad[joypad_A] = KeyMap.NES_btn_A;
            if (joypad_B != "") NES_KeyMAP_joypad[joypad_B] = KeyMap.NES_btn_B;
            if (joypad_SELECT != "") NES_KeyMAP_joypad[joypad_SELECT] = KeyMap.NES_btn_SELECT;
            if (joypad_START != "") NES_KeyMAP_joypad[joypad_START] = KeyMap.NES_btn_START;
            if (joypad_UP != "") NES_KeyMAP_joypad[joypad_UP] = KeyMap.NES_btn_UP;
            if (joypad_DOWN != "") NES_KeyMAP_joypad[joypad_DOWN] = KeyMap.NES_btn_DOWN;
            if (joypad_LEFT != "") NES_KeyMAP_joypad[joypad_LEFT] = KeyMap.NES_btn_LEFT;
            if (joypad_RIGHT != "") NES_KeyMAP_joypad[joypad_RIGHT] = KeyMap.NES_btn_RIGHT;
            // P2 joypad
            if (joypad_P2_A != "") NES_KeyMAP_joypad[joypad_P2_A] = KeyMap.NES_btn_P2_A;
            if (joypad_P2_B != "") NES_KeyMAP_joypad[joypad_P2_B] = KeyMap.NES_btn_P2_B;
            if (joypad_P2_SELECT != "") NES_KeyMAP_joypad[joypad_P2_SELECT] = KeyMap.NES_btn_P2_SELECT;
            if (joypad_P2_START != "") NES_KeyMAP_joypad[joypad_P2_START] = KeyMap.NES_btn_P2_START;
            if (joypad_P2_UP != "") NES_KeyMAP_joypad[joypad_P2_UP] = KeyMap.NES_btn_P2_UP;
            if (joypad_P2_DOWN != "") NES_KeyMAP_joypad[joypad_P2_DOWN] = KeyMap.NES_btn_P2_DOWN;
            if (joypad_P2_LEFT != "") NES_KeyMAP_joypad[joypad_P2_LEFT] = KeyMap.NES_btn_P2_LEFT;
            if (joypad_P2_RIGHT != "") NES_KeyMAP_joypad[joypad_P2_RIGHT] = KeyMap.NES_btn_P2_RIGHT;

            // P1 keyboard
            NES_KeyMAP.Clear();
            if (key_A != 0) NES_KeyMAP[key_A] = KeyMap.NES_btn_A;
            if (key_B != 0) NES_KeyMAP[key_B] = KeyMap.NES_btn_B;
            if (key_SELECT != 0) NES_KeyMAP[key_SELECT] = KeyMap.NES_btn_SELECT;
            if (key_START != 0) NES_KeyMAP[key_START] = KeyMap.NES_btn_START;
            if (key_RIGHT != 0) NES_KeyMAP[key_RIGHT] = KeyMap.NES_btn_RIGHT;
            if (key_LEFT != 0) NES_KeyMAP[key_LEFT] = KeyMap.NES_btn_LEFT;
            if (key_UP != 0) NES_KeyMAP[key_UP] = KeyMap.NES_btn_UP;
            if (key_DOWN != 0) NES_KeyMAP[key_DOWN] = KeyMap.NES_btn_DOWN;
            // P2 keyboard
            if (key_P2_A != 0) NES_KeyMAP[key_P2_A] = KeyMap.NES_btn_P2_A;
            if (key_P2_B != 0) NES_KeyMAP[key_P2_B] = KeyMap.NES_btn_P2_B;
            if (key_P2_SELECT != 0) NES_KeyMAP[key_P2_SELECT] = KeyMap.NES_btn_P2_SELECT;
            if (key_P2_START != 0) NES_KeyMAP[key_P2_START] = KeyMap.NES_btn_P2_START;
            if (key_P2_RIGHT != 0) NES_KeyMAP[key_P2_RIGHT] = KeyMap.NES_btn_P2_RIGHT;
            if (key_P2_LEFT != 0) NES_KeyMAP[key_P2_LEFT] = KeyMap.NES_btn_P2_LEFT;
            if (key_P2_UP != 0) NES_KeyMAP[key_P2_UP] = KeyMap.NES_btn_P2_UP;
            if (key_P2_DOWN != 0) NES_KeyMAP[key_P2_DOWN] = KeyMap.NES_btn_P2_DOWN;
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

            // P2 keyboard
            AppConfigure["key_P2_A"] = key_P2_A.ToString();
            AppConfigure["key_P2_B"] = key_P2_B.ToString();
            AppConfigure["key_P2_SELECT"] = key_P2_SELECT.ToString();
            AppConfigure["key_P2_START"] = key_P2_START.ToString();
            AppConfigure["key_P2_UP"] = key_P2_UP.ToString();
            AppConfigure["key_P2_DOWN"] = key_P2_DOWN.ToString();
            AppConfigure["key_P2_LEFT"] = key_P2_LEFT.ToString();
            AppConfigure["key_P2_RIGHT"] = key_P2_RIGHT.ToString();

            // P2 joypad
            AppConfigure["joypad_P2_A"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_P2_A))
                AppConfigure["joypad_P2_A"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_P2_A).Key;
            AppConfigure["joypad_P2_B"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_P2_B))
                AppConfigure["joypad_P2_B"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_P2_B).Key;
            AppConfigure["joypad_P2_SELECT"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_P2_SELECT))
                AppConfigure["joypad_P2_SELECT"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_P2_SELECT).Key;
            AppConfigure["joypad_P2_START"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_P2_START))
                AppConfigure["joypad_P2_START"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_P2_START).Key;
            AppConfigure["joypad_P2_UP"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_P2_UP))
                AppConfigure["joypad_P2_UP"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_P2_UP).Key;
            AppConfigure["joypad_P2_DOWN"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_P2_DOWN))
                AppConfigure["joypad_P2_DOWN"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_P2_DOWN).Key;
            AppConfigure["joypad_P2_LEFT"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_P2_LEFT))
                AppConfigure["joypad_P2_LEFT"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_P2_LEFT).Key;
            AppConfigure["joypad_P2_RIGHT"] = "";
            if (NES_KeyMAP_joypad.Values.Contains(KeyMap.NES_btn_P2_RIGHT))
                AppConfigure["joypad_P2_RIGHT"] = NES_KeyMAP_joypad.FirstOrDefault(x => x.Value == KeyMap.NES_btn_P2_RIGHT).Key;

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
            _menuFileRecent.DropDownItems.Clear();
            if (_recentROMs.Count == 0)
            {
                _recentROMsMenuItem.Enabled = false;
                _menuFileRecent.Enabled = false;
                return;
            }
            _recentROMsMenuItem.Enabled = true;
            _menuFileRecent.Enabled = true;
            foreach (string path in _recentROMs)
            {
                var item = new ToolStripMenuItem(Path.GetFileName(path));
                item.ToolTipText = path;
                item.Tag = path;
                item.Click += RecentROM_Click;
                _recentROMsMenuItem.DropDownItems.Add(item);

                var item2 = new ToolStripMenuItem(Path.GetFileName(path));
                item2.ToolTipText = path;
                item2.Tag = path;
                item2.Click += RecentROM_Click;
                _menuFileRecent.DropDownItems.Add(item2);
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
            File.WriteAllText(path, str);
        }

        // ────────────────────────────────────────────────────────────────────
        // AprNesAudioPlus.ini — 音效處理參數獨立設定檔
        // ────────────────────────────────────────────────────────────────────

        void LoadAudioPlusIni()
        {
            InitChipDefaults();
            var cfg = new Dictionary<string, string>();

            if (!File.Exists(AudioPlusIniFile))
            {
                // 檔案遺漏：用預設值重建
                SaveAudioPlusIni();
            }

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

            // ── Channel Volume ──
            string[] nesChKeys = { "Pulse1", "Pulse2", "Triangle", "Noise", "DMC" };
            for (int i = 0; i < 5; i++)
            {
                NesCore.ChannelVolume[i] = ReadInt("ChVol_" + nesChKeys[i], 70, 0, 100);
                NesCore.ChannelEnabled[i] = !cfg.ContainsKey("ChEn_" + nesChKeys[i]) || cfg["ChEn_" + nesChKeys[i]] != "0";
            }
            // Expansion channel volumes: per-chip keys (ChVol_VRC6_0, ChVol_N163_0, etc.)
            string[] chipPrefixes = { "", "VRC6", "VRC7", "N163", "S5B", "MMC5", "FDS" };
            for (int chip = 1; chip < chipPrefixes.Length; chip++)
            {
                for (int ch = 0; ch < 8; ch++)
                {
                    string volKey = "ChVol_" + chipPrefixes[chip] + "_" + ch;
                    string enKey  = "ChEn_"  + chipPrefixes[chip] + "_" + ch;
                    // Store per-chip settings in a parallel array; on ROM load, copy matching chip to ChannelVolume[5..12]
                    if (cfg.ContainsKey(volKey))
                    {
                        int v;
                        if (int.TryParse(cfg[volKey], out v))
                            _chipChVol[chip, ch] = Math.Max(0, Math.Min(100, v));
                    }
                    if (cfg.ContainsKey(enKey))
                        _chipChEn[chip, ch] = cfg[enKey] != "0";
                }
            }
            // Apply current chip's settings to ChannelVolume[5..12]
            ApplyChipChannelSettings((int)NesCore.expansionChipType);
        }

        // Per-chip channel volume/enable storage (persisted across chip changes)
        int[,] _chipChVol = new int[7, 8];
        bool[,] _chipChEn = new bool[7, 8];

        void InitChipDefaults()
        {
            for (int c = 0; c < 7; c++)
                for (int i = 0; i < 8; i++)
                { _chipChVol[c, i] = 70; _chipChEn[c, i] = true; }
        }

        void ApplyChipChannelSettings(int chipIdx)
        {
            if (chipIdx < 0 || chipIdx >= 7) chipIdx = 0;
            for (int i = 0; i < 8; i++)
            {
                NesCore.ChannelVolume[5 + i] = _chipChVol[chipIdx, i];
                NesCore.ChannelEnabled[5 + i] = _chipChEn[chipIdx, i];
            }
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
                "BassBoostFreq=" + NesCore.BassBoostFreq + "\r\n" +
                "\r\n" +
                "; ── Channel Volume ──\r\n";

            // NES channels
            string[] nesChKeys = { "Pulse1", "Pulse2", "Triangle", "Noise", "DMC" };
            for (int i = 0; i < 5; i++)
            {
                content += "ChVol_" + nesChKeys[i] + "=" + NesCore.ChannelVolume[i] + "\r\n";
                content += "ChEn_"  + nesChKeys[i] + "=" + (NesCore.ChannelEnabled[i] ? "1" : "0") + "\r\n";
            }

            // Save current chip's expansion channel settings back to _chipChVol
            int curChip = (int)NesCore.expansionChipType;
            if (curChip > 0 && curChip < 7)
            {
                for (int i = 0; i < 8; i++)
                {
                    _chipChVol[curChip, i] = NesCore.ChannelVolume[5 + i];
                    _chipChEn[curChip, i] = NesCore.ChannelEnabled[5 + i];
                }
            }

            // Per-chip expansion channels
            string[] chipPrefixes = { "", "VRC6", "VRC7", "N163", "S5B", "MMC5", "FDS" };
            for (int chip = 1; chip < chipPrefixes.Length; chip++)
            {
                for (int ch = 0; ch < 8; ch++)
                {
                    content += "ChVol_" + chipPrefixes[chip] + "_" + ch + "=" + _chipChVol[chip, ch] + "\r\n";
                    content += "ChEn_"  + chipPrefixes[chip] + "_" + ch + "=" + (_chipChEn[chip, ch] ? "1" : "0") + "\r\n";
                }
            }

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
                                    // Check if this axis is bound to P1 or P2
                                    KeyMap boundA = KeyMap.NES_btn_LEFT, boundB = KeyMap.NES_btn_RIGHT;
                                    bool isP2 = false;
                                    if (NES_KeyMAP_joypad.ContainsKey(key_a)) { boundA = NES_KeyMAP_joypad[key_a]; isP2 = (int)boundA >= 8; }
                                    else if (NES_KeyMAP_joypad.ContainsKey(key_b)) { boundB = NES_KeyMAP_joypad[key_b]; isP2 = (int)boundB >= 8; }

                                    if (XY == "X")
                                    {
                                        if (isP2) { NesCore.P2_ButtonUnPress((byte)KeyMap.NES_btn_LEFT); NesCore.P2_ButtonUnPress((byte)KeyMap.NES_btn_RIGHT); }
                                        else { NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_LEFT); NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_RIGHT); }
                                    }
                                    if (XY == "Y")
                                    {
                                        if (isP2) { NesCore.P2_ButtonUnPress((byte)KeyMap.NES_btn_UP); NesCore.P2_ButtonUnPress((byte)KeyMap.NES_btn_DOWN); }
                                        else { NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_UP); NesCore.P1_ButtonUnPress((byte)KeyMap.NES_btn_DOWN); }
                                    }
                                }
                                continue;
                            }

                        }

                        // Dispatch to P1 or P2 based on KeyMap value
                        bool isP2btn = (int)joy >= 8;
                        byte btnIndex = isP2btn ? (byte)((int)joy - 8) : (byte)joy;
                        bool pressed = (joy_event.event_type == 0) || (joy_event.button_event == 1);

                        if (pressed)
                        {
                            if (isP2btn) NesCore.P2_ButtonPress(btnIndex);
                            else NesCore.P1_ButtonPress(btnIndex);
                        }
                        else
                        {
                            if (isP2btn) NesCore.P2_ButtonUnPress(btnIndex);
                            else NesCore.P1_ButtonUnPress(btnIndex);
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
        byte[] current_bios_bytes; // FDS BIOS bytes (null for normal NES ROMs)

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
            string text = NesCore.AudioEnabled
                ? LangINI.Get(lang, "SoundON",  "Sound ON")
                : LangINI.Get(lang, "SoundOFF", "Sound OFF");
            _soundMenuItem.Text = text;
            _menuViewSound.Text = text;
        }

        void UpdateUltraAnalogMenuText()
        {
            if (_ultraAnalogMenuItem == null) return;
            string text = NesCore.UltraAnalog
                ? "Ultra Analog: ON"
                : "Ultra Analog: OFF";
            _ultraAnalogMenuItem.Text = text;
            _menuViewUltraAnalog.Text = text;
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
            fd.Filter = "NES/FDS ROM (*.nes *.fds *.zip)|*.nes;*.fds;*.zip";
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
                    string ext = entry.FullName.ToLower();
                    if (ext.EndsWith(".nes") || ext.EndsWith(".fds"))
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

            StopRecordingIfActive(true);

            if (nes_t != null)
            {
                try
                {
                    StopAnalogRenderThread();
                    EndHighResPeriod();
                    NesCore.exit = true;
                    NesCore._event.Set();
                    nes_t.Join(500);
                }
                catch (Exception ex)
                {
                    LogError("Thread join error: " + ex.Message);
                    MessageBox.Show(ex.Message);
                }
            }

            SaveSRam();
            NesCore.exit = false;
            NesCore.rom_file_name = rom_file_name;

            bool init_result;
            if (NesCore.IsFdsFile(rom_bytes))
            {
                // FDS mode: load and validate BIOS first
                byte[] bios = NesCore.LoadAndValidateFdsBios(Application.StartupPath);
                if (bios == null)
                {
                    fps_count_timer.Enabled = false;
                    running = false;
                    return;
                }
                current_bios_bytes = bios;
                init_result = NesCore.initFDS(bios, rom_bytes);
            }
            else
            {
                current_bios_bytes = null;
                init_result = NesCore.init(rom_bytes);
            }

            if (RenderObj != null) RenderObj.freeMem();
            RenderObj = NesCore.AnalogEnabled ? (InterfaceGraphic)new Render_Analog() : CreateRenderResize();
            RenderObj.init(NesCore.ScreenBuf1x, grfx);

            NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
            NesCore.VideoOutput += new EventHandler(VideoOutputDeal);

            if (!init_result)
            {
                fps_count_timer.Enabled = false;
                running = false;
                label3.Text = "fps : ";
                LogError("ROM init failed: " + (nes_name ?? "(unknown)"));
                MessageBox.Show("fail !");
                return;
            }
            LoadSRam();
            _fpsDeadline = 0;
            _fpsStopWatch.Restart();
            if (NesCore.AudioEnabled) WaveOutPlayer.OpenAudio();
            BeginHighResPeriod();
            // Analog mode: 啟動獨立渲染執行緒（async double buffer）
            if (NesCore.AnalogEnabled) StartAnalogRenderThread();
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
            StopAnalogRenderThread();
            EndHighResPeriod();
            NesCore.exit = true;
            NesCore._event.Set();
            SaveSRam();
            WaveOutPlayer.CloseAudio();
            Thread.Sleep(10);
        }

        //http://stackoverflow.com/questions/11754874/keydown-not-firing-for-up-down-left-and-right
        protected override bool ProcessCmdKey(ref System.Windows.Forms.Message msg, System.Windows.Forms.Keys keyData)
        {
            // Escape: 退出全螢幕（MenuStrip 無法處理的特殊邏輯）
            if (keyData == Keys.Escape)
            {
                if (ScreenCenterFull || analogFullScreen)
                    fun8ToolStripMenuItem_Click(null, null);
                return true;
            }

            // F11: 全螢幕切換（MenuStrip 隱藏時 ShortcutKeys 失效，需手動處理）
            if (keyData == Keys.F11)
            {
                _menuViewToggleFullScreen_Click(null, null);
                return true;
            }

            // 讓 MenuStrip ShortcutKeys 優先處理 (Ctrl+O, Ctrl+R, Ctrl+Shift+P 等)
            if (base.ProcessCmdKey(ref msg, keyData))
                return true;

            // 遊戲手把按鍵
            if (!running) return false;
            int keyboard_key = (int)keyData;

            if (NES_KeyMAP.ContainsKey(keyboard_key))
            {
                KeyMap km = NES_KeyMAP[keyboard_key];
                if ((int)km >= 8)
                    NesCore.P2_ButtonPress((byte)((int)km - 8));
                else
                    NesCore.P1_ButtonPress((byte)km);
                return true;
            }
            return false;
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

            // Async analog mode: 暫停渲染執行緒以安全讀取 buffer
            bool wasAsync = NesCore.analogRenderThreadRunning;
            if (wasAsync) StopAnalogRenderThread();
            NesCore._event.Reset();

            DateTime dt = DateTime.Now;
            string stamp = (dt.ToLongDateString() + " " + dt.ToLongTimeString()).Replace(":", "-");
            try
            {
                if (!System.IO.Directory.Exists(AppConfigure["CaptureScreenPath"]))
                    System.IO.Directory.CreateDirectory(AppConfigure["CaptureScreenPath"]);
                using (var bmp = RenderObj.GetOutput())
                    bmp.Save(AppConfigure["CaptureScreenPath"] + @"\Screen-" + stamp + ".png", System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (Exception e) { Console.WriteLine("i:" + e.Message); }

            if (wasAsync) StartAnalogRenderThread();
            NesCore._event.Set();

            Console.WriteLine("Screen-" + stamp + ".png" + " write finish !");
            writing = false;

            MessageBox.Show(AppConfigure["CaptureScreenPath"] + @"\Screen-" + stamp + ".png" + " " + "save!");
        }

        private void AprNesUI_KeyUp(object sender, KeyEventArgs e)
        {
            if (!running) return;
            if (NES_KeyMAP.ContainsKey(e.KeyValue))
            {
                KeyMap km = NES_KeyMAP[e.KeyValue];
                if ((int)km >= 8)
                    NesCore.P2_ButtonUnPress((byte)((int)km - 8));
                else
                    NesCore.P1_ButtonUnPress((byte)km);
            }
        }

        bool LimitFPS = true;
        readonly Stopwatch _fpsStopWatch = new Stopwatch();
        double _fpsDeadline = 0;

        // ── Async Analog Render Thread ──
        Thread analogRenderThread;

        void StartAnalogRenderThread()
        {
            if (analogRenderThread != null) return;
            NesCore.analogRenderDone.Set();
            NesCore.analogRenderReady.Reset();
            NesCore.analogRenderThreadRunning = true;
            analogRenderThread = new Thread(AnalogRenderThreadLoop);
            analogRenderThread.IsBackground = true;
            analogRenderThread.Name = "AnalogRender";
            analogRenderThread.Start();
        }

        public void StopAnalogRenderThread()
        {
            if (analogRenderThread == null) return;
            NesCore._event.Reset();
            NesCore.analogRenderThreadRunning = false;
            NesCore.analogRenderReady.Set();
            analogRenderThread.Join(500);
            analogRenderThread = null;
            NesCore.analogRenderDone.Set();
        }

        unsafe void AnalogRenderThreadLoop()
        {
            while (NesCore.analogRenderThreadRunning)
            {
                NesCore.analogRenderReady.Wait();
                if (!NesCore.analogRenderThreadRunning) break;
                NesCore.analogRenderReady.Reset();

                WINAPIGDI.NativeGDI.UpdateDataPtr(NesCore.AnalogScreenBufBack);
                WINAPIGDI.NativeGDI.DrawImageHighSpeedtoDevice();

                if (VideoRecorder.IsRecording && NesCore.AnalogScreenBufBack != null)
                    VideoRecorder.PushFrame(NesCore.AnalogScreenBufBack);

                if (LimitFPS)
                {
                    if (!_fpsStopWatch.IsRunning) _fpsStopWatch.Restart();
                    double now = _fpsStopWatch.Elapsed.TotalSeconds;
                    if (_fpsDeadline < now)
                        _fpsDeadline = now + NesCore.FrameSeconds;
                    while (_fpsDeadline - _fpsStopWatch.Elapsed.TotalSeconds > 0.001)
                        Thread.Sleep(1);
                    while (_fpsStopWatch.Elapsed.TotalSeconds < _fpsDeadline) { }
                    _fpsDeadline += NesCore.FrameSeconds;
                }

                NesCore.analogRenderDone.Set();
            }
        }

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
                    _fpsDeadline = now + NesCore.FrameSeconds;
                while (_fpsDeadline - _fpsStopWatch.Elapsed.TotalSeconds > 0.001)
                    Thread.Sleep(1);
                while (_fpsStopWatch.Elapsed.TotalSeconds < _fpsDeadline) { }
                _fpsDeadline += NesCore.FrameSeconds;
            }
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
            StopRecordingIfActive(true);

            SaveSRam();
            NesCore.rom_file_name = rom_file_name;

            StopAnalogRenderThread();
            NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
            NesCore._event.Reset();
            while (!NesCore.emuWaiting) Thread.Sleep(1);
            if (RenderObj != null) RenderObj.freeMem();
            RenderObj = NesCore.AnalogEnabled ? (InterfaceGraphic)new Render_Analog() : CreateRenderResize();
            RenderObj.init(NesCore.ScreenBuf1x, grfx);
            NesCore.VideoOutput += new EventHandler(VideoOutputDeal);

            NesCore.SoftReset();   // 設 flag（模擬線程暫停中，無 race condition）
            if (NesCore.AnalogEnabled) StartAnalogRenderThread();
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

            // 停止 async 渲染執行緒（若有），讓模擬端回到同步模式以便安全暫停
            StopAnalogRenderThread();

            NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
            NesCore._event.Reset();
            // 等待模擬執行緒完成當前整幀並阻塞於 _event.WaitOne()（同步模式）
            while (!NesCore.emuWaiting)
                Thread.Sleep(1);

            // AnalogEnabled 時：重建 CrtScreen 快取，並在必要時重新分配 AnalogScreenBuf
            if (NesCore.AnalogEnabled)
            {
                NesCore.SyncAnalogConfig();
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
                        if (NesCore.AnalogScreenBufBack != null)
                        {
                            System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)NesCore.AnalogScreenBufBack);
                            NesCore.AnalogScreenBufBack = null;
                        }
                        NesCore.AnalogBufSize       = needed;
                        NesCore.AnalogScreenBuf     = (uint*)System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(uint) * needed);
                        NesCore.AnalogScreenBufBack = (uint*)System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(uint) * needed);
                    }
                }
                NesCore.SyncAnalogConfig();
                NesCore.Ntsc_Init();
                NesCore.Crt_Init();
            }

            if (RenderObj != null) RenderObj.freeMem();
            RenderObj = NesCore.AnalogEnabled ? (InterfaceGraphic)new Render_Analog() : CreateRenderResize();
            RenderObj.init(NesCore.ScreenBuf1x, grfx);
            NesCore.VideoOutput += new EventHandler(VideoOutputDeal);

            // Analog mode: 重啟 async 渲染執行緒
            if (NesCore.AnalogEnabled)
                StartAnalogRenderThread();

            NesCore._event.Set();
        }

        unsafe public void HardReset()
        {
            if (!running || current_rom_bytes == null) return;
            StopRecordingIfActive(true);

            // 停止 async 渲染執行緒 + 模擬線程
            StopAnalogRenderThread();
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

            bool init_result;
            if (current_bios_bytes != null && NesCore.IsFdsFile(current_rom_bytes))
                init_result = NesCore.initFDS(current_bios_bytes, current_rom_bytes);
            else
                init_result = NesCore.init(current_rom_bytes);

            if (RenderObj != null) RenderObj.freeMem();
            RenderObj = NesCore.AnalogEnabled ? (InterfaceGraphic)new Render_Analog() : CreateRenderResize();
            RenderObj.init(NesCore.ScreenBuf1x, grfx);

            NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
            NesCore.VideoOutput += new EventHandler(VideoOutputDeal);

            if (!init_result)
            {
                fps_count_timer.Enabled = false;
                running = false;
                label3.Text = "fps : ";
                LogError("Hard Reset failed: " + (nes_name ?? "(unknown)"));
                MessageBox.Show("Hard Reset fail !");
                return;
            }

            LoadSRam();
            _fpsDeadline = 0;
            _fpsStopWatch.Restart();
            if (NesCore.AudioEnabled) WaveOutPlayer.OpenAudio();
            BeginHighResPeriod();
            if (NesCore.AnalogEnabled) StartAnalogRenderThread();
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
            StopRecordingIfActive(true);
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
            StopRecordingIfActive(true);
            NesCore.UltraAnalog = !NesCore.UltraAnalog;
            UpdateUltraAnalogMenuText();
            AppConfigure["UltraAnalog"] = NesCore.UltraAnalog ? "1" : "0";
            Configure_Write();

            // 同步渲染管線（Ntsc._ultraAnalog 需要與 NesCore.UltraAnalog 一致）
            if (running && NesCore.AnalogEnabled)
            {
                StopAnalogRenderThread();
                NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
                NesCore._event.Reset();
                while (!NesCore.emuWaiting) Thread.Sleep(1);

                NesCore.SyncAnalogConfig();
                NesCore.Ntsc_Init();
                NesCore.Crt_Init();

                NesCore.VideoOutput += new EventHandler(VideoOutputDeal);
                StartAnalogRenderThread();
                NesCore._event.Set();
            }
        }

        // ── Recording ──
        static string _ffmpegPath;

        static string GetFfmpegPath()
        {
            string path = Path.Combine(Application.StartupPath, "tools", "ffmpeg", "ffmpeg.exe");
            _ffmpegPath = File.Exists(path) ? path : "";
            return _ffmpegPath;
        }

        void UpdateRecordMenuVisibility()
        {
            bool hasFfmpeg = !string.IsNullOrEmpty(GetFfmpegPath());
            _recordMenuItem.Visible = hasFfmpeg;
            _menuToolsRecord.Visible = hasFfmpeg;
            if (hasFfmpeg)
            {
                bool videoRec = VideoRecorder.IsRecording;
                bool audioRec = AudioRecorder.IsRecording;

                string videoText = videoRec ? "■ Stop Recording" : "● Record Video";
                bool videoEnabled = videoRec || (running && !audioRec);
                _recordVideoMenuItem.Text = videoText;
                _recordVideoMenuItem.Enabled = videoEnabled;
                _menuToolsRecordVideo.Text = videoText;
                _menuToolsRecordVideo.Enabled = videoEnabled;

                string audioText = audioRec ? "■ Stop Audio Recording" : "● Record Audio";
                bool audioEnabled = audioRec || (running && !videoRec);
                _recordAudioMenuItem.Text = audioText;
                _recordAudioMenuItem.Enabled = audioEnabled;
                _menuToolsRecordAudio.Text = audioText;
                _menuToolsRecordAudio.Enabled = audioEnabled;
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

            string capturesDir = AppConfigure.ContainsKey("CaptureVideoPath") ? AppConfigure["CaptureVideoPath"] : Path.Combine(Application.StartupPath, "Captures", "Video");
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

        void _recordAudioMenuItem_Click(object sender, EventArgs e)
        {
            if (AudioRecorder.IsRecording)
            {
                AudioRecorder.Stop();
                UpdateRecordMenuVisibility();
                if (!VideoRecorder.IsRecording)
                    this.Text = "AprNes";
                return;
            }

            if (!running) return;

            string capturesDir = AppConfigure.ContainsKey("CaptureAudioPath") ? AppConfigure["CaptureAudioPath"] : Path.Combine(Application.StartupPath, "Captures", "Audio");
            bool ok = AudioRecorder.Start(GetFfmpegPath(), capturesDir);
            if (ok)
            {
                UpdateRecordMenuVisibility();
                if (!VideoRecorder.IsRecording)
                    this.Text = "AprNes [REC Audio]";
            }
            else
            {
                string err = AudioRecorder.LastError ?? "Unknown error";
                MessageBox.Show("Failed to start audio recording.\n\n" + err,
                    "Recording Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Called from ConfigureUI when settings change while recording.
        /// </summary>
        public void StopRecordingOnSettingsChange() => StopRecordingIfActive(true);

        void StopRecordingIfActive(bool notify = false)
        {
            bool wasStopped = false;
            if (VideoRecorder.IsRecording)
            {
                VideoRecorder.Stop();
                wasStopped = true;
            }
            if (AudioRecorder.IsRecording)
            {
                AudioRecorder.Stop();
                wasStopped = true;
            }
            if (wasStopped)
            {
                UpdateRecordMenuVisibility();
                try { this.Text = "AprNes"; } catch { }
                if (notify)
                {
                    string lang = AppConfigure.ContainsKey("Lang") ? AppConfigure["Lang"] : "en-us";
                    string title = LangINI.Get(lang, "rec_stopped_title", "Recording Stopped");
                    string msg = LangINI.Get(lang, "rec_stopped_msg",
                        "Recording has been stopped due to settings change.");
                    MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
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

        private void _menuEmulationLimitFps_Click(object sender, EventArgs e)
        {
            LimitFPS = _menuEmulationLimitFps.Checked;
            AppConfigure["LimitFPS"] = LimitFPS ? "1" : "0";
            Configure_Write();
        }

        private void _menuEmulationPerdotFSM_Click(object sender, EventArgs e)
        {
            NesCore.AccuracyOptA = _menuEmulationPerdotFSM.Checked;
            AppConfigure["AccuracyOptA"] = NesCore.AccuracyOptA ? "1" : "0";
            Configure_Write();
        }

        private void _menuEmulationRegion_Click(object sender, EventArgs e)
        {
            var item = sender as ToolStripMenuItem;
            if (item == null) return;

            NesCore.RegionType newRegion;
            if (item == _menuEmulationRegionPAL) newRegion = NesCore.RegionType.PAL;
            else if (item == _menuEmulationRegionDendy) newRegion = NesCore.RegionType.Dendy;
            else newRegion = NesCore.RegionType.NTSC;

            if (newRegion == NesCore.Region) return;

            NesCore.Region = newRegion;
            UpdateRegionCheckmarks();
            AppConfigure["Region"] = NesCore.Region.ToString();
            Configure_Write();

            if (running) HardReset();
        }

        void UpdateRegionCheckmarks()
        {
            _menuEmulationRegionNTSC.Checked  = NesCore.Region == NesCore.RegionType.NTSC;
            _menuEmulationRegionPAL.Checked   = NesCore.Region == NesCore.RegionType.PAL;
            _menuEmulationRegionDendy.Checked = NesCore.Region == NesCore.RegionType.Dendy;
        }

        private void _menuToolsScreenshot_Click(object sender, EventArgs e)
        {
            if (running) NESCaptureScreen();
        }

        private void _menuHelpShortcuts_Click(object sender, EventArgs e)
        {
            string lang = AppConfigure.ContainsKey("Lang") ? AppConfigure["Lang"] : "en-us";
            string title = LangINI.Get(lang, "shortcuts", "Keyboard Shortcuts");
            string body = LangINI.Get(lang, "shortcuts_body",
                "Ctrl+O\tOpen ROM\nCtrl+R\tSoft Reset\nCtrl+Shift+P\tScreenshot\nF11\tToggle FullScreen\nEscape\tExit FullScreen");
            body = body.Replace("\\n", "\r\n").Replace("\\t", "\t");
            MessageBox.Show(body, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                StopAnalogRenderThread();
                NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
                NesCore._event.Reset();
                while (!NesCore.emuWaiting) Thread.Sleep(1);
            }

            // 設定全螢幕覆寫解析度
            NesCore.Crt_SetFullscreenSize(displayW, displayH);

            // 重新分配 AnalogScreenBuf + back buffer
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
                    if (NesCore.AnalogScreenBufBack != null)
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)NesCore.AnalogScreenBufBack);
                        NesCore.AnalogScreenBufBack = null;
                    }
                    NesCore.AnalogBufSize       = needed;
                    NesCore.AnalogScreenBuf     = (uint*)System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(uint) * needed);
                    NesCore.AnalogScreenBufBack = (uint*)System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(uint) * needed);
                }
                NesCore.SyncAnalogConfig();
                NesCore.Ntsc_Init();
                NesCore.Crt_Init();
            }

            // UI 全螢幕
            menuStrip1.Visible = false;
            panel1.Visible = false;
            panel1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            label3.Visible = false;
            this.BackColor = Color.Black;
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;

            panel1.Size = new Size(displayW, displayH);
            panel1.Location = new Point(padX, padY);

            // 重建 Graphics + RenderObj
            grfx?.Dispose();
            grfx = panel1.CreateGraphics();
            if (RenderObj != null) RenderObj.freeMem();
            RenderObj = new Render_Analog();
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
                if (NesCore.AnalogEnabled) StartAnalogRenderThread();
                NesCore._event.Set();
            }

            Configure_Write();
        }

        unsafe void ExitAnalogFullScreen()
        {
            // 暫停模擬執行緒
            if (running)
            {
                StopAnalogRenderThread();
                NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
                NesCore._event.Reset();
                while (!NesCore.emuWaiting) Thread.Sleep(1);
            }

            // 清除全螢幕覆寫，恢復 AnalogSize 驅動的 DstW/DstH
            NesCore.Crt_ClearFullscreenSize();

            // 重新分配 AnalogScreenBuf + back buffer 回原始大小
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
                    if (NesCore.AnalogScreenBufBack != null)
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)NesCore.AnalogScreenBufBack);
                        NesCore.AnalogScreenBufBack = null;
                    }
                    NesCore.AnalogBufSize       = needed;
                    NesCore.AnalogScreenBuf     = (uint*)System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(uint) * needed);
                    NesCore.AnalogScreenBufBack = (uint*)System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(uint) * needed);
                }
                NesCore.SyncAnalogConfig();
                NesCore.Ntsc_Init();
                NesCore.Crt_Init();
            }

            // 還原 UI 狀態
            menuStrip1.Visible = true;
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
                if (NesCore.AnalogEnabled) StartAnalogRenderThread();
                NesCore._event.Set();
            }

            Configure_Write();
        }

        private void fun8ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StopRecordingIfActive(true);
            if (analogFullScreen) { ExitAnalogFullScreen(); return; }
            menuStrip1.Visible = true;
            panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.BackColor = SystemColors.Menu;
            this.WindowState = FormWindowState.Normal;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            ScreenCenterFull = false;
            initUIsize();
        }

        private void _menuViewToggleFullScreen_Click(object sender, EventArgs e)
        {
            if (ScreenCenterFull || analogFullScreen)
                fun8ToolStripMenuItem_Click(null, null);
            else
                fullScreeenToolStripMenuItem_Click(null, null);
        }

        private void fullScreeenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StopRecordingIfActive(true);
            if (NesCore.AnalogEnabled) { EnterAnalogFullScreen(); return; }

            if (this.WindowState != FormWindowState.Maximized) Opacity = 0;
            menuStrip1.Visible = false;
            panel1.Visible = false;
            panel1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            label3.Visible = false;
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
            StopRecordingIfActive(true);
            if (analogFullScreen) { ExitAnalogFullScreen(); return; }
            menuStrip1.Visible = true;
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
