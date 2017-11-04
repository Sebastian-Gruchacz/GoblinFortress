using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using ObscureWare.D20Common;
using ObscureWare.ModernD20;
using ObscureWare.ModernD20.BaseCharacterClasses;
using ObscureWare.ModernD20.Descriptors;

namespace D20SerializationTests
{
    class Program
    {
        static readonly XmlSerializer arrayOfBaseCharacterClassDefinitionSerializer = new XmlSerializer(typeof(BaseCharacterClassDefinition[]));

        static void Main(string[] args)
        {
            ICoreResourceManager resourceManager = new ConsoleResourceManager();

            SerializeBaseCharacterClassDefinition(resourceManager);

            Console.WriteLine();
            Console.WriteLine("Press ENTER");
            Console.ReadLine();
        }

        private static void SerializeBaseCharacterClassDefinition(ICoreResourceManager resourceManager)
        {
            using (Stream sr = resourceManager.SaveResource("BaseCharacterClassDefinitions"))
            {
                BaseCharacterClassDefinition def = new BaseCharacterClassDefinition
                {
                    Id = new Guid("{77540CFC-B23A-4A61-AAD0-A3FDB4F9AC5B}"),
                    GoverningAbility = AbilityEnum.Strength,
                    ClassSkills = new List<Guid>(new Guid[]
                    {
                        new Guid("{04A56EAD-4734-4CBD-97E5-2598B7AB2C5B}"),
                        new Guid("{B1B5CADB-BE0C-46DD-8186-2D419F804A80}"),
                    }),
                    BaseCharacterClassTableRows = new List<BaseCharacterClassTableRow>(new BaseCharacterClassTableRow[]
                    {
                        new BaseCharacterClassTableRow
                        {
                            BaseAttackBonus = 1,
                            ClassFeatures = BaseClassFeatureSelectEnum.ClassFeature,
                            ClassLevel = 1,
                            DefenseBonus = 5,
                            FortitudeSave = 5,
                            ReflexSave = 3,
                            ReputationBonus = 3,
                            WillSave = 3
                        },
                    }),
                    HitDieDescription = "1d8",
                    ImplementationClassName = typeof (StrongHeroBaseClass).FullName
                };

                arrayOfBaseCharacterClassDefinitionSerializer.Serialize(sr, new BaseCharacterClassDefinition[]
                {
                    def
                });
            }
        }
    }

    internal class ConsoleResourceManager : ICoreResourceManager
    {
        public Stream SaveResource(string basecharacterclassdefinitions)
        {
            return Console.OpenStandardOutput();
        }
    }
}
