using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;


namespace AprNes
{
    public partial class AprNes_RomInfoUI : Form
    {

        string inf = "";

        public AprNes_RomInfoUI()
        {
            InitializeComponent();
            init();
        }

        public void init()
        {
            inf = AprNesUI.GetInstance().GetRomInfo();
            richTextBox1.Text = inf;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Clipboard.SetText( inf  );
            MessageBox.Show("rom information copy to clipboard !"); 
        }
    }
}
