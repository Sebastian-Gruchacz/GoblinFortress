using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace D20Editor
{
    public partial class FormAddNewItems : Form
    {
        private string[] _items;
        private SelectionMode _selectionMode;

        public FormAddNewItems()
        {
            this.InitializeComponent();
        }

        private void button5_Click(object sender, EventArgs e)
        {
            this.SelectionMode = SelectionMode.One;
            this.Items = this.textBox1.Lines.Where(line => !string.IsNullOrWhiteSpace(line)).Take(1).ToArray();
            this.DialogResult = DialogResult.OK;
        }

        public SelectionMode SelectionMode
        {
            get { return this._selectionMode; }
            set { this._selectionMode = value; }
        }

        public string[] Items
        {
            get { return this._items; }
            private set { this._items = value; }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            this.SelectionMode = SelectionMode.MultiSimple;
            this.Items = this.textBox1.Lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            this.DialogResult = DialogResult.OK;
        }
    }
}
