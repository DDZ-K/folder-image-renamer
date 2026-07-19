namespace NgStationTool.Services;

public enum LogLevel
{
    Info,
    Success,
    Skip,
    Warn,
    Error
}

public sealed class LogEntry
{
    public DateTime Time { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; }
    public string Module { get; init; } = "";
    public string Message { get; init; } = "";

    public override string ToString()
        => $"[{Time:yyyy-MM-dd HH:mm:ss.fff}] [{Level}] [{Module}] {Message}";
}

/// <summary>线程安全环形日志 + 文件落盘。</summary>
public sealed class AppLogger
{
    private readonly object _lock = new();
    private readonly List<LogEntry> _entries = new();
    private readonly string _filePath;
    private int _maxLines;
    private int _writesSinceTrim;

    public event Action<LogEntry>? Logged;

    public AppLogger(string filePath, int maxLines = 500)
    {
        _filePath = filePath;
        _maxLines = Math.Max(50, maxLines);
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
        catch { /* ignore */ }
    }

    public void SetMaxLines(int n) => _maxLines = Math.Max(50, n);

    public void Info(string module, string msg) => Write(LogLevel.Info, module, msg);
    public void Success(string module, string msg) => Write(LogLevel.Success, module, msg);
    public void Skip(string module, string msg) => Write(LogLevel.Skip, module, msg);
    public void Warn(string module, string msg) => Write(LogLevel.Warn, module, msg);
    public void Error(string module, string msg) => Write(LogLevel.Error, module, msg);

    public void Write(LogLevel level, string module, string message)
    {
        var e = new LogEntry { Level = level, Module = module, Message = message };
        lock (_lock)
        {
            _entries.Add(e);
            if (_entries.Count > _maxLines)
                _entries.RemoveRange(0, _entries.Count - _maxLines);
            try
            {
                File.AppendAllText(_filePath, e + Environment.NewLine);
                _writesSinceTrim++;
                if (_writesSinceTrim >= 20)
                {
                    _writesSinceTrim = 0;
                    TrimFile();
                }
            }
            catch { /* 磁盘满等不拖垮主流程 */ }
        }
        try { Logged?.Invoke(e); } catch { /* UI 异常隔离 */ }
    }

    public List<LogEntry> Snapshot(int lastN = 200)
    {
        lock (_lock)
        {
            if (_entries.Count == 0) return new List<LogEntry>();
            var start = Math.Max(0, _entries.Count - lastN);
            return _entries.GetRange(start, _entries.Count - start);
        }
    }

    private void TrimFile()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var fi = new FileInfo(_filePath);
            if (fi.Length < 4096) return;
            var lines = File.ReadAllLines(_filePath);
            if (lines.Length <= _maxLines) return;
            var keep = lines.Skip(lines.Length - _maxLines).ToArray();
            File.WriteAllLines(_filePath, keep);
        }
        catch { /* ignore */ }
    }
}
