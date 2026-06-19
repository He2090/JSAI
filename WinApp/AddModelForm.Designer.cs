namespace JSAI.WinApp
{
    partial class AddModelForm
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
            label1 = new System.Windows.Forms.Label();
            txtId = new System.Windows.Forms.TextBox();
            label2 = new System.Windows.Forms.Label();
            txtName = new System.Windows.Forms.TextBox();
            label6 = new System.Windows.Forms.Label();
            comboWorkflowJson = new System.Windows.Forms.ComboBox();
            label4 = new System.Windows.Forms.Label();
            txtUrl = new System.Windows.Forms.TextBox();
            label5 = new System.Windows.Forms.Label();
            txtKey = new System.Windows.Forms.TextBox();
            label3 = new System.Windows.Forms.Label();
            comboCategory = new System.Windows.Forms.ComboBox();
            btnTest = new System.Windows.Forms.Button();
            txtTestResult = new System.Windows.Forms.TextBox();
            btnOk = new System.Windows.Forms.Button();
            btnCancel = new System.Windows.Forms.Button();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(12, 18);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(59, 17);
            label1.TabIndex = 0;
            label1.Text = "模型 ID：";
            // 
            // txtId
            // 
            txtId.Location = new System.Drawing.Point(110, 15);
            txtId.Name = "txtId";
            txtId.Size = new System.Drawing.Size(262, 23);
            txtId.TabIndex = 1;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new System.Drawing.Point(12, 56);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(59, 17);
            label2.TabIndex = 2;
            label2.Text = "模型名称";
            // 
            // txtName
            // 
            txtName.Location = new System.Drawing.Point(110, 53);
            txtName.Name = "txtName";
            txtName.Size = new System.Drawing.Size(262, 23);
            txtName.TabIndex = 3;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new System.Drawing.Point(12, 94);
            label6.Name = "label6";
            label6.Size = new System.Drawing.Size(87, 17);
            label6.TabIndex = 4;
            label6.Text = "工作流 JSON";
            // 
            // comboWorkflowJson
            // 
            comboWorkflowJson.FormattingEnabled = true;
            comboWorkflowJson.Location = new System.Drawing.Point(110, 91);
            comboWorkflowJson.Name = "comboWorkflowJson";
            comboWorkflowJson.Size = new System.Drawing.Size(262, 25);
            comboWorkflowJson.TabIndex = 4;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new System.Drawing.Point(12, 132);
            label4.Name = "label4";
            label4.Size = new System.Drawing.Size(59, 17);
            label4.TabIndex = 5;
            label4.Text = "模型地址";
            // 
            // txtUrl
            // 
            txtUrl.Location = new System.Drawing.Point(110, 129);
            txtUrl.Name = "txtUrl";
            txtUrl.Size = new System.Drawing.Size(262, 23);
            txtUrl.TabIndex = 5;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new System.Drawing.Point(12, 170);
            label5.Name = "label5";
            label5.Size = new System.Drawing.Size(65, 17);
            label5.TabIndex = 6;
            label5.Text = "模型 KEY";
            // 
            // txtKey
            // 
            txtKey.Location = new System.Drawing.Point(110, 167);
            txtKey.Name = "txtKey";
            txtKey.Size = new System.Drawing.Size(262, 23);
            txtKey.TabIndex = 6;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new System.Drawing.Point(12, 208);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(59, 17);
            label3.TabIndex = 7;
            label3.Text = "模型类别";
            // 
            // comboCategory
            // 
            comboCategory.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            comboCategory.FormattingEnabled = true;
            comboCategory.Location = new System.Drawing.Point(110, 205);
            comboCategory.Name = "comboCategory";
            comboCategory.Size = new System.Drawing.Size(262, 25);
            comboCategory.TabIndex = 7;
            // 
            // btnTest
            // 
            btnTest.Location = new System.Drawing.Point(110, 276);
            btnTest.Name = "btnTest";
            btnTest.Size = new System.Drawing.Size(100, 30);
            btnTest.TabIndex = 8;
            btnTest.Text = "测试";
            btnTest.UseVisualStyleBackColor = true;
            btnTest.Click += new System.EventHandler(btnTest_Click);
            // 
            // txtTestResult
            // 
            txtTestResult.Location = new System.Drawing.Point(110, 316);
            txtTestResult.Multiline = true;
            txtTestResult.Name = "txtTestResult";
            txtTestResult.ReadOnly = true;
            txtTestResult.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            txtTestResult.Size = new System.Drawing.Size(262, 60);
            txtTestResult.TabIndex = 9;
            txtTestResult.Text = "测试结果：等待";
            // 
            // btnOk
            // 
            btnOk.Location = new System.Drawing.Point(164, 386);
            btnOk.Name = "btnOk";
            btnOk.Size = new System.Drawing.Size(100, 30);
            btnOk.TabIndex = 10;
            btnOk.Text = "确定";
            btnOk.UseVisualStyleBackColor = true;
            btnOk.Click += new System.EventHandler(btnOk_Click);
            // 
            // btnCancel
            // 
            btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            btnCancel.Location = new System.Drawing.Point(274, 386);
            btnCancel.Name = "btnCancel";
            btnCancel.Size = new System.Drawing.Size(100, 30);
            btnCancel.TabIndex = 11;
            btnCancel.Text = "取消";
            btnCancel.UseVisualStyleBackColor = true;
            // 
            // AddModelForm
            // 
            AcceptButton = btnOk;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            BackColor = System.Drawing.Color.FromArgb(245, 245, 245);
            CancelButton = btnCancel;
            ClientSize = new System.Drawing.Size(384, 430);
            Controls.Add(btnCancel);
            Controls.Add(btnOk);
            Controls.Add(txtTestResult);
            Controls.Add(btnTest);
            Controls.Add(comboCategory);
            Controls.Add(label3);
            Controls.Add(txtKey);
            Controls.Add(label5);
            Controls.Add(txtUrl);
            Controls.Add(label4);
            Controls.Add(comboWorkflowJson);
            Controls.Add(label6);
            Controls.Add(txtName);
            Controls.Add(label2);
            Controls.Add(txtId);
            Controls.Add(label1);
            Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "AddModelForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "新增模型";
            Load += new System.EventHandler(AddModelForm_Load);
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtId;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox txtName;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.ComboBox comboWorkflowJson;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox txtUrl;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox txtKey;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ComboBox comboCategory;
        private System.Windows.Forms.Button btnTest;
        private System.Windows.Forms.TextBox txtTestResult;
        private System.Windows.Forms.Button btnOk;
        private System.Windows.Forms.Button btnCancel;
    }
}
