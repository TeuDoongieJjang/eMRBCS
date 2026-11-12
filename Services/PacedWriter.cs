using System.Diagnostics;

namespace eMRBCS.Services;

internal static class PacedWriter
{
    public static void Write(Stream stream, byte[] data, int chunk, int bytesPerSecond,
                             int prebuffer, List<(int Pos, int Ms)>? pauses)
    {
        try { Thread.CurrentThread.Priority = ThreadPriority.AboveNormal; } catch { }
        if (chunk <= 0) chunk = 512;

        var cuts = (pauses ?? new()).Where(p => p.Pos > 0 && p.Pos < data.Length)
                                    .OrderBy(p => p.Pos).ToList();
        int start = 0;
        bool first = true;
        foreach (var (pos, ms) in cuts)
        {
            WriteSegment(stream, data, start, pos - start, chunk, bytesPerSecond, first ? prebuffer : 0);
            stream.Flush();
            if (ms > 0) Thread.Sleep(ms);
            start = pos;
            first = false;
        }
        WriteSegment(stream, data, start, data.Length - start, chunk, bytesPerSecond, first ? prebuffer : 0);
        stream.Flush();
    }

    private static void WriteSegment(Stream stream, byte[] data, int offset, int count,
                                     int chunk, int bytesPerSecond, int prebuffer)
    {
        if (count <= 0) return;
        double ticksPerByte = bytesPerSecond > 0 ? (double)Stopwatch.Frequency / bytesPerSecond : 0;
        long maxLag = Stopwatch.Frequency / 10;
        var sw = Stopwatch.StartNew();
        long baseTicks = 0;
        int baseSent = prebuffer;
        int sent = 0;

        while (sent < count)
        {
            int len = Math.Min(chunk, count - sent);
            stream.Write(data, offset + sent, len);
            sent += len;

            if (ticksPerByte <= 0 || sent <= prebuffer) continue;

            long due = baseTicks + (long)((sent - baseSent) * ticksPerByte);
            long now = sw.ElapsedTicks;
            if (now - due > maxLag) { baseTicks = now; baseSent = sent; continue; }
            WaitUntil(sw, due);
        }
    }

    private static void WaitUntil(Stopwatch sw, long due)
    {
        while (true)
        {
            long remain = due - sw.ElapsedTicks;
            if (remain <= 0) return;
            double ms = remain * 1000.0 / Stopwatch.Frequency;
            if (ms >= 4) Thread.Sleep((int)ms - 2);
            else Thread.Yield();
        }
    }
}