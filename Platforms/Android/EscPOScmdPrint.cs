using Android.Bluetooth;
using eMRBCS.Services;
using Java.Util;
using QRCoder;
using SkiaSharp;
using System.Text;


namespace eMRBCS.Platforms.Android
{
    public enum PrintAlign { Left, Center, Right }
    public enum TextSize { Default, Small, Normal, Big, Large }

    public readonly record struct PrintStyle(bool Bold = false,
                                              bool Wide = false,
                                              bool High = false,
                                              bool Underline = false,
                                              bool FontB = false,
                                              TextSize Size = TextSize.Default,   // NEW
                                              int WidthMul = 0,                   // NEW: 0 = derive from Size/Wide
                                              int HeightMul = 0);                 // NEW: 0 = derive from Size/High
    public enum BarcodeType { Code39, Code128 }

    /// <summary>
    /// NEW: how the logo is sent to the printer.
    /// Raster      = ONE GS v 0 command for the whole logo.
    /// RasterBands = GS v 0 in smaller bands, recommended for Bluetooth printers that skip raster data.
    /// BitImage    = ESC * 24-dot strips, available for printers that do not handle GS v 0 reliably.
    /// </summary>
    public enum LogoMode { Raster, RasterBands, BitImage }


    /// <summary>
    /// Generic Bluetooth thermal printer connection using classic SPP (RFCOMM).
    /// Works with any ESC/POS-compatible printer, not tied to a specific vendor SDK.
    ///
    /// NEW (speed): every command is collected in memory and the whole receipt goes to the printer
    /// in one continuous stream when you call FlushAsync() (Disconnect() also flushes as a safety net).
    /// Before, every text line was 3 separate Bluetooth writes with a flush and a 10 ms pause each.
    /// </summary>
    public class EscPOScmdPrint : IDisposable
    {
        private static readonly UUID SppUuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB");

        // NEW: the code page provider must be registered BEFORE GetEncoding(850), so it is done here.
        private static readonly Encoding PrinterEncoding;
        static EscPOScmdPrint()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            PrinterEncoding = Encoding.GetEncoding(850);
        }
        public int LogoStripFeedDots { get; set; } = 22;
        // ---------------- NEW: sending / speed settings ----------------
        /// <summary>true = collect everything and send it in one go on FlushAsync() (fast). false = send every command immediately (old behaviour).</summary>
        public bool BufferOutput { get; set; } = true;

        /// <summary>Bytes per Bluetooth write. Bigger = faster.</summary>
        public int SendChunkSize { get; set; } = 2048;

        /// <summary>Pause between writes (ms). 0 = fastest. If a printer ever loses characters, try 5.</summary>
        public int SendChunkDelayMs { get; set; } = 0;

        /// <summary>Short wait after the last byte so closing the socket cannot cut the end of the receipt.</summary>
        public int TailDelayMs { get; set; } = 150;

        /// <summary>Seconds to wait for each connect method before trying the next one.</summary>
        public int ConnectTimeoutSeconds { get; set; } = 6;

        // ---------------- underline settings ----------------
        /// <summary>Blank dots between the bottom of the text and the underline. Raise it if the line still touches the letters.</summary>
        public int UnderlineGapDots { get; set; } = 6;

        /// <summary>Underline thickness in dots for NORMAL text (203 dpi: 4 dots is about 0.5 mm).</summary>
        public int UnderlineThicknessDots { get; set; } = 3;

        /// <summary>Underline thickness in dots for BOLD text.</summary>
        public int UnderlineBoldThicknessDots { get; set; } = 2;

        /// <summary>Extra paper feed after the underline so the next line does not crowd it.</summary>
        public int UnderlineTailFeed { get; set; } = 6;

        private int _widthMul = 1;
        private int _heightMul = 1;

        private int UnderlineThickness(bool bold)
        {
            int t = bold ? UnderlineBoldThicknessDots : UnderlineThicknessDots;
            return Math.Max(1, t * _heightMul);          // was: _doubleHigh ? 2 : 1
        }

        private int CellHeightDots =>
            (_fontB ? FontBCellHeight : FontACellHeight) * _heightMul;

        private int ColToDot(int col) =>
            (int)Math.Round(col * DotsPerCol * _widthMul);

        public int Cols => Math.Max(1, (_fontB ? Profile.ColsB : Profile.ColsA) / _widthMul);

        public async Task DoubleWideAsync(bool on) { _widthMul = on ? 2 : 1; await ApplySizeAsync(); }
        public async Task DoubleHighAsync(bool on) { _heightMul = on ? 2 : 1; await ApplySizeAsync(); }

        // ---------------- raster settings (barcode, QR, underline) ----------------
        /// <summary>Bytes per write for raster data. 0 = use SendChunkSize.</summary>
        public int RasterChunkSize { get; set; } = 0;

        /// <summary>Pause between raster chunks (ms). -1 = use SendChunkDelayMs.</summary>
        public int RasterChunkDelayMs { get; set; } = -1;

        /// <summary>Rows per GS v 0 command for general raster images. 0 = one command.</summary>
        public int RasterSliceRows { get; set; } = 0;

        // ---------------- logo settings ----------------
        /// <summary>
        /// Use smaller raster bands by default to reduce the chance of a Bluetooth printer
        /// dropping rows while processing one large image command.
        /// </summary>
        public LogoMode LogoMode { get; set; } = LogoMode.BitImage;

        /// <summary>Kept so old code still compiles. true = LogoMode.BitImage, false = LogoMode.Raster.</summary>
        public bool LogoUseBitImage
        {
            get => LogoMode == LogoMode.BitImage;
            set => LogoMode = value ? LogoMode.BitImage : LogoMode.Raster;
        }

        /// <summary>Rows per raster band. Smaller values reduce printer buffer pressure.</summary>
        public int LogoSliceRows { get; set; } = 64;

        /// <summary>Bytes per Bluetooth write for logo data. Smaller chunks can improve reliability.</summary>
        public int LogoChunkSize { get; set; } = 256;

        /// <summary>Pause between Bluetooth writes for logo data, in milliseconds.</summary>
        public int LogoChunkDelayMs { get; set; } = 5;

        /// <summary>Only for RasterBands: pause after each band = rows * this value. Leave 0 (pauses create gaps).</summary>
        public int LogoMsPerRow { get; set; } = 0;

        /// <summary>Only for BitImage: paper feed after each 24-row strip (24 = exact; if a thin gap remains try 23 or 22).</summary>

        /// <summary>Hard cap for the logo width in dots (0 = no cap). LESS WIDTH = LESS DATA = FEWER STALLS.
        /// If blank lines remain try 384, then 320, then 256.</summary>
        public int LogoMaxWidthDots { get; set; } = 0;

        /// <summary>NEW: true = send the logo with half the rows and let the printer print every row twice (GS v 0 double-height).
        /// This halves the data the printer needs per second. Use it if blank lines remain after lowering LogoMaxWidthDots.
        /// The logo gets slightly coarser vertically. Ignored in BitImage mode.</summary>
        public bool LogoVerticalDouble { get; set; } = false;

        /// <summary>Only for BitImage: false = position with the left margin (smallest data); true = pad with empty bytes.</summary>
        public bool LogoPadWithZeros { get; set; } = false;

        public int FontACellHeight { get; set; } = 24;
        public int FontBCellHeight { get; set; } = 17;


        private double DotsPerCol => (double)Profile.WidthDots / (_fontB ? Profile.ColsB : Profile.ColsA);


        private static byte[] FeedDots(int n) => new byte[] { 0x1B, 0x4A, (byte)Math.Clamp(n, 0, 255) };

        public PaperProfile Profile { get; set; } = PaperProfile.FromColumns(32);

        public int RasterRows { get; private set; }

        private bool _fontB;

        private BluetoothSocket _socket;
        private Stream _outputStream;

        public bool IsConnected => _socket?.IsConnected == true;

        // ---------------- NEW: output queue ----------------
        private readonly record struct SendSegment(byte[] Data, int Chunk, int DelayMs, int PauseAfterMs);

        private readonly List<SendSegment> _queue = new();
        private readonly List<byte> _cur = new();

        private int ResolveChunk(int v) => v > 0 ? v : SendChunkSize;
        private int ResolveDelay(int v) => v >= 0 ? v : SendChunkDelayMs;

        private void CloseCurrentSegment()
        {
            if (_cur.Count == 0) return;
            _queue.Add(new SendSegment(_cur.ToArray(), SendChunkSize, SendChunkDelayMs, 0));
            _cur.Clear();
        }

        private void Enqueue(byte[] data, int chunk, int delayMs, int pauseAfterMs)
        {
            if (chunk <= 0) chunk = data.Length;
            bool isDefault = chunk == SendChunkSize && delayMs == SendChunkDelayMs && pauseAfterMs == 0;
            if (isDefault)
            {
                _cur.AddRange(data);       // normal data: merged into ONE continuous stream
                return;
            }
            CloseCurrentSegment();         // special pacing: keep it as its own segment
            _queue.Add(new SendSegment(data, chunk, delayMs, pauseAfterMs));
        }

        private static void WriteSegmentSync(Stream stream, SendSegment s)
        {
            int chunk = s.Chunk > 0 ? s.Chunk : s.Data.Length;
            for (int i = 0; i < s.Data.Length; i += chunk)
            {
                int len = Math.Min(chunk, s.Data.Length - i);
                stream.Write(s.Data, i, len);
                if (s.DelayMs > 0 && i + len < s.Data.Length)
                    System.Threading.Thread.Sleep(s.DelayMs);
            }
            if (s.PauseAfterMs > 0) System.Threading.Thread.Sleep(s.PauseAfterMs);
        }

        private async Task EnqueueOrSendAsync(byte[] payload, int chunk, int delayMs, int pauseAfterMs)
        {
            if (_outputStream == null)
                throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
            if (payload == null || payload.Length == 0) return;

            if (BufferOutput)
            {
                Enqueue(payload, chunk, delayMs, pauseAfterMs);
                return;
            }

            var stream = _outputStream;
            var seg = new SendSegment(payload, chunk, delayMs, pauseAfterMs);
            await Task.Run(() =>
            {
                WriteSegmentSync(stream, seg);
                stream.Flush();
            });
        }

        /// <summary>
        /// NEW: sends everything collected so far to the printer in one continuous stream.
        /// Call it once at the end of the receipt, before Disconnect().
        /// </summary>
        public async Task FlushAsync()
        {
            CloseCurrentSegment();
            if (_queue.Count == 0) return;

            var stream = _outputStream
                ?? throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

            var segments = _queue.ToArray();
            _queue.Clear();

            await Task.Run(() =>
            {
                foreach (var s in segments) WriteSegmentSync(stream, s);
                stream.Flush();
            });

            if (TailDelayMs > 0) await Task.Delay(TailDelayMs);
        }

        /// <summary>NEW: throws away anything not yet sent.</summary>
        public void ClearBuffer()
        {
            _queue.Clear();
            _cur.Clear();
        }

        // ---------------- connection ----------------
        public async Task ConnectAsync(string macAddress)
        {
            var adapter = BluetoothAdapter.DefaultAdapter
                ?? throw new InvalidOperationException("No Bluetooth adapter available on this device.");

            BluetoothDevice device = adapter.GetRemoteDevice(macAddress)
                ?? throw new InvalidOperationException($"Could not resolve device: {macAddress}");

            adapter.CancelDiscovery();      // discovery slows the link a lot

            var errors = new List<string>();

            if (await TryConnectAsync(() => device.CreateInsecureRfcommSocketToServiceRecord(SppUuid), errors, "insecure"))
                return;

            if (await TryConnectAsync(() => device.CreateRfcommSocketToServiceRecord(SppUuid), errors, "secure"))
                return;

            if (await TryConnectAsync(() => CreateFallbackChannelSocket(device), errors, "reflection channel 1"))
                return;

            throw new InvalidOperationException(
                "Could not connect using any known method (insecure, secure, reflection). Details: " + string.Join(" | ", errors));
        }

        private async Task<bool> TryConnectAsync(Func<BluetoothSocket> createSocket, List<string> errors, string label)
        {
            BluetoothSocket socket;
            try
            {
                socket = createSocket() ?? throw new InvalidOperationException("Socket creation returned null.");
            }
            catch (Exception ex)
            {
                errors.Add($"{label}: create failed - {ex.Message}");
                return false;
            }

            try
            {
                var connectTask = Task.Run(() => socket.Connect());
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(Math.Max(1, ConnectTimeoutSeconds)));
                var finished = await Task.WhenAny(connectTask, timeoutTask);

                if (finished == timeoutTask)
                {
                    errors.Add($"{label}: timed out after {ConnectTimeoutSeconds} seconds");
                    try { socket.Close(); } catch { }
                    return false;
                }

                await connectTask;

                _socket = socket;
                _outputStream = socket.OutputStream;
                ClearBuffer();
                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"{label}: {ex.GetType().Name} - {ex.Message}");
                try { socket.Close(); } catch { }
                return false;
            }
        }

        private static BluetoothSocket CreateFallbackChannelSocket(BluetoothDevice device)
        {
            var method = device.Class.GetMethod("createRfcommSocket", Java.Lang.Integer.Type);
            return (BluetoothSocket)method.Invoke(device, 1);
        }

        public async Task InitializeAsync(byte codePage = 2)
        {
            int w = Profile.WidthDots;
            int o = Profile.OffsetDots;

            // CHANGED: one write instead of three (same bytes, same order)
            await WriteRawAsync(new byte[]
            {
                0x1B, 0x40,                         // ESC @ reset
                0x1B, 0x74, codePage,               // ESC t n code page
                0x1B, 0x61, 0x00,
                0x1B, 0x20, 0x00,
                0x1B, 0x32,
                0x1D, 0x4C, (byte)(o & 0xFF), (byte)(o >> 8),
                0x1D, 0x57, (byte)(w & 0xFF), (byte)(w >> 8),
                0x1B, 0x24, 0x00, 0x00
            });
        }

        public async Task SetCodePageAsync(byte n)
        {
            await WriteRawAsync(new byte[] { 0x1B, 0x74, n });
        }

        public void Disconnect()
        {
            try
            {
                if (_outputStream != null && (_queue.Count > 0 || _cur.Count > 0))
                {
                    // Wait for FlushAsync to actually finish, not just for the lambda to return.
                    Task.Run(async () => await FlushAsync()).GetAwaiter().GetResult();
                }
            }
            catch { /* swallow – best effort */ }

            ClearBuffer();
            try { _outputStream?.Close(); } catch { }
            try { _socket?.Close(); } catch { }
            _outputStream = null;
            _socket = null;
        }

        public Task WriteRawAsync(byte[] payload) => WriteRawAsync(payload, SendChunkSize, SendChunkDelayMs);

        public Task WriteRawAsync(byte[] payload, int chunk, int delayMs) =>
            EnqueueOrSendAsync(payload, chunk, delayMs, 0);

        public async Task WriteGraphicAsync(byte[] imageBytes, int widthDots, int heightDots, byte threshold = 128)
        {
            using var original = SKBitmap.Decode(imageBytes)
                ?? throw new InvalidOperationException("Could not decode image bytes.");

            var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);
            using var resized = original.Resize(new SKImageInfo(widthDots, heightDots), sampling)
                ?? throw new InvalidOperationException("Image resize failed.");

            int bytesPerRow = (widthDots + 7) / 8;
            var raster = new byte[bytesPerRow * heightDots];
            SKColor[] pixels = resized.Pixels;      // CHANGED: one read instead of GetPixel per dot

            for (int y = 0; y < heightDots; y++)
            {
                for (int x = 0; x < widthDots; x++)
                {
                    SKColor px = pixels[y * widthDots + x];
                    double luminance = 0.299 * px.Red + 0.587 * px.Green + 0.114 * px.Blue;
                    if (luminance < threshold)
                    {
                        int byteIndex = y * bytesPerRow + (x / 8);
                        int bitIndex = 7 - (x % 8);
                        raster[byteIndex] |= (byte)(1 << bitIndex);
                    }
                }
            }

            byte xL = (byte)(bytesPerRow & 0xFF);
            byte xH = (byte)((bytesPerRow >> 8) & 0xFF);
            byte yL = (byte)(heightDots & 0xFF);
            byte yH = (byte)((heightDots >> 8) & 0xFF);

            var command = new byte[8 + raster.Length];
            command[0] = 0x1D; command[1] = 0x76; command[2] = 0x30; command[3] = 0x00;
            command[4] = xL; command[5] = xH; command[6] = yL; command[7] = yH;
            Array.Copy(raster, 0, command, 8, raster.Length);

            await EnqueueOrSendAsync(command, ResolveChunk(RasterChunkSize), ResolveDelay(RasterChunkDelayMs), 0);
        }

        /// <summary>
        /// Prints Code39 or Code128 as a raster image.
        /// </summary>
        public async Task WriteBarcodeAsync(string data,
                                            BarcodeType type = BarcodeType.Code128,
                                            double widthFraction = 0.8,
                                            int heightDots = 70,
                                            bool humanText = true,
                                            int maxModuleDots = 3,
                                            PrintAlign align = PrintAlign.Center)
        {
            if (string.IsNullOrWhiteSpace(data))
                throw new ArgumentException("Barcode data cannot be empty.", nameof(data));

            if (type == BarcodeType.Code39) data = data.ToUpperInvariant();

            string pattern = type == BarcodeType.Code39 ? BuildCode39Pattern(data) : BuildCode128Pattern(data);

            int totalModules = pattern.Sum(c => c - '0');
            const int quiet = 10;

            int maxDots = (int)(Profile.WidthDots * widthFraction);
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
            await PrintRasterAsync(bars.Length, h, (x, y) => bars[x], align);

            if (humanText) await PrintTextAsync(data, align, new PrintStyle(FontB: true));
        }

        public async Task AlignAsync(PrintAlign align)
        {
            byte code = align switch { PrintAlign.Center => 1, PrintAlign.Right => 2, _ => 0 };
            await WriteRawAsync(new byte[] { 0x1B, 0x61, code });
        }

        public async Task WriteTextAsync(string text)
        {
            await WriteRawAsync(PrinterEncoding.GetBytes(text));
        }

        private bool _doubleWide = false;
        private bool _doubleHigh = false;

        public async Task BoldAsync(bool on)
        {
            await WriteRawAsync(new byte[] { 0x1B, 0x45, (byte)(on ? 1 : 0) });
        }

        private async Task ApplySizeAsync()
        {
            byte n = (byte)(((_widthMul - 1) << 4) | (_heightMul - 1));
            await WriteRawAsync(new byte[] { 0x1D, 0x21, n });
        }

        public async Task UnderlineAsync(bool on)
        {
            await WriteRawAsync(new byte[] { 0x1B, 0x2D, (byte)(on ? 1 : 0) });
        }

        public async Task SetFontAsync(bool compressed)
        {
            await WriteRawAsync(new byte[] { 0x1B, 0x4D, (byte)(compressed ? 1 : 0) });
        }

        public static string NormalizeMacAddress(string raw)
        {
            string hexOnly = new string(raw.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

            if (hexOnly.Length != 12)
                throw new ArgumentException($"'{raw}' does not contain a valid 12-hex-digit MAC address.");

            return string.Join(":", Enumerable.Range(0, 6).Select(i => hexOnly.Substring(i * 2, 2)));
        }

        public async Task WriteLineAsync(string text = "")
        {
            await WriteRawAsync(PrinterEncoding.GetBytes(text + "\n"));
        }

        public static byte[] QrCodeBytes(string data, byte moduleSize = 6, char errorLevel = 'M')
        {
            var payload = Encoding.UTF8.GetBytes(data);
            int len = payload.Length + 3;
            byte ec = errorLevel switch { 'L' => 48, 'M' => 49, 'Q' => 50, _ => 51 };

            var b = new List<byte>();

            b.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00 });
            b.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, moduleSize });
            b.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, ec });
            b.AddRange(new byte[] { 0x1D, 0x28, 0x6B, (byte)(len & 0xFF), (byte)(len >> 8), 0x31, 0x50, 0x30 });
            b.AddRange(payload);
            b.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30 });

            return b.ToArray();
        }

        public static string BuildAccountPayload(string accNo, string name, decimal balance)
        {
            return $"Acct#: {accNo}\nName: {name}\nBalance: {balance:N2}";
        }

        public async Task PrintQrAsync(string acct_No, string cons_name, decimal total_balance)
        {
            string qrText = BuildAccountPayload(acct_No, cons_name, total_balance);
            byte[] qrBytes = QrCodeBytes(qrText, moduleSize: 6, errorLevel: 'M');

            await WriteRawAsync(new byte[] { 0x1B, 0x61, 0x01 });
            await WriteRawAsync(qrBytes);
            await WriteRawAsync(new byte[] { 0x0A, 0x0A });
            await WriteRawAsync(new byte[] { 0x1B, 0x61, 0x00 });
        }

        private (int wMul, int hMul, bool fontB) ResolveSize(PrintStyle s)
        {
            // explicit multipliers win
            if (s.WidthMul > 0 || s.HeightMul > 0)
            {
                int w = s.WidthMul > 0 ? s.WidthMul : 1;
                int h = s.HeightMul > 0 ? s.HeightMul : 1;
                return (Math.Clamp(w, 1, 8), Math.Clamp(h, 1, 8), s.FontB);
            }

            // named sizes (FontA unless Small)
            if (s.Size != TextSize.Default)
                return s.Size switch
                {
                    TextSize.Small => (1, 1, true),   // FontB 1x1  (smallest)
                    TextSize.Normal => (1, 1, false),  // FontA 1x1
                    TextSize.Big => (2, 2, false),  // FontA 2x2
                    TextSize.Large => (3, 3, false),  // FontA 3x3
                    _ => (1, 1, false)
                };

            // backwards compatible: old Wide / High / FontB flags
            return ((s.Wide ? 2 : 1), (s.High ? 2 : 1), s.FontB);
        }


        private async Task ApplyStyleAsync(PrintStyle s)
        {
            var (wMul, hMul, fontB) = ResolveSize(s);
            _widthMul = wMul;
            _heightMul = hMul;
            _fontB = fontB;

            // --- ESC ! (0x1B 0x21) : cheap printers obey this for 1x/2x -----------
            byte escBang = 0;
            if (fontB) escBang |= 0x01;   // font B
            if (s.Bold) escBang |= 0x08;   // bold
            if (_heightMul >= 2) escBang |= 0x10;   // double height
            if (_widthMul >= 2) escBang |= 0x20;   // double width   <-- the fix
            if (s.Underline) escBang |= 0x80;   // underline

            // --- GS ! (0x1D 0x21) : real multipliers 1x..8x, sent LAST so it wins --
            byte gsBang = (byte)(((_widthMul - 1) << 4) | (_heightMul - 1));

            await WriteRawAsync(new byte[]
            {
                0x1B, 0x4D, (byte)(fontB ? 1 : 0),       // ESC M  select font
                0x1B, 0x45, (byte)(s.Bold ? 1 : 0),      // ESC E  bold
                0x1B, 0x2D, (byte)(s.Underline ? 1 : 0), // ESC -  underline
                0x1B, 0x21, escBang,                     // ESC !  print mode
                0x1D, 0x21, gsBang                       // GS !   char size
            });
        }

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

        /// <summary>
        /// Text block: left / center / right justified, auto-wrapped. The underline is drawn below the text only.
        /// </summary>
        public async Task PrintTextAsync(string text, PrintAlign align = PrintAlign.Left, PrintStyle style = default)
        {
            bool underline = style.Underline;
            bool bold = style.Bold;
            await ApplyStyleAsync(style with { Underline = false });
            int cols = Cols;

            var buf = new List<byte>();
            foreach (var raw in Wrap(text, cols))
            {
                string l = raw.TrimEnd();
                int pad = align switch
                {
                    PrintAlign.Center => (cols - l.Length) / 2,
                    PrintAlign.Right => cols - l.Length,
                    _ => 0
                };

                if (pad > 0) buf.AddRange(PrinterEncoding.GetBytes(new string(' ', pad)));
                buf.AddRange(PrinterEncoding.GetBytes(l));

                if (underline && l.Length > 0)
                {
                    buf.AddRange(FeedDots(CellHeightDots));
                    await WriteRawAsync(buf.ToArray());
                    buf.Clear();
                    await PrintUnderlineStripAsync(new List<(int, int)> { (Math.Max(0, pad), l.Length) }, bold);
                }
                else
                {
                    buf.Add(0x0A);
                }
            }

            if (buf.Count > 0) await WriteRawAsync(buf.ToArray());
            await ApplyStyleAsync(default);
        }

        public async Task PrintRowAsync(string left, string right, PrintStyle style = default)
        {
            bool underline = style.Underline;
            bool bold = style.Bold;
            await ApplyStyleAsync(style with { Underline = false });
            int cols = Cols;
            left ??= ""; right ??= "";
            if (right.Length > cols - 2) right = right.Substring(0, Math.Max(0, cols - 2));

            var leftLines = Wrap(left, Math.Max(1, cols - right.Length - 1)).ToList();
            var buf = new List<byte>();

            for (int i = 0; i < leftLines.Count; i++)
            {
                string l = leftLines[i].TrimEnd();
                buf.AddRange(PrinterEncoding.GetBytes(l));

                var spans = new List<(int col, int len)>();
                if (l.Length > 0) spans.Add((0, l.Length));

                if (i == leftLines.Count - 1)
                {
                    int gapCols = Math.Max(1, cols - l.Length - right.Length);
                    buf.AddRange(PrinterEncoding.GetBytes(new string(' ', gapCols)));
                    buf.AddRange(PrinterEncoding.GetBytes(right));
                    if (right.Length > 0) spans.Add((l.Length + gapCols, right.Length));
                }

                if (underline && spans.Count > 0)
                {
                    buf.AddRange(FeedDots(CellHeightDots));
                    await WriteRawAsync(buf.ToArray());
                    buf.Clear();
                    await PrintUnderlineStripAsync(spans, bold);
                }
                else
                {
                    buf.Add(0x0A);
                }
            }

            if (buf.Count > 0) await WriteRawAsync(buf.ToArray());
            await ApplyStyleAsync(default);
        }

        public async Task PrintSeparatorAsync(char c = '-', bool fontB = false)
        {
            await ApplyStyleAsync(new PrintStyle(FontB: fontB));
            await WriteRawAsync(PrinterEncoding.GetBytes(new string(c, Cols) + "\n"));
            await ApplyStyleAsync(default);
        }

        public async Task FeedAsync(int lines = 1) => await WriteRawAsync(PrinterEncoding.GetBytes(new string('\n', lines)));

        public async Task PrintRulerAsync()
        {
            int cols = Cols;
            var chars = new char[cols];
            for (int i = 0; i < cols; i++) chars[i] = (char)('0' + ((i + 1) % 10));
            chars[0] = '|'; chars[cols - 1] = '|';
            await WriteRawAsync(PrinterEncoding.GetBytes(new string(chars) + "\n"));
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


        /// <summary>
        /// Core raster printer (GS v 0).
        /// The left margin (GS L) and print area (GS W) are set to 0 / full head while the image is sent and the image is
        /// placed in ABSOLUTE dots from the left edge of the head. The margin and width are restored afterwards for text.
        /// CHANGED:
        ///  - sendW: only the columns up to sendW are sent (blank columns on the right are never transmitted).
        ///  - vDouble: GS v 0 mode 2 (every data row is printed twice) to halve the data.
        ///  - the bands go into the same continuous stream as the rest of the receipt.
        /// </summary>
        private async Task PrintRasterAsync(int contentW, int h, Func<int, int, bool> isBlack,
                            PrintAlign align = PrintAlign.Center,
                            int sliceRows = -1, int chunk = -1, int chunkDelay = -1, int msPerRow = 0,
                            int sendW = -1, bool vDouble = false)
        {
            if (sliceRows < 0) sliceRows = RasterSliceRows;
            int useChunk = ResolveChunk(chunk > 0 ? chunk : RasterChunkSize);
            int useDelay = ResolveDelay(chunkDelay >= 0 ? chunkDelay : RasterChunkDelayMs);
            if (sendW < 0 || sendW > contentW) sendW = contentW;

            int head = PrinterHardware.HeadWidthDots;
            int headBytes = head / 8;
            int paperW = Profile.WidthDots;

            int x0 = align switch
            {
                PrintAlign.Center => (paperW - contentW) / 2,
                PrintAlign.Right => paperW - contentW,
                _ => 0
            };
            x0 = Math.Max(0, x0);
            x0 += Profile.OffsetDots;                                      // always absolute from the head edge

            int usedDots = Math.Min(head, x0 + sendW);
            int rowBytes = Math.Clamp((usedDots + 7) / 8, 1, headBytes);
            int rowDots = rowBytes * 8;

            // neutral margin + full-width print area, left aligned, so x0 is exact
            await WriteRawAsync(new byte[]
            {
                0x1B, 0x61, 0x00,
                0x1D, 0x4C, 0x00, 0x00,
                0x1D, 0x57, (byte)(head & 0xFF), (byte)(head >> 8)
            });

            sliceRows = sliceRows <= 0 ? h : sliceRows;
            sliceRows = Math.Clamp(sliceRows, 1, 2303);
            bool banded = sliceRows < h;
            byte mode = (byte)(vDouble ? 2 : 0);

            if (banded) await WriteRawAsync(new byte[] { 0x1B, 0x33, 0x00 });     // ESC 3 0: no gap between bands

            for (int y0 = 0; y0 < h; y0 += sliceRows)
            {
                int rows = Math.Min(sliceRows, h - y0);
                var cmd = new byte[8 + rowBytes * rows];
                cmd[0] = 0x1D; cmd[1] = 0x76; cmd[2] = 0x30; cmd[3] = mode;
                cmd[4] = (byte)(rowBytes & 0xFF); cmd[5] = (byte)(rowBytes >> 8);
                cmd[6] = (byte)(rows & 0xFF); cmd[7] = (byte)(rows >> 8);

                for (int r = 0; r < rows; r++)
                    for (int x = 0; x < sendW; x++)
                    {
                        if (!isBlack(x, y0 + r)) continue;
                        int px = x0 + x;
                        if (px >= rowDots) break;
                        cmd[8 + r * rowBytes + px / 8] |= (byte)(0x80 >> (px % 8));
                    }

                int pause = msPerRow > 0 ? rows * msPerRow : 0;
                await EnqueueOrSendAsync(cmd, useChunk, useDelay, pause);
                RasterRows += vDouble ? rows * 2 : rows;
            }

            if (banded) await WriteRawAsync(new byte[] { 0x1B, 0x32 });            // ESC 2: back to default spacing

            // restore the paper margin and width for text
            int o = Profile.OffsetDots;
            int w = Profile.WidthDots;
            await WriteRawAsync(new byte[]
            {
                0x1D, 0x4C, (byte)(o & 0xFF), (byte)(o >> 8),
                0x1D, 0x57, (byte)(w & 0xFF), (byte)(w >> 8)
            });
        }

        /// <summary>
        /// Prints an image as ESC * 33 (24-dot) strips in ONE continuous stream (LogoMode.BitImage).
        /// Only the trimmed part of the image is sent and it is positioned with the left margin.
        /// </summary>
        private async Task PrintBitImageAsync(int fullW, int trimLeft, int trimW, int h,
                                      Func<int, int, bool> isBlack, PrintAlign align)
        {
            int head = PrinterHardware.HeadWidthDots;
            int paperW = Profile.WidthDots;

            int baseX = align switch
            {
                PrintAlign.Center => (paperW - fullW) / 2,
                PrintAlign.Right => paperW - fullW,
                _ => 0
            };
            int x0 = Math.Max(0, baseX) + Profile.OffsetDots + trimLeft;
            x0 = Math.Min(x0, head - 8);

            int dataW = Math.Clamp(trimW, 1, Math.Max(1, head - x0));
            int lead = LogoPadWithZeros ? x0 : 0;
            int marginDots = LogoPadWithZeros ? 0 : x0;
            int totalW = lead + dataW;

            // ESC 3 0 makes any stray LF do nothing so only ESC J controls the feed.
            await WriteRawAsync(new byte[]
            {
        0x1B, 0x61, 0x00,                                              // left align
        0x1D, 0x4C, (byte)(marginDots & 0xFF), (byte)(marginDots >> 8),
        0x1D, 0x57, (byte)((head - marginDots) & 0xFF), (byte)((head - marginDots) >> 8),
        0x1B, 0x33, 0x00                                               // ESC 3 0 - zero line spacing
            });

            const int StripHeight = 24;                                  // ESC * mode 33 prints 24 dots tall
            int step = Math.Clamp(LogoStripFeedDots, 1, StripHeight);    // paper advance per strip, <= 24

            var all = new List<byte>();

            for (int y0 = 0; y0 < h; y0 += step)
            {
                var buf = new byte[5 + totalW * 3];
                buf[0] = 0x1B; buf[1] = 0x2A; buf[2] = 33;               // ESC * 33
                buf[3] = (byte)(totalW & 0xFF); buf[4] = (byte)(totalW >> 8);

                for (int x = 0; x < dataW; x++)
                {
                    int col = lead + x;
                    for (int b = 0; b < 3; b++)
                    {
                        byte v = 0;
                        for (int bit = 0; bit < 8; bit++)
                        {
                            int y = y0 + b * 8 + bit;
                            if (y < h && isBlack(trimLeft + x, y)) v |= (byte)(0x80 >> bit);
                        }
                        buf[5 + col * 3 + b] = v;
                    }
                }
                all.AddRange(buf);

                // ESC J n - print and feed n vertical motion units.
                // On the last strip advance only the rows still needed, so the bottom
                // of the logo is not followed by a blank gap.
                int advance = Math.Min(step, h - y0);
                all.AddRange(new byte[] { 0x1B, 0x4A, (byte)advance });
                RasterRows += advance;
            }

            await EnqueueOrSendAsync(all.ToArray(), ResolveChunk(LogoChunkSize), ResolveDelay(LogoChunkDelayMs), 0);

            // restore state for text
            int o = Profile.OffsetDots;
            int w = Profile.WidthDots;
            await WriteRawAsync(new byte[]
            {
        0x1B, 0x32,                                                   // ESC 2 - default line spacing
        0x1D, 0x4C, (byte)(o & 0xFF), (byte)(o >> 8),
        0x1D, 0x57, (byte)(w & 0xFF), (byte)(w >> 8)
            });
        }

        // ---------- NEW: logo preparation (decoded and thresholded ONCE, then cached) ----------
        private static readonly object LogoCacheLock = new();
        private static byte[] _logoCacheSrc;
        private static int _logoCacheW;
        private static int _logoCacheH;
        private static bool[] _logoCacheMask;

        private static (bool[] Mask, int H) PrepareLogoMask(byte[] imageBytes, int w)
        {
            lock (LogoCacheLock)
            {
                if (_logoCacheMask != null && _logoCacheW == w && _logoCacheSrc != null
                    && _logoCacheSrc.Length == imageBytes.Length && _logoCacheSrc.SequenceEqual(imageBytes))
                    return (_logoCacheMask, _logoCacheH);

                using var bmp = SKBitmap.Decode(imageBytes) ?? throw new InvalidOperationException("Bad image.");
                int h = Math.Max(1, (int)Math.Round(bmp.Height * (w / (double)bmp.Width)));

                using var resized = bmp.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKCubicResampler.Mitchell))
                    ?? throw new InvalidOperationException("Image resize failed.");

                SKColor[] px = resized.Pixels;
                var mask = new bool[w * h];
                for (int i = 0; i < mask.Length; i++)
                {
                    SKColor p = px[i];
                    mask[i] = p.Alpha >= 128 && (0.299 * p.Red + 0.587 * p.Green + 0.114 * p.Blue) < 128;
                }

                _logoCacheSrc = (byte[])imageBytes.Clone();
                _logoCacheW = w;
                _logoCacheH = h;
                _logoCacheMask = mask;
                return (mask, h);
            }
        }

        /// <summary>
        /// Prints a logo scaled to the paper. WidthFraction 1.0 uses the configured paper width.
        /// The image is decoded and thresholded once, then cached. BitImage is the default
        /// because it sends sequential 24-dot strips instead of GS v 0 raster bands.
        /// </summary>
        public async Task WriteLogoAsync(byte[] imageBytes, double widthFraction = 1.0,
                                         PrintAlign align = PrintAlign.Center)
        {
            int w = Math.Max(8, (int)(Profile.WidthDots * widthFraction) / 8 * 8);
            if (LogoMaxWidthDots > 0) w = Math.Max(8, Math.Min(w, LogoMaxWidthDots) / 8 * 8);

            var prepared = await Task.Run(() => PrepareLogoMask(imageBytes, w));
            bool[] mask = prepared.Mask;
            int h = prepared.H;

            Func<int, int, bool> isBlack = (x, y) => mask[y * w + x];

            // find the empty columns on the right (and left) so they are never sent
            int minX = w, maxX = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                    if (mask[row + x]) { if (x < minX) minX = x; if (x > maxX) maxX = x; }
            }
            if (maxX < 0) { minX = 0; maxX = 0; }

            if (LogoMode == LogoMode.BitImage)
            {
                await PrintBitImageAsync(w, minX, maxX - minX + 1, h, isBlack, align);
                return;
            }

            int sliceRows = LogoMode == LogoMode.RasterBands ? Math.Max(8, LogoSliceRows) : 0;   // 0 = whole logo, ONE command

            if (LogoVerticalDouble)
            {
                int halfRows = (h + 1) / 2;
                Func<int, int, bool> isBlackHalf = (x, y) =>
                {
                    int y2 = y * 2;
                    return mask[y2 * w + x] || (y2 + 1 < h && mask[(y2 + 1) * w + x]);
                };
                await PrintRasterAsync(w, halfRows, isBlackHalf, align,
                    sliceRows: sliceRows, chunk: LogoChunkSize, chunkDelay: LogoChunkDelayMs,
                    msPerRow: LogoMsPerRow, sendW: maxX + 1, vDouble: true);
            }
            else
            {
                await PrintRasterAsync(w, h, isBlack, align,
                    sliceRows: sliceRows, chunk: LogoChunkSize, chunkDelay: LogoChunkDelayMs,
                    msPerRow: LogoMsPerRow, sendW: maxX + 1, vDouble: false);
            }
        }

        /// <param name="widthFraction">QR width as a fraction of the paper.</param>
        public async Task PrintQrSizedAsync(string payload, double widthFraction = 0.35, int minModuleDots = 2)
        {
            using var gen = new QRCodeGenerator();
            using var qr = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var m = qr.ModuleMatrix;
            int n = m.Count;

            int module = Math.Max(minModuleDots, (int)(Profile.WidthDots * widthFraction) / n);
            int size = n * module;
            if (size > Profile.WidthDots)
                module = Math.Max(1, Profile.WidthDots / n);
            size = n * module;

            await PrintRasterAsync(size, size, (x, y) => m[y / module][x / module], PrintAlign.Center);
        }

        private async Task PrintUnderlineStripAsync(List<(int col, int len)> spans, bool bold)
        {
            if (spans.Count == 0) return;

            var segs = spans
                .Where(s => s.len > 0)
                .Select(s => (x0: ColToDot(s.col), x1: Math.Min(Profile.WidthDots, ColToDot(s.col + s.len))))
                .ToList();
            if (segs.Count == 0) return;

            int gap = Math.Max(0, UnderlineGapDots);
            int thick = UnderlineThickness(bold);
            int width = segs.Max(s => s.x1);

            // positioned by dots from the left edge, so it must NOT use the global alignment
            await PrintRasterAsync(width, gap + thick,
                (x, y) => y >= gap && segs.Any(s => x >= s.x0 && x < s.x1),
                PrintAlign.Left);

            await WriteRawAsync(FeedDots(UnderlineTailFeed));
        }


        public void Dispose() => Disconnect();
    }
}
