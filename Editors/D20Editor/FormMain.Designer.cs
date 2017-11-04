namespace D20Editor
{
    partial class FormMain
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
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.coreToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.skillsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.featsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.baseCharacterClassesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.occupationsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.wordToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.gameLanguagesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.nationsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.futureWordToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.gameLanguagesToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.nationsToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.menuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem,
            this.coreToolStripMenuItem,
            this.wordToolStripMenuItem,
            this.futureWordToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(1092, 24);
            this.menuStrip1.TabIndex = 0;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
            this.fileToolStripMenuItem.Text = "&File";
            // 
            // coreToolStripMenuItem
            // 
            this.coreToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.skillsToolStripMenuItem,
            this.featsToolStripMenuItem,
            this.baseCharacterClassesToolStripMenuItem,
            this.occupationsToolStripMenuItem});
            this.coreToolStripMenuItem.Name = "coreToolStripMenuItem";
            this.coreToolStripMenuItem.Size = new System.Drawing.Size(44, 20);
            this.coreToolStripMenuItem.Text = "&Core";
            // 
            // skillsToolStripMenuItem
            // 
            this.skillsToolStripMenuItem.Name = "skillsToolStripMenuItem";
            this.skillsToolStripMenuItem.Size = new System.Drawing.Size(202, 22);
            this.skillsToolStripMenuItem.Text = "&Skills...";
            this.skillsToolStripMenuItem.Click += new System.EventHandler(this.skillsToolStripMenuItem_Click);
            // 
            // featsToolStripMenuItem
            // 
            this.featsToolStripMenuItem.Name = "featsToolStripMenuItem";
            this.featsToolStripMenuItem.Size = new System.Drawing.Size(202, 22);
            this.featsToolStripMenuItem.Text = "&Feats...";
            // 
            // baseCharacterClassesToolStripMenuItem
            // 
            this.baseCharacterClassesToolStripMenuItem.Name = "baseCharacterClassesToolStripMenuItem";
            this.baseCharacterClassesToolStripMenuItem.Size = new System.Drawing.Size(202, 22);
            this.baseCharacterClassesToolStripMenuItem.Text = "&Base Character Classes...";
            this.baseCharacterClassesToolStripMenuItem.Click += new System.EventHandler(this.baseCharacterClassesToolStripMenuItem_Click);
            // 
            // occupationsToolStripMenuItem
            // 
            this.occupationsToolStripMenuItem.Name = "occupationsToolStripMenuItem";
            this.occupationsToolStripMenuItem.Size = new System.Drawing.Size(242, 22);
            this.occupationsToolStripMenuItem.Text = "&Occupations && specializations...";
            // 
            // wordToolStripMenuItem
            // 
            this.wordToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.gameLanguagesToolStripMenuItem,
            this.nationsToolStripMenuItem});
            this.wordToolStripMenuItem.Name = "wordToolStripMenuItem";
            this.wordToolStripMenuItem.Size = new System.Drawing.Size(96, 20);
            this.wordToolStripMenuItem.Text = "&Modern World";
            // 
            // gameLanguagesToolStripMenuItem
            // 
            this.gameLanguagesToolStripMenuItem.Name = "gameLanguagesToolStripMenuItem";
            this.gameLanguagesToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            this.gameLanguagesToolStripMenuItem.Text = "Game &Languages...";
            this.gameLanguagesToolStripMenuItem.Click += new System.EventHandler(this.gameLanguagesToolStripMenuItem_Click);
            // 
            // nationsToolStripMenuItem
            // 
            this.nationsToolStripMenuItem.Name = "nationsToolStripMenuItem";
            this.nationsToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
            this.nationsToolStripMenuItem.Text = "&Nations...";
            this.nationsToolStripMenuItem.Click += new System.EventHandler(this.nationsToolStripMenuItem_Click);
            // 
            // futureWordToolStripMenuItem
            // 
            this.futureWordToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.gameLanguagesToolStripMenuItem1,
            this.nationsToolStripMenuItem1});
            this.futureWordToolStripMenuItem.Name = "futureWordToolStripMenuItem";
            this.futureWordToolStripMenuItem.Size = new System.Drawing.Size(88, 20);
            this.futureWordToolStripMenuItem.Text = "F&uture World";
            // 
            // gameLanguagesToolStripMenuItem1
            // 
            this.gameLanguagesToolStripMenuItem1.Name = "gameLanguagesToolStripMenuItem1";
            this.gameLanguagesToolStripMenuItem1.Size = new System.Drawing.Size(174, 22);
            this.gameLanguagesToolStripMenuItem1.Text = "Game &Languages...";
            this.gameLanguagesToolStripMenuItem1.Click += new System.EventHandler(this.gameLanguagesToolStripMenuItem1_Click);
            // 
            // nationsToolStripMenuItem1
            // 
            this.nationsToolStripMenuItem1.Name = "nationsToolStripMenuItem1";
            this.nationsToolStripMenuItem1.Size = new System.Drawing.Size(174, 22);
            this.nationsToolStripMenuItem1.Text = "&Nations...";
            this.nationsToolStripMenuItem1.Click += new System.EventHandler(this.nationsToolStripMenuItem1_Click);
            // 
            // FormMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1092, 593);
            this.Controls.Add(this.menuStrip1);
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "FormMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "D20 Data Editor";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem coreToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem skillsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem featsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem baseCharacterClassesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem occupationsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem wordToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem gameLanguagesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem nationsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem futureWordToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem gameLanguagesToolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem nationsToolStripMenuItem1;
    }
}

