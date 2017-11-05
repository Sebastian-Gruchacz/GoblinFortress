namespace ObscureWare.D20Common
{
    public class DieRollDescriptor
    {
        private readonly int _dieCount;
        private readonly DieEnum _d8;

        public DieRollDescriptor(int dieCount, DieEnum d8)
        {
            this._dieCount = dieCount;
            this._d8 = d8;
        }

        public DieRollDescriptor(string hitDieDescription)
        {
            // TODO: parsing

            throw new System.NotImplementedException();
        }

        public int GetMax()
        {
            throw new System.NotImplementedException();
        }
    }
}