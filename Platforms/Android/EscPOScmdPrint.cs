using Android.Bluetooth;
using eMRBCS.Services;
using QRCoder;
using SkiaSharp;
using System.Text;

namespace eMRBCS.Platforms.Android
{
    public enum PrinterTransport { Auto, SppOnly, BleOnly }
    public enum PrintAlign { Left, Center, Right }

    /// <summary>
    /// Small  = Font B 1x1
    /// Normal / Medium = Font A 1x1
    /// Big / Large     = Font A 2x2
    /// XL              = Font A 3x3
    /// (WidthMul / HeightMul in PrintStyle override these.)
    /// </summary>
    public enum TextSize { Default, Small, Normal, Big, XL, Medium = Normal, Large = Big }

    /// <summary>Single '-' / double '=' rules, drawn as plain ASCII characters.</summary>
    public enum SeparatorStyle
    {
        Blank,         // blank paper feed
        Solid,         // ----
        DoubleSolid,   // ====
        Broken,        // ---- (same as Solid, kept so old calls still compile)
        DoubleBroken   // ==== (same as DoubleSolid)
    }

    public readonly record struct PrintStyle(
        bool Bold = false,
        bool Wide = false,
        bool High = false,
        bool Underline = false,
        bool FontB = false,
        TextSize Size = TextSize.Default,
        int WidthMul = 0,
        int HeightMul = 0);

    public enum BarcodeType { Code39, Code128 }

    /// <summary>
    /// Generic ESC/POS printer, ASCII text mode only.
    ///   * Text, alignment, separators, fonts, sizes, bold, underline = plain ESC/POS text commands.
    ///   * Only graphics: logo, barcode, QR code (one GS v 0 raster command each).
    ///   * Everything is collected in memory and sent in ONE continuous stream by FlushAsync():
    ///     no pauses between commands, no banding, no separate flushes, so the paper never stops mid-print.
    /// Always finish with: await printer.FlushAsync(); ... finally { await printer.CloseAsync(); }
    /// </summary>
    public class EscPOScmdPrint : IDisposable
    {
        // ======================================================================
        //  settings
        // ======================================================================
        private static bool _logoCacheTrim;

        /// <summary>Global alignment from the Line Space digit: 321=Left, 322=Center, 323=Right.</summary>
        public PrintAlign PaperAlign => Profile.Alignment switch
        {
            1 => PrintAlign.Left,
            3 => PrintAlign.Right,
            _ => PrintAlign.Center
        };
        public int LogoMaxWidthDots { get; set; } = 0;   // 0 = full paper width

        public PrinterTransport Transport { get; set; } = PrinterTransport.Auto;
        public int ConnectTimeoutSeconds { get; set; } = 6;

        /// <summary>Bytes per Bluetooth write. No delay between writes.</summary>
        public int SendChunkSize { get; set; } = PrinterHardware.SendChunkBytes;

        /// <summary>Minimum wait in CloseAsync so closing the socket cannot cut the end of the receipt.</summary>
        public int TailDelayMs { get; set; } = 600;

        public int LogoThreshold { get; set; } = PrinterHardware.LogoThreshold;
        public bool LogoBolden { get; set; } = PrinterHardware.LogoBolden;

        public PaperProfile Profile { get; set; } = PaperProfile.FromColumns(32);

        /// <summary>Raster rows queued so far (used to estimate print time in CloseAsync).</summary>
        public int RasterRows { get; private set; }

        public string DeviceName { get; private set; } = "";
        public string TransportInUse => _link?.TransportName ?? "-";
        public bool IsConnected => _link?.IsConnected == true;

        private int Head => Profile.HeadDots > 0 ? Profile.HeadDots : 384;

        /// <summary>True for 32-column (2-inch) paper.</summary>
        public bool IsSmallPaper => Profile.ColsA <= 34;

        /// <summary>Columns of the default style (Font A, 1x).</summary>
        public int Cols => ColsFor(Plain);

        /// <summary>ESC a is exact when the paper fills the whole head. Otherwise leading spaces are used.</summary>
        private bool HwAlignAvailable => Profile.WidthDots == Head;

        // ======================================================================
        //  buffer (the whole receipt lives here until FlushAsync)
        // ======================================================================
        private readonly List<byte> _buf = new(16384);
        private int _lines;
        private readonly List<(int End, double Cost)> _marks = new();   // print-time cost of the bytes in _buf
        private DateTime _printDoneUtc = DateTime.MinValue;             // when the printer should be finished
        private IPrinterLink _link;

        public void ClearBuffer() { _buf.Clear(); _marks.Clear(); _lines = 0; BytesSent = 0; }
        private static double RowMs => 1000.0 / Math.Max(50, PrinterHardware.PrintRowsPerSecond);
        private void MarkMs(double ms)
        {
            if (ms <= 0 || !PrinterHardware.PacingEnabled) return;
            if (_marks.Count > 0 && _marks[^1].End == _buf.Count)
                _marks[^1] = (_buf.Count, _marks[^1].Cost + ms);
            else
                _marks.Add((_buf.Count, ms));
        }

        private void Mark(double dots) => MarkMs(dots * RowMs);

        /// <summary>Records that everything added to _buf since the last mark takes about 'dots' dot-rows to print.</summary>

        private static double LineDots(Rs r) => Math.Max(30, (r.FontB ? 17 : 24) * r.H + 6);

        private static double[] BuildChunkCosts(int total, int chunk, (int End, double Cost)[] marks)
        {
            var costs = new double[(total + chunk - 1) / chunk];
            int start = 0;
            foreach (var (end0, cost) in marks)
            {
                int end = Math.Min(end0, total);
                int span = end - start;
                if (span > 0 && cost > 0)
                {
                    double perByte = cost / span;
                    int pos = start;
                    while (pos < end)
                    {
                        int ci = pos / chunk;
                        int chunkEnd = Math.Min((ci + 1) * chunk, end);
                        costs[ci] += perByte * (chunkEnd - pos);
                        pos = chunkEnd;
                    }
                }
                start = Math.Max(start, end);
            }
            return costs;
        }
        public Task WriteRawAsync(byte[] payload)
        {
            if (payload != null && payload.Length > 0) _buf.AddRange(payload);
            return Task.CompletedTask;
        }

        // ======================================================================
        //  connection
        // ======================================================================
        public async Task ConnectAsync(string macAddress)
        {
            var adapter = BluetoothAdapter.DefaultAdapter
                ?? throw new InvalidOperationException("No Bluetooth adapter available on this device.");
            BluetoothDevice device = adapter.GetRemoteDevice(macAddress)
                ?? throw new InvalidOperationException($"Could not resolve device: {macAddress}");
            try { adapter.CancelDiscovery(); } catch { }

            bool bleCapable = false, bleOnly = false;
            try
            {
                bleCapable = device.Type is BluetoothDeviceType.Le or BluetoothDeviceType.Dual;
                bleOnly = device.Type == BluetoothDeviceType.Le;
            }
            catch { }

            bool bleFirst = bleOnly || (!PrinterHardware.PreferSpp && bleCapable);

            var order = Transport switch
            {
                PrinterTransport.SppOnly => new[] { "spp" },
                PrinterTransport.BleOnly => new[] { "ble" },
                _ => bleFirst ? new[] { "ble", "spp" } : new[] { "spp", "ble" }
            };

            var errors = new List<string>();
            foreach (var kind in order)
            {
                try
                {
                    if (kind == "spp")
                    {
                        var spp = new SppLink();
                        await spp.ConnectAsync(device, ConnectTimeoutSeconds);
                        _link = spp;
                    }
                    else
                    {
                        var ble = new BleLink();
                        await ble.ConnectAsync(device, ConnectTimeoutSeconds);
                        _link = ble;
                    }
                    DeviceName = _link.DeviceName;
                    ClearBuffer();
                    RasterRows = 0;
                    return;
                }
                catch (Exception ex) { errors.Add($"{kind.ToUpperInvariant()}: {ex.Message}"); }
            }
            throw new InvalidOperationException("Could not connect via SPP or BLE. " + string.Join(" | ", errors));
        }

        /// <summary>Connect, then queue the reset. Nothing is sent yet: the reset travels in the same stream as the receipt.</summary>
        public async Task ConnectAndPrepareAsync(string macAddress, int postConnectDelayMs = 200, int settleMs = 300)
        {
            await ConnectAsync(macAddress);
            if (postConnectDelayMs > 0) await Task.Delay(postConnectDelayMs);

            // Send the reset ALONE and give the printer time to finish it, so the first
            // bytes of the logo are never swallowed. Nothing is printed yet, so this is not a visible pause.
            await InitializeAsync();
            await FlushAsync();
            if (settleMs > 0) await Task.Delay(settleMs);
        }

        /// <summary>
        /// Reset + force plain ASCII mode. Queued at the START of the stream.
        /// ESC @ reset, FS . leave Chinese/GBK mode, ESC t 0 code page 0, then every text mode back to default.
        /// </summary>
        public Task InitializeAsync()
        {
            var c = new List<byte>
            {
                0x1B, 0x40,                 // ESC @    reset
                0x1B, 0x74, 0x00,           // ESC t 0  code page 0 (PC437)
            };
            if (PrinterHardware.ExitKanjiMode)
                c.AddRange(new byte[] { 0x1C, 0x2E });          // FS .  cancel Chinese / GBK (Kanji) mode

            c.AddRange(new byte[]
            {
                0x1B, 0x4D, 0x00,           // ESC M 0  Font A
                0x1B, 0x45, 0x00,           // ESC E 0  bold off
                0x1B, 0x2D, 0x00,           // ESC - 0  underline off
                0x1B, 0x21, 0x00,           // ESC ! 0  print mode normal
                0x1D, 0x21, 0x00,           // GS ! 0   size 1x1
                0x1B, 0x61, 0x00,           // ESC a 0  left
                0x1B, 0x32                  // ESC 2    default line spacing
            });

            if (PrinterHardware.ExtendedReset)
            {
                c.AddRange(new byte[]
                {
                    0x1B, 0x52, 0x00,       // ESC R 0  international set: USA
                    0x1B, 0x47, 0x00,       // ESC G 0  double-strike off
                    0x1B, 0x20, 0x00,       // ESC SP 0 char spacing 0
                    0x1B, 0x7B, 0x00,       // ESC { 0  upside-down off
                    0x1B, 0x56, 0x00,       // ESC V 0  rotation off
                    0x1D, 0x42, 0x00        // GS B 0   reverse off
                });
            }

            int head = Head;
            if (PrinterHardware.ResetMargins && head > 384)
            {
                c.AddRange(new byte[]
                {
                    0x1D, 0x4C, 0x00, 0x00,                                // GS L 0   left margin 0
                    0x1D, 0x57, (byte)(head & 0xFF), (byte)(head >> 8)     // GS W     full head width
                });
            }

            c.AddRange(DensityBytes());

            if (PrinterHardware.ExitKanjiMode)
                c.AddRange(new byte[] { 0x1C, 0x2E });          // once more, in case a setting above re-entered it

            _buf.AddRange(c);
            return Task.CompletedTask;
        }

        private static byte[] DensityBytes()
        {
            int level = Math.Clamp(PrinterHardware.DensityLevel, 0, 4);
            switch (PrinterHardware.DensityMethod)
            {
                case DensityMethod.Esc7HeatTime:
                    {
                        int[] heat = { 80, 100, 120, 160, 200 };
                        byte interval = (byte)(level == 0 ? 2 : 40);
                        return new byte[] { 0x1B, 0x37, 7, (byte)heat[level], interval };
                    }
                case DensityMethod.Dc2Hash:
                    {
                        int[] dens = { 10, 14, 18, 24, 31 };
                        return new byte[] { 0x12, 0x23, (byte)((2 << 5) | dens[level]) };
                    }
                default:
                    return Array.Empty<byte>();
            }
        }

        // ======================================================================
        //  sending: ONE continuous stream
        // ======================================================================
        /// <summary>Sends everything collected so far as one continuous stream. Does not wait for the printer to finish.</summary>
        public int BytesSent { get; private set; }
        public string Stats => $"{TransportInUse} | head {Head} | paper {Profile.WidthDots}+{Profile.OffsetDots} | {BytesSent} B | {RasterRows} rows";

        public async Task FlushAsync(bool sendTailDelay = false)
        {
            if (_buf.Count == 0) return;
            var link = _link ?? throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

            byte[] data = _buf.ToArray();
            _buf.Clear();
            _marks.Clear();

            int chunk = SendChunkSize > 0 ? SendChunkSize : 512;
            if (link.MaxChunk > 0) chunk = Math.Min(chunk, link.MaxChunk);
            chunk = Math.Max(1, chunk);

            int n = (data.Length + chunk - 1) / chunk;
            var costs = new double[n];
            int rate = PrinterHardware.MaxBytesPerSecond;
            if (PrinterHardware.PacingEnabled && rate > 0)
                for (int i = 0; i < n; i++)
                    costs[i] = Math.Min(chunk, data.Length - i * chunk) * 1000.0 / rate;

            BytesSent += data.Length;

            double remainingMs = await link.WriteStreamAsync(
                data, chunk, costs, PrinterHardware.LeadMs, PrinterHardware.MaxAheadBytes)
                .ConfigureAwait(false);

            double printMs = (_lines * 30.0 + RasterRows) * 1000.0
                             / Math.Max(50, PrinterHardware.PrintRowsPerSecond);
            var done = DateTime.UtcNow.AddMilliseconds(Math.Max(remainingMs, printMs));
            if (done > _printDoneUtc) _printDoneUtc = done;

            if (sendTailDelay && TailDelayMs > 0) await Task.Delay(TailDelayMs);
        }

        public void Disconnect()
        {
            // IMPORTANT: always await CloseAsync() first - Disconnect does not flush.
            ClearBuffer();
            try { _link?.Close(); } catch { }
            _link = null;
        }

        /// <summary>Flush, let the printer finish printing, then close. Use in finally.</summary>
        public async Task CloseAsync(int extraDelayMs = 0)
        {
            if (_link == null) { Disconnect(); return; }
            try { await FlushAsync(); } catch { }

            // Wait until the printer has really finished what is still in its buffer,
            // otherwise closing the socket cuts the end of the receipt.
            double remaining = Math.Max(0, (_printDoneUtc - DateTime.UtcNow).TotalMilliseconds);
            int wait = (int)Math.Min(60000, TailDelayMs + remaining * 1.25 + extraDelayMs);
            await Task.Delay(wait);
            Disconnect();
        }

        public void Dispose() => Disconnect();

        // ======================================================================
        //  style
        // ======================================================================
        private readonly record struct Rs(int W, int H, bool FontB, bool Bold, bool Underline);
        private static readonly Rs Plain = new(1, 1, false, false, false);

        private static (int wMul, int hMul, bool fontB) ResolveSize(PrintStyle s)
        {
            if (s.WidthMul > 0 || s.HeightMul > 0)
            {
                int w = s.WidthMul > 0 ? s.WidthMul : 1;
                int h = s.HeightMul > 0 ? s.HeightMul : 1;
                return (Math.Clamp(w, 1, 8), Math.Clamp(h, 1, 8), s.FontB);
            }
            if (s.Size != TextSize.Default)
                return s.Size switch
                {
                    TextSize.Small => (1, 1, true),
                    TextSize.Normal => (1, 1, false),
                    TextSize.Big => (2, 2, false),
                    TextSize.XL => (3, 3, false),
                    _ => (1, 1, false)
                };
            return ((s.Wide ? 2 : 1), (s.High ? 2 : 1), s.FontB);
        }

        private static Rs Resolve(PrintStyle s)
        {
            var (w, h, fb) = ResolveSize(s);
            return new Rs(w, h, fb, s.Bold || PrinterHardware.ForceBold, s.Underline);
        }

        /// <summary>style = 48 (wider than 34 columns). style32 = used on 32 when given. Otherwise smallOn32 = Font B 1x1.</summary>
        private PrintStyle PickStyle(PrintStyle style, PrintStyle? style32, bool smallOn32)
        {
            if (!IsSmallPaper) return style;
            if (style32.HasValue) return style32.Value;
            if (smallOn32) return style with { FontB = true, WidthMul = 1, HeightMul = 1, Size = TextSize.Default };
            return style;
        }

        private static int CellW(Rs r) => (r.FontB ? PrinterHardware.FontBDots : PrinterHardware.FontADots) * r.W;
        private int ColsFor(Rs r) => Math.Max(1, Profile.WidthDots / Math.Max(1, CellW(r)));

        private void AppendStyle(Rs r)
        {
            byte escBang = 0;
            if (r.FontB) escBang |= 0x01;
            if (r.Bold) escBang |= 0x08;
            if (r.H >= 2) escBang |= 0x10;
            if (r.W >= 2) escBang |= 0x20;

            _buf.AddRange(new byte[]
            {
                0x1B, 0x4D, (byte)(r.FontB ? 1 : 0),                        // ESC M  font A/B
                0x1B, 0x45, (byte)(r.Bold ? 1 : 0),                         // ESC E  bold
                0x1B, 0x21, escBang,                                        // ESC !  1x / 2x (cheap printers obey this)
                0x1D, 0x21, (byte)(((r.W - 1) << 4) | (r.H - 1)),           // GS !   1x..8x (sent after, so it wins)
                0x1B, 0x2D, (byte)(r.Underline ? 1 : 0)                     // ESC -  underline (after ESC !, which clears it)
            });
        }

        private void AppendReset()
        {
            AppendStyle(Plain);
            _buf.AddRange(new byte[] { 0x1B, 0x61, 0x00 });
        }

        // ======================================================================
        //  ASCII conversion (no special characters ever reach the printer)
        // ======================================================================
        private static readonly Dictionary<char, string> AsciiMap = BuildAsciiMap();

        private static Dictionary<char, string> BuildAsciiMap()
        {
            var d = new Dictionary<char, string>();
            void Add(string from, string to) { foreach (char c in from) d[c] = to; }

            Add("ÀÁÂÃÄÅĀĂĄ", "A"); Add("àáâãäåāăą", "a");
            Add("ÇĆĈĊČ", "C"); Add("çćĉċč", "c");
            Add("ĎĐ", "D"); Add("ďđ", "d");
            Add("ÈÉÊËĒĔĖĘĚ", "E"); Add("èéêëēĕėęě", "e");
            Add("ÌÍÎÏĨĪĬĮİ", "I"); Add("ìíîïĩīĭįı", "i");
            Add("ÑŃŅŇ", "N"); Add("ñńņň", "n");
            Add("ÒÓÔÕÖØŌŎŐ", "O"); Add("òóôõöøōŏő", "o");
            Add("ÙÚÛÜŨŪŬŮŰŲ", "U"); Add("ùúûüũūŭůűų", "u");
            Add("ÝŸ", "Y"); Add("ýÿ", "y");
            Add("ŚŜŞŠ", "S"); Add("śŝşš", "s");
            Add("ŹŻŽ", "Z"); Add("źżž", "z");
            Add("Ł", "L"); Add("ł", "l");
            Add("ß", "ss"); Add("Æ", "AE"); Add("æ", "ae"); Add("Œ", "OE"); Add("œ", "oe");
            Add("‘’‚´`", "'"); Add("“”„", "\"");
            Add("–—−", "-"); Add("•·", "*"); Add("…", "...");
            Add("×", "x"); Add("°", "o"); Add("€", "EUR"); Add("₱", "P");
            Add("\t", " "); Add("\u00A0", " ");
            return d;
        }

        private static string ToAscii(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '\r') continue;
                if (c == '\n' || (c >= 0x20 && c <= 0x7E)) { sb.Append(c); continue; }
                if (AsciiMap.TryGetValue(c, out var rep)) sb.Append(rep);
                else if (char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) { /* drop accents */ }
                else sb.Append('?');
            }
            return sb.ToString();
        }

        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);
        private static byte AlignCode(PrintAlign a) => a switch { PrintAlign.Center => 1, PrintAlign.Right => 2, _ => 0 };
        private static byte[] FeedDots(int n) => new byte[] { 0x1B, 0x4A, (byte)Math.Clamp(n, 0, 255) };   // ESC J n

        private static IEnumerable<string> Wrap(string text, int width)
        {
            foreach (var para in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = "";
                foreach (var word in para.Split(' '))
                {
                    string w = word;
                    while (w.Length > width)
                    {
                        if (line.Length > 0) { yield return line; line = ""; }
                        yield return w.Substring(0, width);
                        w = w.Substring(width);
                    }
                    if (line.Length == 0) line = w;
                    else if (line.Length + 1 + w.Length <= width) line += " " + w;
                    else { yield return line; line = w; }
                }
                yield return line;
            }
        }

        // ======================================================================
        //  text
        // ======================================================================
        /// <summary>
        /// One text line with alignment. ESC a (exact) when the paper fills the head,
        /// otherwise leading spaces. ulGap* = a part of the line that must NOT be underlined (row gap).
        /// </summary>
        private void EmitLine(string text, PrintAlign align, Rs r, int ulGapStart = -1, int ulGapLen = 0)
        {
            int textDots = text.Length * CellW(r);

            if (HwAlignAvailable)
            {
                _buf.AddRange(new byte[] { 0x1B, 0x61, AlignCode(align) });
            }
            else
            {
                _buf.AddRange(new byte[] { 0x1B, 0x61, 0x00 });

                int pad = align switch
                {
                    PrintAlign.Center => (Profile.WidthDots - textDots) / 2,
                    PrintAlign.Right => Profile.WidthDots - textDots,
                    _ => 0
                };
                pad = Math.Max(0, pad);

                // leading spaces are always Font A 1x so the shift is exact to one cell
                int cell = Math.Max(1, PrinterHardware.FontADots);
                int lead = Profile.OffsetDots + pad;
                int leadCols = align == PrintAlign.Center ? (int)Math.Round(lead / (double)cell) : lead / cell;
                int maxLead = Math.Max(0, (Head - textDots) / cell);
                leadCols = Math.Clamp(leadCols, 0, maxLead);

                if (leadCols > 0)
                {
                    AppendStyle(Plain);
                    _buf.AddRange(Ascii(new string(' ', leadCols)));
                    AppendStyle(r);
                }
            }

            if (r.Underline && ulGapLen > 0 && ulGapStart >= 0 && ulGapStart + ulGapLen <= text.Length)
            {
                _buf.AddRange(Ascii(text.Substring(0, ulGapStart)));
                _buf.AddRange(new byte[] { 0x1B, 0x2D, 0x00 });
                _buf.AddRange(Ascii(text.Substring(ulGapStart, ulGapLen)));
                _buf.AddRange(new byte[] { 0x1B, 0x2D, 0x01 });
                _buf.AddRange(Ascii(text.Substring(ulGapStart + ulGapLen)));
            }
            else
            {
                _buf.AddRange(Ascii(text));
            }

            _buf.Add(0x0A);
            _lines++;
            Mark(LineDots(r));
        }

        public Task PrintTextAsync(string text, PrintAlign align = PrintAlign.Left,
                                   PrintStyle style = default, PrintStyle? style32 = null,
                                   bool smallOn32 = false)
        {
            var r = Resolve(PickStyle(style, style32, smallOn32));
            text = ToAscii(text);
            int cols = ColsFor(r);

            AppendStyle(r);
            foreach (var raw in Wrap(text, cols))
                EmitLine(raw.TrimEnd(), align, r);
            AppendReset();
            return Task.CompletedTask;
        }

        public Task PrintRowAsync(string left, string right,
                                  PrintStyle style = default, PrintStyle? style32 = null,
                                  bool smallOn32 = false)
        {
            var r = Resolve(PickStyle(style, style32, smallOn32));
            left = ToAscii(left).Replace('\n', ' ');
            right = ToAscii(right).Replace('\n', ' ');
            int cols = ColsFor(r);

            AppendStyle(r);

            if (left.Length + 1 + right.Length > cols && right.Length > cols / 2)
            {
                // value too long to share a line -> label first, value right aligned below
                foreach (var l in Wrap(left, cols)) EmitLine(l.TrimEnd(), PrintAlign.Left, r);
                foreach (var l in Wrap(right, cols)) EmitLine(l.TrimEnd(), PrintAlign.Right, r);
            }
            else
            {
                var ll = Wrap(left, Math.Max(1, cols - right.Length - 1)).ToList();
                for (int i = 0; i < ll.Count; i++)
                {
                    string l = ll[i].TrimEnd();
                    if (i == ll.Count - 1)
                    {
                        int gap = Math.Max(1, cols - l.Length - right.Length);
                        EmitLine(l + new string(' ', gap) + right, PrintAlign.Left, r, l.Length, gap);
                    }
                    else EmitLine(l, PrintAlign.Left, r);
                }
            }

            AppendReset();
            return Task.CompletedTask;
        }

        /// <summary>Raw line (leading spaces kept, no word re-flow). Used for table rows.</summary>
        public Task WriteLineAsync(string text = "")
        {
            text = ToAscii(text).Replace('\n', ' ');
            int cols = ColsFor(Plain);

            AppendStyle(Plain);
            if (text.Length == 0) EmitLine("", PrintAlign.Left, Plain);
            for (int i = 0; i < text.Length; i += cols)
                EmitLine(text.Substring(i, Math.Min(cols, text.Length - i)), PrintAlign.Left, Plain);
            AppendReset();
            return Task.CompletedTask;
        }

        public Task AlignAsync(PrintAlign align)
        {
            _buf.AddRange(new byte[] { 0x1B, 0x61, AlignCode(align) });
            return Task.CompletedTask;
        }

        public Task WriteTextAsync(string text)
        {
            _buf.AddRange(Ascii(ToAscii(text)));
            return Task.CompletedTask;
        }

        public Task FeedAsync(int lines = 1)
        {
            int n = Math.Max(0, lines);
            for (int i = 0; i < n; i++) _buf.Add(0x0A);
            _lines += n;
            Mark(30.0 * n);
            return Task.CompletedTask;
        }

        public Task PrintRulerAsync()
        {
            int cols = Cols;
            var chars = new char[cols];
            for (int i = 0; i < cols; i++) chars[i] = (char)('0' + ((i + 1) % 10));
            chars[0] = '|'; chars[cols - 1] = '|';
            AppendStyle(Plain);
            EmitLine(new string(chars), PrintAlign.Left, Plain);
            AppendReset();
            return Task.CompletedTask;
        }

        public static string NormalizeMacAddress(string raw)
        {
            string hexOnly = new string(raw.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
            if (hexOnly.Length != 12)
                throw new ArgumentException($"'{raw}' does not contain a valid 12-hex-digit MAC address.");
            return string.Join(":", Enumerable.Range(0, 6).Select(i => hexOnly.Substring(i * 2, 2)));
        }

        // ======================================================================
        //  separators: '-' single, '=' double, plain ASCII text
        // ======================================================================
        public Task PrintSeparatorAsync(char c = '-', bool fontB = false)
        {
            switch (c)
            {
                case ' ': return PrintSeparatorAsync(SeparatorStyle.Blank, fontB);
                case '-':
                case '_':
                case '~': return PrintSeparatorAsync(SeparatorStyle.Solid, fontB);
                case '=':
                case '#': return PrintSeparatorAsync(SeparatorStyle.DoubleSolid, fontB);
            }
            return PrintRepeat(c, fontB);
        }

        public Task PrintSeparatorAsync(SeparatorStyle style, bool fontB = false)
        {
            switch (style)
            {
                case SeparatorStyle.Blank:
                    _buf.AddRange(FeedDots(PrinterHardware.BlankLineDots));
                    Mark(PrinterHardware.BlankLineDots);
                    return Task.CompletedTask;
                case SeparatorStyle.DoubleSolid:
                case SeparatorStyle.DoubleBroken:
                    return PrintRepeat('=', fontB);
                default:
                    return PrintRepeat('-', fontB);
            }
        }

        private Task PrintRepeat(char c, bool fontB)
        {
            var r = Resolve(new PrintStyle(FontB: fontB));
            AppendStyle(r);
            EmitLine(new string(c, ColsFor(r)), PrintAlign.Left, r);
            AppendReset();
            return Task.CompletedTask;
        }

        private void EmitRaster(int contentW, int h, Func<int, int, bool> isBlack,
                                 PrintAlign align, int sendW = -1, bool vDouble = false)
        {
            if (h <= 0 || contentW <= 0) return;
            if (sendW < 0 || sendW > contentW) sendW = contentW;

            int head = Head;
            int headBytes = head / 8;
            int paperW = Math.Min(Profile.WidthDots, head);

            int x0 = align switch
            {
                PrintAlign.Center => (paperW - contentW) / 2,
                PrintAlign.Right => paperW - contentW,
                _ => 0
            };
            x0 = Math.Max(0, x0) + Profile.OffsetDots;
            x0 = Math.Clamp(x0, 0, Math.Max(0, head - 8));
            sendW = Math.Min(sendW, head - x0);          // NEVER past the physical head
            if (sendW <= 0) return;

            bool useMargin = PrinterHardware.RasterUseMargin;
            int margin = useMargin ? (x0 / 8) * 8 : 0;
            int shift = x0 - margin;                     // 0..7 with margin, full x0 without
            int rowBytes = Math.Clamp((shift + sendW + 7) / 8, 1, headBytes - margin / 8);
            int rowDots = rowBytes * 8;

            // rows per command: whole image unless a cap is configured
            int band = Math.Clamp(PrinterHardware.RasterBandRows, 1, 65535);
            if (PrinterHardware.RasterBandMaxBytes > 0)
                band = Math.Min(band, Math.Max(1, PrinterHardware.RasterBandMaxBytes / rowBytes));
            bool banded = band < h;

            _buf.AddRange(new byte[] { 0x1B, 0x61, 0x00 });                       // ESC a 0 (we position ourselves)
            if (useMargin)
            {
                int areaW = head - margin;
                _buf.AddRange(new byte[] { 0x1D, 0x4C, (byte)(margin & 0xFF), (byte)(margin >> 8) }); // GS L
                _buf.AddRange(new byte[] { 0x1D, 0x57, (byte)(areaW & 0xFF), (byte)(areaW >> 8) });   // GS W
            }
            if (banded) _buf.AddRange(new byte[] { 0x1B, 0x33, 0x00 });           // ESC 3 0 (only if we must split)

            for (int y0 = 0; y0 < h; y0 += band)
            {
                int rows = Math.Min(band, h - y0);
                var cmd = new byte[8 + rowBytes * rows];
                cmd[0] = 0x1D; cmd[1] = 0x76; cmd[2] = 0x30;
                cmd[3] = (byte)(vDouble ? 2 : 0);
                cmd[4] = (byte)(rowBytes & 0xFF); cmd[5] = (byte)(rowBytes >> 8);
                cmd[6] = (byte)(rows & 0xFF); cmd[7] = (byte)(rows >> 8);

                for (int r = 0; r < rows; r++)
                    for (int x = 0; x < sendW; x++)
                    {
                        if (!isBlack(x, y0 + r)) continue;
                        int px = shift + x;
                        if (px >= rowDots) break;
                        cmd[8 + r * rowBytes + px / 8] |= (byte)(0x80 >> (px % 8));
                    }
                _buf.AddRange(cmd);
            }

            if (banded) _buf.AddRange(new byte[] { 0x1B, 0x32 });                 // ESC 2
            if (useMargin)
            {
                _buf.AddRange(new byte[] { 0x1D, 0x4C, 0x00, 0x00 });
                _buf.AddRange(new byte[] { 0x1D, 0x57, (byte)(head & 0xFF), (byte)(head >> 8) });
            }

            RasterRows += vDouble ? h * 2 : h;
        }

        public Task WriteGraphicAsync(byte[] imageBytes, int widthDots, int heightDots, byte threshold = 150)
        {
            using var original = SKBitmap.Decode(imageBytes)
                ?? throw new InvalidOperationException("Could not decode image bytes.");
            using var resized = original.Resize(new SKImageInfo(widthDots, heightDots), new SKSamplingOptions(SKCubicResampler.Mitchell))
                ?? throw new InvalidOperationException("Image resize failed.");

            SKColor[] pixels = resized.Pixels;
            EmitRaster(widthDots, heightDots, (x, y) =>
            {
                SKColor px = pixels[y * widthDots + x];
                double a = px.Alpha / 255.0;
                double lum = (0.299 * px.Red + 0.587 * px.Green + 0.114 * px.Blue) * a + 255 * (1 - a);
                return lum < threshold;
            }, PrintAlign.Left);
            return Task.CompletedTask;
        }

        // ---------- logo (decoded and thresholded once, then cached) ----------
        private static readonly object LogoCacheLock = new();
        private static byte[] _logoCacheSrc;
        private static int _logoCacheW, _logoCacheH, _logoCacheThr;
        private static bool _logoCacheBold;
        private static bool[] _logoCacheMask;

        private static (bool[] Mask, int H) PrepareLogoMask(byte[] imageBytes, int w, int threshold, bool bolden)
        {
            lock (LogoCacheLock)
            {
                bool trim = PrinterHardware.LogoTrimWhitespace;
                if (_logoCacheMask != null && _logoCacheW == w && _logoCacheThr == threshold
                    && _logoCacheBold == bolden && _logoCacheTrim == trim
                    && _logoCacheSrc != null && _logoCacheSrc.Length == imageBytes.Length
                    && _logoCacheSrc.SequenceEqual(imageBytes))
                    return (_logoCacheMask, _logoCacheH);

                using var bmp = SKBitmap.Decode(imageBytes) ?? throw new InvalidOperationException("Bad image.");
                int sw = bmp.Width, sh = bmp.Height;
                int cx = 0, cy = 0, cw = sw, ch = sh;

                // ---- find the ink bounding box in the ORIGINAL image ----
                if (trim)
                {
                    SKColor[] src = bmp.Pixels;
                    int minX = sw, maxX = -1, minY = sh, maxY = -1;
                    for (int y = 0; y < sh; y++)
                    {
                        int row = y * sw;
                        for (int x = 0; x < sw; x++)
                        {
                            SKColor p = src[row + x];
                            double a = p.Alpha / 255.0;
                            double lum = (0.299 * p.Red + 0.587 * p.Green + 0.114 * p.Blue) * a + 255 * (1 - a);
                            if (lum >= threshold) continue;
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                    if (maxX >= 0) { cx = minX; cy = minY; cw = maxX - minX + 1; ch = maxY - minY + 1; }
                }

                SKBitmap source = bmp;
                SKBitmap cropped = null;
                if (cx != 0 || cy != 0 || cw != sw || ch != sh)
                {
                    cropped = new SKBitmap();
                    if (bmp.ExtractSubset(cropped, new SKRectI(cx, cy, cx + cw, cy + ch))) source = cropped;
                    else { cropped.Dispose(); cropped = null; }
                }

                try
                {
                    // ---- scale the CROPPED artwork to the full paper width ----
                    int h = Math.Max(1, (int)Math.Round(ch * (w / (double)cw)));
                    using var resized = source.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKCubicResampler.Mitchell))
                        ?? throw new InvalidOperationException("Image resize failed.");

                    SKColor[] px = resized.Pixels;
                    var mask = new bool[w * h];
                    for (int i = 0; i < mask.Length; i++)
                    {
                        SKColor p = px[i];
                        double a = p.Alpha / 255.0;
                        double lum = (0.299 * p.Red + 0.587 * p.Green + 0.114 * p.Blue) * a + 255 * (1 - a);
                        mask[i] = lum < threshold;
                    }
                    if (bolden)
                    {
                        var thick = (bool[])mask.Clone();
                        for (int y = 0; y < h; y++)
                            for (int x = 1; x < w; x++)
                                if (mask[y * w + x - 1]) thick[y * w + x] = true;
                        mask = thick;
                    }

                    _logoCacheSrc = (byte[])imageBytes.Clone();
                    _logoCacheW = w; _logoCacheH = h; _logoCacheThr = threshold;
                    _logoCacheBold = bolden; _logoCacheTrim = trim;
                    _logoCacheMask = mask;
                    return (mask, h);
                }
                finally { cropped?.Dispose(); }
            }
        }

        public async Task WriteLogoAsync(byte[] imageBytes, double widthFraction = 1.0, PrintAlign? align = null)
        {
            PrintAlign a = align ?? PaperAlign;          // follows 321 / 322 / 323

            int paperW = Profile.WidthDots;
            int w = Math.Max(8, (int)(paperW * widthFraction) / 8 * 8);
            if (LogoMaxWidthDots > 0) w = Math.Max(8, Math.Min(w, LogoMaxWidthDots) / 8 * 8);
            w = Math.Min(w, paperW / 8 * 8);

            int thr = LogoThreshold; bool bold = LogoBolden;
            var prepared = await Task.Run(() => PrepareLogoMask(imageBytes, w, thr, bold));
            bool[] mask = prepared.Mask;
            int h = prepared.H;

            int maxX = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = w - 1; x > maxX; x--)
                    if (mask[row + x]) { maxX = x; break; }
            }
            if (maxX < 0) return;                        // blank logo -> print nothing

            if (PrinterHardware.LogoVerticalDouble)
            {
                int half = (h + 1) / 2;
                EmitRaster(w, half, (x, y) =>
                {
                    int y0 = y * 2;
                    if (y0 >= h) return false;
                    return mask[y0 * w + x] || (y0 + 1 < h && mask[(y0 + 1) * w + x]);
                }, a, sendW: maxX + 1, vDouble: true);
            }
            else
            {
                EmitRaster(w, h, (x, y) => mask[y * w + x], a, sendW: maxX + 1);
            }
        }

        // ---------- barcode ----------
        public Task WriteBarcodeAsync(string data, BarcodeType type = BarcodeType.Code128,
                                      double widthFraction = 0.8, int heightDots = 70,
                                      bool humanText = true, int maxModuleDots = 3,
                                      PrintAlign align = PrintAlign.Center)
        {
            if (string.IsNullOrWhiteSpace(data))
                throw new ArgumentException("Barcode data cannot be empty.", nameof(data));

            if (type == BarcodeType.Code39) data = data.ToUpperInvariant();
            string pattern = type == BarcodeType.Code39 ? BuildCode39Pattern(data) : BuildCode128Pattern(data);

            int totalModules = pattern.Sum(c => c - '0');
            const int quiet = 10;

            int maxDots = Math.Min((int)(Profile.WidthDots * widthFraction), Profile.WidthDots);
            int mw = Math.Min(maxModuleDots, maxDots / (totalModules + 2 * quiet));
            if (mw < 1)
                throw new InvalidOperationException($"Barcode '{data}' is too long for this paper width.");

            var bars = new bool[totalModules * mw];
            int pos = 0; bool bar = true;
            foreach (char d in pattern)
            {
                int len = (d - '0') * mw;
                for (int k = 0; k < len; k++) bars[pos++] = bar;
                bar = !bar;
            }

            int h = Math.Clamp(heightDots, 8, 600);
            EmitRaster(bars.Length, h, (x, y) => bars[x], align);

            if (humanText) return PrintTextAsync(data, align, new PrintStyle(FontB: true));
            return Task.CompletedTask;
        }

        // ---------- QR ----------
        public static string BuildAccountPayload(string accNo, string name, decimal balance)
            => $"Acct#: {accNo}\nName: {name}\nBalance: {balance:N2}";

        public Task PrintQrSizedAsync(string payload, double widthFraction = 0.35, int minModuleDots = 2)
        {
            using var gen = new QRCodeGenerator();
            using var qr = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var m = qr.ModuleMatrix;
            int n = m.Count;

            int module = Math.Max(minModuleDots, (int)(Profile.WidthDots * widthFraction) / n);
            if (n * module > Profile.WidthDots) module = Math.Max(1, Profile.WidthDots / n);
            int size = n * module;

            EmitRaster(size, size, (x, y) => m[y / module][x / module], PrintAlign.Center);
            return Task.CompletedTask;
        }

        // ---------- CODE 128 / CODE 39 patterns ----------
        private static readonly string[] Code128Patterns =
        {
             "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
             "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
             "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
             "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
             "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
             "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
             "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
             "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
             "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
             "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
             "114131","311141","411131","211412","211214","211232","2331112"
        };

        private static readonly Dictionary<char, string> Code39Patterns = new()
        {
            ['0'] = "nnnwwnwnn",
            ['1'] = "wnnwnnnnw",
            ['2'] = "nnwwnnnnw",
            ['3'] = "wnwwnnnnn",
            ['4'] = "nnnwwnnnw",
            ['5'] = "wnnwwnnnn",
            ['6'] = "nnwwwnnnn",
            ['7'] = "nnnwnnwnw",
            ['8'] = "wnnwnnwnn",
            ['9'] = "nnwwnnwnn",
            ['A'] = "wnnnnwnnw",
            ['B'] = "nnwnnwnnw",
            ['C'] = "wnwnnwnnn",
            ['D'] = "nnnnwwnnw",
            ['E'] = "wnnnwwnnn",
            ['F'] = "nnwnwwnnn",
            ['G'] = "nnnnnwwnw",
            ['H'] = "wnnnnwwnn",
            ['I'] = "nnwnnwwnn",
            ['J'] = "nnnnwwwnn",
            ['K'] = "wnnnnnnww",
            ['L'] = "nnwnnnnww",
            ['M'] = "wnwnnnnwn",
            ['N'] = "nnnnwnnww",
            ['O'] = "wnnnwnnwn",
            ['P'] = "nnwnwnnwn",
            ['Q'] = "nnnnnnwww",
            ['R'] = "wnnnnnwwn",
            ['S'] = "nnwnnnwwn",
            ['T'] = "nnnnwnwwn",
            ['U'] = "wwnnnnnnw",
            ['V'] = "nwwnnnnnw",
            ['W'] = "wwwnnnnnn",
            ['X'] = "nwnnwnnnw",
            ['Y'] = "wwnnwnnnn",
            ['Z'] = "nwwnwnnnn",
            ['-'] = "nwnnnnwnw",
            ['.'] = "wwnnnnwnn",
            [' '] = "nwwnnnwnn",
            ['$'] = "nwnwnwnnn",
            ['/'] = "nwnwnnnwn",
            ['+'] = "nwnnnwnwn",
            ['%'] = "nnnwnwnwn",
            ['*'] = "nwnnwnwnn"
        };

        private static string BuildCode128Pattern(string data)
        {
            var values = new List<int> { 104 };
            foreach (char ch in data)
            {
                if (ch < 32 || ch > 126)
                    throw new ArgumentException($"Character '{ch}' not allowed in Code128-B.");
                values.Add(ch - 32);
            }
            int sum = 104;
            for (int i = 1; i < values.Count; i++) sum += i * values[i];
            values.Add(sum % 103);
            values.Add(106);
            return string.Concat(values.Select(v => Code128Patterns[v]));
        }

        private static string BuildCode39Pattern(string data)
        {
            var sb = new StringBuilder();
            string text = "*" + data + "*";
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (i > 0 && i < text.Length - 1 && ch == '*')
                    throw new ArgumentException("'*' is reserved in Code39.");
                if (!Code39Patterns.TryGetValue(ch, out var p))
                    throw new ArgumentException($"Character '{ch}' not allowed in Code39.");
                if (i > 0) sb.Append('1');
                foreach (char e in p) sb.Append(e == 'w' ? '3' : '1');
            }
            return sb.ToString();
        }
        private void EmitSolidBar(int rowBytes, int rows)
        {
            _buf.AddRange(new byte[] { 0x1B, 0x61, 0x00, 0x1D, 0x4C, 0x00, 0x00 });
            var cmd = new byte[8 + rowBytes * rows];
            cmd[0] = 0x1D; cmd[1] = 0x76; cmd[2] = 0x30; cmd[3] = 0x00;
            cmd[4] = (byte)rowBytes; cmd[5] = 0; cmd[6] = (byte)rows; cmd[7] = 0;
            for (int i = 8; i < cmd.Length; i++) cmd[i] = 0xFF;
            _buf.AddRange(cmd);
            RasterRows += rows;
        }

        public Task PrintHeadProbeAsync()
        {
            PrintTextAsync("BAR A = 384 DOTS (58mm)", PrintAlign.Left, new PrintStyle(Bold: true));
            EmitSolidBar(48, 24);
            FeedAsync(1);
            PrintTextAsync("BAR B = 576 DOTS (80mm)", PrintAlign.Left, new PrintStyle(Bold: true));
            EmitSolidBar(72, 24);
            FeedAsync(2);
            WriteRawAsync(new byte[] { 0x1B, 0x40 });
            FeedAsync(3);
            return Task.CompletedTask;
        }
    }
}