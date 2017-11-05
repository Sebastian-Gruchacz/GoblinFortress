using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ObscureWare.D20Common;
using ObscureWare.ModernD20;
using ObscureWare.ModernD20.Resources;

namespace D20Editor
{
    public partial class FormEditNations : Form
    {
        private readonly ILibrary _library;
        private readonly ICoreDatabaseEdit _dbEdit;

        public FormEditNations(ILibrary library, ICoreDatabaseEdit dbEdit)
        {
            this._library = library;
            this._dbEdit = dbEdit;
            this.InitializeComponent();
        }

        private void FormEditNations_Load(object sender, EventArgs e)
        {

        }
    }
}
