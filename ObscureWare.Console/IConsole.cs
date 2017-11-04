using System.Drawing;

namespace ObscureWare.Console
{
    public interface IConsole
    {
        /// <summary>
        /// Writes specific text at given position, and using given colors
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="text"></param>
        /// <param name="foreColor"></param>
        /// <param name="bgColor"></param>
        void WriteText(int x, int y, string text, Color foreColor, Color bgColor);

        /// <summary>
        /// Clears entire visible console area (window)
        /// </summary>
        void Clear();


        void WriteText(string text);

        void SetColors(Color foreColor, Color bgColor);
 
        void PositionCursor(int x, int y);

        void WriteText(char character);
    }
}
