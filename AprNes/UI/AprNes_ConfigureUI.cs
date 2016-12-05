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
            init();
        }

        public void init()
        {

            if (!LangINI.LangLoadOK) return;

            Ok_btn.Text = LangINI.lang_table[AprNesUI.GetInstance().AppConfigure["Lang"]]["ok"];
            this.Text = LangINI.lang_table[AprNesUI.GetInstance().AppConfigure["Lang"]]["setting"];
            choose_dir.Text = LangINI.lang_table[AprNesUI.GetInstance().AppConfigure["Lang"]]["selectfolder"];
            groupBox1.Text = LangINI.lang_table[AprNesUI.GetInstance().AppConfigure["Lang"]]["keypad"];
            groupBox2.Text = LangINI.lang_table[AprNesUI.GetInstance().AppConfigure["Lang"]]["joypad"];
            groupBox4.Text = LangINI.lang_table[AprNesUI.GetInstance().AppConfigure["Lang"]]["screen"];
            LimitFPS_checkBox.Text = LangINI.lang_table[AprNesUI.GetInstance().AppConfigure["Lang"]]["limitfps"];
            label18.Text = LangINI.lang_table[AprNesUI.GetInstance().AppConfigure["Lang"]]["langchoose"];
            label9.Text = "Shift + p " + LangINI.lang_table[AprNesUI.GetInstance().AppConfigure["Lang"]]["capture_path"];
            

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

        public void BeforClose()
        {
            if (radioButtonX1.Checked) AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "1";
            else if (radioButtonX2.Checked) AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "2";
            else if (radioButtonX3.Checked) AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "3";
            else if (radioButtonX4.Checked) AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "4";
            else if (radioButtonX5.Checked) AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "5";
            else if (radioButtonX6.Checked) AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "6";
            else if (radioButtonX8.Checked) AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "8";
            else if (radioButtonX9.Checked) AprNesUI.GetInstance().AppConfigure["ScreenSize"] = "9";


            AprNesUI.GetInstance().NES_KeyMAP_joypad.Clear();

            foreach (string key in NES_KeyMAP_joypad_config.Keys)
                AprNesUI.GetInstance().NES_KeyMAP_joypad[key] = NES_KeyMAP_joypad_config[key];

            AprNesUI.GetInstance().AppConfigure["LimitFPS"] = "0";
            if (LimitFPS_checkBox.Checked) AprNesUI.GetInstance().AppConfigure["LimitFPS"] = "1";

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

            AprNesUI.GetInstance().Reset();
        }

        private void OK(object sender, EventArgs e)
        {
            BeforClose();
            Close();
        }

        public void Setup_JoyPad_define(string uid, string btn_name, int raw_id, int value)
        {


            if (joypad_A.Focused)
            {
                if (value != 128) return;
                if (!btn_name.StartsWith("Buttons"))
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
            else if (joypad_B.Focused)
            {
                if (!btn_name.StartsWith("Buttons"))
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
            else if (joypad_START.Focused)
            {
                if (!btn_name.StartsWith("Buttons"))
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
            else if (joypad_SELECT.Focused)
            {

                if (!btn_name.StartsWith("Buttons"))
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
            else if (joypad_UP.Focused)
            {
                if (btn_name.StartsWith("Button") && value == 0)
                    return;
                if (!btn_name.StartsWith("X") && !btn_name.StartsWith("Y"))
                {
                    MessageBox.Show("非 X Y 方向鍵類型輸入!");
                    return;
                }
                if (value == 32511) return;
                joypad_UP.Text = JoyPadWayName(btn_name, value);

                if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_UP))
                {
                    string key = NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_UP).Key;
                    NES_KeyMAP_joypad_config.Remove(key);
                }

                NES_KeyMAP_joypad_config[uid + "," + joypad_UP.Text + "," + raw_id + "," + value] = AprNesUI.KeyMap.NES_btn_UP;
            }
            else if (joypad_DOWN.Focused)
            {
                if (btn_name.StartsWith("Button") && value == 0)
                    return;
                if (!btn_name.StartsWith("X") && !btn_name.StartsWith("Y"))
                {
                    MessageBox.Show("非 X Y 方向鍵類型輸入!");
                    return;
                }
                if (value == 32511) return;
                joypad_DOWN.Text = JoyPadWayName(btn_name, value);

                if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_DOWN))
                {
                    string key = NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_DOWN).Key;
                    NES_KeyMAP_joypad_config.Remove(key);
                }

                NES_KeyMAP_joypad_config[uid + "," + joypad_DOWN.Text + "," + raw_id + "," + value] = AprNesUI.KeyMap.NES_btn_DOWN;
            }
            else if (joypad_LEFT.Focused)
            {
                if (btn_name.StartsWith("Button") && value == 0)
                    return;
                if (!btn_name.StartsWith("X") && !btn_name.StartsWith("Y"))
                {
                    MessageBox.Show("非 X Y 方向鍵類型輸入!");
                    return;
                }
                if (value == 32511) return;
                joypad_LEFT.Text = JoyPadWayName(btn_name, value);

                if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_LEFT))
                {
                    string key = NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_LEFT).Key;
                    NES_KeyMAP_joypad_config.Remove(key);
                }

                NES_KeyMAP_joypad_config[uid + "," + joypad_LEFT.Text + "," + raw_id + "," + value] = AprNesUI.KeyMap.NES_btn_LEFT;
            }
            else if (joypad_RIGHT.Focused)
            {
                if (btn_name.StartsWith("Button") && value == 0)
                    return;
                if (!btn_name.StartsWith("X") && !btn_name.StartsWith("Y"))
                {
                    MessageBox.Show("非 X Y 方向鍵類型輸入!");
                    return;
                }
                if (value == 32511) return;
                joypad_RIGHT.Text = JoyPadWayName(btn_name, value);

                if (NES_KeyMAP_joypad_config.Values.Contains(AprNesUI.KeyMap.NES_btn_RIGHT))
                {
                    string key = NES_KeyMAP_joypad_config.FirstOrDefault(x => x.Value == AprNesUI.KeyMap.NES_btn_RIGHT).Key;
                    NES_KeyMAP_joypad_config.Remove(key);
                }

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
            (groupBox4.Controls.Find("radioButtonX" + AprNesUI.GetInstance().AppConfigure["ScreenSize"], true)[0] as RadioButton).Checked = true;

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
        }

        private void choose_dir_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fd = new FolderBrowserDialog();
            if (fd.ShowDialog() != DialogResult.OK) return;
            screen_path.Text = fd.SelectedPath;
        }

        int key_A = 0, key_B = 0, key_SELECT = 0, key_START = 0, key_RIGHT = 0, key_LEFT = 0, key_UP = 0, key_DOWN = 0;
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
