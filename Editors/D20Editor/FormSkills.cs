using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ObscureWare.Common;
using ObscureWare.ModernD20;
using ObscureWare.ModernD20.Descriptors;

namespace D20Editor
{
    public partial class FormSkills : Form
    {
        private readonly GlobalDefinitions _globalDefinitions;
        //private readonly List<Skill> _knownSkills = new List<Skill>();

        public FormSkills(GlobalDefinitions globalDefinitions)
        {
            _globalDefinitions = globalDefinitions;
            InitializeComponent();
        }

        private void LoadSkills(GlobalDefinitions globalDefinitions)
        {
           // _knownSkills.Clear();
            this.listBoxSkills.Items.Clear();

            try
            {
                this.listBoxSkills.SuspendLayout();

                var knownLibraryAssemblies = globalDefinitions.GetRegistredLibraryAssemblies();
                foreach (var asm in knownLibraryAssemblies)
                {
                    Type[] types = asm.GetTypes();
                    foreach (Type t in types.Where(t => !t.IsAbstract && t.IsDerrivedFrom(typeof(Skill))))
                    {
                        // find desc...
                        // find translation
                        var skillTuple = new SkillTuple(t, null);
                        var decor = new DisplayItem<SkillTuple>(skillTuple, skillTuple.Description);

                        this.listBoxSkills.Items.Add(decor);

                        //_knownSkills.Add();
                    }
                }


            }
            finally
            {
                this.listBoxSkills.ResumeLayout();
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.SaveChanges();
            this.Close();
        }

        private void SaveChanges()
        {
            
        }

        private void FormSkills_Load(object sender, EventArgs e)
        {
            LoadSkills(_globalDefinitions);
        }
    }



    internal class SkillTuple
    {
        private readonly Type _type;
        private readonly SkillDescriptor _descriptor;

        public SkillTuple(Type type, SkillDescriptor descriptor)
        {
            _type = type;
            _descriptor = descriptor ?? new SkillDescriptor();
            if (_descriptor.Id == Guid.Empty)
            {
                _descriptor.Id = Guid.NewGuid();
            }
        }

        public string Description
        {
            get { return _type.Name; }
        }

        public SkillDescriptor Descriptor
        {
            get { return _descriptor; }
        }

        public Type Type
        {
            get { return _type; }
        }
    }

    
}
