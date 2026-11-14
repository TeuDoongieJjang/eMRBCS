using Android.Bluetooth;
using Android.OS;
using System.Diagnostics;
using Thread = System.Threading.Thread;
using ThreadPriority = System.Threading.ThreadPriority;
using UUID = Java.Util.UUID;

namespace eMRBCS.Platforms.Android
{
    /// <summary>Decides how long to wait before the next chunk so the printer always has data, but never too much.</summary>
    internal sealed class Pacer
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly double _leadMs;
        private readonly int _maxAhead;
        private readonly Queue<(double DoneAt, int Bytes)> _q = new();
        private int _ahead;
        private double _freeAt;

        public Pacer(double leadMs, int maxAheadBytes)
        {
            _leadMs = Math.Max(0, leadMs);
            _maxAhead = Math.Max(256, maxAheadBytes);
        }

        private double Now => _sw.Elapsed.TotalMilliseconds;

        /// <summary>Milliseconds to wait before sending the next chunk (0 = send now).</summary>
        public double WaitMs(int bytes)
        {
            double now = Now;
            while (_q.Count > 0 && _q.Peek().DoneAt <= now) _ahead -= _q.Dequeue().Bytes;

            double wait = _freeAt - now - _leadMs;

            if (_ahead + bytes > _maxAhead)
            {
                int need = _ahead + bytes - _maxAhead, freed = 0;
                foreach (var c in _q)
                {
                    freed += c.Bytes;
                    if (freed >= need) { wait = Math.Max(wait, c.DoneAt - now); break; }
                }
            }
            return wait > 0 ? wait : 0;
        }

        /// <summary>Call right after a chunk was written.</summary>
        public void Sent(int bytes, double costMs)
        {
            double now = Now;
            _freeAt = Math.Max(_freeAt, now) + costMs;
            _q.Enqueue((_freeAt, bytes));
            _ahead += bytes;
        }

        /// <summary>Estimated ms the printer still needs to finish what was sent.</summary>
        public double RemainingMs => Math.Max(0, _freeAt - Now);
    }
    /// <summary>One reliable transport. WriteChunkAsync must not return until the
    /// chunk was actually handed to the stack (BLE: acknowledged by the printer).</summary>
    internal interface IPrinterLink : IDisposable
    {
        Task<double> WriteStreamAsync(byte[] data, int chunk, double[] chunkCostMs, int leadMs, int maxAheadBytes);
        bool IsConnected { get; }
        bool IsAcked { get; }            // true = every chunk confirmed by the printer
        string TransportName { get; }
        string DeviceName { get; }
        int MaxChunk { get; }
        Task WriteChunkAsync(byte[] data, int offset, int count);
        void Close();
    }

    // ===================== Bluetooth Classic (SPP) =====================
    internal sealed class SppLink : IPrinterLink
    {
        public bool IsAcked => false;
        public string TransportName => "SPP";
        private static readonly UUID SppUuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB");
        private BluetoothSocket _socket;
        private Stream _out;

        public bool IsConnected => _socket?.IsConnected == true;
        public string DeviceName { get; private set; } = "";
        public int MaxChunk => 0;

        public Task<double> WriteStreamAsync(byte[] data, int chunk, double[] chunkCostMs, int leadMs, int maxAheadBytes)
        {
            var s = _out ?? throw new InvalidOperationException("SPP not connected.");
            var tcs = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
            int step = Math.Max(1, chunk);

            // One dedicated thread for the whole job: no thread-pool hops, steady timing.
            var thread = new Thread(() =>
            {
                try
                {
                    var pacer = new Pacer(leadMs, maxAheadBytes);
                    for (int i = 0, ci = 0; i < data.Length; i += step, ci++)
                    {
                        int len = Math.Min(step, data.Length - i);
                        double wait = pacer.WaitMs(len);
                        if (wait >= 1) Thread.Sleep((int)wait);

                        s.Write(data, i, len);
                        pacer.Sent(len, ci < chunkCostMs.Length ? chunkCostMs[ci] : 0);
                    }
                    s.Flush();
                    tcs.TrySetResult(pacer.RemainingMs);
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
            })
            { IsBackground = true, Priority = ThreadPriority.Highest, Name = "EscPosSend" };

            thread.Start();
            return tcs.Task;
        }
        public async Task ConnectAsync(BluetoothDevice device, int timeoutSeconds)
        {
            try { DeviceName = device.Name ?? ""; } catch { }
            var errors = new List<string>();
            if (await Try(() => device.CreateInsecureRfcommSocketToServiceRecord(SppUuid), errors, "insecure", timeoutSeconds)) return;
            if (await Try(() => device.CreateRfcommSocketToServiceRecord(SppUuid), errors, "secure", timeoutSeconds)) return;
            if (await Try(() => CreateFallback(device), errors, "channel 1", timeoutSeconds)) return;
            throw new InvalidOperationException("SPP failed: " + string.Join(" | ", errors));
        }

        private async Task<bool> Try(Func<BluetoothSocket> create, List<string> errors, string label, int timeout)
        {
            BluetoothSocket socket = null;
            try
            {
                socket = create() ?? throw new InvalidOperationException("null socket");
                var connect = Task.Run(() => socket.Connect());
                var done = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(Math.Max(1, timeout))));
                if (done != connect) { errors.Add(label + ": timeout"); try { socket.Close(); } catch { } return false; }
                await connect;
                _socket = socket;
                _out = socket.OutputStream;
                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"{label}: {ex.Message}");
                try { socket?.Close(); } catch { }
                return false;
            }
        }

        private static BluetoothSocket CreateFallback(BluetoothDevice device)
        {
            var m = device.Class.GetMethod("createRfcommSocket", Java.Lang.Integer.Type);
            return (BluetoothSocket)m.Invoke(device, 1);
        }

        public Task WriteChunkAsync(byte[] data, int offset, int count)
        {
            var s = _out ?? throw new InvalidOperationException("SPP not connected.");
            return Task.Run(() => s.Write(data, offset, count));
        }

        public void Close()
        {
            try { _out?.Close(); } catch { }
            try { _socket?.Close(); } catch { }
            _out = null; _socket = null;
        }
        public void Dispose() => Close();
    }

    // ===================== BLE (GATT) =====================
    internal sealed class BleLink : BluetoothGattCallback, IPrinterLink
    {
        public bool IsAcked => !_noResponse;
        public string TransportName => _noResponse ? "BLE (no-ack)" : "BLE (acked)";
        // Service / characteristic UUIDs used by common ESC/POS BLE printers.
        private static readonly string[] KnownServices =
        {
            "0000ff00-0000-1000-8000-00805f9b34fb",
            "0000ffe0-0000-1000-8000-00805f9b34fb",
            "000018f0-0000-1000-8000-00805f9b34fb",
            "0000ae30-0000-1000-8000-00805f9b34fb",
            "49535343-fe7d-4ae5-8fa9-9fafd205e455",
            "e7810a71-73ae-499d-8c15-faa9aef0c3f2",
        };
        private static readonly string[] KnownChars =
        {
            "0000ff02-0000-1000-8000-00805f9b34fb",
            "0000ffe1-0000-1000-8000-00805f9b34fb",
            "00002af1-0000-1000-8000-00805f9b34fb",
            "0000ae01-0000-1000-8000-00805f9b34fb",
            "49535343-8841-43f4-a8d4-ecbe34729bb3",
            "bef8d6c9-9c21-4c9e-b632-bd58a1009f9f",
        };

        private BluetoothGatt _gatt;
        private BluetoothGattCharacteristic _char;
        private bool _noResponse;
        private int _payload = 20;                       // safe BLE default (ATT MTU 23 - 3)
        private volatile bool _connected;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private TaskCompletionSource<bool> _ready, _mtu, _written;

        public bool IsConnected => _connected && _gatt != null;
        public string DeviceName { get; private set; } = "";
        public int MaxChunk => _payload;

        private static TaskCompletionSource<bool> NewTcs() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ConnectAsync(BluetoothDevice device, int timeoutSeconds)
        {
            try { DeviceName = device.Name ?? ""; } catch { }

            _ready = NewTcs();
            var ctx = global::Android.App.Application.Context;
            // TRANSPORT_LE is essential on dual-mode phones (API 23+), otherwise
            // Android may try BR/EDR and fail on BLE-only printers.
            _gatt = Build.VERSION.SdkInt >= BuildVersionCodes.M
                ? device.ConnectGatt(ctx, false, this, BluetoothTransports.Le)
                : device.ConnectGatt(ctx, false, this);
            if (_gatt == null) throw new InvalidOperationException("ConnectGatt returned null.");

            await WithTimeout(_ready.Task, timeoutSeconds + 6, "BLE connect/discovery");
            _connected = true;
            PickCharacteristic();
            try { _gatt.RequestConnectionPriority(GattConnectionPriority.High); } catch { }

            // Bigger MTU = fewer packets. Old phones that refuse keep 20-byte chunks.
            try
            {
                _mtu = NewTcs();
                if (_gatt.RequestMtu(247))
                    await WithTimeout(_mtu.Task, 2, "MTU negotiation");
            }
            catch { _payload = 20; }
        }

        public async Task<double> WriteStreamAsync(byte[] data, int chunk, double[] chunkCostMs, int leadMs, int maxAheadBytes)
        {
            int step = Math.Max(1, chunk);
            var pacer = new Pacer(leadMs, maxAheadBytes);
            for (int i = 0, ci = 0; i < data.Length; i += step, ci++)
            {
                int len = Math.Min(step, data.Length - i);
                double wait = pacer.WaitMs(len);
                if (wait >= 1) await Task.Delay((int)wait).ConfigureAwait(false);

                await WriteChunkAsync(data, i, len).ConfigureAwait(false);
                pacer.Sent(len, ci < chunkCostMs.Length ? chunkCostMs[ci] : 0);
            }
            return pacer.RemainingMs;
        }
        private void PickCharacteristic()
        {
            BluetoothGattCharacteristic best = null; int bestScore = -1;
            foreach (var svc in _gatt.Services)
            {
                string s = svc.Uuid.ToString().ToLowerInvariant();
                foreach (var ch in svc.Characteristics)
                {
                    bool canWrite = ch.Properties.HasFlag(GattProperty.Write);
                    bool canNoResp = ch.Properties.HasFlag(GattProperty.WriteNoResponse);
                    if (!canWrite && !canNoResp) continue;
                    string c = ch.Uuid.ToString().ToLowerInvariant();
                    int score = (KnownServices.Contains(s) ? 4 : 0)
                              + (KnownChars.Contains(c) ? 4 : 0)
                              + (canWrite ? 2 : 0);   // with-response preferred = real flow control
                    if (score > bestScore) { bestScore = score; best = ch; }
                }
            }
            _char = best ?? throw new InvalidOperationException(
                "No writable BLE characteristic found (not an ESC/POS BLE printer?).");
            _noResponse = !best.Properties.HasFlag(GattProperty.Write);
            _char.WriteType = _noResponse ? GattWriteType.NoResponse : GattWriteType.Default;
        }

        public async Task WriteChunkAsync(byte[] data, int offset, int count)
        {
            if (!IsConnected || _char == null) throw new InvalidOperationException("BLE not connected.");
            if (count > _payload) throw new ArgumentOutOfRangeException(nameof(count), "Chunk exceeds MTU payload.");

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var buf = new byte[count];
                Buffer.BlockCopy(data, offset, buf, 0, count);

                _written = NewTcs();
                if (!_char.SetValue(buf)) throw new IOException("BLE SetValue failed.");
                if (!_gatt.WriteCharacteristic(_char)) throw new IOException("BLE WriteCharacteristic rejected.");

                // Acked: printer confirmed receipt (byte loss impossible).
                // No-ack: Android confirms the controller sent it (local flow control).
                await WithTimeout(_written.Task, 5, "BLE write confirm").ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
        {
            if (newState == ProfileState.Connected && status == GattStatus.Success)
            {
                try
                {
                    if (!gatt.DiscoverServices())
                        _ready?.TrySetException(new InvalidOperationException("DiscoverServices failed."));
                }
                catch (Exception ex) { _ready?.TrySetException(ex); }
            }
            else if (newState == ProfileState.Disconnected)
            {
                _connected = false;
                var ex = new IOException($"BLE disconnected (status {(int)status}).");
                _ready?.TrySetException(ex);
                _mtu?.TrySetException(ex);
                _written?.TrySetException(ex);
            }
            else
            {
                _ready?.TrySetException(new InvalidOperationException($"BLE connect error (status {(int)status})."));
            }
        }

        public override void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status)
        {
            if (status == GattStatus.Success) _ready?.TrySetResult(true);
            else _ready?.TrySetException(new InvalidOperationException($"Service discovery failed (status {(int)status})."));
        }

        public override void OnMtuChanged(BluetoothGatt gatt, int mtu, GattStatus status)
        {
            if (status == GattStatus.Success) _payload = Math.Clamp(mtu - 3, 20, 512);
            _mtu?.TrySetResult(true);
        }

        public override void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
        {
            if (status == GattStatus.Success) _written?.TrySetResult(true);
            else _written?.TrySetException(new IOException($"BLE write failed (status {(int)status})."));
        }

        private static async Task WithTimeout(Task task, int seconds, string what)
        {
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))).ConfigureAwait(false);
            if (done != task) throw new TimeoutException($"{what} timed out after {seconds}s.");
            await task.ConfigureAwait(false);
        }

        public void Close()
        {
            _connected = false;
            try { _gatt?.Disconnect(); } catch { }
            try { _gatt?.Close(); } catch { }
            _gatt = null; _char = null;
        }
        public void Dispose() => Close();
    }
}