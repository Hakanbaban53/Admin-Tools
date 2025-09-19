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

        // New logging settings
        // If true, logs will be persisted to the activity log file
        public bool LogToFile { get; set; } = true;

        // Minimum log level to show/write. Valid values: "Debug", "Info", "Warning", "Error"
        public string MinimumLogLevel { get; set; } = "Info";

        // Maximum number of lines to keep in the UI log textbox. Older lines will be trimmed.
        public int MaxLogLines { get; set; } = 1000;

        // Start application with Windows (auto-start)
        public bool StartWithWindows { get; set; } = false;

        // Minimize to system tray when the window is closed
        public bool MinimizeToTray { get; set; } = true;

        // Automatically start monitoring when app launches
        public bool StartMonitoringOnLaunch { get; set; } = false;
    }
}