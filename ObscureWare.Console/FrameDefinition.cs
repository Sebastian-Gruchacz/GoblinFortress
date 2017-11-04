namespace ObscureWare.Console
{
    public class FrameDefinition
    {
        internal enum FramePiece : byte
        {
            TopLeft = 0,
            Top,
            TopRight,
            Left,
            Right,
            BottomLeft,
            Bottom,
            BottomRight
        };

        private readonly char[] _frameChars;

        public FrameDefinition(ConsoleFontColor frameColor, ConsoleFontColor textColor, string frameChars, char backgroundFiller)
        {
            FrameColor = frameColor;
            TextColor = textColor;
            BackgroundFiller = backgroundFiller;
            _frameChars = frameChars.ToCharArray();
        }

        public ConsoleFontColor FrameColor { get; private set; }

        public ConsoleFontColor TextColor { get; private set; }

        public char BackgroundFiller { get; private set; }

        public char TopLeft { get { return _frameChars[(byte)FramePiece.TopLeft]; } }

        public char Top { get { return _frameChars[(byte)FramePiece.Top]; } }

        public char TopRight { get { return _frameChars[(byte)FramePiece.TopRight]; } }

        public char Left { get { return _frameChars[(byte)FramePiece.Left]; } }

        public char Right { get { return _frameChars[(byte)FramePiece.Right]; } }

        public char BottomLeft { get { return _frameChars[(byte)FramePiece.BottomLeft]; } }

        public char Bottom { get { return _frameChars[(byte)FramePiece.Bottom]; } }

        public char BottomRight { get { return _frameChars[(byte)FramePiece.BottomRight]; } }
    }
}