using System;
using System.IO;
using System.Text;

namespace PBIPortWrapper.Services
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    public interface ILogger
    {
        void Log(LogLevel level, string category, string message, Exception exception = null);
        string GetLogFilePath();
    }

    public class LoggerService : ILogger
    {
        private readonly string _logDirectory;
        private readonly string _logFilePath;
        private const long MaxLogFileSizeBytes = 5 * 1024 * 1024; // 5 MB
        private const int MaxLogFiles = 5; // Keep last 5 rotated logs
        private readonly object _lockObject = new object();
        private LogLevel _minimumLevel = LogLevel.Info;

        public event EventHandler<LogEventArgs> OnLogMessage;

        public LoggerService(LogLevel minimumLevel = LogLevel.Info)
        {
            _minimumLevel = minimumLevel;
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PBIPortWrapper"
            );

            Directory.CreateDirectory(_logDirectory);
            _logFilePath = Path.Combine(_logDirectory, "log.txt");

            // Initialize or rotate logs if needed
            if (File.Exists(_logFilePath))
            {
                var fileInfo = new FileInfo(_logFilePath);
                if (fileInfo.Length > MaxLogFileSizeBytes)
                {
                    RotateLogs();
                }
            }
        }

        public void Log(LogLevel level, string category, string message, Exception exception = null)
        {
            if (level < _minimumLevel)
                return;

            lock (_lockObject)
            {
                try
                {
                    string logEntry = FormatLogEntry(level, category, message, exception);
                    
                    // Write to file
                    File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);

                    // Check if rotation needed
                    var fileInfo = new FileInfo(_logFilePath);
                    if (fileInfo.Length > MaxLogFileSizeBytes)
                    {
                        RotateLogs();
                    }

                    // Raise event for UI logging
                    OnLogMessage?.Invoke(this, new LogEventArgs(level, logEntry));
                }
                catch
                {
                    // If logging fails, silently ignore
                }
            }
        }

        public void SetMinimumLogLevel(LogLevel level)
        {
            _minimumLevel = level;
        }

        public string GetLogFilePath()
        {
            return _logFilePath;
        }

        private string FormatLogEntry(LogLevel level, string category, string message, Exception exception)
        {
            var sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ");
            sb.Append($"[{level.ToString().ToUpper()}] ");
            
            if (!string.IsNullOrEmpty(category))
            {
                sb.Append($"[{category}] ");
            }

            sb.Append(message);

            if (exception != null)
            {
                sb.AppendLine();
                sb.Append($"  Exception: {exception.GetType().Name}: {exception.Message}");
                sb.AppendLine();
                sb.Append($"  Stack Trace: {exception.StackTrace}");
            }

            return sb.ToString();
        }

        private void RotateLogs()
        {
            try
            {
                // Delete oldest log if we already have max files
                for (int i = MaxLogFiles - 1; i >= 1; i--)
                {
                    string oldPath = $"{_logFilePath}.{i}";
                    string newPath = $"{_logFilePath}.{i + 1}";

                    if (File.Exists(oldPath))
                    {
                        if (File.Exists(newPath))
                            File.Delete(newPath);
                        File.Move(oldPath, newPath);
                    }
                }

                // Rename current log to .1
                if (File.Exists(_logFilePath))
                {
                    string rotatedPath = $"{_logFilePath}.1";
                    if (File.Exists(rotatedPath))
                        File.Delete(rotatedPath);
                    File.Move(_logFilePath, rotatedPath);
                }
            }
            catch
            {
                // If rotation fails, just continue with current log
            }
        }
    }

    public class LogEventArgs : EventArgs
    {
        public LogLevel Level { get; }
        public string Message { get; }

        public LogEventArgs(LogLevel level, string message)
        {
            Level = level;
            Message = message;
        }
    }

    // Extension methods for easier logging
    public static class LoggerExtensions
    {
        public static void LogDebug(this ILogger logger, string category, string message)
            => logger.Log(LogLevel.Debug, category, message);

        public static void LogInfo(this ILogger logger, string category, string message)
            => logger.Log(LogLevel.Info, category, message);

        public static void LogWarning(this ILogger logger, string category, string message)
            => logger.Log(LogLevel.Warning, category, message);

        public static void LogError(this ILogger logger, string category, string message, Exception exception = null)
            => logger.Log(LogLevel.Error, category, message, exception);

        public static void LogConnectionInfo(this ILogger logger, string remoteEndPoint, int proxyPort, int targetPort, string modelName)
            => logger.LogInfo("Proxy", $"Connection from {remoteEndPoint} | Proxy Port: {proxyPort} -> Target Port: {targetPort} | Model: {modelName}");

        public static void LogConnectionClosed(this ILogger logger, string remoteEndPoint, int proxyPort, int activeConnections)
            => logger.LogInfo("Proxy", $"Connection closed from {remoteEndPoint} | Proxy Port: {proxyPort} | Active: {activeConnections}");
    }
}
