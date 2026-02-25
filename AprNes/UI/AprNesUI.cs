using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Threading;
using LangTool;
using NativeTools;

namespace AprNes
{
    public partial class AprNesUI : Form
    {
        Graphics grfx;
        public Dictionary<string, string> AppConfigure = new Dictionary<string, string>();
        string ConfigureFile = Application.StartupPath + @"\AprNes.ini";

        Dictionary<int, KeyMap> NES_KeyMAP = new Dictionary<int, KeyMap>();
        public Dictionary<string, KeyMap> NES_KeyMAP_joypad = new Dictionary<string, KeyMap>();

        List<string> background_pics = new List<string>();

        joystick _joystick = new joystick();

        Stopwatch st = new Stopwatch();//test UI finish time

        public AprNesUI()
        {
            st.Restart();
            InitializeComponent();

            if (Directory.Exists(Application.StartupPath + "/Background"))
                background_pics = Directory.GetFiles(Application.StartupPath + "/Background").Where(s => s.ToLower().EndsWith(".jpg") || s.ToLower().EndsWith(".png")).ToList();

            LangINI.init();
            LoadConfig();
            initUILang();
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
        }

        int ScreenSize = 1;
        public void initUIsize()
        {
            panel1.Visible = false;
            panel1.Width = 256 * ScreenSize;
            panel1.Height = 240 * ScreenSize;

            if (AppConfigure["filter"] == "scanline")
            {
                switch (ScreenSize)
                {
                    case 2: panel1.Width = 600; break;
                    case 4: panel1.Width = 1196; break;
                    case 6: panel1.Width = 1792; break;
                }
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
            this.Width = 282 + 256 * (ScreenSize - 1);
            this.Height = 332 + 240 * (ScreenSize - 1);

            if (AppConfigure["filter"] == "scanline")
            {
                switch (ScreenSize)
                {
                    case 2: Width = 600; break;
                    case 4: Width = 1196; break;
                    case 6: Width = 1792; break;
                }
                Width += 26;
            }
            UIAbout.Location = new Point(Width - 82, 277 + 240 * (ScreenSize - 1));

            RomInf.Location = new Point(5, 277 + 240 * (ScreenSize - 1));
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
                AppConfigure["CaptureScreenPath"] = Application.StartupPath+ @"\Screenshot";
                AppConfigure["joypad_A"] = "";
                AppConfigure["joypad_B"] = "";
                AppConfigure["joypad_SELECT"] = "";
                AppConfigure["joypad_START"] = "";
                AppConfigure["joypad_UP"] = "";
                AppConfigure["joypad_DOWN"] = "";
                AppConfigure["joypad_LEFT"] = "";
                AppConfigure["joypad_RIGHT"] = "";
                AppConfigure["Lang"] = "en-us";
                AppConfigure["filter"] = "xbrz";
                AppConfigure["Sound"] = "1";
                Configure_Write();
            }

            List<string> lines = File.ReadAllLines(ConfigureFile).ToList();
            foreach (string i in lines)
            {
                List<string> keyvalue = i.Split(new char[] { '=' }).ToList();

                if (keyvalue.Count == 2)
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

            string conf = "";
            foreach (string i in AppConfigure.Keys)
                conf += i + "=" + AppConfigure[i] + "\r\n";
            FileWriteAllText(ConfigureFile, conf);
        }

        public void FileWriteAllText(string path, string str)
        {
            Console.WriteLine("Configure save !");
            Stream s = File.OpenWrite(path);
            StreamWriter sw = new StreamWriter(s);
            sw.WriteLine(str);
            sw.Close();
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

                        AprNesUI.GetInstance().Invoke(new MethodInvoker(() =>
                         {
                             if (joy_event.event_type == 0) //方向鍵觸發
                             {
                                 if (joy_event.way_type == 0)
                                     AprNes_ConfigureUI.GetInstance().Setup_JoyPad_define(joy_event.joystick_id.ToString(), "X", 0, joy_event.way_value);
                                 else
                                     AprNes_ConfigureUI.GetInstance().Setup_JoyPad_define(joy_event.joystick_id.ToString(), "Y", 0, joy_event.way_value);
                             }
                             else //一般按鈕觸發                             
                                 AprNes_ConfigureUI.GetInstance().Setup_JoyPad_define(joy_event.joystick_id.ToString(), "Button " + joy_event.button_id.ToString(), joy_event.button_id, 128);
                         }));
                        break;
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
                                NesCore.P1_ButtonPress((byte)KeyMap.NES_btn_UP);
                                break;

                            case KeyMap.NES_btn_DOWN:
                                NesCore.P1_ButtonPress((byte)KeyMap.NES_btn_DOWN);
                                break;
                            case KeyMap.NES_btn_LEFT:
                                NesCore.P1_ButtonPress((byte)KeyMap.NES_btn_LEFT);
                                break;

                            case KeyMap.NES_btn_RIGHT:
                                NesCore.P1_ButtonPress((byte)KeyMap.NES_btn_RIGHT);
                                break;
                        }
                    }
                }
            }
        }

        Thread nes_t = null;
        bool running = false;
        public string rom_file = "";
        public byte[] rom_bytes;
        byte[] current_rom_bytes;  // 保存已解壓的 ROM 資料供 Hard Reset 使用

        public enum MapperName
        {
            NROM = 0,
            MMC1 = 1,
            UNROM = 2,
            CNROM = 3,
            MMC3 = 4,
            MMC5 = 5,
            AxROM = 7,
            ColorDreams = 11,
            GxROM = 66,
            Camerica = 71
        }

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

                string mapper_name = ((MapperName)mapper).ToString();

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
            if (_soundMenuItem == null) return;
            _soundMenuItem.Text = NesCore.AudioEnabled ? LangINI.lang_table[AppConfigure["Lang"]]["SoundON"] : LangINI.lang_table[AppConfigure["Lang"]]["SoundOFF"];
        }

        void VideoOutputDeal(object sender, EventArgs e)
        {
            RenderObj.Render();
        }

        public string rom_file_name = "";
        public string nes_name = "";

        InterfaceGraphic RenderObj;

        unsafe private void button1_Click(object sender, EventArgs e)
        {

            OpenFileDialog fd = new OpenFileDialog();
            fd.Filter = "nes file (*.nes *.zip)|*.nes;*.zip";
            if (fd.ShowDialog() != DialogResult.OK) return;

            FileInfo fi = new FileInfo(fd.FileName);
            if (fi.Extension.ToLower() == ".zip")
            {
                // tks!! https://github.com/yallie/unzip good!
                // replace with .net use framework 4.6 https://msdn.microsoft.com/zh-tw/library/system.io.compression.zipfile(v=vs.110).aspx
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
                nes_name = new FileInfo(fd.FileName).Name;
                rom_bytes = File.ReadAllBytes(fd.FileName);
            }

            rom_file_name = fd.FileName.Remove(fd.FileName.Length - 4, 4);
            current_rom_bytes = rom_bytes;  // 保存供 Hard Reset 使用

            if (nes_t != null)
            {
                try
                {
                    NesCore.exit = true;
                    Thread.Sleep(50);
                    nes_t.Abort();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

            NesCore.SaveRam();
            NesCore.exit = false;
            NesCore.LimitFPS = LimitFPS;
            NesCore.rom_file_name = rom_file_name;

            bool init_result = NesCore.init(rom_bytes);

            if (RenderObj != null) RenderObj.freeMem();
            RenderObj = (InterfaceGraphic)Activator.CreateInstance(Type.GetType("AprNes.Render_" + AppConfigure["filter"] + "_" + ScreenSize + "x"));
            RenderObj.init(NesCore.ScreenBuf1x, grfx);

            NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
            NesCore.VideoOutput += new EventHandler(VideoOutputDeal);

            Console.WriteLine("init finsih");

            if (!init_result)
            {
                fps_count_timer.Enabled = false;
                running = false;
                label3.Text = "fps : ";
                MessageBox.Show("fail !");
                return;
            }
            nes_t = new Thread(NesCore.run);
            nes_t.IsBackground = true;
            nes_t.Start();
            fps_count_timer.Enabled = true;
            running = true;
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
            app_running = false;
            NesCore.exit = true;
            NesCore.SaveRam();
            NesCore.closeAudio();
            Thread.Sleep(10);
        }

        //http://stackoverflow.com/questions/11754874/keydown-not-firing-for-up-down-left-and-right
        protected override bool ProcessCmdKey(ref System.Windows.Forms.Message msg, System.Windows.Forms.Keys keyData)
        {
            //for KeyDown check  
            if (!running) return true;
            int keyboard_key = (int)keyData;

            if (keyboard_key == 65616)
            {
                NESCaptureScreen();
                return true; ;
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

            NesCore.SaveRam();
            NesCore.LimitFPS = LimitFPS;
            NesCore.rom_file_name = rom_file_name;

            NesCore.VideoOutput -= new EventHandler(VideoOutputDeal);
            NesCore._event.Reset();
            while (NesCore.screen_lock) Thread.Sleep(1);
            if (RenderObj != null) RenderObj.freeMem();
            RenderObj = (InterfaceGraphic)Activator.CreateInstance(Type.GetType("AprNes.Render_" + AppConfigure["filter"] + "_" + ScreenSize + "x"));
            RenderObj.init(NesCore.ScreenBuf1x, grfx);
            NesCore.VideoOutput += new EventHandler(VideoOutputDeal);

            NesCore.SoftReset();   // 設 flag（模擬線程暫停中，無 race condition）
            NesCore._event.Set();  // 恢復模擬線程，cpu_step 中偵測 softreset flag
        }

        unsafe public void HardReset()
        {
            if (!running || current_rom_bytes == null) return;

            // 停止模擬線程
            NesCore.exit = true;
            NesCore._event.Set();
            if (nes_t != null)
            {
                nes_t.Join(500);
                if (nes_t.IsAlive) nes_t.Abort();
            }

            NesCore.SaveRam();
            NesCore.closeAudio();

            // 完整重新初始化（等同 power cycle）
            NesCore.exit = false;
            NesCore.LimitFPS = LimitFPS;
            NesCore.rom_file_name = rom_file_name;

            bool init_result = NesCore.init(current_rom_bytes);

            if (RenderObj != null) RenderObj.freeMem();
            RenderObj = (InterfaceGraphic)Activator.CreateInstance(Type.GetType("AprNes.Render_" + AppConfigure["filter"] + "_" + ScreenSize + "x"));
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
                NesCore.closeAudio();
            else if (running)
                NesCore.openAudio();
            UpdateSoundMenuText();
            // 儲存設定到 ini
            AppConfigure["Sound"] = NesCore.AudioEnabled ? "1" : "0";
            Configure_Write();
        }

        private void AprNesUI_Shown(object sender, EventArgs e)
        {
            initUIsize();
            UpdateSoundMenuText();

            _joystick.Init();
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

        private void fun8ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.BackgroundImage = null;
            this.WindowState = FormWindowState.Normal;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            ScreenCenterFull = false;
            initUIsize();
        }

        private void fullScreeenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.WindowState != FormWindowState.Maximized) Opacity = 0;
            panel1.Visible = false;
            UIAbout.Visible = RomInf.Visible = UIOpenRom.Visible = UIReset.Visible = UIConfig.Visible = label3.Visible = false;
            if (background_pics.Count != 0)
                this.BackgroundImage = Image.FromFile(background_pics[new Random().Next(0, background_pics.Count)]);
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
            this.BackgroundImage = null;
            this.WindowState = FormWindowState.Normal;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            label3.Location = new Point(208, 8);
            ScreenCenterFull = false;
            Configure_Write();
            initUIsize();
        }
    }
}
