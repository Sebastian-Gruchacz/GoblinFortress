﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LiteDB;

namespace ObscureWare.ModernD20.Resources
{
    public interface INationResourceProvider
    {
        IEnumerable<string> GetStringTable(string tableName);
        NationInfoPOCO GetNationInfo(Guid id);
    }

    public class NationInfoPOCO
    {
        [BsonId]
        private Guid _id;

        private Guid _mainLanguageId;
        private List<Guid> _officialLanguages;
        private string _nationResourceName;

        /// <summary>
        /// Deserialization
        /// </summary>
        public NationInfoPOCO()
        {
            
        }

        public NationInfoPOCO(Guid id)
        {
            this.Id = id;
            this._officialLanguages = new List<Guid>();
        }

        public Guid MainLanguageId
        {
            get { return this._mainLanguageId; }
            set { this._mainLanguageId = value; }
        }

        public List<Guid> OfficialLanguages
        {
            get { return this._officialLanguages; }
            set { this._officialLanguages = value; }
        }

        public Guid Id
        {
            get { return this._id; }
            set { this._id = value; }
        }

        public string NationResourceName
        {
            get { return this._nationResourceName; }
            set { this._nationResourceName = value; }
        }
    }
}
