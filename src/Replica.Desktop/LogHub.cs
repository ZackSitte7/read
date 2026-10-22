namespace Replica.Desktop;

/// <summary>各窗口共享的日志，对应原软件 TForm1.SYSLOG 与 TYYCtrlWin.SYSLOG。</summary>
internal sealed class LogHub
{
    private readonly List<string> _lines = [];
    private readonly object _gate = new();

    public event EventHandler<string>? Appended;

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return _lines.ToArray();
        }
    }

    public void Append(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (_gate)
        {
            _lines.Add(line);
            if (_lines.Count > 2000)
            {
                _lines.RemoveRange(0, _lines.Count - 2000);
            }
        }

        Appended?.Invoke(this, line);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
        }
    }
}
