using System;
using LiteDB;

namespace ObscureWare.ModernD20.Resources
{
    /// <summary>
    /// Class to store various versions informations in DB
    /// </summary>
    public class VersionInfo
    {
        /// <summary>
        ///  serialization
        /// </summary>
        public VersionInfo()
        {
            
        }

        public VersionInfo(string target, string version)
        {
            this.Version = version;
            this.Target = target;
        }

        [BsonId]
        public string Target { get; set; }


        public string Version { get; set; }
    }
}