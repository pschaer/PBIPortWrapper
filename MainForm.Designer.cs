namespace PowerBIPortWrapper
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
            listBoxInstances = new ListBox();
            groupBoxConfig = new GroupBox();
            textBoxNetworkPort = new TextBox();
            labelNetworkPort = new Label();
            checkBoxNetworkAccess = new CheckBox();
            textBoxFixedPort = new TextBox();
            labelFixedPort = new Label();
            labelStatus = new Label();
            labelConnectionString = new Label();
            textBoxConnectionString = new TextBox();
            buttonCopy = new Button();
            buttonStart = new Button();
            buttonStop = new Button();
            buttonRefresh = new Button();
            buttonDebug = new Button();
            groupBoxInstances.SuspendLayout();
            groupBoxConfig.SuspendLayout();
            SuspendLayout();
            // 
            // groupBoxInstances
            // 
            groupBoxInstances.Controls.Add(listBoxInstances);
            groupBoxInstances.Location = new Point(12, 12);
            groupBoxInstances.Name = "groupBoxInstances";
            groupBoxInstances.Size = new Size(460, 115);
            groupBoxInstances.TabIndex = 0;
            groupBoxInstances.TabStop = false;
            groupBoxInstances.Text = "Running Power BI Instances";
            // 
            // listBoxInstances
            // 
            listBoxInstances.FormattingEnabled = true;
            listBoxInstances.ItemHeight = 15;
            listBoxInstances.Location = new Point(10, 25);
            listBoxInstances.Name = "listBoxInstances";
            listBoxInstances.Size = new Size(440, 79);
            listBoxInstances.TabIndex = 0;
            // 
            // groupBoxConfig
            // 
            groupBoxConfig.Controls.Add(textBoxNetworkPort);
            groupBoxConfig.Controls.Add(labelNetworkPort);
            groupBoxConfig.Controls.Add(checkBoxNetworkAccess);
            groupBoxConfig.Controls.Add(textBoxFixedPort);
            groupBoxConfig.Controls.Add(labelFixedPort);
            groupBoxConfig.Location = new Point(12, 170);
            groupBoxConfig.Name = "groupBoxConfig";
            groupBoxConfig.Size = new Size(460, 120);
            groupBoxConfig.TabIndex = 1;
            groupBoxConfig.TabStop = false;
            groupBoxConfig.Text = "Proxy Configuration";
            // 
            // textBoxNetworkPort
            // 
            textBoxNetworkPort.Enabled = false;
            textBoxNetworkPort.Location = new Point(100, 87);
            textBoxNetworkPort.Name = "textBoxNetworkPort";
            textBoxNetworkPort.Size = new Size(100, 23);
            textBoxNetworkPort.TabIndex = 4;
            textBoxNetworkPort.Text = "55556";
            // 
            // labelNetworkPort
            // 
            labelNetworkPort.AutoSize = true;
            labelNetworkPort.Enabled = false;
            labelNetworkPort.Location = new Point(15, 90);
            labelNetworkPort.Name = "labelNetworkPort";
            labelNetworkPort.Size = new Size(80, 15);
            labelNetworkPort.TabIndex = 3;
            labelNetworkPort.Text = "Network Port:";
            // 
            // checkBoxNetworkAccess
            // 
            checkBoxNetworkAccess.AutoSize = true;
            checkBoxNetworkAccess.Location = new Point(15, 60);
            checkBoxNetworkAccess.Name = "checkBoxNetworkAccess";
            checkBoxNetworkAccess.Size = new Size(143, 19);
            checkBoxNetworkAccess.TabIndex = 2;
            checkBoxNetworkAccess.Text = "Allow Network Access";
            checkBoxNetworkAccess.UseVisualStyleBackColor = true;
            // 
            // textBoxFixedPort
            // 
            textBoxFixedPort.Location = new Point(100, 27);
            textBoxFixedPort.Name = "textBoxFixedPort";
            textBoxFixedPort.Size = new Size(100, 23);
            textBoxFixedPort.TabIndex = 1;
            textBoxFixedPort.Text = "55555";
            // 
            // labelFixedPort
            // 
            labelFixedPort.AutoSize = true;
            labelFixedPort.Location = new Point(15, 30);
            labelFixedPort.Name = "labelFixedPort";
            labelFixedPort.Size = new Size(62, 15);
            labelFixedPort.TabIndex = 0;
            labelFixedPort.Text = "Fixed Port:";
            // 
            // labelStatus
            // 
            labelStatus.AutoSize = true;
            labelStatus.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point, 0);
            labelStatus.ForeColor = Color.Red;
            labelStatus.Location = new Point(12, 300);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(133, 17);
            labelStatus.TabIndex = 2;
            labelStatus.Text = "Status: Not Running";
            // 
            // labelConnectionString
            // 
            labelConnectionString.AutoSize = true;
            labelConnectionString.Location = new Point(12, 330);
            labelConnectionString.Name = "labelConnectionString";
            labelConnectionString.Size = new Size(106, 15);
            labelConnectionString.TabIndex = 3;
            labelConnectionString.Text = "Connection String:";
            // 
            // textBoxConnectionString
            // 
            textBoxConnectionString.Location = new Point(12, 350);
            textBoxConnectionString.Name = "textBoxConnectionString";
            textBoxConnectionString.ReadOnly = true;
            textBoxConnectionString.Size = new Size(360, 23);
            textBoxConnectionString.TabIndex = 4;
            textBoxConnectionString.Text = "localhost:55555";
            // 
            // buttonCopy
            // 
            buttonCopy.Location = new Point(380, 349);
            buttonCopy.Name = "buttonCopy";
            buttonCopy.Size = new Size(90, 25);
            buttonCopy.TabIndex = 5;
            buttonCopy.Text = "Copy";
            buttonCopy.UseVisualStyleBackColor = true;
            // 
            // buttonStart
            // 
            buttonStart.Location = new Point(12, 385);
            buttonStart.Name = "buttonStart";
            buttonStart.Size = new Size(150, 35);
            buttonStart.TabIndex = 6;
            buttonStart.Text = "Start Proxy";
            buttonStart.UseVisualStyleBackColor = true;
            // 
            // buttonStop
            // 
            buttonStop.Enabled = false;
            buttonStop.Location = new Point(170, 385);
            buttonStop.Name = "buttonStop";
            buttonStop.Size = new Size(150, 35);
            buttonStop.TabIndex = 7;
            buttonStop.Text = "Stop";
            buttonStop.UseVisualStyleBackColor = true;
            // 
            // buttonRefresh
            // 
            buttonRefresh.Location = new Point(328, 385);
            buttonRefresh.Name = "buttonRefresh";
            buttonRefresh.Size = new Size(144, 35);
            buttonRefresh.TabIndex = 8;
            buttonRefresh.Text = "Refresh Instances";
            buttonRefresh.UseVisualStyleBackColor = true;
            // 
            // buttonDebug
            // 
            buttonDebug.Location = new Point(372, 302);
            buttonDebug.Name = "buttonDebug";
            buttonDebug.Size = new Size(96, 23);
            buttonDebug.TabIndex = 9;
            buttonDebug.Text = "Debug Path";
            buttonDebug.UseVisualStyleBackColor = true;
            buttonDebug.Click += this.buttonDebug_Click;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(484, 441);
            Controls.Add(buttonDebug);
            Controls.Add(buttonRefresh);
            Controls.Add(buttonStop);
            Controls.Add(buttonStart);
            Controls.Add(buttonCopy);
            Controls.Add(textBoxConnectionString);
            Controls.Add(labelConnectionString);
            Controls.Add(labelStatus);
            Controls.Add(groupBoxConfig);
            Controls.Add(groupBoxInstances);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            Name = "MainForm";
            Text = "Power BI Port Wrapper";
            groupBoxInstances.ResumeLayout(false);
            groupBoxConfig.ResumeLayout(false);
            groupBoxConfig.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private GroupBox groupBoxInstances;
        private ListBox listBoxInstances;
        private GroupBox groupBoxConfig;
        private CheckBox checkBoxNetworkAccess;
        private TextBox textBoxFixedPort;
        private Label labelFixedPort;
        private Label labelNetworkPort;
        private TextBox textBoxNetworkPort;
        private Label labelStatus;
        private Label labelConnectionString;
        private TextBox textBoxConnectionString;
        private Button buttonCopy;
        private Button buttonStart;
        private Button buttonStop;
        private Button buttonRefresh;
        private Button buttonDebug;
    }
}
