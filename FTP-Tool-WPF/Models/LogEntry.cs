using System;

namespace FTP_Tool.Models
{
    public class LogEntry
    {
        public DateTime Time { get; set; }
        public FTP_Tool.LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;

        public string Formatted => $"[{Time:yyyy-MM-dd HH:mm:ss}] [{Level}] {Message}";
    }
}
