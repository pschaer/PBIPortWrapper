namespace PBIPortWrapper
{
    partial class MainForm
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
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            this.panelTop = new System.Windows.Forms.Panel();
            this.buttonRefresh = new System.Windows.Forms.Button();
            this.labelTitle = new System.Windows.Forms.Label();
            this.panelBottom = new System.Windows.Forms.Panel();
            this.buttonOpenLogs = new System.Windows.Forms.Button();
            this.textBoxLog = new System.Windows.Forms.TextBox();
            this.panelFill = new System.Windows.Forms.Panel();
            this.dataGridViewInstances = new System.Windows.Forms.DataGridView();
            this.timerUpdate = new System.Windows.Forms.Timer(this.components);
            this.colModelName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colPbiPort = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colFixedPort = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colAuto = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.colNetwork = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.colStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colAction = new System.Windows.Forms.DataGridViewButtonColumn();
            this.panelTop.SuspendLayout();
            this.panelBottom.SuspendLayout();
            this.panelFill.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewInstances)).BeginInit();
            this.SuspendLayout();
            // 
            // panelTop
            // 
            this.panelTop.Controls.Add(this.buttonRefresh);
            this.panelTop.Controls.Add(this.labelTitle);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(800, 60);
            this.panelTop.TabIndex = 0;
            // 
            // buttonRefresh
            // 
            this.buttonRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRefresh.Location = new System.Drawing.Point(697, 18);
            this.buttonRefresh.Name = "buttonRefresh";
            this.buttonRefresh.Size = new System.Drawing.Size(91, 28);
            this.buttonRefresh.TabIndex = 1;
            this.buttonRefresh.Text = "Refresh";
            this.buttonRefresh.UseVisualStyleBackColor = true;
            // 
            // labelTitle
            // 
            this.labelTitle.AutoSize = true;
            this.labelTitle.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.labelTitle.Location = new System.Drawing.Point(12, 16);
            this.labelTitle.Name = "labelTitle";
            this.labelTitle.Size = new System.Drawing.Size(206, 25);
            this.labelTitle.TabIndex = 0;
            this.labelTitle.Text = "PBI Port Wrapper";
            // 
            // panelBottom
            // 
            this.panelBottom.Controls.Add(this.buttonOpenLogs);
            this.panelBottom.Controls.Add(this.textBoxLog);
            this.panelBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelBottom.Location = new System.Drawing.Point(0, 300);
            this.panelBottom.Name = "panelBottom";
            this.panelBottom.Size = new System.Drawing.Size(800, 150);
            this.panelBottom.TabIndex = 1;
            // 
            // buttonOpenLogs
            // 
            this.buttonOpenLogs.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonOpenLogs.Location = new System.Drawing.Point(697, 115);
            this.buttonOpenLogs.Name = "buttonOpenLogs";
            this.buttonOpenLogs.Size = new System.Drawing.Size(91, 23);
            this.buttonOpenLogs.TabIndex = 1;
            this.buttonOpenLogs.Text = "Open Logs";
            this.buttonOpenLogs.UseVisualStyleBackColor = true;
            // 
            // textBoxLog
            // 
            this.textBoxLog.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxLog.BackColor = System.Drawing.Color.White;
            this.textBoxLog.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.textBoxLog.Location = new System.Drawing.Point(12, 6);
            this.textBoxLog.Multiline = true;
            this.textBoxLog.Name = "textBoxLog";
            this.textBoxLog.ReadOnly = true;
            this.textBoxLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBoxLog.Size = new System.Drawing.Size(776, 103);
            this.textBoxLog.TabIndex = 0;
            // 
            // panelFill
            // 
            this.panelFill.Controls.Add(this.dataGridViewInstances);
            this.panelFill.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelFill.Location = new System.Drawing.Point(0, 60);
            this.panelFill.Name = "panelFill";
            this.panelFill.Padding = new System.Windows.Forms.Padding(12);
            this.panelFill.Size = new System.Drawing.Size(800, 240);
            this.panelFill.TabIndex = 2;
            // 
            // dataGridViewInstances
            // 
            this.dataGridViewInstances.AllowUserToAddRows = false;
            this.dataGridViewInstances.AllowUserToDeleteRows = false;
            this.dataGridViewInstances.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dataGridViewInstances.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridViewInstances.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colModelName,
            this.colPbiPort,
            this.colFixedPort,
            this.colAuto,
            this.colNetwork,
            this.colStatus,
            this.colAction});
            this.dataGridViewInstances.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridViewInstances.Location = new System.Drawing.Point(12, 12);
            this.dataGridViewInstances.Name = "dataGridViewInstances";
            this.dataGridViewInstances.RowHeadersVisible = false;
            this.dataGridViewInstances.RowTemplate.Height = 25;
            this.dataGridViewInstances.Size = new System.Drawing.Size(776, 216);
            this.dataGridViewInstances.TabIndex = 0;
            // 
            // timerUpdate
            // 
            this.timerUpdate.Enabled = true;
            this.timerUpdate.Interval = 5000;
            // 
            // colModelName
            // 
            this.colModelName.HeaderText = "Model Name";
            this.colModelName.Name = "colModelName";
            this.colModelName.ReadOnly = true;
            // 
            // colPbiPort
            // 
            this.colPbiPort.HeaderText = "PBI Port";
            this.colPbiPort.Name = "colPbiPort";
            this.colPbiPort.ReadOnly = true;
            // 
            // colFixedPort
            // 
            this.colFixedPort.HeaderText = "Fixed Port";
            this.colFixedPort.Name = "colFixedPort";
            // 
            // colAuto
            // 
            this.colAuto.HeaderText = "Auto";
            this.colAuto.Name = "colAuto";
            // 
            // colNetwork
            // 
            this.colNetwork.HeaderText = "Network";
            this.colNetwork.Name = "colNetwork";
            // 
            // colStatus
            // 
            this.colStatus.HeaderText = "Status";
            this.colStatus.Name = "colStatus";
            this.colStatus.ReadOnly = true;
            // 
            // colAction
            // 
            this.colAction.HeaderText = "Action";
            this.colAction.Name = "colAction";
            this.colAction.Text = "Start";
            this.colAction.UseColumnTextForButtonValue = false;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.panelFill);
            this.Controls.Add(this.panelBottom);
            this.Controls.Add(this.panelTop);
            this.Name = "MainForm";
            this.Text = "PBI Port Wrapper";
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            this.panelBottom.ResumeLayout(false);
            this.panelBottom.PerformLayout();
            this.panelFill.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewInstances)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel panelTop;
        private System.Windows.Forms.Panel panelBottom;
        private System.Windows.Forms.Panel panelFill;
        private System.Windows.Forms.DataGridView dataGridViewInstances;
        private System.Windows.Forms.Label labelTitle;
        private System.Windows.Forms.Button buttonRefresh;
        private System.Windows.Forms.TextBox textBoxLog;
        private System.Windows.Forms.Button buttonOpenLogs;
        private System.Windows.Forms.Timer timerUpdate;
        private System.Windows.Forms.DataGridViewTextBoxColumn colModelName;
        private System.Windows.Forms.DataGridViewTextBoxColumn colPbiPort;
        private System.Windows.Forms.DataGridViewTextBoxColumn colFixedPort;
        private System.Windows.Forms.DataGridViewCheckBoxColumn colAuto;
        private System.Windows.Forms.DataGridViewCheckBoxColumn colNetwork;
        private System.Windows.Forms.DataGridViewTextBoxColumn colStatus;
        private System.Windows.Forms.DataGridViewButtonColumn colAction;
    }
}