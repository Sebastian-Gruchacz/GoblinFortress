using System;

namespace ObscureWare.D20Common
{
    public class GameLanguage : IEquatable<GameLanguage>
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public bool Equals(GameLanguage other)
        {
            return (this.Id == other.Id) || (this.Name == other.Name); // both unique keys
        }
    }
}