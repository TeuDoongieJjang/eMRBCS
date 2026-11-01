//using Android.Bluetooth;
//using Android.Content;
//using Android.Graphics;
//using Java.Util;
//using System.Text;

//namespace YourApp.Platforms.Android;

//public class BluetoothPrinterService : IBluetoothPrinterService
//{
//    // Common SPP UUID for Bluetooth printers
//    private readonly UUID _uuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB");

//    public async Task<bool> PrintSampleAsync(string printerName)
//    {
//        return await Task.Run(() =>
//        {
//            BluetoothAdapter? adapter = BluetoothAdapter.DefaultAdapter;

//            if (adapter == null)
//                throw new Exception("Bluetooth is not supported on this device.");

//            if (!adapter.IsEnabled)
//                throw new Exception("Bluetooth is turned off.");

//            var device = adapter.BondedDevices?
//                .FirstOrDefault(d => d.Name?.Equals(printerName, StringComparison.OrdinalIgnoreCase) == true);

//            if (device == null)
//                throw new Exception($"Paired printer '{printerName}' not found.");

//            using var socket = device.CreateRfcommSocketToServiceRecord(_uuid);

//            adapter.CancelDiscovery();
//            socket.Connect();

//            using var outputStream = socket.OutputStream;

//            // Initialize printer
//            outputStream.Write(new byte[] { 0x1B, 0x40 }, 0, 2); // ESC @

//            // Center align
//            outputStream.Write(new byte[] { 0x1B, 0x61, 0x01 }, 0, 3); // ESC a 1

//            // Print logo
//            var logoBytes = GetLogoEscPosBytes("logo.png");
//            if (logoBytes != null)
//            {
//                outputStream.Write(logoBytes, 0, logoBytes.Length);
//                FeedLines(outputStream, 1);
//            }

//            // Print title
//            outputStream.Write(new byte[] { 0x1B, 0x45, 0x01 }, 0, 3); // Bold ON
//            WriteText(outputStream, "MY STORE\n");
//            outputStream.Write(new byte[] { 0x1B, 0x45, 0x00 }, 0, 3); // Bold OFF

//            WriteText(outputStream, "123 Main Street\n");
//            WriteText(outputStream, "Phone: 0912-345-6789\n");
//            FeedLines(outputStream, 1);

//            // Left align
//            outputStream.Write(new byte[] { 0x1B, 0x61, 0x00 }, 0, 3); // ESC a 0

//            WriteText(outputStream, "Item A         2 x 100.00\n");
//            WriteText(outputStream, "Item B         1 x  50.00\n");
//            WriteText(outputStream, "------------------------------\n");
//            WriteText(outputStream, "TOTAL          PHP 250.00\n");
//            FeedLines(outputStream, 1);

//            // Center align
//            outputStream.Write(new byte[] { 0x1B, 0x61, 0x01 }, 0, 3);

//            // Barcode (CODE128 or printer-supported mode)
//            PrintBarcode(outputStream, "123456789012");

//            FeedLines(outputStream, 2);

//            WriteText(outputStream, "Thank you!\n");

//            FeedLines(outputStream, 4);

//            // Cut if supported
//            outputStream.Write(new byte[] { 0x1D, 0x56, 0x42, 0x00 }, 0, 4);

//            outputStream.Flush();
//            outputStream.Close();
//            socket.Close();

//            return true;
//        });
//    }

//    private void WriteText(System.IO.Stream stream, string text)
//    {
//        var bytes = Encoding.ASCII.GetBytes(text);
//        stream.Write(bytes, 0, bytes.Length);
//    }

//    private void FeedLines(System.IO.Stream stream, int lines)
//    {
//        for (int i = 0; i < lines; i++)
//        {
//            stream.WriteByte(0x0A); // LF
//        }
//    }

//    private void PrintBarcode(System.IO.Stream stream, string data)
//    {
//        // HRI position: below barcode
//        stream.Write(new byte[] { 0x1D, 0x48, 0x02 }, 0, 3);

//        // Barcode height
//        stream.Write(new byte[] { 0x1D, 0x68, 100 }, 0, 3);

//        // Barcode width
//        stream.Write(new byte[] { 0x1D, 0x77, 0x03 }, 0, 3);

//        // CODE128
//        // GS k m n d1...dn
//        // m = 73 for CODE128
//        var barcodeBytes = Encoding.ASCII.GetBytes(data);
//        var command = new List<byte>
//        {
//            0x1D, 0x6B, 0x49, (byte)barcodeBytes.Length
//        };
//        command.AddRange(barcodeBytes);

//        stream.Write(command.ToArray(), 0, command.Count);
//        stream.WriteByte(0x0A);
//    }

//    private byte[]? GetLogoEscPosBytes(string fileName)
//    {
//        try
//        {
//            using var stream = FileSystem.OpenAppPackageFileAsync(fileName).Result;
//            var bitmap = BitmapFactory.DecodeStream(stream);

//            if (bitmap == null)
//                return null;

//            // Resize to printer-friendly width
//            int targetWidth = 384; // 58mm printers often use 384 dots
//            int targetHeight = bitmap.Height * targetWidth / bitmap.Width;
//            var resized = Bitmap.CreateScaledBitmap(bitmap, targetWidth, targetHeight, true);

//            return ConvertBitmapToEscPos(resized);
//        }
//        catch
//        {
//            return null;
//        }
//    }

//    private byte[] ConvertBitmapToEscPos(Bitmap bitmap)
//    {
//        int width = bitmap.Width;
//        int height = bitmap.Height;

//        int bytesPerLine = (width + 7) / 8;
//        var imageBytes = new List<byte>();

//        // GS v 0
//        imageBytes.Add(0x1D);
//        imageBytes.Add(0x76);
//        imageBytes.Add(0x30);
//        imageBytes.Add(0x00);

//        imageBytes.Add((byte)(bytesPerLine % 256));
//        imageBytes.Add((byte)(bytesPerLine / 256));
//        imageBytes.Add((byte)(height % 256));
//        imageBytes.Add((byte)(height / 256));

//        for (int y = 0; y < height; y++)
//        {
//            for (int x = 0; x < bytesPerLine; x++)
//            {
//                byte b = 0;

//                for (int bit = 0; bit < 8; bit++)
//                {
//                    int xx = x * 8 + bit;
//                    if (xx >= width)
//                        continue;

//                    var pixel = new Android.Graphics.Color(bitmap.GetPixel(xx, y));
//                    int gray = (pixel.R + pixel.G + pixel.B) / 3;

//                    // threshold
//                    if (gray < 128)
//                    {
//                        b |= (byte)(1 << (7 - bit));
//                    }
//                }

//                imageBytes.Add(b);
//            }
//        }

//        return imageBytes.ToArray();
//    }
//}