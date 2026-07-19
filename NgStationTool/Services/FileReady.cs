using System.Diagnostics;

namespace NgStationTool.Services;

public readonly struct ReadyResult
{
    public bool Ok { get; init; }
    public string Reason { get; init; }
    public long Length { get; init; }
    public long Ms { get; init; }
}

/// <summary>文件写完判定：大小稳定 + 可打开 +（可选）图片魔数。</summary>
public static class FileReady
{
    public static bool IsUnlocked(string path, bool requireExclusiveFallback = true)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch
        {
            if (!requireExclusiveFallback) return false;
            try
            {
                using var fs2 = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch { return false; }
        }
    }

    public static bool HasImageMagic(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var len = (int)Math.Min(12, fs.Length);
            if (len < 3) return false;
            var buf = new byte[len];
            var read = fs.Read(buf, 0, len);
            if (read < 3) return false;
            // JPEG
            if (buf[0] == 0xFF && buf[1] == 0xD8 && buf[2] == 0xFF) return true;
            // PNG
            if (read >= 8 && buf[0] == 0x89 && buf[1] == 0x50 && buf[2] == 0x4E && buf[3] == 0x47) return true;
            // GIF
            if (read >= 6 && buf[0] == 0x47 && buf[1] == 0x49 && buf[2] == 0x46 && buf[3] == 0x38) return true;
            // BMP
            if (read >= 2 && buf[0] == 0x42 && buf[1] == 0x4D) return true;
            // WEBP RIFF....WEBP
            if (read >= 12 && buf[0] == 0x52 && buf[1] == 0x49 && buf[2] == 0x46 && buf[3] == 0x46
                && buf[8] == 0x57 && buf[9] == 0x45 && buf[10] == 0x42 && buf[11] == 0x50) return true;
            return false;
        }
        catch { return false; }
    }

    public static ReadyResult WaitReady(
        string path,
        int budgetMs,
        int sizeStableChecks,
        int sizeStableIntervalMs,
        int retryDelayMs,
        int maxRetries,
        int minBytes,
        bool requireImageMagic)
    {
        var sw = Stopwatch.StartNew();
        long lastLen = -1;
        var stable = 0;
        var attempt = 0;

        while (sw.ElapsedMilliseconds < budgetMs && attempt < maxRetries)
        {
            attempt++;
            if (!File.Exists(path))
                return new ReadyResult { Ok = false, Reason = "源不存在", Length = 0, Ms = sw.ElapsedMilliseconds };

            long len;
            try { len = new FileInfo(path).Length; }
            catch
            {
                Thread.Sleep(retryDelayMs);
                continue;
            }

            if (len < minBytes)
            {
                stable = 0;
                lastLen = len;
                Thread.Sleep(sizeStableIntervalMs);
                continue;
            }

            if (len == lastLen) stable++;
            else { stable = 0; lastLen = len; }

            if (stable < sizeStableChecks)
            {
                Thread.Sleep(sizeStableIntervalMs);
                continue;
            }

            if (!IsUnlocked(path))
            {
                stable = 0;
                Thread.Sleep(retryDelayMs);
                continue;
            }

            if (requireImageMagic && !HasImageMagic(path))
            {
                stable = 0;
                Thread.Sleep(retryDelayMs);
                continue;
            }

            long len2;
            try { len2 = new FileInfo(path).Length; }
            catch
            {
                Thread.Sleep(retryDelayMs);
                continue;
            }

            if (len2 != len || len2 < minBytes)
            {
                lastLen = len2;
                stable = 0;
                Thread.Sleep(sizeStableIntervalMs);
                continue;
            }

            return new ReadyResult { Ok = true, Reason = "ready", Length = len2, Ms = sw.ElapsedMilliseconds };
        }

        long final = 0;
        try { if (File.Exists(path)) final = new FileInfo(path).Length; } catch { /* ignore */ }
        return new ReadyResult
        {
            Ok = false,
            Reason = $"未就绪 size={final}",
            Length = final,
            Ms = sw.ElapsedMilliseconds
        };
    }
}
