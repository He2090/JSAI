namespace JSAI.WinApp
{
    partial class ModelSettingsForm
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
            this.listModels = new System.Windows.Forms.ListView();
            this.colName = new System.Windows.Forms.ColumnHeader();
            this.colSource = new System.Windows.Forms.ColumnHeader();
            this.colCategory = new System.Windows.Forms.ColumnHeader();
            this.colUrl = new System.Windows.Forms.ColumnHeader();
            this.colKey = new System.Windows.Forms.ColumnHeader();
            this.colId = new System.Windows.Forms.ColumnHeader();
            this.btnAddModel = new System.Windows.Forms.Button();
            this.btnDeleteModel = new System.Windows.Forms.Button();
            this.btnSetTextModel = new System.Windows.Forms.Button();
            this.btnSetImagePromptTextModel = new System.Windows.Forms.Button();
            this.btnSetImageModel = new System.Windows.Forms.Button();
            this.btnSetVideoModel = new System.Windows.Forms.Button();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.lblVideoModel = new System.Windows.Forms.Label();
            this.lblImageModel = new System.Windows.Forms.Label();
            this.lblImagePromptTextModel = new System.Windows.Forms.Label();
            this.lblTextModel = new System.Windows.Forms.Label();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // listModels
            // 
            this.listModels.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colName,
            this.colSource,
            this.colCategory,
            this.colUrl,
            this.colKey,
            this.colId});
            this.listModels.FullRowSelect = true;
            this.listModels.GridLines = true;
            this.listModels.HideSelection = false;
            this.listModels.Location = new System.Drawing.Point(12, 52);
            this.listModels.MultiSelect = false;
            this.listModels.Name = "listModels";
            this.listModels.Size = new System.Drawing.Size(560, 240);
            this.listModels.TabIndex = 0;
            this.listModels.UseCompatibleStateImageBehavior = false;
            this.listModels.View = System.Windows.Forms.View.Details;
            // 
            // colName
            // 
            this.colName.Text = "模型名称";
            this.colName.Width = 180;
            // 
            // colCategory
            // 
            this.colCategory.Text = "类别";
            this.colCategory.Width = 80;
            // 
            // colUrl
            // 
            this.colUrl.Text = "地址";
            this.colUrl.Width = 160;
            // 
            // colKey
            // 
            this.colKey.Text = "Key";
            this.colKey.Width = 120;
            // 
            // colId
            // 
            this.colId.Text = "ID";
            this.colId.Width = 120;
            // 
            // btnAddModel
            // 
            this.btnAddModel.Location = new System.Drawing.Point(12, 12);
            this.btnAddModel.Name = "btnAddModel";
            this.btnAddModel.Size = new System.Drawing.Size(100, 30);
            this.btnAddModel.TabIndex = 1;
            this.btnAddModel.Text = "新模型 +";
            this.btnAddModel.UseVisualStyleBackColor = true;
            this.btnAddModel.Click += new System.EventHandler(this.btnAddModel_Click);
            // 
            // btnDeleteModel
            // 
            this.btnDeleteModel.Location = new System.Drawing.Point(118, 12);
            this.btnDeleteModel.Name = "btnDeleteModel";
            this.btnDeleteModel.Size = new System.Drawing.Size(100, 30);
            this.btnDeleteModel.TabIndex = 2;
            this.btnDeleteModel.Text = "删除模型";
            this.btnDeleteModel.UseVisualStyleBackColor = true;
            this.btnDeleteModel.Click += new System.EventHandler(this.btnDeleteModel_Click);
            // 
            // btnSetTextModel
            // 
            this.btnSetTextModel.Location = new System.Drawing.Point(128, 302);
            this.btnSetTextModel.Name = "btnSetTextModel";
            this.btnSetTextModel.Size = new System.Drawing.Size(140, 30);
            this.btnSetTextModel.TabIndex = 3;
            this.btnSetTextModel.Text = "设为文本模型";
            this.btnSetTextModel.UseVisualStyleBackColor = true;
            this.btnSetTextModel.Click += new System.EventHandler(this.btnSetTextModel_Click);
            // 
            // btnSetImagePromptTextModel
            // 
            this.btnSetImagePromptTextModel.Location = new System.Drawing.Point(274, 302);
            this.btnSetImagePromptTextModel.Name = "btnSetImagePromptTextModel";
            this.btnSetImagePromptTextModel.Size = new System.Drawing.Size(160, 30);
            this.btnSetImagePromptTextModel.TabIndex = 4;
            this.btnSetImagePromptTextModel.Text = "设为图片提示词模型";
            this.btnSetImagePromptTextModel.UseVisualStyleBackColor = true;
            this.btnSetImagePromptTextModel.Click += new System.EventHandler(this.btnSetImagePromptTextModel_Click);
            // 
            // btnSetImageModel
            // 
            this.btnSetImageModel.Location = new System.Drawing.Point(440, 302);
            this.btnSetImageModel.Name = "btnSetImageModel";
            this.btnSetImageModel.Size = new System.Drawing.Size(140, 30);
            this.btnSetImageModel.TabIndex = 5;
            this.btnSetImageModel.Text = "设为图片模型";
            this.btnSetImageModel.UseVisualStyleBackColor = true;
            this.btnSetImageModel.Click += new System.EventHandler(this.btnSetImageModel_Click);
            // 
            // btnSetVideoModel
            // 
            this.btnSetVideoModel.Location = new System.Drawing.Point(586, 302);
            this.btnSetVideoModel.Name = "btnSetVideoModel";
            this.btnSetVideoModel.Size = new System.Drawing.Size(152, 30);
            this.btnSetVideoModel.TabIndex = 6;
            this.btnSetVideoModel.Text = "设为视频模型";
            this.btnSetVideoModel.UseVisualStyleBackColor = true;
            this.btnSetVideoModel.Click += new System.EventHandler(this.btnSetVideoModel_Click);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.lblVideoModel);
            this.groupBox1.Controls.Add(this.lblImageModel);
            this.groupBox1.Controls.Add(this.lblImagePromptTextModel);
            this.groupBox1.Controls.Add(this.lblTextModel);
            this.groupBox1.Location = new System.Drawing.Point(12, 342);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(726, 136);
            this.groupBox1.TabIndex = 5;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "当前选择";
            // 
            // lblVideoModel
            // 
            this.lblVideoModel.AutoSize = true;
            this.lblVideoModel.Location = new System.Drawing.Point(12, 79);
            this.lblVideoModel.Name = "lblVideoModel";
            this.lblVideoModel.Size = new System.Drawing.Size(142, 20);
            this.lblVideoModel.TabIndex = 2;
            this.lblVideoModel.Text = "视频模型：未选择";
            // 
            // lblImageModel
            // 
            this.lblImageModel.AutoSize = true;
            this.lblImageModel.Location = new System.Drawing.Point(12, 75);
            this.lblImageModel.Name = "lblImageModel";
            this.lblImageModel.Size = new System.Drawing.Size(142, 20);
            this.lblImageModel.TabIndex = 1;
            this.lblImageModel.Text = "图片模型：未选择";
            // 
            // lblImagePromptTextModel
            // 
            this.lblImagePromptTextModel.AutoSize = true;
            this.lblImagePromptTextModel.Location = new System.Drawing.Point(12, 50);
            this.lblImagePromptTextModel.Name = "lblImagePromptTextModel";
            this.lblImagePromptTextModel.Size = new System.Drawing.Size(177, 20);
            this.lblImagePromptTextModel.TabIndex = 3;
            this.lblImagePromptTextModel.Text = "图片提示词模型：未选择";
            // 
            // lblTextModel
            // 
            this.lblTextModel.AutoSize = true;
            this.lblTextModel.Location = new System.Drawing.Point(12, 25);
            this.lblTextModel.Name = "lblTextModel";
            this.lblTextModel.Size = new System.Drawing.Size(142, 20);
            this.lblTextModel.TabIndex = 0;
            this.lblTextModel.Text = "文本模型：未选择";
            // 
            // ModelSettingsForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(584, 464);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.btnSetVideoModel);
            this.Controls.Add(this.btnSetImageModel);
            this.Controls.Add(this.btnSetImagePromptTextModel);
            this.Controls.Add(this.btnSetTextModel);
            this.Controls.Add(this.btnDeleteModel);
            this.Controls.Add(this.btnAddModel);
            this.Controls.Add(this.listModels);
            this.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            this.BackColor = System.Drawing.Color.White;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ModelSettingsForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "模型设置";
            this.Load += new System.EventHandler(this.ModelSettingsForm_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ListView listModels;
        private System.Windows.Forms.ColumnHeader colName;
        private System.Windows.Forms.ColumnHeader colSource;
        private System.Windows.Forms.ColumnHeader colCategory;
        private System.Windows.Forms.ColumnHeader colUrl;
        private System.Windows.Forms.ColumnHeader colKey;
        private System.Windows.Forms.ColumnHeader colId;
        private System.Windows.Forms.Button btnAddModel;
        private System.Windows.Forms.Button btnDeleteModel;
        private System.Windows.Forms.Button btnSetTextModel;
        private System.Windows.Forms.Button btnSetImagePromptTextModel;
        private System.Windows.Forms.Button btnSetImageModel;
        private System.Windows.Forms.Button btnSetVideoModel;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Label lblVideoModel;
        private System.Windows.Forms.Label lblImageModel;
        private System.Windows.Forms.Label lblImagePromptTextModel;
        private System.Windows.Forms.Label lblTextModel;
    }
}
