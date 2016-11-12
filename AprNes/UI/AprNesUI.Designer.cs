namespace AprNes
{
    partial class AprNesUI
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AprNesUI));
            this.panel1 = new System.Windows.Forms.Panel();
            this.fps_count_timer = new System.Windows.Forms.Timer(this.components);
            this.label3 = new System.Windows.Forms.Label();
            this.UIOpenRom = new System.Windows.Forms.Label();
            this.UIReset = new System.Windows.Forms.Label();
            this.UIConfig = new System.Windows.Forms.Label();
            this.UIAbout = new System.Windows.Forms.LinkLabel();
            this.RomInf = new System.Windows.Forms.LinkLabel();
            this.SuspendLayout();
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.Color.White;
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel1.Location = new System.Drawing.Point(5, 35);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(256, 240);
            this.panel1.TabIndex = 1;
            // 
            // fps_count_timer
            // 
            this.fps_count_timer.Interval = 1000;
            this.fps_count_timer.Tick += new System.EventHandler(this.fps_count_timer_Tick);
            // 
            // label3
            // 
            this.label3.BackColor = System.Drawing.Color.Transparent;
            this.label3.Font = new System.Drawing.Font("Microsoft JhengHei", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label3.Location = new System.Drawing.Point(208, 8);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(53, 20);
            this.label3.TabIndex = 11;
            this.label3.Text = "fps : ";
            this.label3.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UIOpenRom
            // 
            this.UIOpenRom.BackColor = System.Drawing.Color.WhiteSmoke;
            this.UIOpenRom.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.UIOpenRom.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.UIOpenRom.Font = new System.Drawing.Font("Microsoft JhengHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.UIOpenRom.Location = new System.Drawing.Point(5, 8);
            this.UIOpenRom.Name = "UIOpenRom";
            this.UIOpenRom.Size = new System.Drawing.Size(65, 20);
            this.UIOpenRom.TabIndex = 13;
            this.UIOpenRom.Text = "Open";
            this.UIOpenRom.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.UIOpenRom.Click += new System.EventHandler(this.button1_Click);
            this.UIOpenRom.MouseEnter += new System.EventHandler(this.label1_MouseEnter);
            this.UIOpenRom.MouseLeave += new System.EventHandler(this.label1_MouseLeave);
            // 
            // UIReset
            // 
            this.UIReset.BackColor = System.Drawing.Color.WhiteSmoke;
            this.UIReset.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.UIReset.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.UIReset.Font = new System.Drawing.Font("Microsoft JhengHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.UIReset.Location = new System.Drawing.Point(73, 8);
            this.UIReset.Name = "UIReset";
            this.UIReset.Size = new System.Drawing.Size(65, 20);
            this.UIReset.TabIndex = 15;
            this.UIReset.Text = "Reset";
            this.UIReset.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.UIReset.Click += new System.EventHandler(this.label4_Click);
            this.UIReset.MouseEnter += new System.EventHandler(this.label1_MouseEnter);
            this.UIReset.MouseLeave += new System.EventHandler(this.label1_MouseLeave);
            // 
            // UIConfig
            // 
            this.UIConfig.BackColor = System.Drawing.Color.WhiteSmoke;
            this.UIConfig.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.UIConfig.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.UIConfig.Font = new System.Drawing.Font("Microsoft JhengHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.UIConfig.Location = new System.Drawing.Point(141, 8);
            this.UIConfig.Name = "UIConfig";
            this.UIConfig.Size = new System.Drawing.Size(65, 20);
            this.UIConfig.TabIndex = 16;
            this.UIConfig.Text = "Config";
            this.UIConfig.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.UIConfig.Click += new System.EventHandler(this.label2_Click_1);
            this.UIConfig.MouseEnter += new System.EventHandler(this.label1_MouseEnter);
            this.UIConfig.MouseLeave += new System.EventHandler(this.label1_MouseLeave);
            // 
            // UIAbout
            // 
            this.UIAbout.Font = new System.Drawing.Font("Microsoft JhengHei", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.UIAbout.LinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.UIAbout.Location = new System.Drawing.Point(201, 277);
            this.UIAbout.Name = "UIAbout";
            this.UIAbout.Size = new System.Drawing.Size(60, 15);
            this.UIAbout.TabIndex = 17;
            this.UIAbout.TabStop = true;
            this.UIAbout.Text = "About";
            this.UIAbout.TextAlign = System.Drawing.ContentAlignment.TopRight;
            this.UIAbout.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabel1_LinkClicked);
            // 
            // RomInf
            // 
            this.RomInf.Font = new System.Drawing.Font("Microsoft JhengHei", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.RomInf.LinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.RomInf.Location = new System.Drawing.Point(5, 277);
            this.RomInf.Name = "RomInf";
            this.RomInf.Size = new System.Drawing.Size(60, 15);
            this.RomInf.TabIndex = 18;
            this.RomInf.TabStop = true;
            this.RomInf.Text = "Rom Info";
            this.RomInf.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.RomInf_LinkClicked);
            // 
            // AprNesUI
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.ClientSize = new System.Drawing.Size(266, 294);
            this.Controls.Add(this.RomInf);
            this.Controls.Add(this.UIAbout);
            this.Controls.Add(this.UIConfig);
            this.Controls.Add(this.UIReset);
            this.Controls.Add(this.UIOpenRom);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.panel1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.KeyPreview = true;
            this.MaximizeBox = false;
            this.Name = "AprNesUI";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.Text = "AprNes";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.AprNesUI_FormClosing);
            this.Shown += new System.EventHandler(this.AprNesUI_Shown);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.AprNesUI_KeyUp);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Timer fps_count_timer;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label UIOpenRom;
        private System.Windows.Forms.Label UIReset;
        private System.Windows.Forms.Label UIConfig;
        private System.Windows.Forms.LinkLabel UIAbout;
        private System.Windows.Forms.LinkLabel RomInf;
    }
}

