using System.Drawing;

namespace ObscureWare.Console
{
    public class ConsoleFontColor
    {
        public ConsoleFontColor(Color foreColor, Color bgColor)
        {
            this.BgColor = bgColor;
            this.ForeColor = foreColor;
        }

        public Color ForeColor { get; private set; }

        public Color BgColor { get; private set; }
    }
}