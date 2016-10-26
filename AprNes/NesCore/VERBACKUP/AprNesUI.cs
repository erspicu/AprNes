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
        private void button1_Click(object sender, EventArgs e)
        {

            OpenFileDialog fd = new OpenFileDialog();

            if (fd.ShowDialog() != DialogResult.OK)
                return;


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
            bool r = nes_obj.init(grfx, File.ReadAllBytes(fd.FileName));
            Console.WriteLine("init finsih");


            if (!r)
            {
                MessageBox.Show("fail !");
                return;
            }

            nes_t = new Thread(nes_obj.run);
            nes_t.Start();
            fps_count_timer.Enabled = true;

        }



        private void button2_Click(object sender, EventArgs e)
        {

            MessageBox.Show(nes_obj.ppu_ram[0x2000].ToString("X2") + " " + nes_obj.ppu_ram[0x23c0].ToString("X2"));

            return;

            StreamWriter StepsLog;

            if (File.Exists(@"c:\log\log-ppu.txt"))
                File.Delete(@"c:\log\log-ppu.txt");



            StepsLog = File.AppendText(@"c:\log\log-ppu.txt");

            for (int i = 0x2000; i < 0x23c0; i++)
            {
                StepsLog.Write(nes_obj.ppu_ram[i].ToString("X2") + " ");
                if ((i + 1) % 32 == 0)
                    StepsLog.WriteLine("");
            }


            StepsLog.WriteLine("=");

            for (int i = 0x2400; i < 0x27c0; i++)
            {
                StepsLog.Write(nes_obj.ppu_ram[i].ToString("X2") + " ");
                if ((i + 1) % 32 == 0)
                    StepsLog.WriteLine("");
            }


            StepsLog.Close();

            Console.WriteLine("write log ok!");

            /*for (int i = 0; i < 256; i++)
                Console.Write(nes_obj.spr_ram[i].ToString("x2") + " "); */


            for (int i = 0; i < 32; i++)
            {
                Console.Write(nes_obj.ppu_ram[0x3F00 + i].ToString("X2") + " ");
                if ((i + 1) % 4 == 0)
                    Console.WriteLine("");
            }

        }

        private void button3_Click(object sender, EventArgs e)
        {
            Bitmap bitmap = new Bitmap(128, 256);

            for (int w = 0; w < 16; w++)
            {
                for (int h = 0; h < 32; h++)
                {

                    int tile_th = h * 16 + w;


                    for (int i = 0; i < 8; i++) //for x
                        for (int j = 0; j < 8; j++) // for y
                        {

                            byte t = nes_obj.pre_Dec_tiles[64 * tile_th + j * 8 + i];

                            byte c = 0;

                            switch (t)
                            {
                                case 3:
                                    c = 0;
                                    break;
                                case 2:
                                    c = 0xa0;
                                    break;
                                case 1:
                                    c = 0xc0;
                                    break;
                                case 0:
                                    c = 0xfc;
                                    break;
                            }

                            bitmap.SetPixel(w * 8 + i, h * 8 + j, Color.FromArgb(c, c, c));
                        }

                }
            }

            pictureBox1.Image = bitmap;

        }

        private void button4_Click(object sender, EventArgs e)
        {

            Bitmap bitmap = new Bitmap(512, 480);

            StreamWriter StepsLog;

            if (File.Exists(@"c:\log\log-ppu-bg.txt"))
                File.Delete(@"c:\log\log-ppu-bg.txt");
            StepsLog = File.AppendText(@"c:\log\log-ppu-bg.txt");


            int offset = 0;

            if (nes_obj.BgPatternTableAddr == 0x1000)
                offset = 256;


            for (int y = 0; y < 30; y++)//tile y
            {
                for (int x = 0; x < 32; x++)//tile x
                {

                    int tile_th = nes_obj.ppu_ram[0x2000 + x + y * 32];
                    byte attr_pal = nes_obj.ppu_ram[0x23c0 + ((x >> 2) + ((y >> 2) << 3))];
                    int pal_block = ((x >> 1) & 1) + (((y >> 1) & 1) << 1);
                    int pal_index = (attr_pal >> (pal_block * 2)) & 3;
                    int pal_offset = 0x3f00 + pal_index * 4;


                    //MAPPY ATTR 方法
                    //StepsLog.Write(((x >> 2) + ((y >> 2) << 3)).ToString("x2") + " ");
                    // 23C0  (X / 4) +  ((Y / 4) * 8)

                    for (int i = 0; i < 8; i++) //for x
                        for (int j = 0; j < 8; j++) // for y
                        {
                            byte t = nes_obj.pre_Dec_tiles[64 * (tile_th + offset) + j * 8 + i];


                            uint color = nes_obj.NesColors[nes_obj.ppu_ram[pal_offset + t]];
                            bitmap.SetPixel(x * 8 + i, y * 8 + j, Color.FromArgb((int)color));

                        }

                    tile_th = nes_obj.ppu_ram[0x2400 + x + y * 32];

                    attr_pal = nes_obj.ppu_ram[0x27c0 + ((x >> 2) + ((y >> 2) << 3))];
                    pal_block = ((x >> 1) & 1) + (((y >> 1) & 1) << 1);
                    pal_index = (attr_pal >> (pal_block * 2)) & 3;
                    pal_offset = 0x3f00 + pal_index * 4;

                    for (int i = 0; i < 8; i++) //for x
                        for (int j = 0; j < 8; j++) // for y
                        {
                            byte t = nes_obj.pre_Dec_tiles[64 * (tile_th + offset) + j * 8 + i];
                            uint color = nes_obj.NesColors[nes_obj.ppu_ram[pal_offset + t]];
                            bitmap.SetPixel(256 + x * 8 + i, y * 8 + j, Color.FromArgb((int)color));
                        }


                    tile_th = nes_obj.ppu_ram[0x2800 + x + y * 32];

                    attr_pal = nes_obj.ppu_ram[0x2bc0 + ((x >> 2) + ((y >> 2) << 3))];
                    pal_block = ((x >> 1) & 1) + (((y >> 1) & 1) << 1);
                    pal_index = (attr_pal >> (pal_block * 2)) & 3;
                    pal_offset = 0x3f00 + pal_index * 4;

                    for (int i = 0; i < 8; i++) //for x
                        for (int j = 0; j < 8; j++) // for y
                        {
                            byte t = nes_obj.pre_Dec_tiles[64 * (tile_th + offset) + j * 8 + i];
                            uint color = nes_obj.NesColors[nes_obj.ppu_ram[pal_offset + t]];
                            bitmap.SetPixel(x * 8 + i, 240 + y * 8 + j, Color.FromArgb((int)color));
                        }

                    tile_th = nes_obj.ppu_ram[0x2c00 + x + y * 32];

                    attr_pal = nes_obj.ppu_ram[0x2fc0 + ((x >> 2) + ((y >> 2) << 3))];
                    pal_block = ((x >> 1) & 1) + (((y >> 1) & 1) << 1);
                    pal_index = (attr_pal >> (pal_block * 2)) & 3;
                    pal_offset = 0x3f00 + pal_index * 4;

                    for (int i = 0; i < 8; i++) //for x
                        for (int j = 0; j < 8; j++) // for y
                        {
                            byte t = nes_obj.pre_Dec_tiles[64 * (tile_th + offset) + j * 8 + i];
                            uint color = nes_obj.NesColors[nes_obj.ppu_ram[pal_offset + t]];
                            bitmap.SetPixel(256 + x * 8 + i, 240 + y * 8 + j, Color.FromArgb((int)color));
                        }


                }
                StepsLog.WriteLine("");
            }

            StepsLog.Close();
            pictureBox2.Image = bitmap;
        }

        private void button5_Click(object sender, EventArgs e)
        {
        }

        int fps = 0;
        private void fps_count_timer_Tick(object sender, EventArgs e)
        {
            this.Invoke((MethodInvoker)delegate
            {
                fps = nes_obj.frame_count;
                nes_obj.frame_count = 0;
                label3.Text = "fps : " + fps;

               // button4_Click(null, null);
            });


        }
    }
}
