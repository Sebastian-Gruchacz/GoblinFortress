using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ObscureWare.Console
{
    public class ConsoleOperations
    {
        private readonly IConsole _console;

        public ConsoleOperations(IConsole console)
        {
            _console = console;
        }

        public bool WriteTextBox(Rectangle textArea, string text, FrameDefinition frameDef)
        {
            int boxWidth = textArea.Width;
            int boxHeight = textArea.Height;
            LimitBoxDimensions(textArea.X, textArea.Y, ref boxWidth, ref boxHeight);
            Debug.Assert(boxWidth >= 3);
            Debug.Assert(boxHeight >= 3);
            WriteTextBoxFrame(textArea.X, textArea.Y, boxWidth, boxHeight, frameDef);
            return WriteTextBox(textArea.X + 1, textArea.Y + 1, boxWidth - 2, boxHeight - 2, text, frameDef.TextColor);
        }

        private void WriteTextBoxFrame(int boxX, int boxY, int boxWidth, int boxHeight, FrameDefinition frameDef)
        {
            _console.SetColors(frameDef.FrameColor.ForeColor, frameDef.FrameColor.BgColor);
            _console.PositionCursor(boxX, boxY);
            _console.WriteText(frameDef.TopLeft);
            for (int i = 1; i < boxWidth - 1; i++)
            {
                _console.WriteText(frameDef.Top);
            }
            _console.WriteText(frameDef.TopRight);
            string body = frameDef.Left + new string(frameDef.BackgroundFiller, boxWidth - 2) + frameDef.Right;
            for (int j = 1; j < boxHeight - 1; j++)
            {
                _console.PositionCursor(boxX, boxY + j);
                _console.WriteText(body);
            }
            _console.PositionCursor(boxX, boxY + boxHeight - 1);
            _console.WriteText(frameDef.BottomLeft);
            for (int i = 1; i < boxWidth - 1; i++)
            {
                _console.WriteText(frameDef.Bottom);
            }
            _console.WriteText(frameDef.BottomRight);
        }

        public bool WriteTextBox(Rectangle textArea, string text, ConsoleFontColor colorDef)
        {
            return WriteTextBox(textArea.X, textArea.Y, textArea.Width, textArea.Height, text, colorDef);
        }

        public bool WriteTextBox(int x, int y, int boxWidth, int boxHeight, string text, ConsoleFontColor colorDef)
        {
            LimitBoxDimensions(x, y, ref boxWidth, ref boxHeight); // so do not have to check for this every line is drawn...
            _console.PositionCursor(x, y);
            _console.SetColors(colorDef.ForeColor, colorDef.BgColor);

            string[] lines = SplitText(text, boxWidth);
            int i;
            for (i = 0; i < lines.Length && i < boxHeight; ++i)
            {
                _console.PositionCursor(x, y + i);
                WriteJustified(lines[i], boxWidth);
            }

            return (i == lines.Length);
        }

        private void WriteJustified(string text, int boxWidth)
        {
            if (text.Length == boxWidth)
            {
                System.Console.Write(text);
            }
            else
            {
                string[] parts = text.Split(new string[] {@" ", @"\t"}, StringSplitOptions.RemoveEmptyEntries); // both split and clean
                if (parts.Length == 1)
                {
                    System.Console.Write(text); // we cannot do anything about one long word...
                }
                else
                {
                    int cleanedLength = parts.Select(s => s.Length).Sum() + parts.Length - 1;
                    int remainingBlanks = boxWidth - cleanedLength;
                    if (remainingBlanks > cleanedLength/2)
                    {
                        System.Console.Write(text); // text is way too short to expandf it, keep to th eleft
                    }
                    else
                    {
                        int longerSpacesCount = (int) Math.Floor((decimal) remainingBlanks/(parts.Length - 1));
                        if (longerSpacesCount > 1)
                        {
                            decimal remainingLowerSpacesJoins = remainingBlanks - (longerSpacesCount*(parts.Length - 1));
                            if (remainingLowerSpacesJoins > 0)
                            {
                                int longerQty = parts.Length - longerSpacesCount;
                                System.Console.Write(
                                    string.Join(new string(' ', longerSpacesCount), parts.Take(longerQty + 1)) +
                                    string.Join(new string(' ', longerSpacesCount - 1), parts.Skip(longerQty + 1)));
                            }
                            else
                            {
                                // all gaps equal
                                System.Console.Write(string.Join(new string(' ', longerSpacesCount), parts));
                            }
                        }
                        else
                        {
                            System.Console.Write(
                                string.Join(new string(' ', 2), parts.Take(remainingBlanks + 1)) +
                                string.Join(new string(' ', 1), parts.Skip(remainingBlanks + 1)));
                        }
                    }
                }
            }
        }

        private string[] SplitText(string text, int boxWidth)
        {
            // TODO: move it to some external toolset?
            // used this imperfect solution for now: http://stackoverflow.com/a/1678162
            // this will not work properly for long words
            // this is not able to properly break the words in the middle to optimiie space...

            int offset = 0;
            var lines = new List<string>();
            while (offset < text.Length)
            {
                int index = text.LastIndexOf(" ", Math.Min(text.Length, offset + boxWidth));
                string line = text.Substring(offset, (index - offset <= 0 ? text.Length : index) - offset);
                offset += line.Length + 1;
                lines.Add(line);
            }

            return lines.ToArray();
        }


        /// <summary>
        /// Limits box dimensions to window sizes
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        private void LimitBoxDimensions(int x, int y, ref int width, ref int height)
        {
            if (x + width > System.Console.WindowWidth)
            {
                width = System.Console.WindowWidth - x;
            }
            if (y + height > System.Console.WindowHeight)
            {
                height = System.Console.WindowHeight - y;
            }
        }
    }
}
