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
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.fun1ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fun2ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fun7ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fun3ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fun4ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fun5ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fun6ToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.screenModeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fullScreeenToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.normalToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this._soundMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fps_count_timer = new System.Windows.Forms.Timer(this.components);
            this.label3 = new System.Windows.Forms.Label();
            this.UIOpenRom = new System.Windows.Forms.Label();
            this.UIReset = new System.Windows.Forms.Label();
            this.UIConfig = new System.Windows.Forms.Label();
            this.UIAbout = new System.Windows.Forms.LinkLabel();
            this.RomInf = new System.Windows.Forms.LinkLabel();
            this.contextMenuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.Color.White;
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel1.ContextMenuStrip = this.contextMenuStrip1;
            this.panel1.Location = new System.Drawing.Point(8, 52);
            this.panel1.Margin = new System.Windows.Forms.Padding(4);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(383, 359);
            this.panel1.TabIndex = 1;
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fun1ToolStripMenuItem,
            this.fun2ToolStripMenuItem,
            this.fun7ToolStripMenuItem,
            this.fun3ToolStripMenuItem,
            this.fun4ToolStripMenuItem,
            this.fun5ToolStripMenuItem,
            this.fun6ToolStripMenuItem,
            this.screenModeToolStripMenuItem,
            this._soundMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(192, 274);
            // 
            // fun1ToolStripMenuItem
            // 
            this.fun1ToolStripMenuItem.Name = "fun1ToolStripMenuItem";
            this.fun1ToolStripMenuItem.Size = new System.Drawing.Size(191, 30);
            this.fun1ToolStripMenuItem.Text = "Open";
            this.fun1ToolStripMenuItem.Click += new System.EventHandler(this.fun1ToolStripMenuItem_Click);
            // 
            // fun2ToolStripMenuItem
            // 
            this.fun2ToolStripMenuItem.Name = "fun2ToolStripMenuItem";
            this.fun2ToolStripMenuItem.Size = new System.Drawing.Size(191, 30);
            this.fun2ToolStripMenuItem.Text = "Soft Reset";
            this.fun2ToolStripMenuItem.Click += new System.EventHandler(this.fun2ToolStripMenuItem_Click);
            // 
            // fun7ToolStripMenuItem
            // 
            this.fun7ToolStripMenuItem.Name = "fun7ToolStripMenuItem";
            this.fun7ToolStripMenuItem.Size = new System.Drawing.Size(191, 30);
            this.fun7ToolStripMenuItem.Text = "Hard Reset";
            this.fun7ToolStripMenuItem.Click += new System.EventHandler(this.fun7ToolStripMenuItem_Click);
            // 
            // fun3ToolStripMenuItem
            // 
            this.fun3ToolStripMenuItem.Name = "fun3ToolStripMenuItem";
            this.fun3ToolStripMenuItem.Size = new System.Drawing.Size(191, 30);
            this.fun3ToolStripMenuItem.Text = "Config";
            this.fun3ToolStripMenuItem.Click += new System.EventHandler(this.fun3ToolStripMenuItem_Click);
            // 
            // fun4ToolStripMenuItem
            // 
            this.fun4ToolStripMenuItem.Name = "fun4ToolStripMenuItem";
            this.fun4ToolStripMenuItem.Size = new System.Drawing.Size(191, 30);
            this.fun4ToolStripMenuItem.Text = "Rom Info";
            this.fun4ToolStripMenuItem.Click += new System.EventHandler(this.fun4ToolStripMenuItem_Click);
            // 
            // fun5ToolStripMenuItem
            // 
            this.fun5ToolStripMenuItem.Name = "fun5ToolStripMenuItem";
            this.fun5ToolStripMenuItem.Size = new System.Drawing.Size(191, 30);
            this.fun5ToolStripMenuItem.Text = "Exit";
            this.fun5ToolStripMenuItem.Click += new System.EventHandler(this.fun5ToolStripMenuItem_Click);
            // 
            // fun6ToolStripMenuItem
            // 
            this.fun6ToolStripMenuItem.Name = "fun6ToolStripMenuItem";
            this.fun6ToolStripMenuItem.Size = new System.Drawing.Size(191, 30);
            this.fun6ToolStripMenuItem.Text = "About";
            this.fun6ToolStripMenuItem.Click += new System.EventHandler(this.fun6ToolStripMenuItem_Click);
            // 
            // screenModeToolStripMenuItem
            // 
            this.screenModeToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fullScreeenToolStripMenuItem,
            this.normalToolStripMenuItem});
            this.screenModeToolStripMenuItem.Name = "screenModeToolStripMenuItem";
            this.screenModeToolStripMenuItem.Size = new System.Drawing.Size(191, 30);
            this.screenModeToolStripMenuItem.Text = "Screen Mode";
            // 
            // fullScreeenToolStripMenuItem
            // 
            this.fullScreeenToolStripMenuItem.Name = "fullScreeenToolStripMenuItem";
            this.fullScreeenToolStripMenuItem.Size = new System.Drawing.Size(207, 34);
            this.fullScreeenToolStripMenuItem.Text = "FullScreeen";
            this.fullScreeenToolStripMenuItem.Click += new System.EventHandler(this.fullScreeenToolStripMenuItem_Click);
            // 
            // normalToolStripMenuItem
            // 
            this.normalToolStripMenuItem.Name = "normalToolStripMenuItem";
            this.normalToolStripMenuItem.Size = new System.Drawing.Size(207, 34);
            this.normalToolStripMenuItem.Text = "Normal";
            this.normalToolStripMenuItem.Click += new System.EventHandler(this.normalToolStripMenuItem_Click);
            // 
            // _soundMenuItem
            // 
            this._soundMenuItem.Name = "_soundMenuItem";
            this._soundMenuItem.Size = new System.Drawing.Size(191, 30);
            this._soundMenuItem.Text = "Sound: ON";
            this._soundMenuItem.Click += new System.EventHandler(this._soundMenuItem_Click);
            // 
            // fps_count_timer
            // 
            this.fps_count_timer.Interval = 1000;
            this.fps_count_timer.Tick += new System.EventHandler(this.fps_count_timer_Tick);
            // 
            // label3
            // 
            this.label3.BackColor = System.Drawing.Color.Transparent;
            this.label3.Font = new System.Drawing.Font("微軟正黑體", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label3.Location = new System.Drawing.Point(313, 13);
            this.label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(98, 30);
            this.label3.TabIndex = 11;
            this.label3.Text = "fps : ";
            this.label3.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // UIOpenRom
            // 
            this.UIOpenRom.BackColor = System.Drawing.Color.WhiteSmoke;
            this.UIOpenRom.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.UIOpenRom.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            this.UIOpenRom.Font = new System.Drawing.Font("微軟正黑體", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.UIOpenRom.Location = new System.Drawing.Point(9, 13);
            this.UIOpenRom.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.UIOpenRom.Name = "UIOpenRom";
            this.UIOpenRom.Size = new System.Drawing.Size(96, 29);
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
            this.UIReset.Font = new System.Drawing.Font("微軟正黑體", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.UIReset.Location = new System.Drawing.Point(111, 13);
            this.UIReset.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.UIReset.Name = "UIReset";
            this.UIReset.Size = new System.Drawing.Size(96, 29);
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
            this.UIConfig.Font = new System.Drawing.Font("微軟正黑體", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.UIConfig.Location = new System.Drawing.Point(213, 13);
            this.UIConfig.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.UIConfig.Name = "UIConfig";
            this.UIConfig.Size = new System.Drawing.Size(96, 29);
            this.UIConfig.TabIndex = 16;
            this.UIConfig.Text = "Config";
            this.UIConfig.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.UIConfig.Click += new System.EventHandler(this.label2_Click_1);
            this.UIConfig.MouseEnter += new System.EventHandler(this.label1_MouseEnter);
            this.UIConfig.MouseLeave += new System.EventHandler(this.label1_MouseLeave);
            // 
            // UIAbout
            // 
            this.UIAbout.Font = new System.Drawing.Font("微軟正黑體", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.UIAbout.LinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.UIAbout.Location = new System.Drawing.Point(302, 416);
            this.UIAbout.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.UIAbout.Name = "UIAbout";
            this.UIAbout.Size = new System.Drawing.Size(90, 22);
            this.UIAbout.TabIndex = 17;
            this.UIAbout.TabStop = true;
            this.UIAbout.Text = "About";
            this.UIAbout.TextAlign = System.Drawing.ContentAlignment.TopRight;
            this.UIAbout.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkLabel1_LinkClicked);
            // 
            // RomInf
            // 
            this.RomInf.Font = new System.Drawing.Font("微軟正黑體", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.RomInf.LinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.RomInf.Location = new System.Drawing.Point(8, 416);
            this.RomInf.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.RomInf.Name = "RomInf";
            this.RomInf.Size = new System.Drawing.Size(90, 22);
            this.RomInf.TabIndex = 18;
            this.RomInf.TabStop = true;
            this.RomInf.Text = "Rom Info";
            this.RomInf.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.RomInf_LinkClicked);
            // 
            // AprNesUI
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Menu;
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.ClientSize = new System.Drawing.Size(399, 441);
            this.ContextMenuStrip = this.contextMenuStrip1;
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.RomInf);
            this.Controls.Add(this.UIAbout);
            this.Controls.Add(this.UIConfig);
            this.Controls.Add(this.UIReset);
            this.Controls.Add(this.UIOpenRom);
            this.Controls.Add(this.label3);
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.KeyPreview = true;
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MaximizeBox = false;
            this.Name = "AprNesUI";
            this.Opacity = 0D;
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.Text = "AprNes";
            this.TopMost = true;
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.AprNesUI_FormClosing);
            this.Shown += new System.EventHandler(this.AprNesUI_Shown);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.AprNesUI_KeyUp);
            this.contextMenuStrip1.ResumeLayout(false);
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
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem fun1ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem fun2ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem fun7ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem fun3ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem fun4ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem fun5ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem fun6ToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem screenModeToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem fullScreeenToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem normalToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem _soundMenuItem;
    }
}

