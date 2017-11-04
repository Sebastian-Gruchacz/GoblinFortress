using System;

namespace ObscureWare.ModernD20.Localization
{
    public interface ITranslatedResourceProvider
    {
        ILocalizedDescriptor GetDescriptor(string translationsGroup, Guid id);
    }
}