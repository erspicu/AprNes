using System;
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
using AprNes;

namespace AprNes
{
    public partial class AprNesUI : Form
    {
        Graphics grfx;

        public AprNesUI()
        {
            InitializeComponent();
            grfx = panel1.CreateGraphics();
        }

        Thread nes_t = null;
        NesCore nes_obj = null;
        bool running = false;
        private void button1_Click(object sender, EventArgs e)
        {

            OpenFileDialog fd = new OpenFileDialog();
            if (fd.ShowDialog() != DialogResult.OK) return;

            if (nes_t != null)
            {
                try
                {
                    nes_obj.exit = true;
                    Thread.Sleep(100);
                    nes_t.Abort();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

            nes_obj = null;
            nes_obj = new NesCore();
            nes_obj.LimitFPS = limitefps;
            bool r = nes_obj.init(grfx, File.ReadAllBytes(fd.FileName));
            Console.WriteLine("init finsih");

            if (!r)
            {
                fps_count_timer.Enabled = false;
                running = false;
                label3.Text = "FPS : ";
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
                label3.Text = "FPS : " + fps;
            });
        }

        private void AprNesUI_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                nes_obj.exit = true;
                Thread.Sleep(50);
                nes_t.Abort();
            }
            catch
            {
            }
        }

        private void AprNesUI_KeyDown(object sender, KeyEventArgs e)
        {
            if (!running) return;
            switch (e.KeyValue)
            {
                case 90: nes_obj.P1_ButtonPress(0); break;//z -> A
                case 88: nes_obj.P1_ButtonPress(1); break;//x -> B
                case 83: nes_obj.P1_ButtonPress(2); break;//s -> select
                case 65: nes_obj.P1_ButtonPress(3); break;//a -> Start
                case 38: nes_obj.P1_ButtonPress(4); break;//up
                case 40: nes_obj.P1_ButtonPress(5); break;//down
                case 37: nes_obj.P1_ButtonPress(6); break;//left
                case 39: nes_obj.P1_ButtonPress(7); break;//right
            }
        }

        private void AprNesUI_KeyUp(object sender, KeyEventArgs e)
        {
            if (!running) return;
            switch (e.KeyValue)
            {
                case 90: nes_obj.P1_ButtonUnPress(0); break;//z -> A
                case 88: nes_obj.P1_ButtonUnPress(1); break;//x -> B
                case 83: nes_obj.P1_ButtonUnPress(2); break;// s -> select
                case 65: nes_obj.P1_ButtonUnPress(3); break;//a -> Start
                case 38: nes_obj.P1_ButtonUnPress(4); break;//up
                case 40: nes_obj.P1_ButtonUnPress(5); break;//down
                case 37: nes_obj.P1_ButtonUnPress(6); break;//left
                case 39: nes_obj.P1_ButtonUnPress(7); break;//right
            }
        }

        bool limitefps = true;
        private void label1_MouseEnter(object sender, EventArgs e)
        {
            this.Cursor = Cursors.Hand;
        }

        private void label1_MouseLeave(object sender, EventArgs e)
        {
            this.Cursor = Cursors.Default;
        }

        private void label2_Click(object sender, EventArgs e)
        {
            limitefps = !limitefps;
            label2.Text = (limitefps) ? "限制速度" : "不限制速度";
            if (nes_obj != null) nes_obj.LimitFPS = limitefps;
        }
    }
}
