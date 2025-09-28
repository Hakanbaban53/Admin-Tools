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
        public bool StartWithWindows { get; set; } = true;

        // When started by OS (via startup), start minimized to tray instead of showing window
        public bool StartMinimizedOnStartup { get; set; } = true;

        // Minimize to system tray when the window is closed
        public bool MinimizeToTray { get; set; } = true;

        // Automatically start monitoring when app launches
        public bool StartMonitoringOnLaunch { get; set; } = false;

        // --- Network & Performance settings ---
        // Connection timeout for FTP operations (seconds)
        public int ConnectionTimeoutSeconds { get; set; } = 30;

        // Number of retry attempts for FTP operations
        public int MaxRetryAttempts { get; set; } = 3;

        // Use passive FTP mode when true
        public bool UsePassiveMode { get; set; } = true;

        // How many days to keep log files (retained files count will be approx equal to days)
        public int LogRetentionDays { get; set; } = 30;

        // --- Email / Alert settings ---
        // SMTP server settings
        public string SmtpHost { get; set; } = string.Empty;
        public int SmtpPort { get; set; } = 25;
        public bool SmtpEnableSsl { get; set; } = false;
        public string SmtpUsername { get; set; } = string.Empty;
        public string SmtpPassword { get; set; } = string.Empty; // stored plain-text for now

        // Email addresses
        public string EmailFrom { get; set; } = string.Empty;
        // Recipients are stored as a single string separated by ';'
        public string EmailRecipients { get; set; } = string.Empty;

        // --- What to send by email ---
        // Send periodic info summaries (downloads, runtime)
        public bool EmailOnInfo { get; set; } = false;
        // Send emails when warnings occur
        public bool EmailOnWarnings { get; set; } = true;
        // Send emails when errors occur
        public bool EmailOnErrors { get; set; } = true;
        // Interval (minutes) for sending periodic info summary emails
        public int EmailSummaryIntervalMinutes { get; set; } = 60;

        // Alert schedule
        // Comma separated list of enabled weekdays (e.g. "Mon,Tue,Wed") -- or empty for none
        public string AlertWeekdays { get; set; } = "Mon,Tue,Wed,Thu,Fri";

        // Work hours and lunch interval stored as HH:mm strings
        public string WorkStart { get; set; } = "08:00";
        public string WorkEnd { get; set; } = "17:00";
        public string LunchStart { get; set; } = "12:00";
        public string LunchEnd { get; set; } = "13:00";

        // Minutes to wait without any download before triggering alert
        public int AlertThresholdMinutes { get; set; } = 15;

        // Whether alerts are enabled at all (UI default: disabled to reduce noise)
        public bool AlertsEnabled { get; set; } = false;

        // If true, alerts are active at all times (ignore weekdays/work hours)
        public bool AlertAlways { get; set; } = false;
    }
}