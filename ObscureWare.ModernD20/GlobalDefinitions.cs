using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using ObscureWare.ModernD20.BaseCharacterClasses;
using ObscureWare.ModernD20.Builders;
using ObscureWare.ModernD20.Descriptors;
using ObscureWare.ModernD20.Resources;

namespace ObscureWare.ModernD20
{
    public class GlobalDefinitions
    {
        private readonly IModernDatabase _db;

        private readonly Dictionary<Guid, Skill> _skillDefinitions;
        private readonly Dictionary<Guid, BaseCharacterClass> _baseCharacterClassDefinitions;
        private readonly Dictionary<Guid, Feat> _featDefinitions;
        private readonly Dictionary<Guid, ClassFeature> _classFeatureDefinitions;

        private GlobalDefinitions(ICoreResourceManager resourceManager)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// New, and empty
        /// </summary>
        /// <param name="db"></param>
        public GlobalDefinitions(IModernDatabase db)
        {
            this._db = db;
            this._skillDefinitions = new Dictionary<Guid, Skill>();
            this._baseCharacterClassDefinitions = new Dictionary<Guid, BaseCharacterClass>();
            this._featDefinitions = new Dictionary<Guid, Feat>();
            this._classFeatureDefinitions = new Dictionary<Guid, ClassFeature>();
        }

        public Skill FindSkill(Guid id)
        {
            Skill skill;
            return this._skillDefinitions.TryGetValue(id, out skill) ? skill : null;
            // TODO: exception + stack trace?
        }

        public BaseCharacterClass FindBaseCharacterClass(Guid id)
        {
            BaseCharacterClass characterClass;
            return this._baseCharacterClassDefinitions.TryGetValue(id, out characterClass) ? characterClass : null;
        }

        public Feat FindFeat(Guid id)
        {
            Feat feat;
            return this._featDefinitions.TryGetValue(id, out feat) ? feat : null;
        }

        public ClassFeature FindClassFeature(Guid id)
        {
            ClassFeature classFeature;
            return this._classFeatureDefinitions.TryGetValue(id, out classFeature) ? classFeature : null;
        }

        #region Static

        public static GlobalDefinitions LoadFromResources(ICoreResourceManager resourceManager)
        {
            return new GlobalDefinitions(resourceManager);
        }

        private readonly XmlSerializer arrayOfBaseCharacterClassDefinitionSerializer = new XmlSerializer(typeof(BaseCharacterClassDefinition[]));
        

        public void SaveResources(CoreResourceManager resourceManager)
        {
            
        }

        #endregion

        private readonly List<Assembly> _assemblies = new List<Assembly>();

        public IEnumerable<Assembly> GetRegistredLibraryAssemblies()
        {
            return this._assemblies;
        }

        public void RegisterLibrary(Assembly assembly)
        {
            this._assemblies.Add(assembly);
        }

        public void LoadSystem(string languageIdentifier)
        {
            // TODO: ...
        }

        public Guid FindEffectId(Type type)
        {
            throw new NotImplementedException();
        }
    }

    public class CoreResourceManager : ICoreResourceManager
    {
        public Stream SaveResource(string basecharacterclassdefinitions)
        {
            throw new NotImplementedException();
        }
    }

    public interface ICoreResourceManager
    {
        Stream SaveResource(string basecharacterclassdefinitions);
    }
}