namespace eMRBCS.Services
{
    /// <summary>Optional darkness commands. Off = send nothing (safe on every printer).</summary>
    public enum DensityMethod { Off = 0, Esc7HeatTime = 1, Dc2Hash = 2 }

    public static class PrinterHardware
    {
        /// <summary>true = crop the logo's white border BEFORE scaling, so the artwork itself fills the paper width.</summary>
        public static bool LogoTrimWhitespace { get; set; } = true;
        // 0 = auto. SET THIS to what Step 1 told you (384 or 576).
        public static int HeadWidthDots
        {
            get => Preferences.Get("print_head_dots", 0);
            set => Preferences.Set("print_head_dots", value is 576 ? value : 0);
        }

        /// <summary>false = burst. RFCOMM blocking writes are the real flow control.</summary>
        public static bool PacingEnabled { get; set; } = false;

        /// <summary>Only used when PacingEnabled = true. 0 = unlimited. Simple byte rate, not print speed.</summary>
        public static int MaxBytesPerSecond { get; set; } = 0;

        public static int SendChunkBytes { get; set; } = 512;
        public static int LeadMs { get; set; } = 400;
        public static int MaxAheadBytes { get; set; } = 8192;

        // ---- raster ----
        public static int RasterBandRows { get; set; } = 24;
        public static int RasterBandMaxBytes { get; set; } = 1024;
        /// <summary>Position images with GS L instead of blank padding bytes (less data).</summary>
        public static bool RasterUseMargin { get; set; } = true;
        /// <summary>Send half the logo rows, printer prints each twice (GS v 0 mode 2). Halves the byte rate.</summary>
        public static bool LogoVerticalDouble { get; set; } = false;
        /// <summary>Extra print-time cost per band (ms) for firmware turnaround. Not a sleep.</summary>
        public static int RasterBandGapMs { get; set; } = 3;
        // ---------------- send pacing (keeps printing continuous) ----------------

        /// <summary>Printer speed assumed by the pacing, in dot rows per second (8 rows = 1 mm). 350 = about 44 mm/s.
        /// Printing garbled / rows missing  -> LOWER it. Printer visibly stopping and starting -> RAISE it.</summary>
        public static int PrintRowsPerSecond
        {
            get => Preferences.Get("print_rows_per_sec", 600);
            set => Preferences.Set("print_rows_per_sec", Math.Clamp(value, 50, 2000));
        }

        /// <summary>Bytes per Bluetooth write. Small = smooth.</summary>

        /// true = Bluetooth Classic (SPP) is tried first, even on dual-mode printers. SPP is far faster than BLE,
        /// which is what keeps the logo from having blank lines. BLE is used only if SPP fails (or the printer is BLE-only).
        public static bool PreferSpp { get; set; } = true;
        // ---------------- reset / ASCII mode ----------------
        /// <summary>
        /// true = send FS . (0x1C 0x2E) after the reset to leave Chinese / GBK (Kanji) mode.
        /// If a printer does not know FS it may print a stray '.', then set this to false.
        /// </summary>
        public static bool ExitKanjiMode { get; set; } = true;

        /// <summary>
        /// true = also send ESC R 0, ESC { 0, ESC V 0, GS B 0, ESC G 0, ESC SP 0 in the reset.
        /// ESC @ already resets them, and printers that do not know these commands print a stray letter,
        /// so this is off by default.
        /// </summary>
        public static bool ExtendedReset { get; set; } = false;

        // ---------------- paper / head ----------------
        /// <summary>0 = automatic (384 for 2-inch, 576 for 3-inch). Set a number only to force a head width.</summary>

        /// <summary>Used when the Line Space has no alignment digit (e.g. "48"). 1 = left, 2 = center, 3 = right.</summary>
        public static int DefaultAlignment { get; set; } = 2;

        /// <summary>true = every print on a 3-inch head starts with GS L 0 / GS W (full head).</summary>
        public static bool ResetMargins { get; set; } = true;

        /// <summary>Height of a blank line made by PrintSeparatorAsync(' ') (dots).</summary>
        public static int BlankLineDots { get; set; } = 24;

        /// <summary>true = all normal text is printed emphasised (darker). Helps faded printers.</summary>
        public static bool ForceBold { get; set; } = false;

        // ---------------- font metrics (dots) ----------------
        public static int FontADots { get; set; } = 12;
        public static int FontBDots { get; set; } = 9;

        // ---------------- logo quality ----------------
        /// <summary>Luminance below this becomes black. Higher = darker logo.</summary>
        public static int LogoThreshold { get; set; } = 170;

        /// <summary>true = thicken every black pixel by 1 dot horizontally.</summary>
        public static bool LogoBolden { get; set; } = false;

        // ---------------- print density (persisted) ----------------
        public static DensityMethod DensityMethod
        {
            get => (DensityMethod)Preferences.Get("print_density_method", 0);
            set => Preferences.Set("print_density_method", (int)value);
        }

        /// <summary>0..4 (0 = printer default, 4 = darkest).</summary>
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