using System;
using System.Collections.Generic;
using ObscureWare.D20Common;

namespace ObscureWare.ModernD20.CharacterCreation
{
    public interface INationProvider
    {
        Guid Id { get; }

        string GetRandomName(Gender gender, IRoller roller);

        string GetRandomSurname(Gender gender, IRoller roller);

        Guid GetNationMainLanguage();

        IEnumerable<Guid> GetNationOfficialLanguages();
    }
}
