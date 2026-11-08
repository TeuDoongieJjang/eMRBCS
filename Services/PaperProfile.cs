namespace eMRBCS.Services
{
    public static class PrinterHardware
    {
        /// <summary>0 = automatic. Set a number only to force a head width.</summary>
        public static int HeadWidthDots { get; set; } = 0;

        // CHANGED: used when the Line Space has no alignment digit (e.g. "48"). 1 = left, 2 = center, 3 = right
        public static int DefaultAlignment { get; set; } = 2;

        public const bool RasterIgnoresMargin = true;
    }

    public sealed class PrinterCaps
    {
        public string Name { get; init; } = "Universal";

        public int CodePage { get; init; } = 850;
        public byte CodeTableId { get; init; } = 2;
        public bool AsciiOnly { get; init; } = false;
        public bool CancelKanji { get; init; } = false;

        public bool UseMargins { get; init; } = false;
        public bool AutoMargins { get; init; } = true;
        public int SendChunkSize { get; init; } = 512;
        public int SendChunkDelayMs { get; init; } = 4;
        public int TailDelayMs { get; init; } = 400;

        public int RasterSliceRows { get; init; } = 48;
        public int RasterChunkSize { get; init; } = 192;
        public int RasterChunkDelayMs { get; init; } = 8;

        public bool LogoBitImage { get; init; } = false;
        public int LogoSliceRows { get; init; } = 24;
        public int LogoChunkSize { get; init; } = 192;
        public int LogoChunkDelayMs { get; init; } = 8;
        public int LogoStripPauseMs { get; init; } = 0;

        /// <summary>NEW: true for P6824BT and every profile that is not explicitly MPD31D or MHT-P29L.</summary>
        public static bool IsAuto(string name)
        {
            string n = (name ?? "").Trim().ToUpperInvariant();
            return n != "MPD31D" && n != "MHT-P29L";
        }

        /// <summary>NEW: picks the settings from the Bluetooth device name. null = unknown printer (use Universal).
        /// Add new models here, one line each.</summary>
        public static PrinterCaps FromDeviceName(string deviceName)
        {
            string n = (deviceName ?? "").ToUpperInvariant();
            if (n.Contains("MPD31")) return ForProfile("MPD31D");
            if (n.Contains("MHT") || n.Contains("P29")) return ForProfile("MHT-P29L");
            return null;
        }

        public static PrinterCaps ForProfile(string name)
        {
            switch ((name ?? "").Trim().ToUpperInvariant())
            {
                case "MPD31D":
                    return new PrinterCaps
                    {
                        Name = "MPD31D",
                        CodePage = 850,
                        CodeTableId = 2,
                        UseMargins = true,
                        SendChunkSize = 2048,
                        SendChunkDelayMs = 0,
                        TailDelayMs = 150,
                        RasterSliceRows = 0,
                        RasterChunkSize = 0,
                        RasterChunkDelayMs = -1,
                        LogoBitImage = true,
                        LogoChunkSize = 256,
                        LogoChunkDelayMs = 10,
                        LogoStripPauseMs = 3
                    };

                case "MHT-P29L":
                    return new PrinterCaps { Name = "MHT-P29L", CancelKanji = true };

                default:
                    return new PrinterCaps();
            }
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
            return Build(cols, align, raw >= 100);      // CHANGED: remember if an alignment digit was typed
        }

        public static PaperProfile FromColumns(int cols) => Build(cols, PrinterHardware.DefaultAlignment, false);

        private static PaperProfile Build(int cols, int align, bool explicitAlign)
        {
            // CHANGED: 321/322/323 = 2-inch paper inside a 3-inch (576) printer.
            //          "32" alone = 2-inch printer (384). 48/46 = 576.
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