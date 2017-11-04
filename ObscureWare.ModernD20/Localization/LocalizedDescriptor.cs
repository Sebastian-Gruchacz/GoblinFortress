using System;
using ObscureWare.ModernD20.Descriptors;

namespace ObscureWare.ModernD20.Localization
{
    [Serializable]
    public class LocalizedDescriptor : ILocalizedDescriptor
    {
        public string TranslationGroupName { get; set; }
        public Guid TranslationId { get; set; }
        public string Name { get; }
        public string Description { get; }
    }
}