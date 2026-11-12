using Android.Bluetooth;
using Android.OS;
using UUID = Java.Util.UUID;

namespace eMRBCS.Platforms.Android
{
    /// <summary>One reliable transport. WriteChunkAsync must not return until the
    /// chunk was actually handed to the stack (BLE: acknowledged by the printer).</summary>
    internal interface IPrinterLink : IDisposable
    {
        bool IsConnected { get; }
        string DeviceName { get; }
        int MaxChunk { get; }                 // 0 = no limit (SPP); BLE = MTU payload
        Task WriteChunkAsync(byte[] data, int offset, int count);
        void Close();
    }

    // ===================== Bluetooth Classic (SPP) =====================
    internal sealed class SppLink : IPrinterLink
    {
        private static readonly UUID SppUuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB");
        private BluetoothSocket _socket;
        private Stream _out;

        public bool IsConnected => _socket?.IsConnected == true;
        public string DeviceName { get; private set; } = "";
        public int MaxChunk => 0;

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

            // Bigger MTU = fewer packets. Old phones that refuse keep 20-byte chunks.
            try
            {
                _mtu = NewTcs();
                if (_gatt.RequestMtu(247))
                    await WithTimeout(_mtu.Task, 2, "MTU negotiation");
            }
            catch { _payload = 20; }
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

                if (!_noResponse) _written = NewTcs();
                if (!_char.SetValue(buf)) throw new IOException("BLE SetValue failed.");
                if (!_gatt.WriteCharacteristic(_char)) throw new IOException("BLE WriteCharacteristic rejected.");

                // Write-with-response: wait for the printer's ACK before the next chunk.
                // This is what makes cuts impossible on BLE.
                if (!_noResponse)
                    await WithTimeout(_written.Task, 5, "BLE write ack").ConfigureAwait(false);
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