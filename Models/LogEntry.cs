using System;

namespace QuakeServerManager.Models
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Success
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; }
        public string Message { get; }
        public LogLevel Level { get; }

        public LogEntry(string message, LogLevel level)
        {
            Timestamp = DateTime.Now;
            Message = message;
            Level = level;
        }
    }
}
