namespace eMRBCS.Services
{
    public static class PrinterHardware
    {
        // Widest area the print head can print (576 = 3-inch capable, 384 = 2-inch only)
        public static int HeadWidthDots { get; set; } = 576;

        // Used when the Line Space has no alignment digit (e.g. "32" or "48")
        public static int DefaultAlignment { get; set; } = 1;   // 1 = left, 2 = center, 3 = right

        public const bool RasterIgnoresMargin = true;
    }

    public sealed class PaperProfile
    {
        public string Name { get; init; }
        public int WidthDots { get; init; }
        public int OffsetDots { get; init; }
        public int ColsA { get; init; }
        public int ColsB { get; init; }
        public int Alignment { get; init; }

        /// <summary>Splits the Line Space value: 32/48 = columns only, 321/482 = columns + alignment digit.</summary>
        public static void ParseLineSpace(int raw, out int cols, out int alignment)
        {
            cols = raw;
            alignment = PrinterHardware.DefaultAlignment;
            if (raw >= 100)
            {
                cols = raw / 10;
                int a = raw % 10;
                if (a >= 1 && a <= 3) alignment = a;
            }
            if (cols <= 0) cols = 32;
        }

        public static PaperProfile FromLineSpace(int raw)
        {
            ParseLineSpace(raw, out int cols, out int align);
            return Build(cols, align);
        }

        public static PaperProfile FromColumns(int cols) => Build(cols, PrinterHardware.DefaultAlignment);

        private static PaperProfile Build(int cols, int align)
        {
            int head = PrinterHardware.HeadWidthDots;
            int width = Math.Min(cols * 12, head) / 8 * 8;
            int free = head - width;
            int offset = align switch { 2 => free / 2, 3 => free, _ => 0 };

            return new PaperProfile
            {
                Name = cols + " cols",
                WidthDots = width,
                OffsetDots = offset,
                ColsA = width / 12,
                ColsB = width / 9,
                Alignment = align
            };
        }
    }
}
