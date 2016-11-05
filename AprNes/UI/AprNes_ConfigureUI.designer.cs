namespace AprNes
{
    partial class AprNes_ConfigureUI
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
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.textBox_RIGHT = new System.Windows.Forms.TextBox();
            this.textBox_LEFT = new System.Windows.Forms.TextBox();
            this.textBox_DOWN = new System.Windows.Forms.TextBox();
            this.textBox_UP = new System.Windows.Forms.TextBox();
            this.textBox_START = new System.Windows.Forms.TextBox();
            this.textBox_SELECT = new System.Windows.Forms.TextBox();
            this.textBox_B = new System.Windows.Forms.TextBox();
            this.label8 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.textBox_A = new System.Windows.Forms.TextBox();
            this.LimitFPS_checkBox = new System.Windows.Forms.CheckBox();
            this.screen_path = new System.Windows.Forms.TextBox();
            this.label9 = new System.Windows.Forms.Label();
            this.choose_dir = new System.Windows.Forms.Button();
            this.Ok_btn = new System.Windows.Forms.Button();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.joypad_RIGHT = new System.Windows.Forms.TextBox();
            this.joypad_LEFT = new System.Windows.Forms.TextBox();
            this.joypad_DOWN = new System.Windows.Forms.TextBox();
            this.joypad_UP = new System.Windows.Forms.TextBox();
            this.joypad_START = new System.Windows.Forms.TextBox();
            this.joypad_SELECT = new System.Windows.Forms.TextBox();
            this.joypad_B = new System.Windows.Forms.TextBox();
            this.label10 = new System.Windows.Forms.Label();
            this.label11 = new System.Windows.Forms.Label();
            this.label12 = new System.Windows.Forms.Label();
            this.label13 = new System.Windows.Forms.Label();
            this.label14 = new System.Windows.Forms.Label();
            this.label15 = new System.Windows.Forms.Label();
            this.label16 = new System.Windows.Forms.Label();
            this.label17 = new System.Windows.Forms.Label();
            this.joypad_A = new System.Windows.Forms.TextBox();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.radioButtonX5 = new System.Windows.Forms.RadioButton();
            this.radioButtonX4 = new System.Windows.Forms.RadioButton();
            this.radioButtonX3 = new System.Windows.Forms.RadioButton();
            this.radioButtonX2 = new System.Windows.Forms.RadioButton();
            this.radioButtonX1 = new System.Windows.Forms.RadioButton();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.label18 = new System.Windows.Forms.Label();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.groupBox4.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.textBox_RIGHT);
            this.groupBox1.Controls.Add(this.textBox_LEFT);
            this.groupBox1.Controls.Add(this.textBox_DOWN);
            this.groupBox1.Controls.Add(this.textBox_UP);
            this.groupBox1.Controls.Add(this.textBox_START);
            this.groupBox1.Controls.Add(this.textBox_SELECT);
            this.groupBox1.Controls.Add(this.textBox_B);
            this.groupBox1.Controls.Add(this.label8);
            this.groupBox1.Controls.Add(this.label7);
            this.groupBox1.Controls.Add(this.label6);
            this.groupBox1.Controls.Add(this.label5);
            this.groupBox1.Controls.Add(this.label4);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Controls.Add(this.textBox_A);
            this.groupBox1.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.groupBox1.Location = new System.Drawing.Point(4, 8);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(142, 280);
            this.groupBox1.TabIndex = 1;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "configure keypad";
            // 
            // textBox_RIGHT
            // 
            this.textBox_RIGHT.Location = new System.Drawing.Point(60, 244);
            this.textBox_RIGHT.MaxLength = 1;
            this.textBox_RIGHT.Name = "textBox_RIGHT";
            this.textBox_RIGHT.ReadOnly = true;
            this.textBox_RIGHT.Size = new System.Drawing.Size(76, 22);
            this.textBox_RIGHT.TabIndex = 15;
            this.textBox_RIGHT.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.textBox_RIGHT.MouseClick += new System.Windows.Forms.MouseEventHandler(this.textBox_KeyConfig_MouseClick);
            this.textBox_RIGHT.KeyUp += new System.Windows.Forms.KeyEventHandler(this.textBox_KeyConfig_KeyUp);
            this.textBox_RIGHT.Leave += new System.EventHandler(this.textBox_A_Leave);
            // 
            // textBox_LEFT
            // 
            this.textBox_LEFT.Location = new System.Drawing.Point(60, 212);
            this.textBox_LEFT.MaxLength = 1;
            this.textBox_LEFT.Name = "textBox_LEFT";
            this.textBox_LEFT.ReadOnly = true;
            this.textBox_LEFT.Size = new System.Drawing.Size(76, 22);
            this.textBox_LEFT.TabIndex = 14;
            this.textBox_LEFT.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.textBox_LEFT.MouseClick += new System.Windows.Forms.MouseEventHandler(this.textBox_KeyConfig_MouseClick);
            this.textBox_LEFT.KeyUp += new System.Windows.Forms.KeyEventHandler(this.textBox_KeyConfig_KeyUp);
            this.textBox_LEFT.Leave += new System.EventHandler(this.textBox_A_Leave);
            // 
            // textBox_DOWN
            // 
            this.textBox_DOWN.Location = new System.Drawing.Point(60, 180);
            this.textBox_DOWN.MaxLength = 1;
            this.textBox_DOWN.Name = "textBox_DOWN";
            this.textBox_DOWN.ReadOnly = true;
            this.textBox_DOWN.Size = new System.Drawing.Size(76, 22);
            this.textBox_DOWN.TabIndex = 13;
            this.textBox_DOWN.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.textBox_DOWN.MouseClick += new System.Windows.Forms.MouseEventHandler(this.textBox_KeyConfig_MouseClick);
            this.textBox_DOWN.KeyUp += new System.Windows.Forms.KeyEventHandler(this.textBox_KeyConfig_KeyUp);
            this.textBox_DOWN.Leave += new System.EventHandler(this.textBox_A_Leave);
            // 
            // textBox_UP
            // 
            this.textBox_UP.Location = new System.Drawing.Point(60, 148);
            this.textBox_UP.MaxLength = 1;
            this.textBox_UP.Name = "textBox_UP";
            this.textBox_UP.ReadOnly = true;
            this.textBox_UP.Size = new System.Drawing.Size(76, 22);
            this.textBox_UP.TabIndex = 12;
            this.textBox_UP.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.textBox_UP.MouseClick += new System.Windows.Forms.MouseEventHandler(this.textBox_KeyConfig_MouseClick);
            this.textBox_UP.KeyUp += new System.Windows.Forms.KeyEventHandler(this.textBox_KeyConfig_KeyUp);
            this.textBox_UP.Leave += new System.EventHandler(this.textBox_A_Leave);
            // 
            // textBox_START
            // 
            this.textBox_START.Location = new System.Drawing.Point(60, 116);
            this.textBox_START.MaxLength = 1;
            this.textBox_START.Name = "textBox_START";
            this.textBox_START.ReadOnly = true;
            this.textBox_START.Size = new System.Drawing.Size(76, 22);
            this.textBox_START.TabIndex = 11;
            this.textBox_START.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.textBox_START.MouseClick += new System.Windows.Forms.MouseEventHandler(this.textBox_KeyConfig_MouseClick);
            this.textBox_START.KeyUp += new System.Windows.Forms.KeyEventHandler(this.textBox_KeyConfig_KeyUp);
            this.textBox_START.Leave += new System.EventHandler(this.textBox_A_Leave);
            // 
            // textBox_SELECT
            // 
            this.textBox_SELECT.Location = new System.Drawing.Point(60, 84);
            this.textBox_SELECT.MaxLength = 1;
            this.textBox_SELECT.Name = "textBox_SELECT";
            this.textBox_SELECT.ReadOnly = true;
            this.textBox_SELECT.Size = new System.Drawing.Size(76, 22);
            this.textBox_SELECT.TabIndex = 10;
            this.textBox_SELECT.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.textBox_SELECT.MouseClick += new System.Windows.Forms.MouseEventHandler(this.textBox_KeyConfig_MouseClick);
            this.textBox_SELECT.KeyUp += new System.Windows.Forms.KeyEventHandler(this.textBox_KeyConfig_KeyUp);
            this.textBox_SELECT.Leave += new System.EventHandler(this.textBox_A_Leave);
            // 
            // textBox_B
            // 
            this.textBox_B.Location = new System.Drawing.Point(60, 52);
            this.textBox_B.MaxLength = 1;
            this.textBox_B.Name = "textBox_B";
            this.textBox_B.ReadOnly = true;
            this.textBox_B.Size = new System.Drawing.Size(76, 22);
            this.textBox_B.TabIndex = 9;
            this.textBox_B.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.textBox_B.MouseClick += new System.Windows.Forms.MouseEventHandler(this.textBox_KeyConfig_MouseClick);
            this.textBox_B.KeyUp += new System.Windows.Forms.KeyEventHandler(this.textBox_KeyConfig_KeyUp);
            this.textBox_B.Leave += new System.EventHandler(this.textBox_A_Leave);
            // 
            // label8
            // 
            this.label8.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label8.Location = new System.Drawing.Point(4, 248);
            this.label8.Name = "label8";
            this.label8.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label8.Size = new System.Drawing.Size(54, 12);
            this.label8.TabIndex = 8;
            this.label8.Text = "RIGHT";
            // 
            // label7
            // 
            this.label7.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label7.Location = new System.Drawing.Point(4, 216);
            this.label7.Name = "label7";
            this.label7.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label7.Size = new System.Drawing.Size(54, 12);
            this.label7.TabIndex = 7;
            this.label7.Text = "LEFT";
            // 
            // label6
            // 
            this.label6.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label6.Location = new System.Drawing.Point(4, 184);
            this.label6.Name = "label6";
            this.label6.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label6.Size = new System.Drawing.Size(54, 12);
            this.label6.TabIndex = 6;
            this.label6.Text = "DOWN";
            // 
            // label5
            // 
            this.label5.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label5.Location = new System.Drawing.Point(4, 152);
            this.label5.Name = "label5";
            this.label5.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label5.Size = new System.Drawing.Size(54, 12);
            this.label5.TabIndex = 5;
            this.label5.Text = "UP";
            // 
            // label4
            // 
            this.label4.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label4.Location = new System.Drawing.Point(4, 120);
            this.label4.Name = "label4";
            this.label4.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label4.Size = new System.Drawing.Size(54, 12);
            this.label4.TabIndex = 4;
            this.label4.Text = "START";
            // 
            // label3
            // 
            this.label3.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label3.Location = new System.Drawing.Point(4, 88);
            this.label3.Name = "label3";
            this.label3.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label3.Size = new System.Drawing.Size(54, 12);
            this.label3.TabIndex = 3;
            this.label3.Text = "SELECT";
            // 
            // label2
            // 
            this.label2.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label2.Location = new System.Drawing.Point(4, 56);
            this.label2.Name = "label2";
            this.label2.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label2.Size = new System.Drawing.Size(54, 12);
            this.label2.TabIndex = 2;
            this.label2.Text = "B";
            // 
            // label1
            // 
            this.label1.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label1.Location = new System.Drawing.Point(4, 24);
            this.label1.Name = "label1";
            this.label1.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label1.Size = new System.Drawing.Size(54, 12);
            this.label1.TabIndex = 1;
            this.label1.Text = "A";
            // 
            // textBox_A
            // 
            this.textBox_A.Location = new System.Drawing.Point(60, 21);
            this.textBox_A.MaxLength = 1;
            this.textBox_A.Name = "textBox_A";
            this.textBox_A.ReadOnly = true;
            this.textBox_A.Size = new System.Drawing.Size(76, 22);
            this.textBox_A.TabIndex = 0;
            this.textBox_A.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.textBox_A.MouseClick += new System.Windows.Forms.MouseEventHandler(this.textBox_KeyConfig_MouseClick);
            this.textBox_A.KeyUp += new System.Windows.Forms.KeyEventHandler(this.textBox_KeyConfig_KeyUp);
            this.textBox_A.Leave += new System.EventHandler(this.textBox_A_Leave);
            // 
            // LimitFPS_checkBox
            // 
            this.LimitFPS_checkBox.AutoSize = true;
            this.LimitFPS_checkBox.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.LimitFPS_checkBox.Location = new System.Drawing.Point(298, 12);
            this.LimitFPS_checkBox.Name = "LimitFPS_checkBox";
            this.LimitFPS_checkBox.Size = new System.Drawing.Size(70, 16);
            this.LimitFPS_checkBox.TabIndex = 3;
            this.LimitFPS_checkBox.Text = "Limit FPS";
            this.LimitFPS_checkBox.UseVisualStyleBackColor = true;
            // 
            // screen_path
            // 
            this.screen_path.BackColor = System.Drawing.SystemColors.ControlLight;
            this.screen_path.Location = new System.Drawing.Point(298, 56);
            this.screen_path.Name = "screen_path";
            this.screen_path.ReadOnly = true;
            this.screen_path.Size = new System.Drawing.Size(326, 22);
            this.screen_path.TabIndex = 4;
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label9.Location = new System.Drawing.Point(298, 37);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(195, 12);
            this.label9.TabIndex = 5;
            this.label9.Text = "Shift + p screen capture save file location";
            // 
            // choose_dir
            // 
            this.choose_dir.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.choose_dir.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.choose_dir.Location = new System.Drawing.Point(526, 26);
            this.choose_dir.Name = "choose_dir";
            this.choose_dir.Size = new System.Drawing.Size(98, 23);
            this.choose_dir.TabIndex = 6;
            this.choose_dir.Text = "folder select";
            this.choose_dir.UseVisualStyleBackColor = true;
            this.choose_dir.Click += new System.EventHandler(this.choose_dir_Click);
            // 
            // Ok_btn
            // 
            this.Ok_btn.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.Ok_btn.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.Ok_btn.Location = new System.Drawing.Point(526, 261);
            this.Ok_btn.Name = "Ok_btn";
            this.Ok_btn.Size = new System.Drawing.Size(98, 23);
            this.Ok_btn.TabIndex = 7;
            this.Ok_btn.Text = "OK";
            this.Ok_btn.UseVisualStyleBackColor = true;
            this.Ok_btn.Click += new System.EventHandler(this.OK);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.joypad_RIGHT);
            this.groupBox2.Controls.Add(this.joypad_LEFT);
            this.groupBox2.Controls.Add(this.joypad_DOWN);
            this.groupBox2.Controls.Add(this.joypad_UP);
            this.groupBox2.Controls.Add(this.joypad_START);
            this.groupBox2.Controls.Add(this.joypad_SELECT);
            this.groupBox2.Controls.Add(this.joypad_B);
            this.groupBox2.Controls.Add(this.label10);
            this.groupBox2.Controls.Add(this.label11);
            this.groupBox2.Controls.Add(this.label12);
            this.groupBox2.Controls.Add(this.label13);
            this.groupBox2.Controls.Add(this.label14);
            this.groupBox2.Controls.Add(this.label15);
            this.groupBox2.Controls.Add(this.label16);
            this.groupBox2.Controls.Add(this.label17);
            this.groupBox2.Controls.Add(this.joypad_A);
            this.groupBox2.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.groupBox2.Location = new System.Drawing.Point(150, 8);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(142, 280);
            this.groupBox2.TabIndex = 8;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "configure joypad";
            // 
            // joypad_RIGHT
            // 
            this.joypad_RIGHT.Location = new System.Drawing.Point(60, 244);
            this.joypad_RIGHT.MaxLength = 1;
            this.joypad_RIGHT.Name = "joypad_RIGHT";
            this.joypad_RIGHT.ReadOnly = true;
            this.joypad_RIGHT.Size = new System.Drawing.Size(76, 22);
            this.joypad_RIGHT.TabIndex = 15;
            this.joypad_RIGHT.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // joypad_LEFT
            // 
            this.joypad_LEFT.Location = new System.Drawing.Point(60, 212);
            this.joypad_LEFT.MaxLength = 1;
            this.joypad_LEFT.Name = "joypad_LEFT";
            this.joypad_LEFT.ReadOnly = true;
            this.joypad_LEFT.Size = new System.Drawing.Size(76, 22);
            this.joypad_LEFT.TabIndex = 14;
            this.joypad_LEFT.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // joypad_DOWN
            // 
            this.joypad_DOWN.Location = new System.Drawing.Point(60, 180);
            this.joypad_DOWN.MaxLength = 1;
            this.joypad_DOWN.Name = "joypad_DOWN";
            this.joypad_DOWN.ReadOnly = true;
            this.joypad_DOWN.Size = new System.Drawing.Size(76, 22);
            this.joypad_DOWN.TabIndex = 13;
            this.joypad_DOWN.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // joypad_UP
            // 
            this.joypad_UP.Location = new System.Drawing.Point(60, 148);
            this.joypad_UP.MaxLength = 1;
            this.joypad_UP.Name = "joypad_UP";
            this.joypad_UP.ReadOnly = true;
            this.joypad_UP.Size = new System.Drawing.Size(76, 22);
            this.joypad_UP.TabIndex = 12;
            this.joypad_UP.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // joypad_START
            // 
            this.joypad_START.Location = new System.Drawing.Point(60, 116);
            this.joypad_START.MaxLength = 1;
            this.joypad_START.Name = "joypad_START";
            this.joypad_START.ReadOnly = true;
            this.joypad_START.Size = new System.Drawing.Size(76, 22);
            this.joypad_START.TabIndex = 11;
            this.joypad_START.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // joypad_SELECT
            // 
            this.joypad_SELECT.Location = new System.Drawing.Point(60, 84);
            this.joypad_SELECT.MaxLength = 1;
            this.joypad_SELECT.Name = "joypad_SELECT";
            this.joypad_SELECT.ReadOnly = true;
            this.joypad_SELECT.Size = new System.Drawing.Size(76, 22);
            this.joypad_SELECT.TabIndex = 10;
            this.joypad_SELECT.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // joypad_B
            // 
            this.joypad_B.Location = new System.Drawing.Point(60, 52);
            this.joypad_B.MaxLength = 1;
            this.joypad_B.Name = "joypad_B";
            this.joypad_B.ReadOnly = true;
            this.joypad_B.Size = new System.Drawing.Size(76, 22);
            this.joypad_B.TabIndex = 9;
            this.joypad_B.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label10
            // 
            this.label10.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label10.Location = new System.Drawing.Point(4, 248);
            this.label10.Name = "label10";
            this.label10.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label10.Size = new System.Drawing.Size(54, 12);
            this.label10.TabIndex = 8;
            this.label10.Text = "RIGHT";
            // 
            // label11
            // 
            this.label11.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label11.Location = new System.Drawing.Point(4, 216);
            this.label11.Name = "label11";
            this.label11.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label11.Size = new System.Drawing.Size(54, 12);
            this.label11.TabIndex = 7;
            this.label11.Text = "LEFT";
            // 
            // label12
            // 
            this.label12.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label12.Location = new System.Drawing.Point(4, 184);
            this.label12.Name = "label12";
            this.label12.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label12.Size = new System.Drawing.Size(54, 12);
            this.label12.TabIndex = 6;
            this.label12.Text = "DOWN";
            // 
            // label13
            // 
            this.label13.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label13.Location = new System.Drawing.Point(4, 152);
            this.label13.Name = "label13";
            this.label13.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label13.Size = new System.Drawing.Size(54, 12);
            this.label13.TabIndex = 5;
            this.label13.Text = "UP";
            // 
            // label14
            // 
            this.label14.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label14.Location = new System.Drawing.Point(4, 120);
            this.label14.Name = "label14";
            this.label14.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label14.Size = new System.Drawing.Size(54, 12);
            this.label14.TabIndex = 4;
            this.label14.Text = "START";
            // 
            // label15
            // 
            this.label15.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label15.Location = new System.Drawing.Point(4, 88);
            this.label15.Name = "label15";
            this.label15.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label15.Size = new System.Drawing.Size(54, 12);
            this.label15.TabIndex = 3;
            this.label15.Text = "SELECT";
            // 
            // label16
            // 
            this.label16.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label16.Location = new System.Drawing.Point(4, 56);
            this.label16.Name = "label16";
            this.label16.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label16.Size = new System.Drawing.Size(54, 12);
            this.label16.TabIndex = 2;
            this.label16.Text = "B";
            // 
            // label17
            // 
            this.label17.Font = new System.Drawing.Font("PMingLiU", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label17.Location = new System.Drawing.Point(4, 24);
            this.label17.Name = "label17";
            this.label17.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.label17.Size = new System.Drawing.Size(54, 12);
            this.label17.TabIndex = 1;
            this.label17.Text = "A";
            // 
            // joypad_A
            // 
            this.joypad_A.Location = new System.Drawing.Point(60, 21);
            this.joypad_A.MaxLength = 1;
            this.joypad_A.Name = "joypad_A";
            this.joypad_A.ReadOnly = true;
            this.joypad_A.Size = new System.Drawing.Size(76, 22);
            this.joypad_A.TabIndex = 0;
            this.joypad_A.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // groupBox4
            // 
            this.groupBox4.Controls.Add(this.radioButtonX5);
            this.groupBox4.Controls.Add(this.radioButtonX4);
            this.groupBox4.Controls.Add(this.radioButtonX3);
            this.groupBox4.Controls.Add(this.radioButtonX2);
            this.groupBox4.Controls.Add(this.radioButtonX1);
            this.groupBox4.Location = new System.Drawing.Point(298, 90);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Size = new System.Drawing.Size(90, 85);
            this.groupBox4.TabIndex = 14;
            this.groupBox4.TabStop = false;
            this.groupBox4.Text = "Screen";
            // 
            // radioButtonX5
            // 
            this.radioButtonX5.AutoSize = true;
            this.radioButtonX5.Location = new System.Drawing.Point(46, 39);
            this.radioButtonX5.Name = "radioButtonX5";
            this.radioButtonX5.Size = new System.Drawing.Size(38, 16);
            this.radioButtonX5.TabIndex = 16;
            this.radioButtonX5.TabStop = true;
            this.radioButtonX5.Text = "x 5";
            this.radioButtonX5.UseVisualStyleBackColor = true;
            // 
            // radioButtonX4
            // 
            this.radioButtonX4.AutoSize = true;
            this.radioButtonX4.Location = new System.Drawing.Point(46, 17);
            this.radioButtonX4.Name = "radioButtonX4";
            this.radioButtonX4.Size = new System.Drawing.Size(38, 16);
            this.radioButtonX4.TabIndex = 3;
            this.radioButtonX4.TabStop = true;
            this.radioButtonX4.Text = "x 4";
            this.radioButtonX4.UseVisualStyleBackColor = true;
            // 
            // radioButtonX3
            // 
            this.radioButtonX3.AutoSize = true;
            this.radioButtonX3.Location = new System.Drawing.Point(6, 61);
            this.radioButtonX3.Name = "radioButtonX3";
            this.radioButtonX3.Size = new System.Drawing.Size(38, 16);
            this.radioButtonX3.TabIndex = 2;
            this.radioButtonX3.TabStop = true;
            this.radioButtonX3.Text = "x 3";
            this.radioButtonX3.UseVisualStyleBackColor = true;
            // 
            // radioButtonX2
            // 
            this.radioButtonX2.AutoSize = true;
            this.radioButtonX2.Location = new System.Drawing.Point(6, 39);
            this.radioButtonX2.Name = "radioButtonX2";
            this.radioButtonX2.Size = new System.Drawing.Size(38, 16);
            this.radioButtonX2.TabIndex = 1;
            this.radioButtonX2.TabStop = true;
            this.radioButtonX2.Text = "x 2";
            this.radioButtonX2.UseVisualStyleBackColor = true;
            // 
            // radioButtonX1
            // 
            this.radioButtonX1.AutoSize = true;
            this.radioButtonX1.Location = new System.Drawing.Point(6, 17);
            this.radioButtonX1.Name = "radioButtonX1";
            this.radioButtonX1.Size = new System.Drawing.Size(38, 16);
            this.radioButtonX1.TabIndex = 0;
            this.radioButtonX1.TabStop = true;
            this.radioButtonX1.Text = "x 1";
            this.radioButtonX1.UseVisualStyleBackColor = true;
            // 
            // comboBox1
            // 
            this.comboBox1.FormattingEnabled = true;
            this.comboBox1.Location = new System.Drawing.Point(298, 205);
            this.comboBox1.Name = "comboBox1";
            this.comboBox1.Size = new System.Drawing.Size(121, 20);
            this.comboBox1.TabIndex = 18;
            // 
            // label18
            // 
            this.label18.AutoSize = true;
            this.label18.Location = new System.Drawing.Point(298, 188);
            this.label18.Name = "label18";
            this.label18.Size = new System.Drawing.Size(51, 12);
            this.label18.TabIndex = 19;
            this.label18.Text = "Language";
            // 
            // AprNes_ConfigureUI
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(634, 293);
            this.Controls.Add(this.label18);
            this.Controls.Add(this.comboBox1);
            this.Controls.Add(this.groupBox4);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.Ok_btn);
            this.Controls.Add(this.choose_dir);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.screen_path);
            this.Controls.Add(this.LimitFPS_checkBox);
            this.Controls.Add(this.groupBox1);
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.MaximizeBox = false;
            this.Name = "AprNes_ConfigureUI";
            this.Text = "Configure";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.GBEMU_ConfigureUI_FormClosing);
            this.Shown += new System.EventHandler(this.GBEMU_ConfigureUI_Shown);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.groupBox4.ResumeLayout(false);
            this.groupBox4.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TextBox textBox_RIGHT;
        private System.Windows.Forms.TextBox textBox_LEFT;
        private System.Windows.Forms.TextBox textBox_DOWN;
        private System.Windows.Forms.TextBox textBox_UP;
        private System.Windows.Forms.TextBox textBox_START;
        private System.Windows.Forms.TextBox textBox_SELECT;
        private System.Windows.Forms.TextBox textBox_B;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox textBox_A;
        private System.Windows.Forms.CheckBox LimitFPS_checkBox;
        private System.Windows.Forms.TextBox screen_path;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.Button choose_dir;
        private System.Windows.Forms.Button Ok_btn;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.TextBox joypad_RIGHT;
        private System.Windows.Forms.TextBox joypad_LEFT;
        private System.Windows.Forms.TextBox joypad_DOWN;
        private System.Windows.Forms.TextBox joypad_UP;
        private System.Windows.Forms.TextBox joypad_START;
        private System.Windows.Forms.TextBox joypad_SELECT;
        private System.Windows.Forms.TextBox joypad_B;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.Label label15;
        private System.Windows.Forms.Label label16;
        private System.Windows.Forms.Label label17;
        private System.Windows.Forms.TextBox joypad_A;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.RadioButton radioButtonX4;
        private System.Windows.Forms.RadioButton radioButtonX3;
        private System.Windows.Forms.RadioButton radioButtonX2;
        private System.Windows.Forms.RadioButton radioButtonX1;
        private System.Windows.Forms.RadioButton radioButtonX5;
        private System.Windows.Forms.ComboBox comboBox1;
        private System.Windows.Forms.Label label18;
    }
}