using System;
using System.Collections.Generic;

namespace ObscureWare.D20Common
{
    public interface ILibrary
    {
        string LibraryName { get; }

        Version LibraryVersion { get; }

        IList<GameLanguage> GameWorldLanguages { get; }
    }
}
