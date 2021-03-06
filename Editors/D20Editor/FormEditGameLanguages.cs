﻿using System;
using System.Collections.Generic;
using System.Windows.Forms;
using ObscureWare.Common;
using ObscureWare.D20Common;
using ObscureWare.ModernD20.Resources;

namespace D20Editor
{
    public partial class FormEditGameLanguages : Form
    {
        private readonly ILibrary _lib;
        private readonly ICoreDatabaseEdit _databaseEdit;
        private readonly IList<GameLanguage>  _languages = new List<GameLanguage>();

        public FormEditGameLanguages(ILibrary lib, ICoreDatabaseEdit databaseEdit)
        {
            this._lib = lib;
            this._databaseEdit = databaseEdit;
            this.InitializeComponent();
        }

        private void FormEditGameLanguages_Load(object sender, EventArgs e)
        {
            this.listLanguages.SuspendLayout();
            this.listLanguages.Items.Clear();

            foreach (var gameLanguage in this._lib.GameWorldLanguages)
            {
                this.listLanguages.Items.Add(new DisplayItem<GameLanguage>(gameLanguage, gameLanguage.Name));
                this._languages.Add(gameLanguage);
            }

            this.listLanguages.ResumeLayout();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            this._databaseEdit.UpdateGameLanguages(this._lib.LibraryName, this._languages);
            this.DialogResult = DialogResult.OK;
        }

        private void button5_Click(object sender, EventArgs e)
        {
            var editForm = new FormAddNewItems();
            if (editForm.ShowDialog(this) == DialogResult.OK)
            {
                foreach (string item in editForm.Items)
                {
                    var gameLanguage = new GameLanguage { Id = Guid.NewGuid(), Name = item};
                    if (!this._languages.Contains(gameLanguage))
                    {
                        this.listLanguages.Items.Add(new DisplayItem<GameLanguage>(gameLanguage, gameLanguage.Name));
                        this._languages.Add(gameLanguage);
                    }
                }
            };
        }
    }

    
}
