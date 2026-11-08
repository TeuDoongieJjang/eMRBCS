namespace eMRBCS.Services
{
    public enum AlignMethod { Hardware, Software }

    public static class PrinterHardware
    {
        /// <summary>0 = automatic. Set a number only to force a head width.</summary>
        public static int HeadWidthDots { get; set; } = 0;

        /// <summary>Used when the Line Space has no alignment digit (e.g. "48"). 1 = left, 2 = center, 3 = right.</summary>
        public static int DefaultAlignment { get; set; } = 2;

        /// <summary>
        /// Software (default) = leading spaces for text, blank dots for logo/barcode/QR. Works on every printer.
        /// Hardware = GS L / GS W margins. Only for printers that honour them for text AND images.
        /// </summary>
        public static AlignMethod AlignBy { get; set; } = AlignMethod.Software;

        /// <summary>
        /// true = every print starts with GS L 0 / GS W (full head) on 3-inch heads, so margins left in the printer
        /// by an earlier print can't shift a 48 or 32 print. Set false only if a printer prints stray "L" / "W".
        /// </summary>
        public static bool ResetMargins { get; set; } = true;

        public const bool RasterIgnoresMargin = true;
    }

    /// <summary>The one P6824BT behaviour set. Tuned automatically from the Bluetooth device name.</summary>
    public sealed class PrinterCaps
    {
        public string Name { get; init; } = "P6824BT";

        // text encoding
        public int CodePage { get; init; } = 850;          // .NET code page
        public byte CodeTableId { get; init; } = 2;        // ESC t n (0=CP437, 2=CP850, 16=WPC1252, 19=CP858)
        public bool AsciiOnly { get; init; } = false;      // true: Ñ -> N (last resort)
        public bool CancelKanji { get; init; } = false;    // FS . (Chinese-mode printers)

        // text sending
        public int SendChunkSize { get; init; } = 512;
        public int SendChunkDelayMs { get; init; } = 4;
        public int TailDelayMs { get; init; } = 400;

        // barcode / QR / underline raster
        public int RasterSliceRows { get; init; } = 48;
        public int RasterChunkSize { get; init; } = 192;
        public int RasterChunkDelayMs { get; init; } = 8;

        // logo
        public bool LogoBitImage { get; init; } = false;
        public int LogoSliceRows { get; init; } = 24;
        public int LogoChunkSize { get; init; } = 192;
        public int LogoChunkDelayMs { get; init; } = 8;
        public int LogoStripPauseMs { get; init; } = 0;

        /// <summary>Add new models here, one line each. Unknown names keep the safe defaults.</summary>
        public static PrinterCaps ForDevice(string deviceName)
        {
            string n = (deviceName ?? "").ToUpperInvariant();
            bool fast = n.Contains("MPD31");                       // Honeywell MPD31D
            bool chinese = n.Contains("MHT") || n.Contains("P29"); // Milestone MHT-P29L

            return new PrinterCaps
            {
                CancelKanji = chinese,

                SendChunkSize = fast ? 2048 : 512,
                SendChunkDelayMs = fast ? 0 : 4,
                TailDelayMs = fast ? 150 : 400,

                RasterSliceRows = fast ? 0 : 48,
                RasterChunkSize = fast ? 0 : 192,
                RasterChunkDelayMs = fast ? -1 : 8,

                LogoBitImage = fast,
                LogoSliceRows = 24,
                LogoChunkSize = fast ? 256 : 192,
                LogoChunkDelayMs = fast ? 10 : 8,
                LogoStripPauseMs = fast ? 3 : 0
            };
        }
    }

    public sealed class PaperProfile
    {
        public string Name { get; init; }
        public int WidthDots { get; init; }
        public int OffsetDots { get; init; }
        public int HeadDots { get; init; }
        public int ColsA { get; init; }
        public int ColsB { get; init; }
        public int Alignment { get; init; }

        /// <summary>32/48 = columns only, 321/322/323/481... = columns + alignment digit (1 L, 2 C, 3 R).</summary>
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
            return Build(cols, align, raw >= 100);
        }

        public static PaperProfile FromColumns(int cols) => Build(cols, PrinterHardware.DefaultAlignment, false);

        private static PaperProfile Build(int cols, int align, bool explicitAlign)
        {
            // 321/322/323 = 2-inch paper in a 3-inch (576) printer. "32" alone = 2-inch printer (384). 46/48 = 576.
            int head = PrinterHardware.HeadWidthDots > 0
                ? PrinterHardware.HeadWidthDots
                : (explicitAlign ? 576 : (cols <= 34 ? 384 : 576));

            int width = Math.Min(cols * 12, head) / 8 * 8;
            int free = head - width;
            int offset = align switch { 2 => free / 2, 3 => free, _ => 0 };

            return new PaperProfile
            {
                Name = cols + " cols",
                WidthDots = width,
                OffsetDots = offset,
                HeadDots = head,
                ColsA = width / 12,
                ColsB = width / 9,
                Alignment = align
            };
        }
    }
}