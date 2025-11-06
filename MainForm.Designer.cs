namespace PBIPortWrapper
{
    partial class MainForm
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
            groupBoxInstances = new GroupBox();
            buttonRefresh = new Button();
            listBoxInstances = new ListBox();
            groupBoxConfig = new GroupBox();
            checkBoxNetworkAccess = new CheckBox();
            textBoxFixedPort = new TextBox();
            labelFixedPort = new Label();
            groupBoxConnectionInfo = new GroupBox();
            buttonCopy = new Button();
            textBoxConnectionString = new TextBox();
            labelConnectionString = new Label();
            labelStatus = new Label();
            buttonStart = new Button();
            buttonStop = new Button();
            buttonOpenLogs = new Button();
            groupBoxLog = new GroupBox();
            textBoxLog = new TextBox();
            groupBoxInstances.SuspendLayout();
            groupBoxConfig.SuspendLayout();
            groupBoxConnectionInfo.SuspendLayout();
            groupBoxLog.SuspendLayout();
            SuspendLayout();
            // 
            // groupBoxInstances
            // 
            groupBoxInstances.Controls.Add(buttonRefresh);
            groupBoxInstances.Controls.Add(listBoxInstances);
            groupBoxInstances.Location = new Point(12, 12);
            groupBoxInstances.Name = "groupBoxInstances";
            groupBoxInstances.Size = new Size(560, 150);
            groupBoxInstances.TabIndex = 0;
            groupBoxInstances.TabStop = false;
            groupBoxInstances.Text = "Power BI Instance Selection";
            // 
            // buttonRefresh
            // 
            buttonRefresh.Location = new Point(450, 115);
            buttonRefresh.Name = "buttonRefresh";
            buttonRefresh.Size = new Size(100, 25);
            buttonRefresh.TabIndex = 1;
            buttonRefresh.Text = "Refresh";
            buttonRefresh.UseVisualStyleBackColor = true;
            // 
            // listBoxInstances
            // 
            listBoxInstances.FormattingEnabled = true;
            listBoxInstances.ItemHeight = 15;
            listBoxInstances.Location = new Point(10, 25);
            listBoxInstances.Name = "listBoxInstances";
            listBoxInstances.Size = new Size(540, 79);
            listBoxInstances.TabIndex = 0;
            // 
            // groupBoxConfig
            // 
            groupBoxConfig.Controls.Add(checkBoxNetworkAccess);
            groupBoxConfig.Controls.Add(textBoxFixedPort);
            groupBoxConfig.Controls.Add(labelFixedPort);
            groupBoxConfig.Location = new Point(12, 170);
            groupBoxConfig.Name = "groupBoxConfig";
            groupBoxConfig.Size = new Size(270, 120);
            groupBoxConfig.TabIndex = 1;
            groupBoxConfig.TabStop = false;
            groupBoxConfig.Text = "Port Forwarding Configuration";
            // 
            // checkBoxNetworkAccess
            // 
            checkBoxNetworkAccess.AutoSize = true;
            checkBoxNetworkAccess.Location = new Point(15, 65);
            checkBoxNetworkAccess.Name = "checkBoxNetworkAccess";
            checkBoxNetworkAccess.Size = new Size(143, 19);
            checkBoxNetworkAccess.TabIndex = 2;
            checkBoxNetworkAccess.Text = "Allow Network Access";
            checkBoxNetworkAccess.UseVisualStyleBackColor = true;
            // 
            // textBoxFixedPort
            // 
            textBoxFixedPort.Location = new Point(90, 30);
            textBoxFixedPort.Name = "textBoxFixedPort";
            textBoxFixedPort.Size = new Size(100, 23);
            textBoxFixedPort.TabIndex = 1;
            textBoxFixedPort.Text = "55555";
            // 
            // labelFixedPort
            // 
            labelFixedPort.AutoSize = true;
            labelFixedPort.Location = new Point(15, 33);
            labelFixedPort.Name = "labelFixedPort";
            labelFixedPort.Size = new Size(67, 15);
            labelFixedPort.TabIndex = 0;
            labelFixedPort.Text = "Listen Port:";
            // 
            // groupBoxConnectionInfo
            // 
            groupBoxConnectionInfo.Controls.Add(buttonCopy);
            groupBoxConnectionInfo.Controls.Add(textBoxConnectionString);
            groupBoxConnectionInfo.Controls.Add(labelConnectionString);
            groupBoxConnectionInfo.Location = new Point(290, 170);
            groupBoxConnectionInfo.Name = "groupBoxConnectionInfo";
            groupBoxConnectionInfo.Size = new Size(282, 120);
            groupBoxConnectionInfo.TabIndex = 2;
            groupBoxConnectionInfo.TabStop = false;
            groupBoxConnectionInfo.Text = "Connection Information";
            // 
            // buttonCopy
            // 
            buttonCopy.Location = new Point(176, 80);
            buttonCopy.Name = "buttonCopy";
            buttonCopy.Size = new Size(90, 25);
            buttonCopy.TabIndex = 2;
            buttonCopy.Text = "Copy";
            buttonCopy.UseVisualStyleBackColor = true;
            // 
            // textBoxConnectionString
            // 
            textBoxConnectionString.Location = new Point(15, 52);
            textBoxConnectionString.Name = "textBoxConnectionString";
            textBoxConnectionString.ReadOnly = true;
            textBoxConnectionString.Size = new Size(251, 23);
            textBoxConnectionString.TabIndex = 1;
            textBoxConnectionString.Text = "localhost:55555";
            // 
            // labelConnectionString
            // 
            labelConnectionString.AutoSize = true;
            labelConnectionString.Location = new Point(15, 30);
            labelConnectionString.Name = "labelConnectionString";
            labelConnectionString.Size = new Size(87, 15);
            labelConnectionString.TabIndex = 0;
            labelConnectionString.Text = "Server Address:";
            // 
            // labelStatus
            // 
            labelStatus.AutoSize = true;
            labelStatus.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            labelStatus.ForeColor = Color.Red;
            labelStatus.Location = new Point(12, 300);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(139, 19);
            labelStatus.TabIndex = 3;
            labelStatus.Text = "Status: Not Running";
            // 
            // buttonStart
            // 
            buttonStart.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            buttonStart.Location = new Point(12, 325);
            buttonStart.Name = "buttonStart";
            buttonStart.Size = new Size(150, 35);
            buttonStart.TabIndex = 4;
            buttonStart.Text = "Start Port Forwarding";
            buttonStart.UseVisualStyleBackColor = true;
            // 
            // buttonStop
            // 
            buttonStop.Enabled = false;
            buttonStop.Location = new Point(170, 325);
            buttonStop.Name = "buttonStop";
            buttonStop.Size = new Size(150, 35);
            buttonStop.TabIndex = 5;
            buttonStop.Text = "Stop";
            buttonStop.UseVisualStyleBackColor = true;
            // 
            // buttonOpenLogs
            // 
            buttonOpenLogs.Location = new Point(328, 325);
            buttonOpenLogs.Name = "buttonOpenLogs";
            buttonOpenLogs.Size = new Size(120, 35);
            buttonOpenLogs.TabIndex = 6;
            buttonOpenLogs.Text = "Open Logs";
            buttonOpenLogs.UseVisualStyleBackColor = true;
            // 
            // groupBoxLog
            // 
            groupBoxLog.Controls.Add(textBoxLog);
            groupBoxLog.Location = new Point(12, 370);
            groupBoxLog.Name = "groupBoxLog";
            groupBoxLog.Size = new Size(560, 150);
            groupBoxLog.TabIndex = 7;
            groupBoxLog.TabStop = false;
            groupBoxLog.Text = "Activity Log";
            // 
            // textBoxLog
            // 
            textBoxLog.BackColor = SystemColors.Window;
            textBoxLog.Dock = DockStyle.Fill;
            textBoxLog.Font = new Font("Consolas", 8.25F);
            textBoxLog.Location = new Point(3, 19);
            textBoxLog.Multiline = true;
            textBoxLog.Name = "textBoxLog";
            textBoxLog.ReadOnly = true;
            textBoxLog.ScrollBars = ScrollBars.Vertical;
            textBoxLog.Size = new Size(554, 128);
            textBoxLog.TabIndex = 0;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(584, 531);
            Controls.Add(groupBoxLog);
            Controls.Add(buttonOpenLogs);
            Controls.Add(buttonStop);
            Controls.Add(buttonStart);
            Controls.Add(labelStatus);
            Controls.Add(groupBoxConnectionInfo);
            Controls.Add(groupBoxConfig);
            Controls.Add(groupBoxInstances);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "PBI Port Wrapper v0.1";
            groupBoxInstances.ResumeLayout(false);
            groupBoxConfig.ResumeLayout(false);
            groupBoxConfig.PerformLayout();
            groupBoxConnectionInfo.ResumeLayout(false);
            groupBoxConnectionInfo.PerformLayout();
            groupBoxLog.ResumeLayout(false);
            groupBoxLog.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private GroupBox groupBoxInstances;
        private Button buttonRefresh;
        private ListBox listBoxInstances;
        private GroupBox groupBoxConfig;
        private CheckBox checkBoxNetworkAccess;
        private TextBox textBoxFixedPort;
        private Label labelFixedPort;
        private GroupBox groupBoxConnectionInfo;
        private Button buttonCopy;
        private TextBox textBoxConnectionString;
        private Label labelConnectionString;
        private Label labelStatus;
        private Button buttonStart;
        private Button buttonStop;
        private Button buttonOpenLogs;
        private GroupBox groupBoxLog;
        private TextBox textBoxLog;
    }
}