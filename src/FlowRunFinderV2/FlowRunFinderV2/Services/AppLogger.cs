namespace FlowRunFinderV2.Services;

public sealed class AppLogger
{
    private readonly string _logsFolder;
    private readonly object _lock = new();
    private LogVerbosity _verbosity = LogVerbosity.Info;
    private string _connection = "none";

    public AppLogger(string logsFolder)
    {
        _logsFolder = logsFolder;
        Directory.CreateDirectory(_logsFolder);
    }

    public void SetVerbosity(LogVerbosity verbosity)
    {
        _verbosity = verbosity;
    }

    public void SetConnection(ConnectionProfile? connection)
    {
        _connection = connection is null
            ? "none"
            : $"{connection.Name} ({connection.Id}; {connection.EnvironmentUrl})";
    }

    public void Error(string message, Exception? exception = null)
    {
        Write(LogVerbosity.Error, message, exception);
    }

    public void Info(string message)
    {
        Write(LogVerbosity.Info, message);
    }

    public void Debug(string message)
    {
        Write(LogVerbosity.Debug, message);
    }

    public void Trace(string message)
    {
        Write(LogVerbosity.Trace, message);
    }

    private void Write(LogVerbosity level, string message, Exception? exception = null)
    {
        if (_verbosity == LogVerbosity.Off || level > _verbosity)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var path = Path.Combine(_logsFolder, $"{now:yyyy-MM-dd}.log");
        var safeMessage = message.Replace(Environment.NewLine, " ");
        var line = $"{now:O}\t{level}\tconnection={_connection}\t{safeMessage}";
        if (exception is not null)
        {
            line += $"\texception={exception.GetType().Name}: {exception.Message}";
        }

        lock (_lock)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
