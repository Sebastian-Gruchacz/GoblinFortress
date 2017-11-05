using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;

namespace ObscureWare.Console
{
    public class ConsoleColorsHelper
    {
        private const int STD_OUTPUT_HANDLE = -11;                                // per WinBase.h
        internal readonly IntPtr InvalidHandleValue = new IntPtr(-1);    // per WinBase.h
        private readonly IntPtr _hConsoleOutput;

        private readonly Dictionary<Color, ConsoleColor> _knownMappings = new Dictionary<Color, ConsoleColor>();
        private KeyValuePair<ConsoleColor, Color>[] _colorBuffer;

        const float COLOR_WEIGHT_HUE = 47.5f;
        const float COLOR_WEIGHT_SATURATION = 28.75f;
        const float COLOR_WEIGHT_BRIGHTNESS = 23.75f;

        const float COLOR_WEIGHT_RED = 28.5f;
        const float COLOR_WEIGHT_GREEN = 47.75f;
        const float COLOR_WEIGHT_BLUE = 23.75f;

        const float COLOR_PROPORTION = 100f/255f;

        public ConsoleColorsHelper()
        {
            this._hConsoleOutput = GetStdHandle(STD_OUTPUT_HANDLE); // 7
            if (this._hConsoleOutput == this.InvalidHandleValue)
            {
                throw new SystemException("GetStdHandle->WinError: #" + Marshal.GetLastWin32Error());
            }

            this._colorBuffer = this.GetCurrentColorset();
        }

        /// <summary>
        /// Tries to find the closest match for given RGB color among current set of colors used by System.Console
        /// </summary>
        /// <param name="color"></param>
        /// <returns></returns>
        /// <remarks>Influenced by http://stackoverflow.com/questions/1720528/what-is-the-best-algorithm-for-finding-the-closest-color-in-an-array-to-another</remarks>
        public ConsoleColor FindClosestColor(Color color)
        {
            // TODO: make thread safe?

            ConsoleColor cc;
            if (this._knownMappings.TryGetValue(color, out cc))
            {
                return cc;
            }

            cc = Enumerable.OrderBy(this._colorBuffer, kp => this.ColorMatching(color, kp.Value)).First().Key;
            this._knownMappings.Add(color, cc);
            return cc;
        }

        private float ColorMatching(Color srcColor, Color destColor)
        {
            var sh = srcColor.GetHue();
            var ss = srcColor.GetSaturation();
            var sb = srcColor.GetBrightness();
            var dh = destColor.GetHue();
            var ds = destColor.GetSaturation();
            var db = destColor.GetBrightness();

            var sr = srcColor.R;
            var sg = srcColor.G;
            var sc = srcColor.B;
            var dr = destColor.R;
            var dg = destColor.G;
            var dc = destColor.B;

            float result = (float) Math.Sqrt(
                Math.Abs(sh - dh)/(COLOR_WEIGHT_HUE)*COLOR_PROPORTION +
                Math.Abs(ss - ds)/(COLOR_WEIGHT_SATURATION)*COLOR_PROPORTION +
                Math.Abs(sb - db)/(COLOR_WEIGHT_BRIGHTNESS)*COLOR_PROPORTION +
                Math.Abs(sr - dr)/(COLOR_WEIGHT_RED)*COLOR_PROPORTION +
                Math.Abs(sg - dg)/(COLOR_WEIGHT_GREEN)*COLOR_PROPORTION +
                Math.Abs(sc - dc)/(COLOR_WEIGHT_BLUE)*COLOR_PROPORTION);

            return result;
        }

        public void ReplaceConsoleColors(params Tuple<ConsoleColor, Color>[] mappings)
        {
            var csbe = this.GetConsoleScreenBufferInfoEx();

            foreach (var mapping in mappings)
            {
                SetNewColorDefinition(ref csbe, mapping.Item1, mapping.Item2);
            }

            // strange, needs to be done because window is shrunken somehow
            ++csbe.srWindow.Bottom;
            ++csbe.srWindow.Right;

            bool brc = SetConsoleScreenBufferInfoEx(this._hConsoleOutput, ref csbe);
            if (!brc)
            {
                throw new SystemException("SetConsoleScreenBufferInfoEx->WinError: #" + Marshal.GetLastWin32Error());
            }

            this.ResetColorCache();
        }

        public void ReplaceConsoleColor(ConsoleColor color, Color rgbColor)
        {
            var csbe = this.GetConsoleScreenBufferInfoEx();

            SetNewColorDefinition(ref csbe, color, rgbColor);

            // strange, needs to be done because window is shrunken somehow
            ++csbe.srWindow.Bottom;
            ++csbe.srWindow.Right;

            bool brc = SetConsoleScreenBufferInfoEx(this._hConsoleOutput, ref csbe);
            if (!brc)
            {
                throw new SystemException("SetConsoleScreenBufferInfoEx->WinError: #" + Marshal.GetLastWin32Error());
            }

            this.ResetColorCache();
        }

        private void ResetColorCache()
        {
            // remove cache, new mappings are required
            this._colorBuffer = this.GetCurrentColorset();
            this._knownMappings.Clear();
        }

        private CONSOLE_SCREEN_BUFFER_INFO_EX GetConsoleScreenBufferInfoEx()
        {
            CONSOLE_SCREEN_BUFFER_INFO_EX csbe = new CONSOLE_SCREEN_BUFFER_INFO_EX();
            csbe.cbSize = (int)Marshal.SizeOf(csbe); // 96 = 0x60

            bool brc = GetConsoleScreenBufferInfoEx(this._hConsoleOutput, ref csbe);
            if (!brc)
            {
                throw new SystemException("GetConsoleScreenBufferInfoEx->WinError: #" + Marshal.GetLastWin32Error());
            }
            return csbe;
        }

        private static void SetNewColorDefinition(ref CONSOLE_SCREEN_BUFFER_INFO_EX csbe, ConsoleColor color, Color rgbColor)
        {
            var r = rgbColor.R;
            var g = rgbColor.G;
            var b = rgbColor.B;

            switch (color)
            {
                case ConsoleColor.Black:
                    csbe.black = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.DarkBlue:
                    csbe.darkBlue = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.DarkGreen:
                    csbe.darkGreen = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.DarkCyan:
                    csbe.darkCyan = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.DarkRed:
                    csbe.darkRed = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.DarkMagenta:
                    csbe.darkMagenta = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.DarkYellow:
                    csbe.darkYellow = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.Gray:
                    csbe.gray = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.DarkGray:
                    csbe.darkGray = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.Blue:
                    csbe.blue = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.Green:
                    csbe.green = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.Cyan:
                    csbe.cyan = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.Red:
                    csbe.red = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.Magenta:
                    csbe.magenta = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.Yellow:
                    csbe.yellow = new COLORREF(r, g, b);
                    break;
                case ConsoleColor.White:
                    csbe.white = new COLORREF(r, g, b);
                    break;
            }
        }

        private unsafe KeyValuePair<ConsoleColor, Color>[] GetCurrentColorset()
        {
            var csbe = this.GetConsoleScreenBufferInfoEx();

            return new[]
            {
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.Black, csbe.black.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.DarkBlue, csbe.darkBlue.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.DarkGreen, csbe.darkGreen.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.DarkCyan, csbe.darkCyan.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.DarkRed, csbe.darkRed.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.DarkMagenta, csbe.darkMagenta.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.DarkYellow, csbe.darkYellow.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.Gray, csbe.gray.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.DarkGray, csbe.darkGray.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.Blue, csbe.blue.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.Green, csbe.green.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.Cyan, csbe.cyan.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.Red, csbe.red.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.Magenta, csbe.magenta.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.Yellow, csbe.yellow.GetColor()),
                new KeyValuePair<ConsoleColor, Color>(ConsoleColor.White, csbe.white.GetColor()),
            };
        }

        #region PInvoke

        [StructLayout(LayoutKind.Sequential)]
        internal struct COORD
        {
            internal short X;
            internal short Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SMALL_RECT
        {
            internal short Left;
            internal short Top;
            internal short Right;
            internal short Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct COLORREF
        {
            internal uint ColorDWORD;

            internal COLORREF(Color color)
            {
                this.ColorDWORD = (uint)color.R + (((uint)color.G) << 8) + (((uint)color.B) << 16);
            }

            internal COLORREF(uint r, uint g, uint b)
            {
                this.ColorDWORD = r + (g << 8) + (b << 16);
            }

            internal Color GetColor()
            {
                return Color.FromArgb((int)(0x000000FFU & this.ColorDWORD),
                    (int)(0x0000FF00U & this.ColorDWORD) >> 8, (int)(0x00FF0000U & this.ColorDWORD) >> 16);
            }

            internal void SetColor(Color color)
            {
                this.ColorDWORD = (uint)color.R + (((uint)color.G) << 8) + (((uint)color.B) << 16);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CONSOLE_SCREEN_BUFFER_INFO_EX
        {
            internal int cbSize;
            internal COORD dwSize;
            internal COORD dwCursorPosition;
            internal ushort wAttributes;
            internal SMALL_RECT srWindow;
            internal COORD dwMaximumWindowSize;
            internal ushort wPopupAttributes;
            internal bool bFullscreenSupported;
            internal COLORREF black;
            internal COLORREF darkBlue;
            internal COLORREF darkGreen;
            internal COLORREF darkCyan;
            internal COLORREF darkRed;
            internal COLORREF darkMagenta;
            internal COLORREF darkYellow;
            internal COLORREF gray;
            internal COLORREF darkGray;
            internal COLORREF blue;
            internal COLORREF green;
            internal COLORREF cyan;
            internal COLORREF red;
            internal COLORREF magenta;
            internal COLORREF yellow;
            internal COLORREF white;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleScreenBufferInfoEx(IntPtr hConsoleOutput, ref CONSOLE_SCREEN_BUFFER_INFO_EX csbe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleScreenBufferInfoEx(IntPtr hConsoleOutput, ref CONSOLE_SCREEN_BUFFER_INFO_EX csbe);

        #endregion
    }
}