namespace FTP_Tool.Models
{
    public class AppSettings
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 21;
        public string Username { get; set; } = string.Empty;
        public string RemoteFolder { get; set; } = "/";
        public string LocalFolder { get; set; } = string.Empty;
        public int IntervalSeconds { get; set; } = 30;
        public bool DeleteAfterDownload { get; set; } = false;
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public string? LastPage { get; set; }
    }
}