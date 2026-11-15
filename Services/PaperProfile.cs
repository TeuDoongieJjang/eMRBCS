namespace eMRBCS.Services
{
    /// <summary>Optional darkness commands. Off = send nothing (safe on every printer).</summary>
    public enum DensityMethod { Off = 0, Esc7HeatTime = 1, Dc2Hash = 2 }

    public static class PrinterHardware
    {
        public static int LeadMs { get; set; } = 600;            // was 1000
        public static int MaxAheadBytes { get; set; } = 2048;    // was 8192
        /// <summary>false (default) = on acknowledged BLE the ACK is the flow control, no extra pacing
        /// (continuous print). Set true only if a BLE printer prints garbage: it re-enables the window below.</summary>
        public static bool PaceAckedBle { get; set; } = false;

        /// <summary>Tiny pause between acknowledged BLE writes (same as the 2 ms that worked in the chat).</summary>
        public static int BleChunkDelayMs { get; set; } = 2;
        public static bool PreferSpp { get; set; } = false;
        /// <summary>true = remove fully-white rows at the TOP and BOTTOM of the logo (width is never cropped).
        /// Removes the blank space that would otherwise print after the logo.</summary>
        public static bool LogoTrimWhitespace { get; set; } = true;
        // ---------------- raster ----------------
        /// <summary>0 = the WHOLE image in ONE GS v 0 command (no band seams = no blank strips).
        /// Only set a number (e.g. 512) if your firmware refuses tall rasters.</summary>
        public static int RasterBandRows { get; set; } = 0;

        /// <summary>0 = no byte cap per command.</summary>
        public static int RasterBandMaxBytes { get; set; } = 0;

        /// <summary>Ignored (kept so other pages compile). Images are always positioned with blank padding bytes.</summary>
        public static bool RasterUseMargin { get; set; } = false;

        /// <summary>Ignored (kept so other pages compile). Raster is always sent 1:1 (mode 0).</summary>
        public static bool LogoVerticalDouble { get; set; } = false;

        /// <summary>0 = auto. Set to what the head probe told you (384 or 576).</summary>
        public static int HeadWidthDots
        {
            get => Preferences.Get("print_head_dots", 0);
            set => Preferences.Set("print_head_dots", value is 576 ? value : 0);
        }

        // ---------------- send pacing (keeps printing continuous) ----------------
        /// <summary>MUST stay true: it keeps the sender ~LeadMs ahead of the print engine.
        /// false = burst = printer buffer overflow = missing logo rows.</summary>
        public static bool PacingEnabled { get; set; } = true;

        /// <summary>Optional extra byte-rate cap. 0 = use the print-time model only (recommended).</summary>
        public static int MaxBytesPerSecond { get; set; } = 0;

        /// <summary>Bytes per Bluetooth write. Small = smooth.</summary>
        public static int SendChunkBytes { get; set; } = 512;



        /// <summary>Printer speed used by the pacing, in dot rows/s (8 rows = 1 mm). 600 ≈ 75 mm/s.
        /// Rows missing -> LOWER it. Visibly stopping/starting -> RAISE it.</summary>
        public static int PrintRowsPerSecond
        {
            get => Preferences.Get("print_rows_per_sec", 500);   // was 800
            set => Preferences.Set("print_rows_per_sec", Math.Clamp(value, 50, 2000));
        }

        /// <summary>Not used any more (no inter-band sleeps).</summary>
        public static int RasterBandGapMs { get; set; } = 0;


        // ---------------- reset / ASCII mode ----------------
        public static bool ExitKanjiMode { get; set; } = true;
        public static bool ExtendedReset { get; set; } = false;

        // ---------------- paper / head ----------------
        public static int DefaultAlignment { get; set; } = 2;
        public static bool ResetMargins { get; set; } = true;
        public static int BlankLineDots { get; set; } = 24;
        public static bool ForceBold { get; set; } = false;

        // ---------------- font metrics (dots) ----------------
        public static int FontADots { get; set; } = 12;
        public static int FontBDots { get; set; } = 9;

        // ---------------- logo quality ----------------
        public static int LogoThreshold { get; set; } = 170;
        public static bool LogoBolden { get; set; } = false;

        // ---------------- print density (persisted) ----------------
        public static DensityMethod DensityMethod
        {
            get => (DensityMethod)Preferences.Get("print_density_method", 0);
            set => Preferences.Set("print_density_method", (int)value);
        }

        public static int DensityLevel
        {
            get => Preferences.Get("print_density_level", 3);
            set => Preferences.Set("print_density_level", Math.Clamp(value, 0, 4));
        }
    }


    public sealed class PaperProfile
    {
        public string Name { get; init; } = "";
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
            if (raw == 32) raw = 321;
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

            int cellA = Math.Max(1, PrinterHardware.FontADots);
            int cellB = Math.Max(1, PrinterHardware.FontBDots);

            int width = Math.Min(cols * cellA, head) / 8 * 8;
            int free = head - width;
            int offset = align switch { 2 => free / 2, 3 => free, _ => 0 };

            return new PaperProfile
            {
                Name = cols + " cols",
                WidthDots = width,
                OffsetDots = offset,
                HeadDots = head,
                ColsA = width / cellA,
                ColsB = width / cellB,
                Alignment = align
            };
        }
    }
}