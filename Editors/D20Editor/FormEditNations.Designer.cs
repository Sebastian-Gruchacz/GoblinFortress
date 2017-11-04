namespace D20Editor
{
    partial class FormEditNations
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
            this.panel1 = new System.Windows.Forms.Panel();
            this.bSave = new System.Windows.Forms.Button();
            this.bClose = new System.Windows.Forms.Button();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.panel2 = new System.Windows.Forms.Panel();
            this.bAdd = new System.Windows.Forms.Button();
            this.listNations = new System.Windows.Forms.ListBox();
            this.panel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.panel2.SuspendLayout();
            this.SuspendLayout();
            // 
            // panel1
            // 
            this.panel1.Controls.Add(this.bSave);
            this.panel1.Controls.Add(this.bClose);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel1.Location = new System.Drawing.Point(0, 532);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(1164, 44);
            this.panel1.TabIndex = 3;
            // 
            // bSave
            // 
            this.bSave.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bSave.Location = new System.Drawing.Point(994, 10);
            this.bSave.Name = "bSave";
            this.bSave.Size = new System.Drawing.Size(80, 23);
            this.bSave.TabIndex = 3;
            this.bSave.Text = "Close && Save";
            this.bSave.UseVisualStyleBackColor = true;
            // 
            // bClose
            // 
            this.bClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.bClose.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.bClose.Location = new System.Drawing.Point(1079, 11);
            this.bClose.Name = "bClose";
            this.bClose.Size = new System.Drawing.Size(75, 23);
            this.bClose.TabIndex = 2;
            this.bClose.Text = "Close";
            this.bClose.UseVisualStyleBackColor = true;
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 0);
            this.splitContainer1.Margin = new System.Windows.Forms.Padding(2);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.listNations);
            this.splitContainer1.Panel1.Controls.Add(this.panel2);
            this.splitContainer1.Size = new System.Drawing.Size(1164, 532);
            this.splitContainer1.SplitterDistance = 336;
            this.splitContainer1.SplitterWidth = 3;
            this.splitContainer1.TabIndex = 4;
            // 
            // panel2
            // 
            this.panel2.Controls.Add(this.bAdd);
            this.panel2.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panel2.Location = new System.Drawing.Point(0, 490);
            this.panel2.Name = "panel2";
            this.panel2.Size = new System.Drawing.Size(336, 42);
            this.panel2.TabIndex = 1;
            // 
            // bAdd
            // 
            this.bAdd.Location = new System.Drawing.Point(11, 10);
            this.bAdd.Margin = new System.Windows.Forms.Padding(2);
            this.bAdd.Name = "bAdd";
            this.bAdd.Size = new System.Drawing.Size(81, 21);
            this.bAdd.TabIndex = 1;
            this.bAdd.Text = "Add &new...";
            this.bAdd.UseVisualStyleBackColor = true;
            // 
            // listNations
            // 
            this.listNations.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listNations.FormattingEnabled = true;
            this.listNations.Location = new System.Drawing.Point(0, 0);
            this.listNations.Margin = new System.Windows.Forms.Padding(2);
            this.listNations.Name = "listNations";
            this.listNations.Size = new System.Drawing.Size(336, 490);
            this.listNations.TabIndex = 2;
            // 
            // FormEditNations
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.bClose;
            this.ClientSize = new System.Drawing.Size(1164, 576);
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.panel1);
            this.MinimizeBox = false;
            this.Name = "FormEditNations";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Edit Nations";
            this.Load += new System.EventHandler(this.FormEditNations_Load);
            this.panel1.ResumeLayout(false);
            this.splitContainer1.Panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.panel2.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button bSave;
        private System.Windows.Forms.Button bClose;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.ListBox listNations;
        private System.Windows.Forms.Panel panel2;
        private System.Windows.Forms.Button bAdd;
    }
}