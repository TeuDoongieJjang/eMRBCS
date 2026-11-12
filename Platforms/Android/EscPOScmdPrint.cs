using Android.Bluetooth;
using eMRBCS.Services;
using QRCoder;
using SkiaSharp;
using System.Diagnostics;
using System.Text;
using UUID = Java.Util.UUID;

namespace eMRBCS.Platforms.Android
{
    public enum PrintAlign { Left, Center, Right }
    public enum TextSize { Default, Small, Normal, Big, Large }
    /// <summary>Visual style of a separator/rule line.</summary>
    public enum SeparatorStyle
    {
        Blank,         // ' ' – just feed blank dots (old BlankLineDots)
        Solid,         // '-' / '_' – one continuous line
        DoubleSolid,   // '=' – two continuous lines
        Broken,        // '~' – short dashes with gaps
        DoubleBroken   // '#' – two lines of short dashes
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
    public enum LogoMode { Raster, RasterBands, BitImage }

    /// <summary>
    /// Hybrid ESC/POS printer.
    ///   * plain ASCII (0x20-0x7E) -> text mode (same bytes in CP437/CP850/GBK/..., no code page needed)
    ///   * anything else, underline, small font (default) -> drawn with SkiaSharp and sent as a raster image
    ///   * logo / barcode / QR / separators -> raster
    /// </summary>
    public class EscPOScmdPrint : IDisposable
    {
        private string _pendingMac;
        private int _pendingPostConnectDelayMs = 500;
        private int _pendingSettleMs = 400;
        private const int MaxCatchUpMs = 24;
        private static readonly UUID SppUuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB");

        // ---------------- capabilities ----------------
        private PrinterCaps _caps = PrinterCaps.Default;
        public PrinterCaps Caps
        {
            get => _caps;
            set { _caps = value ?? PrinterCaps.Default; ApplyCaps(); }
        }

        public string DeviceName { get; private set; } = "";

        public EscPOScmdPrint() { ApplyCaps(); }

        private void ApplyCaps()
        {
            SendChunkSize = _caps.SendChunkSize;
            SendChunkDelayMs = _caps.SendChunkDelayMs;
            TailDelayMs = _caps.TailDelayMs;
            RasterSliceRows = _caps.RasterSliceRows;
            RasterChunkSize = _caps.RasterChunkSize;
            RasterChunkDelayMs = _caps.RasterChunkDelayMs;
            LogoMode = _caps.LogoBitImage ? LogoMode.BitImage : LogoMode.RasterBands;
            LogoSliceRows = _caps.LogoSliceRows;
            LogoChunkSize = _caps.LogoChunkSize;
            LogoChunkDelayMs = _caps.LogoChunkDelayMs;
            LogoInterStripPauseMs = _caps.LogoStripPauseMs;
        }

        // ---------------- sending settings ----------------
        public bool BufferOutput { get; set; } = true;
        public int SendChunkSize { get; set; } = 512;
        public int SendChunkDelayMs { get; set; } = 4;
        public int TailDelayMs { get; set; } = 400;
        public int ConnectTimeoutSeconds { get; set; } = 6;

        // ---------------- raster settings ----------------
        public int RasterChunkSize { get; set; } = 192;
        public int RasterChunkDelayMs { get; set; } = 8;
        public int RasterSliceRows { get; set; } = 48;

        // ---------------- logo settings ----------------
        public LogoMode LogoMode { get; set; } = LogoMode.RasterBands;
        public bool LogoUseBitImage
        {
            get => LogoMode == LogoMode.BitImage;
            set => LogoMode = value ? LogoMode.BitImage : LogoMode.Raster;
        }
        public int LogoSliceRows { get; set; } = 24;
        public int LogoChunkSize { get; set; } = 192;
        public int LogoChunkDelayMs { get; set; } = 8;
        public int LogoMsPerRow { get; set; } = 0;
        public int LogoInterStripPauseMs { get; set; } = 0;
        public int LogoStripFeedDots { get; set; } = 22;
        public int LogoMaxWidthDots { get; set; } = 0;
        public bool LogoVerticalDouble { get; set; } = false;
        public bool LogoPadWithZeros { get; set; } = false;
        public int LogoThreshold { get; set; } = PrinterHardware.LogoThreshold;
        public bool LogoBolden { get; set; } = PrinterHardware.LogoBolden;

        // ---------------- state ----------------
        private bool _gsSizeDirty;

        public PaperProfile Profile { get; set; } = PaperProfile.FromColumns(32);
        public int RasterRows { get; private set; }

        private int Head => Profile.HeadDots > 0 ? Profile.HeadDots : 384;

        /// <summary>True for 32-column (2-inch) paper.</summary>
        public bool IsSmallPaper => Profile.ColsA <= 34;

        /// <summary>Columns of the default style (Font A, 1x).</summary>
        public int Cols => ColsFor(Plain);

        private bool UseMarginsNow => PrinterHardware.AlignBy == AlignMethod.Hardware && Profile.OffsetDots > 0;

        /// <summary>ESC a (printer-side alignment) is exact, so use it whenever the paper fills the head or margins are honoured.</summary>
        /// <summary>ESC a (printer-side alignment) is exact, so use it whenever the paper fills the head
        /// or hardware margins are honoured.</summary>
        private bool HwAlignAvailable => Profile.WidthDots == Head || UseMarginsNow;
        public string Describe() =>
            $"{(string.IsNullOrEmpty(DeviceName) ? "?" : DeviceName)} | {Profile.Name} align {Profile.Alignment} " +
            $"head {Profile.HeadDots} off {Profile.OffsetDots} | " +
            (Profile.OffsetDots == 0 ? "hw align" : UseMarginsNow ? "HW margin" : "space shift");

        private static byte[] FeedDots(int n) => new byte[] { 0x1B, 0x4A, (byte)Math.Clamp(n, 0, 255) };

        private BluetoothSocket _socket;
        private Stream _outputStream;
        public bool IsConnected => _socket?.IsConnected == true;

        // ======================================================================
        //  output queue
        // ======================================================================
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
            if (isDefault) { _cur.AddRange(data); return; }
            CloseCurrentSegment();
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
            if (_outputStream == null && _pendingMac == null)
                throw new InvalidOperationException("Not connected. Call ConnectAndPrepareAsync first.");
            if (payload == null || payload.Length == 0) return;

            if (BufferOutput) { Enqueue(payload, chunk, delayMs, pauseAfterMs); return; }

            await EnsureConnectedAsync();
            var stream = _outputStream;
            var seg = new SendSegment(payload, chunk, delayMs, pauseAfterMs);
            await RunSenderAsync(() => { WriteSegmentPaced(stream, seg); stream.Flush(); });
        }

        public async Task FlushAsync(bool sendTailDelay = true)
        {
            CloseCurrentSegment();
            if (_queue.Count == 0) return;

            // The job is completely built at this point -> NOW connect and stream it without gaps.
            await EnsureConnectedAsync();

            var stream = _outputStream
                ?? throw new InvalidOperationException("Not connected. Call ConnectAndPrepareAsync first.");

            var segments = _queue.ToArray();
            _queue.Clear();

            await RunSenderAsync(() =>
            {
                foreach (var s in segments) WriteSegmentPaced(stream, s);
                stream.Flush();
            });

            if (sendTailDelay && TailDelayMs > 0) await Task.Delay(TailDelayMs);
        }
        private static void WriteSegmentPaced(Stream stream, SendSegment s)
        {
            int chunk = s.Chunk > 0 ? s.Chunk : s.Data.Length;
            var clock = Stopwatch.StartNew();
            long due = 0;

            for (int i = 0; i < s.Data.Length; i += chunk)
            {
                int len = Math.Min(chunk, s.Data.Length - i);
                stream.Write(s.Data, i, len);

                if (s.DelayMs > 0 && i + len < s.Data.Length)
                {
                    due += s.DelayMs;
                    long now = clock.ElapsedMilliseconds;
                    long wait = due - now;

                    if (wait > 0)
                        System.Threading.Thread.Sleep((int)wait);
                    else if (-wait > MaxCatchUpMs)
                        due = now;      // too far behind (e.g. a stall): re-sync, do not burst
                }
            }

            if (s.PauseAfterMs > 0) System.Threading.Thread.Sleep(s.PauseAfterMs);
        }
        private Task WriteDirectAsync(byte[] data)
        {
            var stream = _outputStream
                ?? throw new InvalidOperationException("Not connected.");
            return RunSenderAsync(() => { stream.Write(data, 0, data.Length); stream.Flush(); });
        }
        private static Task RunSenderAsync(Action work) =>
            Task.Factory.StartNew(() =>
            {
                try { System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.AboveNormal; }
                catch { }
                work();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        public void ClearBuffer() { _queue.Clear(); _cur.Clear(); }

        // ======================================================================
        //  connection
        // ======================================================================
        public async Task ConnectAsync(string macAddress)
        {
            var adapter = BluetoothAdapter.DefaultAdapter
                ?? throw new InvalidOperationException("No Bluetooth adapter available on this device.");

            BluetoothDevice device = adapter.GetRemoteDevice(macAddress)
                ?? throw new InvalidOperationException($"Could not resolve device: {macAddress}");

            adapter.CancelDiscovery();

            try { DeviceName = device.Name ?? ""; } catch { }

            var errors = new List<string>();

            if (await TryConnectAsync(() => device.CreateInsecureRfcommSocketToServiceRecord(SppUuid), errors, "insecure")) return;
            if (await TryConnectAsync(() => device.CreateRfcommSocketToServiceRecord(SppUuid), errors, "secure")) return;
            if (await TryConnectAsync(() => CreateFallbackChannelSocket(device), errors, "reflection channel 1")) return;

            throw new InvalidOperationException(
                "Could not connect using any known method (insecure, secure, reflection). Details: " + string.Join(" | ", errors));
        }

        private async Task<bool> TryConnectAsync(Func<BluetoothSocket> createSocket, List<string> errors, string label)
        {
            BluetoothSocket socket;
            try { socket = createSocket() ?? throw new InvalidOperationException("Socket creation returned null."); }
            catch (Exception ex) { errors.Add($"{label}: create failed - {ex.Message}"); return false; }

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

        // ---------------- print density (optional, see PrinterHardware.DensityMethod) ----------------
        private static byte[] DensityBytes()
        {
            int level = Math.Clamp(PrinterHardware.DensityLevel, 0, 4);
            switch (PrinterHardware.DensityMethod)
            {
                case DensityMethod.Esc7HeatTime:
                    {
                        // ESC 7 n1 n2 n3 : max heating dots, heating time (x10us), heating interval (x10us)
                        int[] heat = { 80, 100, 120, 160, 200 };
                        byte interval = (byte)(level == 0 ? 2 : 40);
                        return new byte[] { 0x1B, 0x37, 7, (byte)heat[level], interval };
                    }
                case DensityMethod.Dc2Hash:
                    {
                        // DC2 # n : bits0-4 density, bits5-7 break time
                        int[] dens = { 10, 14, 18, 24, 31 };
                        return new byte[] { 0x12, 0x23, (byte)((2 << 5) | dens[level]) };
                    }
                default:
                    return Array.Empty<byte>();
            }
        }

        public async Task SetDensityAsync()
        {
            var b = DensityBytes();
            if (b.Length > 0) await WriteRawAsync(b);
        }



        public async Task WarmUpAsync(int settleMs = 400)
        {
            await EnsureConnectedAsync();
            await WriteDirectAsync(new byte[] { 0x1B, 0x40 });
            if (settleMs > 0) await Task.Delay(settleMs);
        }
        private byte[] BuildInitBytes()
        {
            int w = Profile.WidthDots;
            int o = Profile.OffsetDots;
            int head = Head;
            bool hw = UseMarginsNow;

            var cmd = new List<byte>
            {
                0x1B, 0x40,                 // ESC @ reset
                0x1B, 0x32,                 // ESC 2 default line spacing
                0x1B, 0x61, 0x00            // left align
            };
            // NOTE: no ESC t (code page) and no FS . : text is plain ASCII only, the rest is an image.

            cmd.AddRange(DensityBytes());

            bool sendMargins = hw || (PrinterHardware.ResetMargins && head > 384);
            if (sendMargins)
            {
                int left = hw ? o : 0;
                int area = hw ? w : head;
                cmd.AddRange(new byte[]
                {
                    0x1D, 0x4C, (byte)(left & 0xFF), (byte)(left >> 8),      // GS L left margin
                    0x1D, 0x57, (byte)(area & 0xFF), (byte)(area >> 8)       // GS W print area width
                });
            }

            _gsSizeDirty = false;
            return cmd.ToArray();
        }
        public async Task InitializeAsync() => await WriteRawAsync(BuildInitBytes());

        public async Task ConnectAndPrepareAsync(string macAddress, int postConnectDelayMs = 500, int settleMs = 400)
        {
            // Fail fast (before the expensive build) if Bluetooth is unusable.
            var adapter = BluetoothAdapter.DefaultAdapter
                ?? throw new InvalidOperationException("No Bluetooth adapter available on this device.");
            if (!adapter.IsEnabled)
                throw new InvalidOperationException("Bluetooth is turned off.");

            ClearBuffer();
            _pendingMac = macAddress;
            _pendingPostConnectDelayMs = postConnectDelayMs;
            _pendingSettleMs = settleMs;

            // The init sequence is always the first thing in the job; it gets a short pause after it
            // so the printer has finished applying margins/density before the first raster arrives.
            byte[] init = BuildInitBytes();
            int pause = Math.Max(0, settleMs / 2);

            if (BufferOutput)
            {
                Enqueue(init, SendChunkSize, SendChunkDelayMs, pause);
            }
            else
            {
                await EnsureConnectedAsync();
                await EnqueueOrSendAsync(init, SendChunkSize, SendChunkDelayMs, pause);
            }
        }
        private async Task EnsureConnectedAsync()
        {
            if (_outputStream != null) return;

            if (_pendingMac == null)
                throw new InvalidOperationException("Not connected. Call ConnectAndPrepareAsync first.");

            string mac = _pendingMac;
            _pendingMac = null;

            await ConnectAsync(mac);

            // let the RFCOMM link and the printer's UART settle
            if (_pendingPostConnectDelayMs > 0) await Task.Delay(_pendingPostConnectDelayMs);

            // wake / reset the printer, then wait until it is really ready for data
            await WriteDirectAsync(new byte[] { 0x1B, 0x40 });
            if (_pendingSettleMs > 0) await Task.Delay(_pendingSettleMs);
        }
        public void Disconnect()
        {
            try
            {
                if (_outputStream != null && (_queue.Count > 0 || _cur.Count > 0))
                    Task.Run(async () => await FlushAsync()).GetAwaiter().GetResult();
            }
            catch { }

            // give the printer time to drain its buffer before the socket is closed,
            // otherwise the last lines / barcode can be cut off
            try { if (_outputStream != null && TailDelayMs > 0) System.Threading.Thread.Sleep(TailDelayMs); } catch { }

            _pendingMac = null;
            ClearBuffer();
            try { _outputStream?.Close(); } catch { }
            try { _socket?.Close(); } catch { }
            _outputStream = null;
            _socket = null;
        }

        /// <summary>
        /// Flush, give the printer time to finish what is in its buffer, then close.
        /// Use in the finally block instead of Task.Delay(...) + Disconnect().
        /// </summary>
        public async Task CloseAsync(int extraDelayMs = 0)
        {
            if (_outputStream == null) { Disconnect(); return; }
            try { await FlushAsync(); } catch { }
            int wait = Math.Min(20000, 1000 + RasterRows * 2 + extraDelayMs);
            await Task.Delay(wait);
            Disconnect();
        }

        public Task WriteRawAsync(byte[] payload) => WriteRawAsync(payload, SendChunkSize, SendChunkDelayMs);
        public Task WriteRawAsync(byte[] payload, int chunk, int delayMs) => EnqueueOrSendAsync(payload, chunk, delayMs, 0);

        // ======================================================================
        //  style resolving
        // ======================================================================
        private readonly record struct Rs(int W, int H, bool FontB, bool Bold, bool Underline);
        private static readonly Rs Plain = new(1, 1, false, false, false);

        private (int wMul, int hMul, bool fontB) ResolveSize(PrintStyle s)
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
                    TextSize.Large => (3, 3, false),
                    _ => (1, 1, false)
                };
            return ((s.Wide ? 2 : 1), (s.High ? 2 : 1), s.FontB);
        }

        private Rs Resolve(PrintStyle s)
        {
            var (w, h, fb) = ResolveSize(s);
            return new Rs(w, h, fb, s.Bold || PrinterHardware.ForceBold, s.Underline);
        }

        /// <summary>style = 48 (anything wider than 34 columns). style32 = used on 32 when given.
        /// Otherwise smallOn32 = Font B 1x1, otherwise style.</summary>
        private PrintStyle PickStyle(PrintStyle style, PrintStyle? style32, bool smallOn32)
        {
            if (!IsSmallPaper) return style;
            if (style32.HasValue) return style32.Value;
            if (smallOn32) return style with { FontB = true, WidthMul = 1, HeightMul = 1 };
            return style;
        }

        private static int CellW(Rs r) => (r.FontB ? PrinterHardware.FontBDots : PrinterHardware.FontADots) * r.W;
        private int ColsFor(Rs r) => Math.Max(1, Profile.WidthDots / Math.Max(1, CellW(r)));

        // ======================================================================
        //  text mode
        // ======================================================================
        private static bool IsPlainAscii(string s)
        {
            foreach (char c in s) if (c < 0x20 || c > 0x7E) return false;
            return true;
        }

        private static string Normalize(string s) =>
            (s ?? "").Replace("\r", "").Replace("\t", "    ").Replace('\u00A0', ' ');

        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

        private static byte AlignCode(PrintAlign a) => a switch { PrintAlign.Center => 1, PrintAlign.Right => 2, _ => 0 };

        /// <summary>Hybrid decision: does this line have to be drawn as an image?</summary>
        private bool UseRaster(string text, Rs r)
        {
            switch (PrinterHardware.Render)
            {
                case RenderMode.RasterOnly: return true;
                case RenderMode.TextOnly: return false;
                default:
                    return r.Underline
                        || (r.FontB && PrinterHardware.SmallFont == SmallFontMode.Raster)
                        || !IsPlainAscii(text);
            }
        }

        private byte[] StyleBytes(Rs r, bool nativeUnderline)
        {
            byte escBang = 0;
            if (r.FontB) escBang |= 0x01;
            if (r.Bold) escBang |= 0x08;
            if (r.H >= 2) escBang |= 0x10;
            if (r.W >= 2) escBang |= 0x20;
            if (nativeUnderline) escBang |= 0x80;

            var cmd = new List<byte>
            {
                0x1B, 0x4D, (byte)(r.FontB ? 1 : 0),
                0x1B, 0x45, (byte)(r.Bold ? 1 : 0),
                0x1B, 0x21, escBang
            };

            bool needGs = r.W > 2 || r.H > 2;
            if (needGs || _gsSizeDirty)
            {
                cmd.AddRange(new byte[] { 0x1D, 0x21, (byte)(((r.W - 1) << 4) | (r.H - 1)) });
                _gsSizeDirty = needGs;
            }
            return cmd.ToArray();
        }

        /// <summary>
        /// One text line. If the printer can align (ESC a) that is used = exact centering whatever the font width.
        /// Only when the paper is narrower than the head (software offset) leading spaces are used.
        /// </summary>
        private void EmitTextLine(List<byte> buf, string text, PrintAlign align, Rs r)
        {
            bool nativeUl = r.Underline && PrinterHardware.Render == RenderMode.TextOnly;
            int textDots = text.Length * CellW(r);

            if (HwAlignAvailable)
            {
                buf.AddRange(new byte[] { 0x1B, 0x61, AlignCode(align) });
            }
            else
            {
                buf.AddRange(new byte[] { 0x1B, 0x61, 0x00 });

                int pad = align switch
                {
                    PrintAlign.Center => (Profile.WidthDots - textDots) / 2,
                    PrintAlign.Right => Profile.WidthDots - textDots,
                    _ => 0
                };
                pad = Math.Max(0, pad);

                // leading spaces are always printed in Font A 1x so the shift is exact to 1 cell (12 dots)
                int cell = Math.Max(1, PrinterHardware.FontADots);
                int lead = Profile.OffsetDots + pad;
                int leadCols = align == PrintAlign.Center ? (int)Math.Round(lead / (double)cell) : lead / cell;
                int maxLead = Math.Max(0, (Head - textDots) / cell);
                leadCols = Math.Clamp(leadCols, 0, maxLead);

                if (leadCols > 0)
                {
                    buf.AddRange(StyleBytes(Plain, false));
                    buf.AddRange(Ascii(new string(' ', leadCols)));
                }
            }

            buf.AddRange(StyleBytes(r, nativeUl));
            buf.AddRange(Ascii(text));
            buf.Add(0x0A);
        }

        private void EmitReset(List<byte> buf)
        {
            buf.AddRange(StyleBytes(Plain, false));
            buf.AddRange(new byte[] { 0x1B, 0x61, 0x00 });
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

        public async Task PrintTextAsync(string text, PrintAlign align = PrintAlign.Left,
                                         PrintStyle style = default, PrintStyle? style32 = null,
                                         bool smallOn32 = false)
        {
            style = PickStyle(style, style32, smallOn32);
            var r = Resolve(style);
            text = Normalize(text);

            if (UseRaster(text, r))
            {
                await PrintRasterTextAsync(text, align, r, wrap: true);
                return;
            }

            int cols = ColsFor(r);
            var buf = new List<byte>();
            foreach (var raw in Wrap(text, cols))
                EmitTextLine(buf, raw.TrimEnd(), align, r);
            EmitReset(buf);
            await WriteRawAsync(buf.ToArray());
        }

        public async Task PrintRowAsync(string left, string right,
                                        PrintStyle style = default, PrintStyle? style32 = null,
                                        bool smallOn32 = false)
        {
            style = PickStyle(style, style32, smallOn32);
            var r = Resolve(style);
            left = Normalize(left);
            right = Normalize(right);

            if (UseRaster(left + right, r))
            {
                await PrintRasterRowAsync(left, right, r);
                return;
            }

            int cols = ColsFor(r);
            var rows = new List<(string Text, PrintAlign Align)>();

            if (left.Length + 1 + right.Length > cols && right.Length > cols / 2)
            {
                // value too long to share a line -> stack: label first, value right aligned below
                foreach (var l in Wrap(left, cols)) rows.Add((l.TrimEnd(), PrintAlign.Left));
                foreach (var l in Wrap(right, cols)) rows.Add((l.TrimEnd(), PrintAlign.Right));
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
                        l = l + new string(' ', gap) + right;
                    }
                    rows.Add((l, PrintAlign.Left));
                }
            }

            var buf = new List<byte>();
            foreach (var (t, a) in rows) EmitTextLine(buf, t, a, r);
            EmitReset(buf);
            await WriteRawAsync(buf.ToArray());
        }

        /// <summary>Raw line (leading spaces kept, no word re-flow). Used for table rows.</summary>
        public async Task WriteLineAsync(string text = "")
        {
            text = Normalize(text);
            var r = Resolve(default);

            if (UseRaster(text, r))
            {
                await PrintRasterTextAsync(text, PrintAlign.Left, r, wrap: false);
                return;
            }

            int cols = ColsFor(r);
            var buf = new List<byte>();
            if (text.Length == 0) EmitTextLine(buf, "", PrintAlign.Left, r);
            for (int i = 0; i < text.Length; i += cols)
                EmitTextLine(buf, text.Substring(i, Math.Min(cols, text.Length - i)), PrintAlign.Left, r);
            EmitReset(buf);
            await WriteRawAsync(buf.ToArray());
        }

        public async Task AlignAsync(PrintAlign align)
            => await WriteRawAsync(new byte[] { 0x1B, 0x61, AlignCode(align) });

        public async Task WriteTextAsync(string text) => await WriteRawAsync(Ascii(Normalize(text)));

        public async Task FeedAsync(int lines = 1)
            => await WriteRawAsync(Enumerable.Repeat((byte)0x0A, Math.Max(0, lines)).ToArray());

        public async Task PrintRulerAsync()
        {
            int cols = Cols;
            var chars = new char[cols];
            for (int i = 0; i < cols; i++) chars[i] = (char)('0' + ((i + 1) % 10));
            chars[0] = '|'; chars[cols - 1] = '|';
            var buf = new List<byte>();
            EmitTextLine(buf, new string(chars), PrintAlign.Left, Plain);
            await WriteRawAsync(buf.ToArray());
        }

        public static string NormalizeMacAddress(string raw)
        {
            string hexOnly = new string(raw.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
            if (hexOnly.Length != 12)
                throw new ArgumentException($"'{raw}' does not contain a valid 12-hex-digit MAC address.");
            return string.Join(":", Enumerable.Range(0, 6).Select(i => hexOnly.Substring(i * 2, 2)));
        }

        // ======================================================================
        //  separators (solid raster rules instead of thin '-' characters)
        // ======================================================================
        // ======================================================================
        //  separators (raster rules instead of thin '-' characters)
        // ======================================================================
        public async Task PrintSeparatorAsync(char c = '-', bool fontB = false)
        {
            SeparatorStyle? style = c switch
            {
                ' ' => SeparatorStyle.Blank,
                '-' => SeparatorStyle.Solid,
                '_' => SeparatorStyle.Solid,      // kept for backward compatibility
                '=' => SeparatorStyle.DoubleSolid,
                '~' => SeparatorStyle.Broken,
                '#' => SeparatorStyle.DoubleBroken,
                _ => null
            };

            if (style.HasValue)
            {
                await PrintSeparatorAsync(style.Value, fontB);
                return;
            }

            // non-standard char → repeat it in text mode (unchanged behavior)
            var r = Resolve(new PrintStyle(FontB: fontB));
            await PrintTextAsync(new string(c, ColsFor(r)), PrintAlign.Left, new PrintStyle(FontB: fontB));
        }

        public async Task PrintSeparatorAsync(SeparatorStyle style, bool fontB = false)
        {
            if (style == SeparatorStyle.Blank)
            {
                await WriteRawAsync(FeedDots(PrinterHardware.BlankLineDots));
                return;
            }

            if (PrinterHardware.Render == RenderMode.TextOnly)
            {
                char c = style switch
                {
                    SeparatorStyle.DoubleSolid => '=',
                    SeparatorStyle.Broken => '-',
                    SeparatorStyle.DoubleBroken => '=',
                    _ => '-'
                };
                var r = Resolve(new PrintStyle(FontB: fontB));
                await PrintTextAsync(new string(c, ColsFor(r)), PrintAlign.Left, new PrintStyle(FontB: fontB));
                return;
            }

            await PrintRuleAsync(style);
        }

        private async Task PrintRuleAsync(SeparatorStyle style)
        {
            int t = Math.Max(1, PrinterHardware.SeparatorThicknessDots);
            int t2 = Math.Max(1, t - 1);                       // thinner second bar for double
            const int top = 5, bottom = 5, gap = 2;
            int w = Profile.WidthDots;

            int dashLen = Math.Max(4, PrinterHardware.SeparatorDashDots);
            int gapLen = Math.Max(2, PrinterHardware.SeparatorGapDots);
            int period = dashLen + gapLen;

            bool isDouble = style is SeparatorStyle.DoubleSolid or SeparatorStyle.DoubleBroken;
            bool isBroken = style is SeparatorStyle.Broken or SeparatorStyle.DoubleBroken;

            if (isDouble)
            {
                int h = top + t2 + gap + t2 + bottom;
                Func<int, int, bool> isBlack = (x, y) =>
                {
                    bool first = y >= top && y < top + t2;
                    bool second = y >= top + t2 + gap && y < top + t2 + gap + t2;
                    if (!first && !second) return false;
                    if (!isBroken) return true;
                    return (x % period) < dashLen;
                };
                await PrintRasterAsync(w, h, isBlack, PrintAlign.Left, sliceRows: h);
            }
            else
            {
                int h = top + t + bottom;
                Func<int, int, bool> isBlack = (x, y) =>
                {
                    if (y < top || y >= top + t) return false;
                    if (!isBroken) return true;
                    return (x % period) < dashLen;
                };
                await PrintRasterAsync(w, h, isBlack, PrintAlign.Left, sliceRows: h);
            }
        }
        // ======================================================================
        //  raster text (international characters, underline, small font)
        // ======================================================================
        private readonly record struct RLine(string L, string R, PrintAlign A);

        private sealed class FontSet : IDisposable
        {
            private readonly SKTypeface _primary;
            private readonly float _size;
            private readonly float _scaleX;
            private readonly Dictionary<string, SKFont> _fonts = new();

            public FontSet(int cellW, int wMul, int hMul, bool bold)
            {
                _primary = SKTypeface.FromFamilyName("monospace", bold ? SKFontStyle.Bold : SKFontStyle.Normal)
                           ?? SKTypeface.Default;

                // size the monospace font so one character is exactly cellW dots wide (Font A = 12, Font B = 9)
                const float probe = 20f;
                using (var p = new SKFont(_primary, probe))
                {
                    float adv = p.MeasureText("0");
                    if (adv < 1f) adv = probe * 0.6f;
                    _size = probe * cellW / adv * hMul;
                }
                _scaleX = (float)wMul / hMul;
            }

            private SKFont FontFor(int cp)
            {
                SKTypeface tf = _primary;
                if (cp > 0x7E && !_primary.ContainsGlyph(cp))
                {
                    var m = SKFontManager.Default.MatchCharacter(cp);   // system fallback font (accents, CJK, symbols)
                    if (m != null) tf = m;
                }

                string key = tf.FamilyName + "|" + tf.FontWeight;
                if (!_fonts.TryGetValue(key, out var f))
                {
                    f = new SKFont(tf, _size, _scaleX)
                    {
                        Edging = SKFontEdging.Alias,   // no grey anti-aliasing -> crisp, dark thermal output
                        Subpixel = false
                    };
                    _fonts[key] = f;
                }
                return f;
            }

            public List<(string Text, SKFont Font)> Runs(string text)
            {
                var runs = new List<(string, SKFont)>();
                var sb = new StringBuilder();
                SKFont cur = null;
                foreach (Rune rune in text.EnumerateRunes())
                {
                    var f = FontFor(rune.Value);
                    if (cur != null && !ReferenceEquals(cur, f))
                    {
                        runs.Add((sb.ToString(), cur));
                        sb.Clear();
                    }
                    cur = f;
                    sb.Append(rune.ToString());
                }
                if (cur != null && sb.Length > 0) runs.Add((sb.ToString(), cur));
                return runs;
            }

            public float Measure(string text)
            {
                if (string.IsNullOrEmpty(text)) return 0f;
                float w = 0f;
                foreach (var (t, f) in Runs(text)) w += f.MeasureText(t);
                return w;
            }

            public void Dispose()
            {
                foreach (var f in _fonts.Values) f.Dispose();
                _fonts.Clear();
            }
        }

        private static List<string> WrapMeasured(FontSet fs, string text, float maxW)
        {
            var lines = new List<string>();
            foreach (var para in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = "";
                foreach (var word in para.Split(' '))
                {
                    string cand = line.Length == 0 ? word : line + " " + word;
                    if (fs.Measure(cand) <= maxW) { line = cand; continue; }

                    if (line.Length > 0) { lines.Add(line); line = ""; }

                    string w = word;
                    while (w.Length > 1 && fs.Measure(w) > maxW)
                    {
                        int n = w.Length;
                        while (n > 1 && fs.Measure(w.Substring(0, n)) > maxW) n--;
                        lines.Add(w.Substring(0, n));
                        w = w.Substring(n);
                    }
                    line = w;
                }
                lines.Add(line);
            }
            return lines;
        }

        private static List<string> HardSplitMeasured(FontSet fs, string text, float maxW)
        {
            var res = new List<string>();
            string rest = text ?? "";
            if (rest.Length == 0) res.Add("");
            while (rest.Length > 0)
            {
                int n = rest.Length;
                while (n > 1 && fs.Measure(rest.Substring(0, n)) > maxW) n--;
                res.Add(rest.Substring(0, n));
                rest = rest.Substring(n);
            }
            return res;
        }

        private FontSet NewFontSet(Rs r) =>
            new FontSet(r.FontB ? PrinterHardware.FontBDots : PrinterHardware.FontADots, r.W, r.H, r.Bold);

        private async Task PrintRasterTextAsync(string text, PrintAlign align, Rs r, bool wrap)
        {
            int w = Profile.WidthDots;
            using var fs = NewFontSet(r);
            var lines = new List<RLine>();
            var parts = wrap ? WrapMeasured(fs, text, w) : HardSplitMeasured(fs, text, w);
            foreach (var p in parts) lines.Add(new RLine(wrap ? p.TrimEnd() : p, "", align));
            foreach (var ln in lines) await PrintOneRasterLineAsync(fs, r, ln);
        }

        private async Task PrintRasterRowAsync(string left, string right, Rs r)
        {
            int w = Profile.WidthDots;
            using var fs = NewFontSet(r);
            float gap = fs.Measure("  ");
            float rw = fs.Measure(right);
            var lines = new List<RLine>();

            if (rw > w / 2f && fs.Measure(left) + rw + gap > w)
            {
                foreach (var p in WrapMeasured(fs, left, w)) lines.Add(new RLine(p.TrimEnd(), "", PrintAlign.Left));
                foreach (var p in WrapMeasured(fs, right, w)) lines.Add(new RLine("", p.TrimEnd(), PrintAlign.Right));
            }
            else
            {
                var ll = WrapMeasured(fs, left, Math.Max(1f, w - rw - gap));
                for (int i = 0; i < ll.Count; i++)
                    lines.Add(new RLine(ll[i].TrimEnd(), i == ll.Count - 1 ? right : "", PrintAlign.Left));
            }

            foreach (var ln in lines) await PrintOneRasterLineAsync(fs, r, ln);
        }

        private async Task PrintOneRasterLineAsync(FontSet fs, Rs r, RLine ln)
        {
            int w = Profile.WidthDots;
            int cellH = (r.FontB ? PrinterHardware.FontBHeight : PrinterHardware.FontAHeight) * r.H;
            int baseline = (int)Math.Round(cellH * 0.80);

            int ulGap = Math.Max(0, PrinterHardware.UnderlineGapDots);
            int ulT = Math.Max(1, (r.Bold ? PrinterHardware.UnderlineBoldThicknessDots : PrinterHardware.UnderlineThicknessDots) * r.H);

            // same line pitch as text mode (30 dots) so raster and text lines mix evenly
            int lineH = r.FontB ? cellH + 8 : Math.Max(30, cellH + 6);
            if (r.Underline) lineH = Math.Max(lineH, baseline + ulGap + ulT + 3);

            using var bmp = new SKBitmap(new SKImageInfo(w, lineH, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };

            void DrawSegment(string s, float x)
            {
                if (string.IsNullOrEmpty(s)) return;
                float cx = x;
                foreach (var (t, f) in fs.Runs(s))
                {
                    canvas.DrawText(t, cx, baseline, f, paint);
                    cx += f.MeasureText(t);
                }
                if (r.Underline)
                    canvas.DrawRect(x, baseline + ulGap, Math.Max(1f, cx - x), ulT, paint);   // right under the text
            }

            if (ln.R.Length == 0)
            {
                float tw = fs.Measure(ln.L);
                float x = ln.A switch
                {
                    PrintAlign.Center => (w - tw) / 2f,
                    PrintAlign.Right => w - tw,
                    _ => 0f
                };
                DrawSegment(ln.L, Math.Max(0f, x));
            }
            else
            {
                DrawSegment(ln.L, 0f);
                DrawSegment(ln.R, Math.Max(0f, w - fs.Measure(ln.R)));
            }

            SKColor[] px = bmp.Pixels;
            var mask = new bool[w * lineH];
            int maxX = -1;
            for (int i = 0; i < mask.Length; i++)
            {
                if (px[i].Red < 160)
                {
                    mask[i] = true;
                    int x = i % w;
                    if (x > maxX) maxX = x;
                }
            }

            if (maxX < 0)
            {
                await WriteRawAsync(FeedDots(lineH));
                return;
            }

            await PrintRasterAsync(w, lineH, (x, y) => mask[y * w + x], PrintAlign.Left, sendW: maxX + 1);
        }

        // ======================================================================
        //  graphics
        // ======================================================================
        public async Task WriteGraphicAsync(byte[] imageBytes, int widthDots, int heightDots, byte threshold = 150)
        {
            using var original = SKBitmap.Decode(imageBytes)
                ?? throw new InvalidOperationException("Could not decode image bytes.");
            using var resized = original.Resize(new SKImageInfo(widthDots, heightDots), new SKSamplingOptions(SKCubicResampler.Mitchell))
                ?? throw new InvalidOperationException("Image resize failed.");

            SKColor[] pixels = resized.Pixels;
            await PrintRasterAsync(widthDots, heightDots, (x, y) =>
            {
                SKColor px = pixels[y * widthDots + x];
                double a = px.Alpha / 255.0;
                double lum = (0.299 * px.Red + 0.587 * px.Green + 0.114 * px.Blue) * a + 255 * (1 - a);
                return lum < threshold;
            }, PrintAlign.Left);
        }

        public async Task WriteBarcodeAsync(string data, BarcodeType type = BarcodeType.Code128,
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
            await PrintRasterAsync(bars.Length, h, (x, y) => bars[x], align);

            if (humanText) await PrintTextAsync(data, align, new PrintStyle(FontB: true));
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
            => $"Acct#: {accNo}\nName: {name}\nBalance: {balance:N2}";

        public async Task PrintQrAsync(string acct_No, string cons_name, decimal total_balance)
        {
            byte[] qrBytes = QrCodeBytes(BuildAccountPayload(acct_No, cons_name, total_balance), 6, 'M');
            await WriteRawAsync(new byte[] { 0x1B, 0x61, 0x01 });
            await WriteRawAsync(qrBytes);
            await WriteRawAsync(new byte[] { 0x0A, 0x0A });
            await WriteRawAsync(new byte[] { 0x1B, 0x61, 0x00 });
        }

        public async Task PrintQrSizedAsync(string payload, double widthFraction = 0.35, int minModuleDots = 2)
        {
            using var gen = new QRCodeGenerator();
            using var qr = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var m = qr.ModuleMatrix;
            int n = m.Count;

            int module = Math.Max(minModuleDots, (int)(Profile.WidthDots * widthFraction) / n);
            if (n * module > Profile.WidthDots) module = Math.Max(1, Profile.WidthDots / n);
            int size = n * module;

            await PrintRasterAsync(size, size, (x, y) => m[y / module][x / module], PrintAlign.Center);
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

        // ======================================================================
        //  raster (GS v 0)
        // ======================================================================
        private async Task PrintRasterAsync(int contentW, int h, Func<int, int, bool> isBlack,
                            PrintAlign align = PrintAlign.Center,
                            int sliceRows = -1, int chunk = -1, int chunkDelay = -1, int msPerRow = 0,
                            int sendW = -1, bool vDouble = false, bool absoluteX = false)
        {
            if (sliceRows < 0) sliceRows = RasterSliceRows;
            int useChunk = ResolveChunk(chunk > 0 ? chunk : RasterChunkSize);
            int useDelay = ResolveDelay(chunkDelay >= 0 ? chunkDelay : RasterChunkDelayMs);
            if (sendW < 0 || sendW > contentW) sendW = contentW;

            int head = Head;
            int headBytes = head / 8;
            int paperW = Profile.WidthDots;

            int x0 = align switch
            {
                PrintAlign.Center => (paperW - contentW) / 2,
                PrintAlign.Right => paperW - contentW,
                _ => 0
            };
            x0 = Math.Max(0, x0) + (absoluteX ? 0 : Profile.OffsetDots);

            int usedDots = Math.Min(head, x0 + sendW);
            int rowBytes = Math.Clamp((usedDots + 7) / 8, 1, headBytes);
            int rowDots = rowBytes * 8;

            var setup = new List<byte> { 0x1B, 0x61, 0x00 };
            if (UseMarginsNow)
                setup.AddRange(new byte[] { 0x1D, 0x4C, 0x00, 0x00, 0x1D, 0x57, (byte)(head & 0xFF), (byte)(head >> 8) });
            await WriteRawAsync(setup.ToArray());

            sliceRows = sliceRows <= 0 ? h : sliceRows;
            sliceRows = Math.Clamp(sliceRows, 1, 2303);
            bool banded = sliceRows < h;
            byte mode = (byte)(vDouble ? 2 : 0);

            if (banded) await WriteRawAsync(new byte[] { 0x1B, 0x33, 0x00 });

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

            if (banded) await WriteRawAsync(new byte[] { 0x1B, 0x32 });

            if (UseMarginsNow)
            {
                int o = Profile.OffsetDots, w = Profile.WidthDots;
                await WriteRawAsync(new byte[]
                {
                    0x1D, 0x4C, (byte)(o & 0xFF), (byte)(o >> 8),
                    0x1D, 0x57, (byte)(w & 0xFF), (byte)(w >> 8)
                });
            }
        }

        // ---------------- bit image (ESC * 33) ----------------
        private async Task PrintBitImageAsync(int fullW, int trimLeft, int trimW, int h,
                                              Func<int, int, bool> isBlack, PrintAlign align)
        {
            int head = Head;
            int paperW = Profile.WidthDots;

            int baseX = align switch
            {
                PrintAlign.Center => (paperW - fullW) / 2,
                PrintAlign.Right => paperW - fullW,
                _ => 0
            };
            int x0 = Math.Max(0, baseX) + Profile.OffsetDots + trimLeft;
            x0 = Math.Min(x0, head - 8);

            bool pad = LogoPadWithZeros || !UseMarginsNow;
            int dataW = Math.Clamp(trimW, 1, Math.Max(1, head - x0));
            int lead = pad ? x0 : 0;
            int marginDots = pad ? 0 : x0;
            int totalW = lead + dataW;

            var setup = new List<byte> { 0x1B, 0x61, 0x00 };
            if (UseMarginsNow)
                setup.AddRange(new byte[]
                {
                    0x1D, 0x4C, (byte)(marginDots & 0xFF), (byte)(marginDots >> 8),
                    0x1D, 0x57, (byte)((head - marginDots) & 0xFF), (byte)((head - marginDots) >> 8)
                });
            setup.AddRange(new byte[] { 0x1B, 0x33, 0x00 });
            await WriteRawAsync(setup.ToArray());

            const int StripHeight = 24;
            int step = Math.Clamp(LogoStripFeedDots, 1, StripHeight);

            await Task.Delay(50);

            for (int y0 = 0; y0 < h; y0 += step)
            {
                var buf = new byte[5 + totalW * 3 + 3];
                buf[0] = 0x1B; buf[1] = 0x2A; buf[2] = 33;
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

                int advance = Math.Min(step, h - y0);
                int tail = 5 + totalW * 3;
                buf[tail] = 0x1B; buf[tail + 1] = 0x4A; buf[tail + 2] = (byte)advance;

                await EnqueueOrSendAsync(buf, ResolveChunk(LogoChunkSize), ResolveDelay(LogoChunkDelayMs),
                                         LogoInterStripPauseMs);
                RasterRows += advance;
            }

            var restore = new List<byte> { 0x1B, 0x32 };
            if (UseMarginsNow)
            {
                int o = Profile.OffsetDots, w = Profile.WidthDots;
                restore.AddRange(new byte[]
                {
                    0x1D, 0x4C, (byte)(o & 0xFF), (byte)(o >> 8),
                    0x1D, 0x57, (byte)(w & 0xFF), (byte)(w >> 8)
                });
            }
            await WriteRawAsync(restore.ToArray());
        }

        // ---------- logo preparation (decoded and thresholded once, then cached) ----------
        private static readonly object LogoCacheLock = new();
        private static byte[] _logoCacheSrc;
        private static int _logoCacheW;
        private static int _logoCacheH;
        private static int _logoCacheThr;
        private static bool _logoCacheBold;
        private static bool[] _logoCacheMask;

        private static (bool[] Mask, int H) PrepareLogoMask(byte[] imageBytes, int w, int threshold, bool bolden)
        {
            lock (LogoCacheLock)
            {
                if (_logoCacheMask != null && _logoCacheW == w && _logoCacheThr == threshold && _logoCacheBold == bolden
                    && _logoCacheSrc != null && _logoCacheSrc.Length == imageBytes.Length
                    && _logoCacheSrc.SequenceEqual(imageBytes))
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
                    double a = p.Alpha / 255.0;
                    double lum = (0.299 * p.Red + 0.587 * p.Green + 0.114 * p.Blue) * a + 255 * (1 - a);
                    mask[i] = lum < threshold;      // 170 instead of 128 -> darker, fuller logo
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
                _logoCacheW = w; _logoCacheH = h; _logoCacheThr = threshold; _logoCacheBold = bolden;
                _logoCacheMask = mask;
                return (mask, h);
            }
        }

        public async Task WriteLogoAsync(byte[] imageBytes, double widthFraction = 1.0, PrintAlign align = PrintAlign.Center)
        {
            int maxW = Math.Min(Profile.WidthDots, Head - Profile.OffsetDots);
            int w = Math.Max(8, (int)(maxW * widthFraction) / 8 * 8);
            w = Math.Min(w, maxW / 8 * 8);
            if (LogoMaxWidthDots > 0) w = Math.Max(8, Math.Min(w, LogoMaxWidthDots) / 8 * 8);

            int thr = LogoThreshold, bold = LogoBolden ? 1 : 0;
            var prepared = await Task.Run(() => PrepareLogoMask(imageBytes, w, thr, bold == 1));
            bool[] mask = prepared.Mask;
            int h = prepared.H;

            Func<int, int, bool> isBlack = (x, y) => mask[y * w + x];

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

            int sliceRows = LogoMode == LogoMode.RasterBands ? Math.Max(8, LogoSliceRows) : 0;

            if (LogoVerticalDouble)
            {
                int halfRows = (h + 1) / 2;
                Func<int, int, bool> isBlackHalf = (x, y) =>
                {
                    int y2 = y * 2;
                    return mask[y2 * w + x] || (y2 + 1 < h && mask[(y2 + 1) * w + x]);
                };
                await PrintRasterAsync(w, halfRows, isBlackHalf, align, sliceRows: sliceRows,
                    chunk: LogoChunkSize, chunkDelay: LogoChunkDelayMs, msPerRow: LogoMsPerRow,
                    sendW: maxX + 1, vDouble: true);
            }
            else
            {
                await PrintRasterAsync(w, h, isBlack, align, sliceRows: sliceRows,
                    chunk: LogoChunkSize, chunkDelay: LogoChunkDelayMs, msPerRow: LogoMsPerRow,
                    sendW: maxX + 1, vDouble: false);
            }
        }

        public void Dispose() => Disconnect();
    }
}