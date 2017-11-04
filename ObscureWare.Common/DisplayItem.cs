namespace ObscureWare.Common
{
    public class DisplayItem<T>
    {
        private readonly T _skillTuple;
        private readonly string _description;

        public DisplayItem(T skillTuple, string description)
        {
            _skillTuple = skillTuple;
            _description = description;
        }

        public T Value
        {
            get { return _skillTuple; }
        }

        public override string ToString()
        {
            return _description;
        }
    }
}