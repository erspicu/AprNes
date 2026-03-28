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
            this._ultraAnalogMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this._recordMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this._recordVideoMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this._recordAudioMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this._recordSettingsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fps_count_timer = new System.Windows.Forms.Timer(this.components);
            this.label3 = new System.Windows.Forms.Label();
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this._menuFile = new System.Windows.Forms.ToolStripMenuItem();
            this._menuFileOpen = new System.Windows.Forms.ToolStripMenuItem();
            this._menuFileRecent = new System.Windows.Forms.ToolStripMenuItem();
            this._menuFileSep1 = new System.Windows.Forms.ToolStripSeparator();
            this._menuFileExit = new System.Windows.Forms.ToolStripMenuItem();
            this._menuEmulation = new System.Windows.Forms.ToolStripMenuItem();
            this._menuEmulationSoftReset = new System.Windows.Forms.ToolStripMenuItem();
            this._menuEmulationHardReset = new System.Windows.Forms.ToolStripMenuItem();
            this._menuEmulationSep1 = new System.Windows.Forms.ToolStripSeparator();
            this._menuEmulationLimitFps = new System.Windows.Forms.ToolStripMenuItem();
            this._menuEmulationPerdotFSM = new System.Windows.Forms.ToolStripMenuItem();
            this._menuView = new System.Windows.Forms.ToolStripMenuItem();
            this._menuViewToggleFullScreen = new System.Windows.Forms.ToolStripMenuItem();
            this._menuViewSep1 = new System.Windows.Forms.ToolStripSeparator();
            this._menuViewSound = new System.Windows.Forms.ToolStripMenuItem();
            this._menuViewUltraAnalog = new System.Windows.Forms.ToolStripMenuItem();
            this._menuTools = new System.Windows.Forms.ToolStripMenuItem();
            this._menuToolsRecord = new System.Windows.Forms.ToolStripMenuItem();
            this._menuToolsRecordVideo = new System.Windows.Forms.ToolStripMenuItem();
            this._menuToolsRecordAudio = new System.Windows.Forms.ToolStripMenuItem();
            this._menuToolsScreenshot = new System.Windows.Forms.ToolStripMenuItem();
            this._menuToolsSep1 = new System.Windows.Forms.ToolStripSeparator();
            this._menuToolsRomInfo = new System.Windows.Forms.ToolStripMenuItem();
            this._menuToolsConfig = new System.Windows.Forms.ToolStripMenuItem();
            this._menuHelp = new System.Windows.Forms.ToolStripMenuItem();
            this._menuHelpShortcuts = new System.Windows.Forms.ToolStripMenuItem();
            this._menuHelpAbout = new System.Windows.Forms.ToolStripMenuItem();
            this.contextMenuStrip1.SuspendLayout();
            this.menuStrip1.SuspendLayout();
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
            this._soundMenuItem,
            this._ultraAnalogMenuItem,
            this._recordMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(230, 334);
            // 
            // fun1ToolStripMenuItem
            // 
            this.fun1ToolStripMenuItem.Name = "fun1ToolStripMenuItem";
            this.fun1ToolStripMenuItem.Size = new System.Drawing.Size(229, 30);
            this.fun1ToolStripMenuItem.Text = "Open";
            this.fun1ToolStripMenuItem.Click += new System.EventHandler(this.fun1ToolStripMenuItem_Click);
            // 
            // fun2ToolStripMenuItem
            // 
            this.fun2ToolStripMenuItem.Name = "fun2ToolStripMenuItem";
            this.fun2ToolStripMenuItem.Size = new System.Drawing.Size(229, 30);
            this.fun2ToolStripMenuItem.Text = "Soft Reset";
            this.fun2ToolStripMenuItem.Click += new System.EventHandler(this.fun2ToolStripMenuItem_Click);
            // 
            // fun7ToolStripMenuItem
            // 
            this.fun7ToolStripMenuItem.Name = "fun7ToolStripMenuItem";
            this.fun7ToolStripMenuItem.Size = new System.Drawing.Size(229, 30);
            this.fun7ToolStripMenuItem.Text = "Hard Reset";
            this.fun7ToolStripMenuItem.Click += new System.EventHandler(this.fun7ToolStripMenuItem_Click);
            // 
            // fun3ToolStripMenuItem
            // 
            this.fun3ToolStripMenuItem.Name = "fun3ToolStripMenuItem";
            this.fun3ToolStripMenuItem.Size = new System.Drawing.Size(229, 30);
            this.fun3ToolStripMenuItem.Text = "Config";
            this.fun3ToolStripMenuItem.Click += new System.EventHandler(this.fun3ToolStripMenuItem_Click);
            // 
            // fun4ToolStripMenuItem
            // 
            this.fun4ToolStripMenuItem.Name = "fun4ToolStripMenuItem";
            this.fun4ToolStripMenuItem.Size = new System.Drawing.Size(229, 30);
            this.fun4ToolStripMenuItem.Text = "Rom Info";
            this.fun4ToolStripMenuItem.Click += new System.EventHandler(this.fun4ToolStripMenuItem_Click);
            // 
            // fun5ToolStripMenuItem
            // 
            this.fun5ToolStripMenuItem.Name = "fun5ToolStripMenuItem";
            this.fun5ToolStripMenuItem.Size = new System.Drawing.Size(229, 30);
            this.fun5ToolStripMenuItem.Text = "Exit";
            this.fun5ToolStripMenuItem.Click += new System.EventHandler(this.fun5ToolStripMenuItem_Click);
            // 
            // fun6ToolStripMenuItem
            // 
            this.fun6ToolStripMenuItem.Name = "fun6ToolStripMenuItem";
            this.fun6ToolStripMenuItem.Size = new System.Drawing.Size(229, 30);
            this.fun6ToolStripMenuItem.Text = "About";
            this.fun6ToolStripMenuItem.Click += new System.EventHandler(this.fun6ToolStripMenuItem_Click);
            // 
            // screenModeToolStripMenuItem
            // 
            this.screenModeToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fullScreeenToolStripMenuItem,
            this.normalToolStripMenuItem});
            this.screenModeToolStripMenuItem.Name = "screenModeToolStripMenuItem";
            this.screenModeToolStripMenuItem.Size = new System.Drawing.Size(229, 30);
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
            this._soundMenuItem.Size = new System.Drawing.Size(229, 30);
            this._soundMenuItem.Text = "Sound: ON";
            this._soundMenuItem.Click += new System.EventHandler(this._soundMenuItem_Click);
            // 
            // _ultraAnalogMenuItem
            // 
            this._ultraAnalogMenuItem.Name = "_ultraAnalogMenuItem";
            this._ultraAnalogMenuItem.Size = new System.Drawing.Size(229, 30);
            this._ultraAnalogMenuItem.Text = "Ultra Analog: OFF";
            this._ultraAnalogMenuItem.Click += new System.EventHandler(this._ultraAnalogMenuItem_Click);
            // 
            // _recordMenuItem
            // 
            this._recordMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._recordVideoMenuItem,
            this._recordAudioMenuItem,
            this._recordSettingsMenuItem});
            this._recordMenuItem.Name = "_recordMenuItem";
            this._recordMenuItem.Size = new System.Drawing.Size(229, 30);
            this._recordMenuItem.Text = "Record";
            // 
            // _recordVideoMenuItem
            // 
            this._recordVideoMenuItem.Name = "_recordVideoMenuItem";
            this._recordVideoMenuItem.Size = new System.Drawing.Size(225, 34);
            this._recordVideoMenuItem.Text = "Record Video";
            this._recordVideoMenuItem.Click += new System.EventHandler(this._recordVideoMenuItem_Click);
            // 
            // _recordAudioMenuItem
            // 
            this._recordAudioMenuItem.Enabled = false;
            this._recordAudioMenuItem.Name = "_recordAudioMenuItem";
            this._recordAudioMenuItem.Size = new System.Drawing.Size(225, 34);
            this._recordAudioMenuItem.Text = "Record Audio";
            this._recordAudioMenuItem.Click += new System.EventHandler(this._recordAudioMenuItem_Click);
            // 
            // _recordSettingsMenuItem
            // 
            this._recordSettingsMenuItem.Enabled = false;
            this._recordSettingsMenuItem.Name = "_recordSettingsMenuItem";
            this._recordSettingsMenuItem.Size = new System.Drawing.Size(225, 34);
            this._recordSettingsMenuItem.Text = "Settings";
            this._recordSettingsMenuItem.Visible = false;
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
            this.label3.Location = new System.Drawing.Point(8, 416);
            this.label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(98, 30);
            this.label3.TabIndex = 11;
            this.label3.Text = "fps : ";
            // 
            // menuStrip1
            // 
            this.menuStrip1.GripMargin = new System.Windows.Forms.Padding(2, 2, 0, 2);
            this.menuStrip1.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._menuFile,
            this._menuEmulation,
            this._menuView,
            this._menuTools,
            this._menuHelp});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(399, 31);
            this.menuStrip1.TabIndex = 19;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // _menuFile
            // 
            this._menuFile.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._menuFileOpen,
            this._menuFileRecent,
            this._menuFileSep1,
            this._menuFileExit});
            this._menuFile.Name = "_menuFile";
            this._menuFile.Size = new System.Drawing.Size(55, 27);
            this._menuFile.Text = "File";
            // 
            // _menuFileOpen
            // 
            this._menuFileOpen.Name = "_menuFileOpen";
            this._menuFileOpen.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.O)));
            this._menuFileOpen.Size = new System.Drawing.Size(226, 34);
            this._menuFileOpen.Text = "Open";
            this._menuFileOpen.Click += new System.EventHandler(this.fun1ToolStripMenuItem_Click);
            // 
            // _menuFileRecent
            // 
            this._menuFileRecent.Name = "_menuFileRecent";
            this._menuFileRecent.Size = new System.Drawing.Size(226, 34);
            this._menuFileRecent.Text = "Recent";
            // 
            // _menuFileSep1
            // 
            this._menuFileSep1.Name = "_menuFileSep1";
            this._menuFileSep1.Size = new System.Drawing.Size(223, 6);
            // 
            // _menuFileExit
            // 
            this._menuFileExit.Name = "_menuFileExit";
            this._menuFileExit.Size = new System.Drawing.Size(226, 34);
            this._menuFileExit.Text = "Exit";
            this._menuFileExit.Click += new System.EventHandler(this.fun5ToolStripMenuItem_Click);
            // 
            // _menuEmulation
            // 
            this._menuEmulation.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._menuEmulationSoftReset,
            this._menuEmulationHardReset,
            this._menuEmulationSep1,
            this._menuEmulationLimitFps,
            this._menuEmulationPerdotFSM});
            this._menuEmulation.Name = "_menuEmulation";
            this._menuEmulation.Size = new System.Drawing.Size(113, 27);
            this._menuEmulation.Text = "Emulation";
            // 
            // _menuEmulationSoftReset
            // 
            this._menuEmulationSoftReset.Name = "_menuEmulationSoftReset";
            this._menuEmulationSoftReset.ShortcutKeys = ((System.Windows.Forms.Keys)((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.R)));
            this._menuEmulationSoftReset.Size = new System.Drawing.Size(265, 34);
            this._menuEmulationSoftReset.Text = "Soft Reset";
            this._menuEmulationSoftReset.Click += new System.EventHandler(this.fun2ToolStripMenuItem_Click);
            // 
            // _menuEmulationHardReset
            // 
            this._menuEmulationHardReset.Name = "_menuEmulationHardReset";
            this._menuEmulationHardReset.Size = new System.Drawing.Size(265, 34);
            this._menuEmulationHardReset.Text = "Hard Reset";
            this._menuEmulationHardReset.Click += new System.EventHandler(this.fun7ToolStripMenuItem_Click);
            // 
            // _menuEmulationSep1
            // 
            this._menuEmulationSep1.Name = "_menuEmulationSep1";
            this._menuEmulationSep1.Size = new System.Drawing.Size(262, 6);
            // 
            // _menuEmulationLimitFps
            // 
            this._menuEmulationLimitFps.CheckOnClick = true;
            this._menuEmulationLimitFps.Name = "_menuEmulationLimitFps";
            this._menuEmulationLimitFps.Size = new System.Drawing.Size(265, 34);
            this._menuEmulationLimitFps.Text = "Limit FPS";
            this._menuEmulationLimitFps.Click += new System.EventHandler(this._menuEmulationLimitFps_Click);
            // 
            // _menuEmulationPerdotFSM
            // 
            this._menuEmulationPerdotFSM.CheckOnClick = true;
            this._menuEmulationPerdotFSM.Name = "_menuEmulationPerdotFSM";
            this._menuEmulationPerdotFSM.Size = new System.Drawing.Size(265, 34);
            this._menuEmulationPerdotFSM.Text = "Per-dot OAM FSM";
            this._menuEmulationPerdotFSM.Click += new System.EventHandler(this._menuEmulationPerdotFSM_Click);
            // 
            // _menuView
            // 
            this._menuView.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._menuViewToggleFullScreen,
            this._menuViewSep1,
            this._menuViewSound,
            this._menuViewUltraAnalog});
            this._menuView.Name = "_menuView";
            this._menuView.Size = new System.Drawing.Size(67, 27);
            this._menuView.Text = "View";
            // 
            // _menuViewToggleFullScreen
            // 
            this._menuViewToggleFullScreen.Name = "_menuViewToggleFullScreen";
            this._menuViewToggleFullScreen.ShortcutKeys = System.Windows.Forms.Keys.F11;
            this._menuViewToggleFullScreen.Size = new System.Drawing.Size(259, 34);
            this._menuViewToggleFullScreen.Text = "FullScreen";
            this._menuViewToggleFullScreen.Click += new System.EventHandler(this._menuViewToggleFullScreen_Click);
            // 
            // _menuViewSep1
            // 
            this._menuViewSep1.Name = "_menuViewSep1";
            this._menuViewSep1.Size = new System.Drawing.Size(256, 6);
            // 
            // _menuViewSound
            // 
            this._menuViewSound.Name = "_menuViewSound";
            this._menuViewSound.Size = new System.Drawing.Size(259, 34);
            this._menuViewSound.Text = "Sound: ON";
            this._menuViewSound.Click += new System.EventHandler(this._soundMenuItem_Click);
            // 
            // _menuViewUltraAnalog
            // 
            this._menuViewUltraAnalog.Name = "_menuViewUltraAnalog";
            this._menuViewUltraAnalog.Size = new System.Drawing.Size(259, 34);
            this._menuViewUltraAnalog.Text = "Ultra Analog: OFF";
            this._menuViewUltraAnalog.Click += new System.EventHandler(this._ultraAnalogMenuItem_Click);
            // 
            // _menuTools
            // 
            this._menuTools.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._menuToolsRecord,
            this._menuToolsScreenshot,
            this._menuToolsSep1,
            this._menuToolsRomInfo,
            this._menuToolsConfig});
            this._menuTools.Name = "_menuTools";
            this._menuTools.Size = new System.Drawing.Size(71, 27);
            this._menuTools.Text = "Tools";
            // 
            // _menuToolsRecord
            // 
            this._menuToolsRecord.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._menuToolsRecordVideo,
            this._menuToolsRecordAudio});
            this._menuToolsRecord.Name = "_menuToolsRecord";
            this._menuToolsRecord.Size = new System.Drawing.Size(321, 34);
            this._menuToolsRecord.Text = "Record";
            // 
            // _menuToolsRecordVideo
            // 
            this._menuToolsRecordVideo.Name = "_menuToolsRecordVideo";
            this._menuToolsRecordVideo.Size = new System.Drawing.Size(225, 34);
            this._menuToolsRecordVideo.Text = "Record Video";
            this._menuToolsRecordVideo.Click += new System.EventHandler(this._recordVideoMenuItem_Click);
            // 
            // _menuToolsRecordAudio
            // 
            this._menuToolsRecordAudio.Name = "_menuToolsRecordAudio";
            this._menuToolsRecordAudio.Size = new System.Drawing.Size(225, 34);
            this._menuToolsRecordAudio.Text = "Record Audio";
            this._menuToolsRecordAudio.Click += new System.EventHandler(this._recordAudioMenuItem_Click);
            // 
            // _menuToolsScreenshot
            // 
            this._menuToolsScreenshot.Name = "_menuToolsScreenshot";
            this._menuToolsScreenshot.ShortcutKeys = ((System.Windows.Forms.Keys)(((System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Shift) 
            | System.Windows.Forms.Keys.P)));
            this._menuToolsScreenshot.Size = new System.Drawing.Size(321, 34);
            this._menuToolsScreenshot.Text = "Screenshot";
            this._menuToolsScreenshot.Click += new System.EventHandler(this._menuToolsScreenshot_Click);
            // 
            // _menuToolsSep1
            // 
            this._menuToolsSep1.Name = "_menuToolsSep1";
            this._menuToolsSep1.Size = new System.Drawing.Size(318, 6);
            // 
            // _menuToolsRomInfo
            // 
            this._menuToolsRomInfo.Name = "_menuToolsRomInfo";
            this._menuToolsRomInfo.Size = new System.Drawing.Size(321, 34);
            this._menuToolsRomInfo.Text = "Rom Info";
            this._menuToolsRomInfo.Click += new System.EventHandler(this.fun4ToolStripMenuItem_Click);
            // 
            // _menuToolsConfig
            // 
            this._menuToolsConfig.Name = "_menuToolsConfig";
            this._menuToolsConfig.Size = new System.Drawing.Size(321, 34);
            this._menuToolsConfig.Text = "Config";
            this._menuToolsConfig.Click += new System.EventHandler(this.fun3ToolStripMenuItem_Click);
            // 
            // _menuHelp
            // 
            this._menuHelp.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._menuHelpShortcuts,
            this._menuHelpAbout});
            this._menuHelp.Name = "_menuHelp";
            this._menuHelp.Size = new System.Drawing.Size(66, 27);
            this._menuHelp.Text = "Help";
            // 
            // _menuHelpShortcuts
            // 
            this._menuHelpShortcuts.Name = "_menuHelpShortcuts";
            this._menuHelpShortcuts.Size = new System.Drawing.Size(276, 34);
            this._menuHelpShortcuts.Text = "Keyboard Shortcuts";
            this._menuHelpShortcuts.Click += new System.EventHandler(this._menuHelpShortcuts_Click);
            // 
            // _menuHelpAbout
            // 
            this._menuHelpAbout.Name = "_menuHelpAbout";
            this._menuHelpAbout.Size = new System.Drawing.Size(276, 34);
            this._menuHelpAbout.Text = "About";
            this._menuHelpAbout.Click += new System.EventHandler(this.fun6ToolStripMenuItem_Click);
            // 
            // AprNesUI
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Menu;
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.ClientSize = new System.Drawing.Size(399, 441);
            this.ContextMenuStrip = this.contextMenuStrip1;
            this.Controls.Add(this.menuStrip1);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.label3);
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.KeyPreview = true;
            this.MainMenuStrip = this.menuStrip1;
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
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Timer fps_count_timer;
        private System.Windows.Forms.Label label3;
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
        private System.Windows.Forms.ToolStripMenuItem _ultraAnalogMenuItem;
        private System.Windows.Forms.ToolStripMenuItem _recordMenuItem;
        private System.Windows.Forms.ToolStripMenuItem _recordVideoMenuItem;
        private System.Windows.Forms.ToolStripMenuItem _recordAudioMenuItem;
        private System.Windows.Forms.ToolStripMenuItem _recordSettingsMenuItem;
        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem _menuFile;
        private System.Windows.Forms.ToolStripMenuItem _menuFileOpen;
        private System.Windows.Forms.ToolStripMenuItem _menuFileRecent;
        private System.Windows.Forms.ToolStripSeparator _menuFileSep1;
        private System.Windows.Forms.ToolStripMenuItem _menuFileExit;
        private System.Windows.Forms.ToolStripMenuItem _menuEmulation;
        private System.Windows.Forms.ToolStripMenuItem _menuEmulationSoftReset;
        private System.Windows.Forms.ToolStripMenuItem _menuEmulationHardReset;
        private System.Windows.Forms.ToolStripSeparator _menuEmulationSep1;
        private System.Windows.Forms.ToolStripMenuItem _menuEmulationLimitFps;
        private System.Windows.Forms.ToolStripMenuItem _menuEmulationPerdotFSM;
        private System.Windows.Forms.ToolStripMenuItem _menuView;
        private System.Windows.Forms.ToolStripMenuItem _menuViewToggleFullScreen;
        private System.Windows.Forms.ToolStripSeparator _menuViewSep1;
        private System.Windows.Forms.ToolStripMenuItem _menuViewSound;
        private System.Windows.Forms.ToolStripMenuItem _menuViewUltraAnalog;
        private System.Windows.Forms.ToolStripMenuItem _menuTools;
        private System.Windows.Forms.ToolStripMenuItem _menuToolsRecord;
        private System.Windows.Forms.ToolStripMenuItem _menuToolsRecordVideo;
        private System.Windows.Forms.ToolStripMenuItem _menuToolsRecordAudio;
        private System.Windows.Forms.ToolStripMenuItem _menuToolsScreenshot;
        private System.Windows.Forms.ToolStripSeparator _menuToolsSep1;
        private System.Windows.Forms.ToolStripMenuItem _menuToolsRomInfo;
        private System.Windows.Forms.ToolStripMenuItem _menuToolsConfig;
        private System.Windows.Forms.ToolStripMenuItem _menuHelp;
        private System.Windows.Forms.ToolStripMenuItem _menuHelpShortcuts;
        private System.Windows.Forms.ToolStripMenuItem _menuHelpAbout;
    }
}

