namespace eMRBCS.Services
{
    public enum AlignMethod { Hardware, Software }

    /// <summary>Hybrid = text for plain ASCII, raster image for everything else (default).</summary>
    public enum RenderMode { Hybrid, TextOnly, RasterOnly }

    /// <summary>Raster = Font B requests are drawn as an image (always followed). Native = ESC M (printer must support it).</summary>
    public enum SmallFontMode { Raster, Native }

    /// <summary>Optional darkness commands. Off = send nothing (safe on every printer).</summary>
    public enum DensityMethod { Off = 0, Esc7HeatTime = 1, Dc2Hash = 2 }

    public static class PrinterHardware
    {
        /// <summary>Weight of '-' separators (solid line). '=' uses two lines.</summary>
        public static int SeparatorThicknessDots { get; set; } = 3;

        /// <summary>Dash length (dots) for broken separators ('~' and '#').</summary>
        public static int SeparatorDashDots { get; set; } = 12;

        /// <summary>Gap length (dots) between dashes for broken separators.</summary>
        public static int SeparatorGapDots { get; set; } = 6;

        /// <summary>Height of a blank line made by PrintSeparatorAsync(' ').</summary>
        public static int BlankLineDots { get; set; } = 24;
        /// <summary>0 = automatic. Set a number only to force a head width.</summary>
        public static int HeadWidthDots { get; set; } = 0;

        /// <summary>Used when the Line Space has no alignment digit (e.g. "48"). 1 = left, 2 = center, 3 = right.</summary>
        public static int DefaultAlignment { get; set; } = 2;

        /// <summary>
        /// Only matters when the paper is narrower than the print head (321/322/323...).
        /// Software = leading spaces for text, blank dots for images. Hardware = GS L / GS W margins.
        /// </summary>
        public static AlignMethod AlignBy { get; set; } = AlignMethod.Software;

        /// <summary>true = every print on a 3-inch head starts with GS L 0 / GS W (full head).</summary>
        public static bool ResetMargins { get; set; } = true;

        public const bool RasterIgnoresMargin = true;

        // ---------------- hybrid rendering ----------------
        public static RenderMode Render { get; set; } = RenderMode.Hybrid;
        public static SmallFontMode SmallFont { get; set; } = SmallFontMode.Raster;

        /// <summary>true = all normal text is printed emphasised (darker). Helps faded printers.</summary>
        public static bool ForceBold { get; set; } = false;

        // ---------------- font metrics (dots) ----------------
        public static int FontADots { get; set; } = 12;
        public static int FontBDots { get; set; } = 9;
        public static int FontAHeight { get; set; } = 24;
        public static int FontBHeight { get; set; } = 17;

        // ---------------- rules / underline ----------------
        /// <summary>Weight of '-' separators (solid line). '=' uses two lines.</summary>

        /// <summary>Distance of the underline below the text baseline (smaller = closer to the text).</summary>
        public static int UnderlineGapDots { get; set; } = 2;
        public static int UnderlineThicknessDots { get; set; } = 2;
        public static int UnderlineBoldThicknessDots { get; set; } = 3;

        // ---------------- image quality ----------------
        /// <summary>Luminance below this becomes black. Higher = darker logo (128 was too light).</summary>
        public static int LogoThreshold { get; set; } = 170;
        /// <summary>true = thicken every black pixel by 1 dot horizontally.</summary>
        public static bool LogoBolden { get; set; } = false;

        // ---------------- print density (persisted, set from Test Print) ----------------
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

    /// <summary>
    /// Generic, model independent send settings. Text is plain ASCII (identical in every code page, incl. GBK),
    /// everything else is printed as an image, so no code page / model detection is needed.
    /// </summary>
    public sealed class PrinterCaps
    {
        // text sending
        public int SendChunkSize { get; init; } = 512;
        public int SendChunkDelayMs { get; init; } = 4;
        public int TailDelayMs { get; init; } = 400;

        // barcode / QR / text-image / rule raster
        public int RasterSliceRows { get; init; } = 48;
        public int RasterChunkSize { get; init; } = 192;
        public int RasterChunkDelayMs { get; init; } = 8;

        // logo
        public bool LogoBitImage { get; init; } = false;
        public int LogoSliceRows { get; init; } = 24;
        public int LogoChunkSize { get; init; } = 192;
        public int LogoChunkDelayMs { get; init; } = 8;
        public int LogoStripPauseMs { get; init; } = 0;

        /// <summary>Safe for every printer (slower).</summary>
        public static PrinterCaps Default => new();

        /// <summary>For printers with a fast Bluetooth buffer. Use: printer.Caps = PrinterCaps.Fast;</summary>
        public static PrinterCaps Fast => new()
        {
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

        /// <summary>Use if the logo does not print with GS v 0 raster.</summary>
        public static PrinterCaps BitImageLogo => new() { LogoBitImage = true };
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