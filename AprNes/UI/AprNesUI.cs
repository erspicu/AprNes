﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
using System.Threading;
using SharpDX.DirectInput;
using AprNes;
using LangTool;

namespace AprNes
{
    public partial class AprNesUI : Form
    {
        Graphics grfx;
        public Dictionary<string, string> AppConfigure = new Dictionary<string, string>();
        string ConfigureFile = Application.StartupPath + @"\AprNes.ini";

        Dictionary<int, KeyMap> NES_KeyMAP = new Dictionary<int, KeyMap>();
        public Dictionary<string, KeyMap> NES_KeyMAP_joypad = new Dictionary<string, KeyMap>();


        public AprNesUI()
        {
            InitializeComponent();
            LangINI.init();
            LoadConfig();
            initUIsize();
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

            UIOpenRom.Text = LangINI.lang_table[AppConfigure["Lang"]]["rom"];
            UIConfig.Text = LangINI.lang_table[AppConfigure["Lang"]]["setting"];
            UIReset.Text = LangINI.lang_table[AppConfigure["Lang"]]["reset"];
            UIAbout.Text = LangINI.lang_table[AppConfigure["Lang"]]["about"];
        }

        int ScreenSize = 1;
        public void initUIsize()
        {
            switch (ScreenSize)
            {
                case 1:
                    panel1.Width = 256;
                    panel1.Height = 240;
                    this.Width = 272;
                    this.Height = 320;
                    UIAbout.Location = new Point(5, 277);
                    break;

                case 2:
                    panel1.Width = 256 * 2;
                    panel1.Height = 240 * 2;
                    this.Width = 272 + 256;
                    this.Height = 320 + 240;
                    UIAbout.Location = new Point(5, 277 + 240);
                    break;

                case 3:
                    panel1.Width = 256 * 3;
                    panel1.Height = 240 * 3;
                    this.Width = 272 + 256 * 2;
                    this.Height = 320 + 240 * 2;
                    UIAbout.Location = new Point(5, 277 + 240 * 2);
                    break;

                case 4:
                    panel1.Width = 256 * 4;
                    panel1.Height = 240 * 4;
                    this.Width = 272 + 256 * 3;
                    this.Height = 320 + 240 * 3;
                    UIAbout.Location = new Point(5, 277 + 240 * 3);
                    break;

                case 5:
                    panel1.Width = 256 * 5;
                    panel1.Height = 240 * 5;
                    this.Width = 272 + 256 * 4;
                    this.Height = 320 + 240 * 4;
                    UIAbout.Location = new Point(5, 277 + 240 * 4);
                    break;
            }
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
                AppConfigure["CaptureScreenPath"] = Application.StartupPath;
                AppConfigure["joypad_A"] = "";
                AppConfigure["joypad_B"] = "";
                AppConfigure["joypad_SELECT"] = "";
                AppConfigure["joypad_START"] = "";
                AppConfigure["joypad_UP"] = "";
                AppConfigure["joypad_DOWN"] = "";
                AppConfigure["joypad_LEFT"] = "";
                AppConfigure["joypad_RIGHT"] = "";
                AppConfigure["Lang"] = "en-us";
                Configure_Write();
            }

            List<string> lines = File.ReadAllLines(ConfigureFile).ToList();
            foreach (string i in lines)
            {
                List<string> keyvalue = i.Split(new char[] { '=' }).ToList();
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

            string conf = "";
            foreach (string i in AppConfigure.Keys)
                conf += i + "=" + AppConfigure[i] + "\r\n";
            File.WriteAllText(ConfigureFile, conf);
        }

        //-------------------------------------------------------

        DirectInput directInput = new DirectInput();
        class JoyPadListener
        {
            Joystick joystick;
            public JoyPadListener(Joystick joypad)
            {
                joystick = joypad;
            }

            private string JoyPadWayName(string xy_name, int value)
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
            public void start()
            {
                int press_key = 0;
                while (true)
                {
                    Thread.Sleep(10);

                    joystick.Poll();

                    JoystickUpdate[] datas = joystick.GetBufferedData();
                    foreach (JoystickUpdate state in datas)
                    {
                        AprNesUI.GetInstance().Invoke(new MethodInvoker(() =>
                        {
                            if (AprNes_ConfigureUI.GetInstance().Visible == true)
                            {
                                AprNes_ConfigureUI.GetInstance().Setup_JoyPad_define(joystick.Information.InstanceGuid.ToString(), state.Offset.ToString(), state.RawOffset, state.Value);
                            }
                        }));

                        KeyMap joy = KeyMap.NES_btn_A;
                        if (state.Offset.ToString().StartsWith("Buttons"))
                        {
                            string key = joystick.Information.InstanceGuid.ToString() + "," + state.Offset.ToString() + "," + state.RawOffset.ToString();
                            if (AprNesUI.GetInstance().NES_KeyMAP_joypad.ContainsKey(key))
                                joy = AprNesUI.GetInstance().NES_KeyMAP_joypad[key];
                            else
                                continue;
                        }

                        if (AprNesUI.GetInstance().running == true)
                        {

                            if (state.Offset.ToString().StartsWith("X") || state.Offset.ToString().StartsWith("Y"))
                            {
                                string key = joystick.Information.InstanceGuid.ToString() + "," + JoyPadWayName(state.Offset.ToString(), state.Value) + "," + state.RawOffset.ToString() + "," + state.Value.ToString();

                                if (AprNesUI.GetInstance().NES_KeyMAP_joypad.ContainsKey(key))
                                    joy = AprNesUI.GetInstance().NES_KeyMAP_joypad[key];
                                else
                                {
                                    string key_a = joystick.Information.InstanceGuid.ToString() + "," + JoyPadWayName(state.Offset.ToString(), 0) + "," + state.RawOffset.ToString() + "," + "0";
                                    string key_b = joystick.Information.InstanceGuid.ToString() + "," + JoyPadWayName(state.Offset.ToString(), 65535) + "," + state.RawOffset.ToString() + "," + "65535";

                                    if (AprNesUI.GetInstance().NES_KeyMAP_joypad.ContainsKey(key_a) || (AprNesUI.GetInstance().NES_KeyMAP_joypad.ContainsKey(key_b)))
                                    {
                                        if (state.Offset.ToString() == "X")
                                        {
                                            AprNesUI.GetInstance().nes_obj.P1_ButtonUnPress((byte)KeyMap.NES_btn_LEFT);
                                            AprNesUI.GetInstance().nes_obj.P1_ButtonUnPress((byte)KeyMap.NES_btn_RIGHT);
                                        }

                                        if (state.Offset.ToString() == "Y")
                                        {
                                            AprNesUI.GetInstance().nes_obj.P1_ButtonUnPress((byte)KeyMap.NES_btn_UP);
                                            AprNesUI.GetInstance().nes_obj.P1_ButtonUnPress((byte)KeyMap.NES_btn_DOWN);
                                        }
                                    }
                                    continue;
                                }
                            }

                            switch (joy)
                            {
                                case KeyMap.NES_btn_A:
                                    {
                                        if (state.Value == 128)
                                            AprNesUI.GetInstance().nes_obj.P1_ButtonPress((byte)KeyMap.NES_btn_A);
                                        else
                                            AprNesUI.GetInstance().nes_obj.P1_ButtonUnPress((byte)KeyMap.NES_btn_A);

                                    }
                                    break;
                                case KeyMap.NES_btn_B:
                                    {
                                        if (state.Value == 128)
                                            AprNesUI.GetInstance().nes_obj.P1_ButtonPress((byte)KeyMap.NES_btn_B);
                                        else
                                            AprNesUI.GetInstance().nes_obj.P1_ButtonUnPress((byte)KeyMap.NES_btn_B);
                                    }
                                    break;

                                case KeyMap.NES_btn_SELECT:
                                    {
                                        if (state.Value == 128)
                                            AprNesUI.GetInstance().nes_obj.P1_ButtonPress((byte)KeyMap.NES_btn_SELECT);
                                        else
                                            AprNesUI.GetInstance().nes_obj.P1_ButtonUnPress((byte)KeyMap.NES_btn_SELECT);
                                    }
                                    break;
                                case KeyMap.NES_btn_START:
                                    {
                                        if (state.Value == 128)
                                            AprNesUI.GetInstance().nes_obj.P1_ButtonPress((byte)KeyMap.NES_btn_START);
                                        else
                                            AprNesUI.GetInstance().nes_obj.P1_ButtonUnPress((byte)KeyMap.NES_btn_START);
                                    }
                                    break;

                                case KeyMap.NES_btn_UP:
                                    AprNesUI.GetInstance().nes_obj.P1_ButtonPress((byte)KeyMap.NES_btn_UP);
                                    break;

                                case KeyMap.NES_btn_DOWN:
                                    AprNesUI.GetInstance().nes_obj.P1_ButtonPress((byte)KeyMap.NES_btn_DOWN);
                                    break;
                                case KeyMap.NES_btn_LEFT:
                                    AprNesUI.GetInstance().nes_obj.P1_ButtonPress((byte)KeyMap.NES_btn_LEFT);
                                    break;

                                case KeyMap.NES_btn_RIGHT:
                                    AprNesUI.GetInstance().nes_obj.P1_ButtonPress((byte)KeyMap.NES_btn_RIGHT);
                                    break;
                            }

                        }
                    }
                }
            }
        }

        List<Guid> joypads = new List<Guid>();



        //-------------------------------------------------------

        Thread nes_t = null;
        public NesCore nes_obj = null;
        bool running = false;
        public string rom_file = "";
        private void button1_Click(object sender, EventArgs e)
        {

            OpenFileDialog fd = new OpenFileDialog();
            if (fd.ShowDialog() != DialogResult.OK) return;

            rom_file = fd.FileName;

            if (nes_t != null)
            {
                try
                {
                    nes_obj.exit = true;
                    Thread.Sleep(50);
                    nes_t.Abort();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

            nes_obj = null;
            nes_obj = new NesCore();
            nes_obj.LimitFPS = LimitFPS;
            nes_obj.ScreenSize = ScreenSize;
            bool init_result = nes_obj.init(grfx, File.ReadAllBytes(rom_file));
            Console.WriteLine("init finsih");

            if (!init_result)
            {
                fps_count_timer.Enabled = false;
                running = false;
                label3.Text = "fps : ";
                MessageBox.Show("fail !");
                return;
            }
            nes_t = new Thread(nes_obj.run);
            nes_t.Start();
            fps_count_timer.Enabled = true;
            running = true;
        }

        int fps = 0;
        private void fps_count_timer_Tick(object sender, EventArgs e)
        {
            this.Invoke((MethodInvoker)delegate
            {
                fps = nes_obj.frame_count;
                nes_obj.frame_count = 0;
                label3.Text = "fps : " + fps;
            });
        }

        private void AprNesUI_FormClosing(object sender, FormClosingEventArgs e)
        {
            Environment.Exit(1);
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
                nes_obj.P1_ButtonPress((byte)NES_KeyMAP[keyboard_key]);
            return true;
        }

        bool writing = false;
        public void NESCaptureScreen()
        {

            if (!running) return;
            if (writing == true)
                return;
            writing = true;
            while (nes_obj.screen_lock)
                Thread.Sleep(0);
            nes_t.Suspend();
            DateTime dt = DateTime.Now;
            string stamp = (dt.ToLongDateString() + " " + dt.ToLongTimeString()).Replace(":", "-");
            try
            {
                nes_obj.GetScreenFrame().Save(AppConfigure["CaptureScreenPath"] + @"\Screen-" + stamp + ".png", System.Drawing.Imaging.ImageFormat.Png);
            }
            catch { }
            nes_t.Resume();
            Console.WriteLine("Screen-" + stamp + ".png" + " write finish !");
            writing = false;

            MessageBox.Show(AppConfigure["CaptureScreenPath"] + @"\Screen-" + stamp + ".png" + " " + "save!");
        }


        private void AprNesUI_KeyUp(object sender, KeyEventArgs e)
        {
            if (!running) return;
            if (NES_KeyMAP.ContainsKey(e.KeyValue))
                nes_obj.P1_ButtonUnPress((byte)NES_KeyMAP[e.KeyValue]);
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

        private void label2_Click(object sender, EventArgs e)
        {
            LimitFPS = !LimitFPS;
            if (nes_obj != null) nes_obj.LimitFPS = LimitFPS;
        }

        private void label2_Click_1(object sender, EventArgs e)
        {

            AprNes_ConfigureUI.GetInstance().StartPosition = FormStartPosition.CenterParent;
            AprNes_ConfigureUI.GetInstance().init();
            AprNes_ConfigureUI.GetInstance().ShowDialog(this);

        }
        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            AprNes_Infocs AprNesInf = new AprNes_Infocs();
            AprNesInf.ShowDialog();
        }

        private void label4_Click(object sender, EventArgs e)
        {
            Reset();
        }

        public void Reset()
        {
            if (!running) return;

            running = false;
            fps_count_timer.Enabled = true;
            label3.Text = "fps : ";

            if (nes_t != null)
            {
                try
                {
                    nes_obj.exit = true;
                    Thread.Sleep(50);
                    nes_t.Abort();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

            nes_obj = null;
            nes_obj = new NesCore();
            nes_obj.LimitFPS = LimitFPS;
            nes_obj.ScreenSize = ScreenSize;
            bool init_result = nes_obj.init(grfx, File.ReadAllBytes(rom_file));
            Console.WriteLine("init finsih");
            nes_t = new Thread(nes_obj.run);
            nes_t.Start();
            fps_count_timer.Enabled = true;
            running = true;
        }

        private void AprNesUI_Shown(object sender, EventArgs e)
        {
            #region joypad init
            //from http://stackoverflow.com/questions/3929764/taking-input-from-a-joystick-with-c-sharp-net
            var joystickGuid = Guid.Empty;

            if (joystickGuid == Guid.Empty)
                foreach (var deviceInstance in directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AllDevices))
                    joypads.Add(deviceInstance.InstanceGuid);

            if (joypads.Count == 0)
            {
                Console.WriteLine("No joystick/Gamepad found.");
            }
            else
            {

                foreach (Guid i in joypads)
                {
                    Console.WriteLine("Found Joystick/Gamepad with GUID: {0}", i.ToString());

                    Joystick joystick = new Joystick(directInput, i);
                    joystick.Properties.BufferSize = 128;
                    joystick.Acquire();

                    JoyPadListener JoyPadListener_obj = new JoyPadListener(joystick);
                    new Thread(JoyPadListener_obj.start).Start();
                }
            }
            #endregion
        }
    }
}
