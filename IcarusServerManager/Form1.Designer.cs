namespace IcarusServerManager
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            mainLabel = new Label();
            primaryPanel = new Panel();
            serverStatusBox = new TextBox();
            startServerButton = new Button();
            forceKillServerButton = new Button();
            installServerButton = new Button();
            label1 = new Label();
            copyrightLabel = new Label();
            settingsTabControl = new TabControl();
            consoleTab = new TabPage();
            consoleTextbox = new RichTextBox();
            serverSettingsTab = new TabPage();
            panel4 = new Panel();
            gamePortBox = new TextBox();
            queryPortBox = new TextBox();
            serverNameBox = new TextBox();
            panel3 = new Panel();
            label5 = new Label();
            label4 = new Label();
            label3 = new Label();
            managerSettingsTab = new TabPage();
            panel2 = new Panel();
            selectLocationButton = new Button();
            panel1 = new Panel();
            restartTimeBox = new ComboBox();
            serverLocationBox = new TextBox();
            severSettingsPanel = new Panel();
            label6 = new Label();
            label2 = new Label();
            primaryPanel.SuspendLayout();
            settingsTabControl.SuspendLayout();
            consoleTab.SuspendLayout();
            serverSettingsTab.SuspendLayout();
            panel4.SuspendLayout();
            panel3.SuspendLayout();
            managerSettingsTab.SuspendLayout();
            panel2.SuspendLayout();
            panel1.SuspendLayout();
            severSettingsPanel.SuspendLayout();
            SuspendLayout();
            // 
            // mainLabel
            // 
            mainLabel.Dock = DockStyle.Top;
            mainLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 0);
            mainLabel.Location = new Point(0, 0);
            mainLabel.Name = "mainLabel";
            mainLabel.Size = new Size(694, 36);
            mainLabel.TabIndex = 0;
            mainLabel.Text = "Icarus Server Manager";
            mainLabel.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // primaryPanel
            // 
            primaryPanel.BorderStyle = BorderStyle.FixedSingle;
            primaryPanel.Controls.Add(serverStatusBox);
            primaryPanel.Controls.Add(forceKillServerButton);
            primaryPanel.Controls.Add(startServerButton);
            primaryPanel.Controls.Add(installServerButton);
            primaryPanel.Controls.Add(label1);
            primaryPanel.Dock = DockStyle.Top;
            primaryPanel.Location = new Point(0, 36);
            primaryPanel.Name = "primaryPanel";
            primaryPanel.Size = new Size(694, 144);
            primaryPanel.TabIndex = 1;
            // 
            // serverStatusBox
            // 
            serverStatusBox.Dock = DockStyle.Top;
            serverStatusBox.Location = new Point(0, 74);
            serverStatusBox.Name = "serverStatusBox";
            serverStatusBox.ReadOnly = true;
            serverStatusBox.Size = new Size(692, 23);
            serverStatusBox.TabIndex = 5;
            serverStatusBox.Text = "Loading...";
            serverStatusBox.TextAlign = HorizontalAlignment.Center;
            // 
            // startServerButton
            // 
            startServerButton.BackColor = Color.DarkRed;
            startServerButton.Dock = DockStyle.Top;
            startServerButton.Enabled = false;
            startServerButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            startServerButton.ForeColor = Color.White;
            startServerButton.Location = new Point(0, 51);
            startServerButton.Name = "startServerButton";
            startServerButton.Size = new Size(692, 23);
            startServerButton.TabIndex = 4;
            startServerButton.Text = "Start Server";
            startServerButton.UseVisualStyleBackColor = false;
            startServerButton.Click += startServerButton_Click;
            // 
            // forceKillServerButton
            // 
            forceKillServerButton.BackColor = SystemColors.ControlDark;
            forceKillServerButton.Dock = DockStyle.Top;
            forceKillServerButton.Enabled = false;
            forceKillServerButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            forceKillServerButton.ForeColor = Color.Black;
            forceKillServerButton.Location = new Point(0, 74);
            forceKillServerButton.Name = "forceKillServerButton";
            forceKillServerButton.Size = new Size(692, 23);
            forceKillServerButton.TabIndex = 6;
            forceKillServerButton.Text = "Force Kill (no save)";
            forceKillServerButton.UseVisualStyleBackColor = false;
            forceKillServerButton.Click += forceKillServerButton_Click;
            // 
            // installServerButton
            // 
            installServerButton.BackColor = SystemColors.ControlDark;
            installServerButton.Dock = DockStyle.Top;
            installServerButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 0);
            installServerButton.ForeColor = Color.Black;
            installServerButton.Location = new Point(0, 28);
            installServerButton.Name = "installServerButton";
            installServerButton.Size = new Size(692, 23);
            installServerButton.TabIndex = 2;
            installServerButton.Text = "Install/Update Server";
            installServerButton.UseVisualStyleBackColor = false;
            installServerButton.Click += installServerButton_Click;
            // 
            // label1
            // 
            label1.Dock = DockStyle.Top;
            label1.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point, 0);
            label1.Location = new Point(0, 0);
            label1.Name = "label1";
            label1.Size = new Size(692, 28);
            label1.TabIndex = 1;
            label1.Text = "Server Controls";
            label1.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // copyrightLabel
            // 
            copyrightLabel.Dock = DockStyle.Bottom;
            copyrightLabel.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            copyrightLabel.Location = new Point(0, 435);
            copyrightLabel.Name = "copyrightLabel";
            copyrightLabel.Size = new Size(694, 15);
            copyrightLabel.TabIndex = 3;
            copyrightLabel.Text = "Icarus Server Manager";
            copyrightLabel.TextAlign = ContentAlignment.TopRight;
            // 
            // settingsTabControl
            // 
            settingsTabControl.Controls.Add(consoleTab);
            settingsTabControl.Controls.Add(serverSettingsTab);
            settingsTabControl.Controls.Add(managerSettingsTab);
            settingsTabControl.Dock = DockStyle.Fill;
            settingsTabControl.Location = new Point(0, 157);
            settingsTabControl.Name = "settingsTabControl";
            settingsTabControl.SelectedIndex = 0;
            settingsTabControl.Size = new Size(694, 278);
            settingsTabControl.TabIndex = 4;
            // 
            // consoleTab
            // 
            consoleTab.Controls.Add(consoleTextbox);
            consoleTab.Location = new Point(4, 24);
            consoleTab.Name = "consoleTab";
            consoleTab.Padding = new Padding(3);
            consoleTab.Size = new Size(686, 250);
            consoleTab.TabIndex = 0;
            consoleTab.Text = "Console";
            consoleTab.UseVisualStyleBackColor = true;
            // 
            // consoleTextbox
            // 
            consoleTextbox.Dock = DockStyle.Fill;
            consoleTextbox.Location = new Point(3, 3);
            consoleTextbox.Name = "consoleTextbox";
            consoleTextbox.ReadOnly = true;
            consoleTextbox.Size = new Size(680, 244);
            consoleTextbox.TabIndex = 0;
            consoleTextbox.Text = "";
            // 
            // serverSettingsTab
            // 
            serverSettingsTab.Controls.Add(panel4);
            serverSettingsTab.Controls.Add(panel3);
            serverSettingsTab.Location = new Point(4, 24);
            serverSettingsTab.Name = "serverSettingsTab";
            serverSettingsTab.Size = new Size(686, 250);
            serverSettingsTab.TabIndex = 2;
            serverSettingsTab.Text = "Server Settings";
            serverSettingsTab.UseVisualStyleBackColor = true;
            // 
            // panel4
            // 
            panel4.Controls.Add(gamePortBox);
            panel4.Controls.Add(queryPortBox);
            panel4.Controls.Add(serverNameBox);
            panel4.Dock = DockStyle.Left;
            panel4.Location = new Point(94, 0);
            panel4.Name = "panel4";
            panel4.Size = new Size(184, 250);
            panel4.TabIndex = 3;
            // 
            // gamePortBox
            // 
            gamePortBox.Dock = DockStyle.Top;
            gamePortBox.Location = new Point(0, 46);
            gamePortBox.Name = "gamePortBox";
            gamePortBox.Size = new Size(184, 23);
            gamePortBox.TabIndex = 2;
            gamePortBox.Text = "17777";
            gamePortBox.TextChanged += gamePortBox_TextChanged;
            // 
            // queryPortBox
            // 
            queryPortBox.Dock = DockStyle.Top;
            queryPortBox.Location = new Point(0, 23);
            queryPortBox.Name = "queryPortBox";
            queryPortBox.Size = new Size(184, 23);
            queryPortBox.TabIndex = 1;
            queryPortBox.Text = "27015";
            queryPortBox.TextChanged += queryPortBox_TextChanged;
            // 
            // serverNameBox
            // 
            serverNameBox.Dock = DockStyle.Top;
            serverNameBox.Location = new Point(0, 0);
            serverNameBox.Name = "serverNameBox";
            serverNameBox.Size = new Size(184, 23);
            serverNameBox.TabIndex = 0;
            serverNameBox.Text = "An Icarus Server";
            serverNameBox.TextChanged += serverNameBox_TextChanged;
            // 
            // panel3
            // 
            panel3.Controls.Add(label5);
            panel3.Controls.Add(label4);
            panel3.Controls.Add(label3);
            panel3.Dock = DockStyle.Left;
            panel3.Location = new Point(0, 0);
            panel3.Name = "panel3";
            panel3.Size = new Size(94, 250);
            panel3.TabIndex = 2;
            // 
            // label5
            // 
            label5.Dock = DockStyle.Top;
            label5.Location = new Point(0, 46);
            label5.Name = "label5";
            label5.Size = new Size(94, 23);
            label5.TabIndex = 2;
            label5.Text = "Game Port:";
            // 
            // label4
            // 
            label4.Dock = DockStyle.Top;
            label4.Location = new Point(0, 23);
            label4.Name = "label4";
            label4.Size = new Size(94, 23);
            label4.TabIndex = 1;
            label4.Text = "Query Port:";
            // 
            // label3
            // 
            label3.Dock = DockStyle.Top;
            label3.Location = new Point(0, 0);
            label3.Name = "label3";
            label3.Size = new Size(94, 23);
            label3.TabIndex = 0;
            label3.Text = "Server Name:";
            // 
            // managerSettingsTab
            // 
            managerSettingsTab.Controls.Add(panel2);
            managerSettingsTab.Controls.Add(panel1);
            managerSettingsTab.Controls.Add(severSettingsPanel);
            managerSettingsTab.Location = new Point(4, 24);
            managerSettingsTab.Name = "managerSettingsTab";
            managerSettingsTab.Size = new Size(686, 250);
            managerSettingsTab.TabIndex = 1;
            managerSettingsTab.Text = "Manager Settings";
            managerSettingsTab.UseVisualStyleBackColor = true;
            // 
            // panel2
            // 
            panel2.Controls.Add(selectLocationButton);
            panel2.Dock = DockStyle.Left;
            panel2.Location = new Point(205, 0);
            panel2.Name = "panel2";
            panel2.Size = new Size(111, 250);
            panel2.TabIndex = 3;
            // 
            // selectLocationButton
            // 
            selectLocationButton.Dock = DockStyle.Top;
            selectLocationButton.Location = new Point(0, 0);
            selectLocationButton.Name = "selectLocationButton";
            selectLocationButton.Size = new Size(111, 23);
            selectLocationButton.TabIndex = 0;
            selectLocationButton.Text = "Set Location";
            selectLocationButton.UseVisualStyleBackColor = true;
            selectLocationButton.Click += selectLocationButton_Click;
            // 
            // panel1
            // 
            panel1.Controls.Add(restartTimeBox);
            panel1.Controls.Add(serverLocationBox);
            panel1.Dock = DockStyle.Left;
            panel1.Location = new Point(94, 0);
            panel1.Name = "panel1";
            panel1.Size = new Size(111, 250);
            panel1.TabIndex = 2;
            // 
            // restartTimeBox
            // 
            restartTimeBox.Dock = DockStyle.Top;
            restartTimeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            restartTimeBox.FormattingEnabled = true;
            restartTimeBox.Items.AddRange(new object[] { "1 Hour", "2 Hours", "3 Hours", "4 Hours", "5 Hours", "6 Hours" });
            restartTimeBox.Location = new Point(0, 23);
            restartTimeBox.Name = "restartTimeBox";
            restartTimeBox.Size = new Size(111, 23);
            restartTimeBox.TabIndex = 1;
            restartTimeBox.SelectedIndexChanged += restartTimeBox_SelectedIndexChanged;
            // 
            // serverLocationBox
            // 
            serverLocationBox.Dock = DockStyle.Top;
            serverLocationBox.Location = new Point(0, 0);
            serverLocationBox.Name = "serverLocationBox";
            serverLocationBox.ReadOnly = true;
            serverLocationBox.Size = new Size(111, 23);
            serverLocationBox.TabIndex = 0;
            // 
            // severSettingsPanel
            // 
            severSettingsPanel.Controls.Add(label6);
            severSettingsPanel.Controls.Add(label2);
            severSettingsPanel.Dock = DockStyle.Left;
            severSettingsPanel.Location = new Point(0, 0);
            severSettingsPanel.Name = "severSettingsPanel";
            severSettingsPanel.Size = new Size(94, 250);
            severSettingsPanel.TabIndex = 1;
            // 
            // label6
            // 
            label6.Dock = DockStyle.Top;
            label6.Location = new Point(0, 23);
            label6.Name = "label6";
            label6.Size = new Size(94, 23);
            label6.TabIndex = 1;
            label6.Text = "Restart Interval: ";
            // 
            // label2
            // 
            label2.Dock = DockStyle.Top;
            label2.Location = new Point(0, 0);
            label2.Name = "label2";
            label2.Size = new Size(94, 23);
            label2.TabIndex = 0;
            label2.Text = "Game Location:";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(694, 473);
            Controls.Add(settingsTabControl);
            Controls.Add(copyrightLabel);
            Controls.Add(primaryPanel);
            Controls.Add(mainLabel);
            Name = "Form1";
            Text = "Icarus Server Manager";
            Load += Form1_Load;
            primaryPanel.ResumeLayout(false);
            primaryPanel.PerformLayout();
            settingsTabControl.ResumeLayout(false);
            consoleTab.ResumeLayout(false);
            serverSettingsTab.ResumeLayout(false);
            panel4.ResumeLayout(false);
            panel4.PerformLayout();
            panel3.ResumeLayout(false);
            managerSettingsTab.ResumeLayout(false);
            panel2.ResumeLayout(false);
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            severSettingsPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private Label mainLabel;
        private Panel primaryPanel;
        private Label copyrightLabel;
        private Label label1;
        private Button installServerButton;
        private TextBox serverStatusBox;
        private Button startServerButton;
        private Button forceKillServerButton;
        private TabControl settingsTabControl;
        private TabPage consoleTab;
        private TabPage managerSettingsTab;
        private RichTextBox consoleTextbox;
        private Panel severSettingsPanel;
        private Panel panel1;
        private TextBox serverLocationBox;
        private Label label2;
        private Panel panel2;
        private Button selectLocationButton;
        private TabPage serverSettingsTab;
        private Panel panel4;
        private TextBox gamePortBox;
        private TextBox queryPortBox;
        private TextBox serverNameBox;
        private Panel panel3;
        private Label label5;
        private Label label4;
        private Label label3;
        private ComboBox restartTimeBox;
        private Label label6;
    }
}
