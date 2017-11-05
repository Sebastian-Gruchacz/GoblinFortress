namespace ObscureWare.Console
{
    using System;
    using System.Drawing;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Windows.Forms;

    /// <summary>
    /// Wraps System.Console with IConsole interface methods
    /// </summary>
    public class SystemConsole : IConsole
    {
        private readonly ConsoleColorsHelper _consoleColorsHelper;
        private readonly Point _windowSize;

        /// <summary>
        /// In characters...
        /// </summary>
        public Point WindowSize
        {
            get { return this._windowSize; }
        }

        public SystemConsole(ConsoleColorsHelper helper)
        {
            Console.OutputEncoding = Encoding.Unicode;
            Console.InputEncoding = Encoding.Unicode;

            this.SetFullScreen();
            // SG: (-2) On Win8 the only working way to keep borders on the screen :(
            // (-1) required on Win10 though :(
            this._windowSize = new Point(Console.LargestWindowWidth - 2, Console.LargestWindowHeight - 1);

            this._consoleColorsHelper = helper ?? new ConsoleColorsHelper();

            // setting fullscreen
            Console.BufferWidth = this._windowSize.X;
            Console.WindowWidth = this._windowSize.X;
            Console.BufferHeight = this._windowSize.Y;
            Console.WindowHeight = this._windowSize.Y;
            Console.SetWindowPosition(0, 0);
        }

        private void SetFullScreen()
        {
            // http://www.codeproject.com/Articles/4426/Console-Enhancements
            this.SetWindowPosition(
                0,
                0,
                Screen.PrimaryScreen.WorkingArea.Width - (2 * 16),
                Screen.PrimaryScreen.WorkingArea.Height - (2 * 16) - SystemInformation.CaptionHeight);
        }

        public void WriteText(int x, int y, string text, Color foreColor, Color bgColor)
        {
            this.PositionCursor(x, y);
            this.SetColors(foreColor, bgColor);
            this.WriteText(text);
        }

        public void WriteText(string text)
        {
            Console.Write(text);
        }

        public void SetColors(Color foreColor, Color bgColor)
        {
            Console.ForegroundColor = this._consoleColorsHelper.FindClosestColor(foreColor);
            Console.BackgroundColor = this._consoleColorsHelper.FindClosestColor(bgColor);
        }

        public void Clear()
        {
            Console.Clear();
        }

        public void PositionCursor(int x, int y)
        {
            Console.SetCursorPosition(x, y);
        }

        public void WriteText(char character)
            {
            Console.Write(character);
        }

        public void ReplaceConsoleColor(ConsoleColor color, Color rgbColor)
        {
            this._consoleColorsHelper.ReplaceConsoleColor(color, rgbColor);
        }

        #region PInvoke

        [DllImport("user32")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, int flags);

        [DllImport("kernel32")]
        private static extern IntPtr GetConsoleWindow();

        private const int SWP_NOZORDER = 0x4;
        private const int SWP_NOACTIVATE = 0x10;

        /// <summary>
        /// Sets the console window location and size in pixels
        /// </summary>
        public void SetWindowPosition(int x, int y, int width, int height)
        {
            IntPtr hwnd = GetConsoleWindow();
            SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
            // no release handle?
        }

        #endregion PInvoke
    }
}