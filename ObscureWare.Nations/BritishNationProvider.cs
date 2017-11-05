using System;
using ObscureWare.ModernD20.Resources;

namespace ObscureWare.Nations
{
    public class BritishNationProvider : BaseNationProvider
    {
        private readonly Guid _id = new Guid(@"{A0117C84-5925-4E1B-943A-B0241A6016F7}");

        public override Guid Id
        {
            get { return this._id; }
        }

        public BritishNationProvider(INationResourceProvider resourceProvider) : base(resourceProvider)
        {
        }
    }
}