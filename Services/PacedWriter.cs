using System.Diagnostics;

namespace eMRBCS.Services;

/// Sends bytes at a target average rate using a clock, not fixed sleeps.
/// If a sleep overshoots (slow phones), the following chunks catch up.
internal static class PacedWriter
{
    public static void Write(Stream stream, byte[] data, int chunk, int bytesPerSecond, int prebuffer)
    {
        try { Thread.CurrentThread.Priority = ThreadPriority.AboveNormal; } catch { }
        if (chunk <= 0) chunk = 512;

        double ticksPerByte = bytesPerSecond > 0 ? (double)Stopwatch.Frequency / bytesPerSecond : 0;
        long maxLag = Stopwatch.Frequency / 10;           // if > 100 ms behind, restart the timeline
        var sw = Stopwatch.StartNew();
        long baseTicks = 0;
        int baseSent = prebuffer;                          // first bytes go out as a burst
        int sent = 0;

        while (sent < data.Length)
        {
            int len = Math.Min(chunk, data.Length - sent);
            stream.Write(data, sent, len);
            sent += len;

            if (ticksPerByte <= 0 || sent <= prebuffer) continue;

            long due = baseTicks + (long)((sent - baseSent) * ticksPerByte);
            long now = sw.ElapsedTicks;
            if (now - due > maxLag)                        // long stall: do not burst to "catch up"
            {
                baseTicks = now;
                baseSent = sent;
                continue;
            }
            WaitUntil(sw, due);
        }
        stream.Flush();
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