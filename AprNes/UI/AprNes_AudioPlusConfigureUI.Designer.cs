namespace AprNes.UI
{
    partial class AprNes_AudioPlusConfigureUI
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.grpAuthentic = new System.Windows.Forms.GroupBox();
            this.lblConsoleModel = new System.Windows.Forms.Label();
            this.cboConsoleModel = new System.Windows.Forms.ComboBox();
            this.chkRfCrosstalk = new System.Windows.Forms.CheckBox();
            this.lblCustomCutoff = new System.Windows.Forms.Label();
            this.trkCustomCutoff = new System.Windows.Forms.TrackBar();
            this.lblCustomCutoffVal = new System.Windows.Forms.Label();
            this.chkCustomBuzz = new System.Windows.Forms.CheckBox();
            this.lblBuzzFreq = new System.Windows.Forms.Label();
            this.cboBuzzFreq = new System.Windows.Forms.ComboBox();
            this.lblBuzzAmp = new System.Windows.Forms.Label();
            this.trkBuzzAmp = new System.Windows.Forms.TrackBar();
            this.lblBuzzAmpVal = new System.Windows.Forms.Label();
            this.lblRfVol = new System.Windows.Forms.Label();
            this.trkRfVol = new System.Windows.Forms.TrackBar();
            this.lblRfVolVal = new System.Windows.Forms.Label();
            this.grpModern = new System.Windows.Forms.GroupBox();
            this.lblStereoWidth = new System.Windows.Forms.Label();
            this.trkStereoWidth = new System.Windows.Forms.TrackBar();
            this.lblStereoWidthVal = new System.Windows.Forms.Label();
            this.lblHaasDelay = new System.Windows.Forms.Label();
            this.trkHaasDelay = new System.Windows.Forms.TrackBar();
            this.lblHaasDelayVal = new System.Windows.Forms.Label();
            this.lblHaasCrossfeed = new System.Windows.Forms.Label();
            this.trkHaasCrossfeed = new System.Windows.Forms.TrackBar();
            this.lblHaasCrossfeedVal = new System.Windows.Forms.Label();
            this.lblReverbWet = new System.Windows.Forms.Label();
            this.trkReverbWet = new System.Windows.Forms.TrackBar();
            this.lblReverbWetVal = new System.Windows.Forms.Label();
            this.lblCombFeedback = new System.Windows.Forms.Label();
            this.trkCombFeedback = new System.Windows.Forms.TrackBar();
            this.lblCombFeedbackVal = new System.Windows.Forms.Label();
            this.lblCombDamp = new System.Windows.Forms.Label();
            this.trkCombDamp = new System.Windows.Forms.TrackBar();
            this.lblCombDampVal = new System.Windows.Forms.Label();
            this.lblBassDb = new System.Windows.Forms.Label();
            this.trkBassDb = new System.Windows.Forms.TrackBar();
            this.lblBassDbVal = new System.Windows.Forms.Label();
            this.lblBassFreq = new System.Windows.Forms.Label();
            this.trkBassFreq = new System.Windows.Forms.TrackBar();
            this.lblBassFreqVal = new System.Windows.Forms.Label();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.ChannelVol = new System.Windows.Forms.GroupBox();
            this.grpAuthentic.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.trkCustomCutoff)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkBuzzAmp)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkRfVol)).BeginInit();
            this.grpModern.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.trkStereoWidth)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkHaasDelay)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkHaasCrossfeed)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkReverbWet)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkCombFeedback)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkCombDamp)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkBassDb)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkBassFreq)).BeginInit();
            this.ChannelVol.SuspendLayout();
            this.SuspendLayout();
            // 
            // grpAuthentic
            // 
            this.grpAuthentic.Controls.Add(this.lblConsoleModel);
            this.grpAuthentic.Controls.Add(this.cboConsoleModel);
            this.grpAuthentic.Controls.Add(this.chkRfCrosstalk);
            this.grpAuthentic.Controls.Add(this.lblCustomCutoff);
            this.grpAuthentic.Controls.Add(this.trkCustomCutoff);
            this.grpAuthentic.Controls.Add(this.lblCustomCutoffVal);
            this.grpAuthentic.Controls.Add(this.chkCustomBuzz);
            this.grpAuthentic.Controls.Add(this.lblBuzzFreq);
            this.grpAuthentic.Controls.Add(this.cboBuzzFreq);
            this.grpAuthentic.Controls.Add(this.lblBuzzAmp);
            this.grpAuthentic.Controls.Add(this.trkBuzzAmp);
            this.grpAuthentic.Controls.Add(this.lblBuzzAmpVal);
            this.grpAuthentic.Controls.Add(this.lblRfVol);
            this.grpAuthentic.Controls.Add(this.trkRfVol);
            this.grpAuthentic.Controls.Add(this.lblRfVolVal);
            this.grpAuthentic.Location = new System.Drawing.Point(12, 12);
            this.grpAuthentic.Name = "grpAuthentic";
            this.grpAuthentic.Size = new System.Drawing.Size(566, 562);
            this.grpAuthentic.TabIndex = 0;
            this.grpAuthentic.TabStop = false;
            this.grpAuthentic.Text = "Authentic (AudioMode=1)";
            // 
            // lblConsoleModel
            // 
            this.lblConsoleModel.AutoSize = true;
            this.lblConsoleModel.Location = new System.Drawing.Point(16, 35);
            this.lblConsoleModel.Name = "lblConsoleModel";
            this.lblConsoleModel.Size = new System.Drawing.Size(112, 18);
            this.lblConsoleModel.TabIndex = 0;
            this.lblConsoleModel.Text = "Console Model";
            // 
            // cboConsoleModel
            // 
            this.cboConsoleModel.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboConsoleModel.FormattingEnabled = true;
            this.cboConsoleModel.Location = new System.Drawing.Point(160, 32);
            this.cboConsoleModel.Name = "cboConsoleModel";
            this.cboConsoleModel.Size = new System.Drawing.Size(390, 26);
            this.cboConsoleModel.TabIndex = 1;
            this.cboConsoleModel.SelectedIndexChanged += new System.EventHandler(this.cboConsoleModel_Changed);
            // 
            // chkRfCrosstalk
            // 
            this.chkRfCrosstalk.AutoSize = true;
            this.chkRfCrosstalk.Location = new System.Drawing.Point(16, 93);
            this.chkRfCrosstalk.Name = "chkRfCrosstalk";
            this.chkRfCrosstalk.Size = new System.Drawing.Size(124, 22);
            this.chkRfCrosstalk.TabIndex = 2;
            this.chkRfCrosstalk.Text = "RF Crosstalk";
            // 
            // lblCustomCutoff
            // 
            this.lblCustomCutoff.AutoSize = true;
            this.lblCustomCutoff.Location = new System.Drawing.Point(16, 150);
            this.lblCustomCutoff.Name = "lblCustomCutoff";
            this.lblCustomCutoff.Size = new System.Drawing.Size(154, 18);
            this.lblCustomCutoff.TabIndex = 3;
            this.lblCustomCutoff.Text = "LPF Cutoff (Custom)";
            // 
            // trkCustomCutoff
            // 
            this.trkCustomCutoff.LargeChange = 1000;
            this.trkCustomCutoff.Location = new System.Drawing.Point(160, 147);
            this.trkCustomCutoff.Maximum = 22000;
            this.trkCustomCutoff.Minimum = 1000;
            this.trkCustomCutoff.Name = "trkCustomCutoff";
            this.trkCustomCutoff.Size = new System.Drawing.Size(320, 69);
            this.trkCustomCutoff.TabIndex = 4;
            this.trkCustomCutoff.TickFrequency = 2000;
            this.trkCustomCutoff.Value = 14000;
            this.trkCustomCutoff.Scroll += new System.EventHandler(this.UpdateAllValueLabels);
            // 
            // lblCustomCutoffVal
            // 
            this.lblCustomCutoffVal.AutoSize = true;
            this.lblCustomCutoffVal.Location = new System.Drawing.Point(490, 150);
            this.lblCustomCutoffVal.Name = "lblCustomCutoffVal";
            this.lblCustomCutoffVal.Size = new System.Drawing.Size(73, 18);
            this.lblCustomCutoffVal.TabIndex = 5;
            this.lblCustomCutoffVal.Text = "14000 Hz";
            // 
            // chkCustomBuzz
            // 
            this.chkCustomBuzz.AutoSize = true;
            this.chkCustomBuzz.Location = new System.Drawing.Point(16, 251);
            this.chkCustomBuzz.Name = "chkCustomBuzz";
            this.chkCustomBuzz.Size = new System.Drawing.Size(138, 22);
            this.chkCustomBuzz.TabIndex = 6;
            this.chkCustomBuzz.Text = "Buzz (Custom)";
            // 
            // lblBuzzFreq
            // 
            this.lblBuzzFreq.AutoSize = true;
            this.lblBuzzFreq.Location = new System.Drawing.Point(260, 254);
            this.lblBuzzFreq.Name = "lblBuzzFreq";
            this.lblBuzzFreq.Size = new System.Drawing.Size(79, 18);
            this.lblBuzzFreq.TabIndex = 7;
            this.lblBuzzFreq.Text = "Buzz Freq";
            // 
            // cboBuzzFreq
            // 
            this.cboBuzzFreq.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboBuzzFreq.FormattingEnabled = true;
            this.cboBuzzFreq.Location = new System.Drawing.Point(350, 251);
            this.cboBuzzFreq.Name = "cboBuzzFreq";
            this.cboBuzzFreq.Size = new System.Drawing.Size(80, 26);
            this.cboBuzzFreq.TabIndex = 8;
            // 
            // lblBuzzAmp
            // 
            this.lblBuzzAmp.AutoSize = true;
            this.lblBuzzAmp.Location = new System.Drawing.Point(16, 312);
            this.lblBuzzAmp.Name = "lblBuzzAmp";
            this.lblBuzzAmp.Size = new System.Drawing.Size(120, 18);
            this.lblBuzzAmp.TabIndex = 9;
            this.lblBuzzAmp.Text = "Buzz Amplitude";
            // 
            // trkBuzzAmp
            // 
            this.trkBuzzAmp.LargeChange = 10;
            this.trkBuzzAmp.Location = new System.Drawing.Point(160, 309);
            this.trkBuzzAmp.Maximum = 100;
            this.trkBuzzAmp.Name = "trkBuzzAmp";
            this.trkBuzzAmp.Size = new System.Drawing.Size(320, 69);
            this.trkBuzzAmp.TabIndex = 10;
            this.trkBuzzAmp.TickFrequency = 10;
            this.trkBuzzAmp.Value = 30;
            this.trkBuzzAmp.Scroll += new System.EventHandler(this.UpdateAllValueLabels);
            // 
            // lblBuzzAmpVal
            // 
            this.lblBuzzAmpVal.AutoSize = true;
            this.lblBuzzAmpVal.Location = new System.Drawing.Point(490, 312);
            this.lblBuzzAmpVal.Name = "lblBuzzAmpVal";
            this.lblBuzzAmpVal.Size = new System.Drawing.Size(38, 18);
            this.lblBuzzAmpVal.TabIndex = 11;
            this.lblBuzzAmpVal.Text = "30%";
            // 
            // lblRfVol
            // 
            this.lblRfVol.AutoSize = true;
            this.lblRfVol.Location = new System.Drawing.Point(16, 413);
            this.lblRfVol.Name = "lblRfVol";
            this.lblRfVol.Size = new System.Drawing.Size(87, 18);
            this.lblRfVol.TabIndex = 12;
            this.lblRfVol.Text = "RF Volume";
            // 
            // trkRfVol
            // 
            this.trkRfVol.LargeChange = 20;
            this.trkRfVol.Location = new System.Drawing.Point(160, 410);
            this.trkRfVol.Maximum = 200;
            this.trkRfVol.Name = "trkRfVol";
            this.trkRfVol.Size = new System.Drawing.Size(320, 69);
            this.trkRfVol.TabIndex = 13;
            this.trkRfVol.TickFrequency = 20;
            this.trkRfVol.Value = 50;
            this.trkRfVol.Scroll += new System.EventHandler(this.UpdateAllValueLabels);
            // 
            // lblRfVolVal
            // 
            this.lblRfVolVal.AutoSize = true;
            this.lblRfVolVal.Location = new System.Drawing.Point(490, 413);
            this.lblRfVolVal.Name = "lblRfVolVal";
            this.lblRfVolVal.Size = new System.Drawing.Size(24, 18);
            this.lblRfVolVal.TabIndex = 14;
            this.lblRfVolVal.Text = "50";
            // 
            // grpModern
            // 
            this.grpModern.Controls.Add(this.lblStereoWidth);
            this.grpModern.Controls.Add(this.trkStereoWidth);
            this.grpModern.Controls.Add(this.lblStereoWidthVal);
            this.grpModern.Controls.Add(this.lblHaasDelay);
            this.grpModern.Controls.Add(this.trkHaasDelay);
            this.grpModern.Controls.Add(this.lblHaasDelayVal);
            this.grpModern.Controls.Add(this.lblHaasCrossfeed);
            this.grpModern.Controls.Add(this.trkHaasCrossfeed);
            this.grpModern.Controls.Add(this.lblHaasCrossfeedVal);
            this.grpModern.Controls.Add(this.lblReverbWet);
            this.grpModern.Controls.Add(this.trkReverbWet);
            this.grpModern.Controls.Add(this.lblReverbWetVal);
            this.grpModern.Controls.Add(this.lblCombFeedback);
            this.grpModern.Controls.Add(this.trkCombFeedback);
            this.grpModern.Controls.Add(this.lblCombFeedbackVal);
            this.grpModern.Controls.Add(this.lblCombDamp);
            this.grpModern.Controls.Add(this.trkCombDamp);
            this.grpModern.Controls.Add(this.lblCombDampVal);
            this.grpModern.Controls.Add(this.lblBassDb);
            this.grpModern.Controls.Add(this.trkBassDb);
            this.grpModern.Controls.Add(this.lblBassDbVal);
            this.grpModern.Controls.Add(this.lblBassFreq);
            this.grpModern.Controls.Add(this.trkBassFreq);
            this.grpModern.Controls.Add(this.lblBassFreqVal);
            this.grpModern.Location = new System.Drawing.Point(598, 12);
            this.grpModern.Name = "grpModern";
            this.grpModern.Size = new System.Drawing.Size(566, 562);
            this.grpModern.TabIndex = 1;
            this.grpModern.TabStop = false;
            this.grpModern.Text = "Modern (AudioMode=2)";
            // 
            // lblStereoWidth
            // 
            this.lblStereoWidth.AutoSize = true;
            this.lblStereoWidth.Location = new System.Drawing.Point(16, 35);
            this.lblStereoWidth.Name = "lblStereoWidth";
            this.lblStereoWidth.Size = new System.Drawing.Size(99, 18);
            this.lblStereoWidth.TabIndex = 0;
            this.lblStereoWidth.Text = "Stereo Width";
            // 
            // trkStereoWidth
            // 
            this.trkStereoWidth.LargeChange = 10;
            this.trkStereoWidth.Location = new System.Drawing.Point(160, 32);
            this.trkStereoWidth.Maximum = 100;
            this.trkStereoWidth.Name = "trkStereoWidth";
            this.trkStereoWidth.Size = new System.Drawing.Size(320, 69);
            this.trkStereoWidth.TabIndex = 1;
            this.trkStereoWidth.TickFrequency = 10;
            this.trkStereoWidth.Value = 50;
            this.trkStereoWidth.Scroll += new System.EventHandler(this.UpdateAllValueLabels);
            // 
            // lblStereoWidthVal
            // 
            this.lblStereoWidthVal.AutoSize = true;
            this.lblStereoWidthVal.Location = new System.Drawing.Point(490, 35);
            this.lblStereoWidthVal.Name = "lblStereoWidthVal";
            this.lblStereoWidthVal.Size = new System.Drawing.Size(38, 18);
            this.lblStereoWidthVal.TabIndex = 2;
            this.lblStereoWidthVal.Text = "50%";
            // 
            // lblHaasDelay
            // 
            this.lblHaasDelay.AutoSize = true;
            this.lblHaasDelay.Location = new System.Drawing.Point(16, 100);
            this.lblHaasDelay.Name = "lblHaasDelay";
            this.lblHaasDelay.Size = new System.Drawing.Size(89, 18);
            this.lblHaasDelay.TabIndex = 3;
            this.lblHaasDelay.Text = "Haas Delay";
            // 
            // trkHaasDelay
            // 
            this.trkHaasDelay.Location = new System.Drawing.Point(160, 97);
            this.trkHaasDelay.Maximum = 30;
            this.trkHaasDelay.Minimum = 10;
            this.trkHaasDelay.Name = "trkHaasDelay";
            this.trkHaasDelay.Size = new System.Drawing.Size(320, 69);
            this.trkHaasDelay.TabIndex = 4;
            this.trkHaasDelay.TickFrequency = 2;
            this.trkHaasDelay.Value = 20;
            this.trkHaasDelay.Scroll += new System.EventHandler(this.UpdateAllValueLabels);
            // 
            // lblHaasDelayVal
            // 
            this.lblHaasDelayVal.AutoSize = true;
            this.lblHaasDelayVal.Location = new System.Drawing.Point(490, 100);
            this.lblHaasDelayVal.Name = "lblHaasDelayVal";
            this.lblHaasDelayVal.Size = new System.Drawing.Size(49, 18);
            this.lblHaasDelayVal.TabIndex = 5;
            this.lblHaasDelayVal.Text = "20 ms";
            // 
            // lblHaasCrossfeed
            // 
            this.lblHaasCrossfeed.AutoSize = true;
            this.lblHaasCrossfeed.Location = new System.Drawing.Point(16, 165);
            this.lblHaasCrossfeed.Name = "lblHaasCrossfeed";
            this.lblHaasCrossfeed.Size = new System.Drawing.Size(117, 18);
            this.lblHaasCrossfeed.TabIndex = 6;
            this.lblHaasCrossfeed.Text = "Haas Crossfeed";
            // 
            // trkHaasCrossfeed
            // 
            this.trkHaasCrossfeed.LargeChange = 10;
            this.trkHaasCrossfeed.Location = new System.Drawing.Point(160, 162);
            this.trkHaasCrossfeed.Maximum = 80;
            this.trkHaasCrossfeed.Name = "trkHaasCrossfeed";
            this.trkHaasCrossfeed.Size = new System.Drawing.Size(320, 69);
            this.trkHaasCrossfeed.TabIndex = 7;
            this.trkHaasCrossfeed.TickFrequency = 10;
            this.trkHaasCrossfeed.Value = 40;
            this.trkHaasCrossfeed.Scroll += new System.EventHandler(this.UpdateAllValueLabels);
            // 
            // lblHaasCrossfeedVal
            // 
            this.lblHaasCrossfeedVal.AutoSize = true;
            this.lblHaasCrossfeedVal.Location = new System.Drawing.Point(490, 165);
            this.lblHaasCrossfeedVal.Name = "lblHaasCrossfeedVal";
            this.lblHaasCrossfeedVal.Size = new System.Drawing.Size(38, 18);
            this.lblHaasCrossfeedVal.TabIndex = 8;
            this.lblHaasCrossfeedVal.Text = "40%";
            // 
            // lblReverbWet
            // 
            this.lblReverbWet.AutoSize = true;
            this.lblReverbWet.Location = new System.Drawing.Point(16, 230);
            this.lblReverbWet.Name = "lblReverbWet";
            this.lblReverbWet.Size = new System.Drawing.Size(91, 18);
            this.lblReverbWet.TabIndex = 9;
            this.lblReverbWet.Text = "Reverb Wet";
            // 
            // trkReverbWet
            // 
            this.trkReverbWet.Location = new System.Drawing.Point(160, 227);
            this.trkReverbWet.Maximum = 30;
            this.trkReverbWet.Name = "trkReverbWet";
            this.trkReverbWet.Size = new System.Drawing.Size(320, 69);
            this.trkReverbWet.TabIndex = 10;
            this.trkReverbWet.TickFrequency = 5;
            this.trkReverbWet.Scroll += new System.EventHandler(this.UpdateAllValueLabels);
            // 
            // lblReverbWetVal
            // 
            this.lblReverbWetVal.AutoSize = true;
            this.lblReverbWetVal.Location = new System.Drawing.Point(490, 230);
            this.lblReverbWetVal.Name = "lblReverbWetVal";
            this.lblReverbWetVal.Size = new System.Drawing.Size(30, 18);
            this.lblReverbWetVal.TabIndex = 11;
            this.lblReverbWetVal.Text = "0%";
            // 
            // lblCombFeedback
            // 
            this.lblCombFeedback.AutoSize = true;
            this.lblCombFeedback.Location = new System.Drawing.Point(16, 295);
            this.lblCombFeedback.Name = "lblCombFeedback";
            this.lblCombFeedback.Size = new System.Drawing.Size(109, 18);
            this.lblCombFeedback.TabIndex = 12;
            this.lblCombFeedback.Text = "Reverb Length";
            // 
            // trkCombFeedback
            // 
            this.trkCombFeedback.LargeChange = 10;
            this.trkCombFeedback.Location = new System.Drawing.Point(160, 292);
            this.trkCombFeedback.Maximum = 90;
            this.trkCombFeedback.Minimum = 30;
            this.trkCombFeedback.Name = "trkCombFeedback";
            this.trkCombFeedback.Size = new System.Drawing.Size(320, 69);
            this.trkCombFeedback.TabIndex = 13;
            this.trkCombFeedback.TickFrequency = 10;
            this.trkCombFeedback.Value = 70;
            this.trkCombFeedback.Scroll += new System.EventHandler(this.UpdateAllValueLabels);
            // 
            // lblCombFeedbackVal
            // 
            this.lblCombFeedbackVal.AutoSize = true;
            this.lblCombFeedbackVal.Location = new System.Drawing.Point(490, 295);
            this.lblCombFeedbackVal.Name = "lblCombFeedbackVal";
            this.lblCombFeedbackVal.Size = new System.Drawing.Size(38, 18);
            this.lblCombFeedbackVal.TabIndex = 14;
            this.lblCombFeedbackVal.Text = "70%";
            // 
            // lblCombDamp
            // 
            this.lblCombDamp.AutoSize = true;
            this.lblCombDamp.Location = new System.Drawing.Point(16, 360);
            this.lblCombDamp.Name = "lblCombDamp";
            this.lblCombDamp.Size = new System.Drawing.Size(124, 18);
            this.lblCombDamp.TabIndex = 15;
            this.lblCombDamp.Text = "Reverb Damping";
            // 
            // trkCombDamp
            // 
            this.trkCombDamp.LargeChange = 10;
            this.trkCombDamp.Location = new System.Drawing.Point(160, 357);
            this.trkCombDamp.Maximum = 70;
            this.trkCombDamp.Minimum = 10;
            this.trkCombDamp.Name = "trkCombDamp";
            this.trkCombDamp.Size = new System.Drawing.Size(320, 69);
            this.trkCombDamp.TabIndex = 16;
            this.trkCombDamp.TickFrequency = 10;
            this.trkCombDamp.Value = 30;
            this.trkCombDamp.Scroll += new System.EventHandler(this.UpdateAllValueLabels);
            // 
            // lblCombDampVal
            // 
            this.lblCombDampVal.AutoSize = true;
            this.lblCombDampVal.Location = new System.Drawing.Point(490, 360);
            this.lblCombDampVal.Name = "lblCombDampVal";
            this.lblCombDampVal.Size = new System.Drawing.Size(38, 18);
            this.lblCombDampVal.TabIndex = 17;
            this.lblCombDampVal.Text = "30%";
            // 
            // lblBassDb
            // 
            this.lblBassDb.AutoSize = true;
            this.lblBassDb.Location = new System.Drawing.Point(16, 425);
            this.lblBassDb.Name = "lblBassDb";
            this.lblBassDb.Size = new System.Drawing.Size(85, 18);
            this.lblBassDb.TabIndex = 18;
            this.lblBassDb.Text = "Bass Boost";
            // 
            // trkBassDb
            // 
            this.trkBassDb.LargeChange = 3;
            this.trkBassDb.Location = new System.Drawing.Point(160, 422);
            this.trkBassDb.Maximum = 12;
            this.trkBassDb.Name = "trkBassDb";
            this.trkBassDb.Size = new System.Drawing.Size(320, 69);
            this.trkBassDb.TabIndex = 19;
            this.trkBassDb.Scroll += new System.EventHandler(this.UpdateAllValueLabels);
            // 
            // lblBassDbVal
            // 
            this.lblBassDbVal.AutoSize = true;
            this.lblBassDbVal.Location = new System.Drawing.Point(490, 425);
            this.lblBassDbVal.Name = "lblBassDbVal";
            this.lblBassDbVal.Size = new System.Drawing.Size(32, 18);
            this.lblBassDbVal.TabIndex = 20;
            this.lblBassDbVal.Text = "Off";
            // 
            // lblBassFreq
            // 
            this.lblBassFreq.AutoSize = true;
            this.lblBassFreq.Location = new System.Drawing.Point(16, 490);
            this.lblBassFreq.Name = "lblBassFreq";
            this.lblBassFreq.Size = new System.Drawing.Size(77, 18);
            this.lblBassFreq.TabIndex = 21;
            this.lblBassFreq.Text = "Bass Freq";
            // 
            // trkBassFreq
            // 
            this.trkBassFreq.LargeChange = 50;
            this.trkBassFreq.Location = new System.Drawing.Point(160, 487);
            this.trkBassFreq.Maximum = 300;
            this.trkBassFreq.Minimum = 80;
            this.trkBassFreq.Name = "trkBassFreq";
            this.trkBassFreq.Size = new System.Drawing.Size(320, 69);
            this.trkBassFreq.TabIndex = 22;
            this.trkBassFreq.TickFrequency = 20;
            this.trkBassFreq.Value = 150;
            this.trkBassFreq.Scroll += new System.EventHandler(this.UpdateAllValueLabels);
            // 
            // lblBassFreqVal
            // 
            this.lblBassFreqVal.AutoSize = true;
            this.lblBassFreqVal.Location = new System.Drawing.Point(490, 490);
            this.lblBassFreqVal.Name = "lblBassFreqVal";
            this.lblBassFreqVal.Size = new System.Drawing.Size(57, 18);
            this.lblBassFreqVal.TabIndex = 23;
            this.lblBassFreqVal.Text = "150 Hz";
            // 
            // btnOK
            // 
            this.btnOK.Location = new System.Drawing.Point(970, 875);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(90, 32);
            this.btnOK.TabIndex = 2;
            this.btnOK.Text = "OK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Location = new System.Drawing.Point(1074, 875);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(90, 32);
            this.btnCancel.TabIndex = 3;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // ChannelVol (children built dynamically in BuildChannelVolumeUI)
            //
            this.ChannelVol.Location = new System.Drawing.Point(12, 581);
            this.ChannelVol.Name = "ChannelVol";
            this.ChannelVol.Size = new System.Drawing.Size(1152, 280);
            this.ChannelVol.TabIndex = 4;
            this.ChannelVol.TabStop = false;
            this.ChannelVol.Text = "Channel Volume";
            // 
            // label1
            // (ChannelVol children built dynamically)
            // (remaining ChannelVol children built dynamically)
            // 
            // AprNes_AudioPlusConfigureUI
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 18F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1186, 920);
            this.Controls.Add(this.ChannelVol);
            this.Controls.Add(this.grpAuthentic);
            this.Controls.Add(this.grpModern);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.btnCancel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.MaximizeBox = false;
            this.Name = "AprNes_AudioPlusConfigureUI";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "AudioPlus Settings";
            this.grpAuthentic.ResumeLayout(false);
            this.grpAuthentic.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.trkCustomCutoff)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkBuzzAmp)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkRfVol)).EndInit();
            this.grpModern.ResumeLayout(false);
            this.grpModern.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.trkStereoWidth)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkHaasDelay)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkHaasCrossfeed)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkReverbWet)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkCombFeedback)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkCombDamp)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkBassDb)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trkBassFreq)).EndInit();
            this.ChannelVol.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox grpAuthentic;
        private System.Windows.Forms.Label lblConsoleModel;
        private System.Windows.Forms.ComboBox cboConsoleModel;
        private System.Windows.Forms.CheckBox chkRfCrosstalk;
        private System.Windows.Forms.Label lblCustomCutoff;
        private System.Windows.Forms.TrackBar trkCustomCutoff;
        private System.Windows.Forms.Label lblCustomCutoffVal;
        private System.Windows.Forms.CheckBox chkCustomBuzz;
        private System.Windows.Forms.Label lblBuzzAmp;
        private System.Windows.Forms.TrackBar trkBuzzAmp;
        private System.Windows.Forms.Label lblBuzzAmpVal;
        private System.Windows.Forms.Label lblBuzzFreq;
        private System.Windows.Forms.ComboBox cboBuzzFreq;
        private System.Windows.Forms.Label lblRfVol;
        private System.Windows.Forms.TrackBar trkRfVol;
        private System.Windows.Forms.Label lblRfVolVal;
        private System.Windows.Forms.GroupBox grpModern;
        private System.Windows.Forms.Label lblStereoWidth;
        private System.Windows.Forms.TrackBar trkStereoWidth;
        private System.Windows.Forms.Label lblStereoWidthVal;
        private System.Windows.Forms.Label lblHaasDelay;
        private System.Windows.Forms.TrackBar trkHaasDelay;
        private System.Windows.Forms.Label lblHaasDelayVal;
        private System.Windows.Forms.Label lblHaasCrossfeed;
        private System.Windows.Forms.TrackBar trkHaasCrossfeed;
        private System.Windows.Forms.Label lblHaasCrossfeedVal;
        private System.Windows.Forms.Label lblReverbWet;
        private System.Windows.Forms.TrackBar trkReverbWet;
        private System.Windows.Forms.Label lblReverbWetVal;
        private System.Windows.Forms.Label lblCombFeedback;
        private System.Windows.Forms.TrackBar trkCombFeedback;
        private System.Windows.Forms.Label lblCombFeedbackVal;
        private System.Windows.Forms.Label lblCombDamp;
        private System.Windows.Forms.TrackBar trkCombDamp;
        private System.Windows.Forms.Label lblCombDampVal;
        private System.Windows.Forms.Label lblBassDb;
        private System.Windows.Forms.TrackBar trkBassDb;
        private System.Windows.Forms.Label lblBassDbVal;
        private System.Windows.Forms.Label lblBassFreq;
        private System.Windows.Forms.TrackBar trkBassFreq;
        private System.Windows.Forms.Label lblBassFreqVal;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.GroupBox ChannelVol;
    }
}
