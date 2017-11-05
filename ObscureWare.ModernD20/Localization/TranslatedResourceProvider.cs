using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace ObscureWare.ModernD20.Localization
{
    public class TranslatedResourceProvider : ITranslatedResourceProvider
    {
        private readonly Dictionary<string, Dictionary<Guid, ILocalizedDescriptor>> _translations = new Dictionary<string, Dictionary<Guid, ILocalizedDescriptor>>();

        private readonly XmlSerializer _serializer = new XmlSerializer(typeof(LocalizedDescriptor[]));

        public void ApplyTranslationsFile(Stream str)
        {
            var newLabels = (LocalizedDescriptor[]) this._serializer.Deserialize(str);
            foreach (var localizedDescriptor in newLabels)
            {
                Dictionary<Guid, ILocalizedDescriptor> groupDict;
                if (!this._translations.TryGetValue(localizedDescriptor.TranslationGroupName, out groupDict))
                {
                    groupDict = new Dictionary<Guid, ILocalizedDescriptor>();
                    this._translations.Add(localizedDescriptor.TranslationGroupName, groupDict);
                }

                groupDict[localizedDescriptor.TranslationId] = localizedDescriptor; // will overwrite! Expected behavior.
            }
        }

        public ILocalizedDescriptor GetDescriptor(string translationsGroup, Guid id)
        {
            Dictionary<Guid, ILocalizedDescriptor> groupDict;
            if (!this._translations.TryGetValue(translationsGroup, out groupDict))
            {
                throw new InvalidOperationException("## TranGroup: " + translationsGroup + " ##");
            }

            ILocalizedDescriptor desc;
            if (!groupDict.TryGetValue(id, out desc))
            {
                throw new InvalidOperationException("## TranGroup: " + translationsGroup + ", id: " + id + " ##");
            }

            return desc;
        }
    }
}