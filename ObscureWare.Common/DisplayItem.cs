namespace ObscureWare.Common
{
    public class DisplayItem<T>
    {
        private readonly T _skillTuple;
        private readonly string _description;

        public DisplayItem(T skillTuple, string description)
        {
            this._skillTuple = skillTuple;
            this._description = description;
        }

        public T Value
        {
            get { return this._skillTuple; }
        }

        public override string ToString()
        {
            return this._description;
        }
    }
}