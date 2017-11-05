namespace ObscureWare.ModernD20.Engine
{
    public class GlobalState
    {
        private readonly ModernD20Library _modernD20Library;

        public GlobalState(ModernD20Library modernD20Library)
        {
            this._modernD20Library = modernD20Library;
        }
    }
}