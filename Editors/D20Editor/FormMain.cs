using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition.Hosting;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ObscureWare.FutureD20;
using ObscureWare.FutureD20.Resources;
using ObscureWare.ModernD20;
using ObscureWare.ModernD20.Engine;
using ObscureWare.ModernD20.Resources;

namespace D20Editor
{
    public partial class FormMain : Form
    {
        private ModernD20Library _modernLib;
        private FutureD20Library _futureLib;
        private ICoreNotifications _globalNotifier;
        private AggregateCatalog _catalog;
        private ModernDbConnect _modernDB;
        private FutureDbConnect _futureDB;

        public FormMain()
        {
            _catalog = new AggregateCatalog();
            //Adds all the parts found in the same assembly as the Program class
            _catalog.Catalogs.Add(new AssemblyCatalog(typeof(Program).Assembly));

            InitializeComponent();
            CreateLibraries();
        }

        private void CreateLibraries()
        {
            string workingDbPath = Path.GetFullPath(@"..\..\..\..\CommonContent\moderngame.db"); // TODO: separate DB per library?
            string futureDbPath = Path.GetFullPath(@"..\..\..\..\CommonContent\futuregame.db"); // TODO: separate DB per library?

            _modernDB = new ModernDbConnect(workingDbPath);
            _futureDB = new FutureDbConnect(futureDbPath);

            // TODO: version upgrading code is something else

            _globalNotifier = new DesignTimeNotifier(this);
            _modernLib = new ModernD20Library(_globalNotifier, _modernDB);
            _futureLib = new FutureD20Library(_modernLib, _futureDB);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if (!DbVersionAreCurrent())
            {
                Application.Exit();
                return;
            }


            // ...
            _modernLib.GlobalDefinitions.LoadSystem("EN");
        }

        private bool DbVersionAreCurrent()
        {
            var modernVer = _modernLib.GetDbVersion();
            var futureVer = _futureLib.GetDbVersion();

            if (modernVer != _modernLib.LibraryVersion)
            {
                if (MessageBox.Show(this, "Modern db version not current. Upgrade?", "DB Version Mismatch",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) ==
                    DialogResult.Yes)
                {
                    return _modernLib.UpgradeDB();
                }
                else
                {
                    return false;
                }
            }
            if (futureVer != _futureLib.LibraryVersion)
            {
                if (MessageBox.Show(this, "Modern db version not current. Upgrade?", "DB Version Mismatch",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) ==
                    DialogResult.Yes)
                {
                    return _futureLib.UpgradeDB();
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        private void baseCharacterClassesToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void skillsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new FormSkills(_modernLib.GlobalDefinitions).ShowDialog(this);
        }

        

        private void gameLanguagesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new FormEditGameLanguages(_modernLib, _modernDB).ShowDialog(this);
        }

        private void nationsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new FormEditNations(_modernLib, _modernDB).ShowDialog(this);
        }

        private void gameLanguagesToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            new FormEditGameLanguages(_futureLib, _futureDB).ShowDialog(this);
        }

        private void nationsToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            new FormEditNations(_futureLib, _futureDB).ShowDialog(this);
        }
    }
}
