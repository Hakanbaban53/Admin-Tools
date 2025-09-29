using FTP_Tool.Models;
using FTP_Tool.Services;
using FTP_Tool.ViewModels;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace FTP_Tool
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource? _cts;
        private int _downloadedCount = 0;

        // Monitoring statistics
        private int _totalFilesMonitored = 0;
        private int _currentFilesInRemote = 0;
        private int _errorCount = 0;
        private DateTime _lastSuccessfulCheck = DateTime.MinValue;

        private readonly FtpService _ftpService = new();
        private readonly SettingsService _settings_service;
        private readonly CredentialService _credentialService = new(); // added
        private AppSettings _settings = new();

        private readonly MainViewModel _viewModel;
        private bool _isLoaded = false; // track whether initial load completed

        private readonly string _logFilePath;
        private readonly object _logFileLock = new();

        // threshold width below which sidebar becomes floating
        private const double SidebarCollapseWidth = 1000; // adjust as needed

        private LoggingService? _logging_service;

        // Monitoring service (extracted)
        private readonly MonitoringService _monitoringService = new();

        // Tray icon
        private NotifyIcon? _trayIcon;

        // suppress hide-to-tray when performing real exit from tray
        private bool _suppressHideOnClose = false;

        // expose collection for XAML binding
        public ObservableCollection<LogEntry> DisplayedLogEntriesPublic => _displayedLogEntries;

        // Email service
        private EmailService? _emailService;

        public MainWindow()
        {
            InitializeComponent();

            _settings_service = new SettingsService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FTP_Tool", "settings.json"));
            _viewModel = new MainViewModel(_ftpService);
            DataContext = this; // set to this so XAML can bind to _displayedLogEntriesPublic

            // initial UI state
            UpdateUiState(false);

            // wire lifetime events (implemented in partial files)
            Loaded += MainWindow_Loaded;
            Closing += Window_Closing; // lightweight handler moved to partial
            SizeChanged += MainWindow_SizeChanged;

            // prepare directories for logs
            var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FTP_Tool");
            var logDir = Path.Combine(baseDir, "logs");
            try { Directory.CreateDirectory(logDir); } catch { }

            // Use a dated log filename (daily) so logs are separated per day
            var datedName = $"activity-{DateTime.Now:yyyy-MM-dd}.log";
            _logFilePath = Path.Combine(logDir, datedName);

            // initialize tray and other platform-specific bits
            try { InitializeTray(); } catch { }

            // Try to instantiate logging service eagerly (will be re-applied on load with settings)
            try
            {
                _logging_service = new LoggingService(_logFilePath, _settings);
            }
            catch { }
        }

        private async Task SendTestEmailAsync()
        {
            // persist UI values into settings first
            try
            {
                _settings.SmtpHost = txtSmtpHost.Text.Trim();
                _settings.SmtpPort = int.TryParse(txtSmtpPort.Text, out var p) ? p : 25;
                _settings.SmtpEnableSsl = chkSmtpSsl.IsChecked == true;
                _settings.SmtpUsername = txtSmtpUser.Text.Trim();
                // store smtp password securely
                try { _credentialService.Save(_settings.SmtpHost ?? string.Empty, _settings.SmtpUsername ?? string.Empty, txtSmtpPass.Password ?? string.Empty, "smtp"); } catch { }

                _settings.EmailFrom = txtEmailFrom.Text.Trim();
                // gather recipients from listbox
                try
                {
                    var recips = lstEmailRecipients.Items.Cast<object>().Select(o => o.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                    _settings.EmailRecipients = string.Join(";", recips);
                }
                catch { _settings.EmailRecipients = string.Empty; }

                // weekdays persisted separately via PersistWeekdays called on checkbox changes
                _settings.WorkStart = txtWorkStart.Text.Trim();
                _settings.WorkEnd = txtWorkEnd.Text.Trim();
                _settings.LunchStart = txtLunchStart.Text.Trim();
                _settings.LunchEnd = txtLunchEnd.Text.Trim();
                _settings.AlertThresholdMinutes = int.TryParse(txtAlertThreshold.Text, out var th) ? th : 15;

                await _settings_service.SaveAsync(_settings);
            }
            catch { }

            // create email service lazily
            try
            {
                _emailService = new EmailService(_settings, _credentialService);
                await _emailService.SendTestEmailAsync();
                Log("Test email sent successfully.", LogLevel.Info);
                global::System.Windows.MessageBox.Show("Test email sent.", "Email", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"Sending test email failed: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        private async void BtnTestAlert_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // persist settings and save SMTP credential
                try
                {
                    _settings.SmtpHost = txtSmtpHost.Text.Trim();
                    _settings.SmtpPort = int.TryParse(txtSmtpPort.Text, out var p) ? p : 25;
                    _settings.SmtpEnableSsl = chkSmtpSsl.IsChecked == true;
                    _settings.SmtpUsername = txtSmtpUser.Text.Trim();
                    try { _credentialService.Save(_settings.SmtpHost ?? string.Empty, _settings.SmtpUsername ?? string.Empty, txtSmtpPass.Password ?? string.Empty, "smtp"); } catch { }

                    _settings.EmailFrom = txtEmailFrom.Text.Trim();
                    try
                    {
                        var recips = lstEmailRecipients.Items.Cast<object>().Select(o => o.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                        _settings.EmailRecipients = string.Join(";", recips);
                    }
                    catch { _settings.EmailRecipients = string.Empty; }

                    await _settings_service.SaveAsync(_settings);
                }
                catch { }

                // create service if needed
                _emailService ??= new EmailService(_settings, _credentialService);

                // send a simple alert email
                await _emailService.SendEmailAsync("FTP Monitor - Alert (test)", "This is a simulated alert from FTP Monitor (test). Please ignore.");
                Log("Test alert email sent.", LogLevel.Info);
                global::System.Windows.MessageBox.Show("Test alert sent.", "Email", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"Test alert failed: {ex.Message}", LogLevel.Error);
                global::System.Windows.MessageBox.Show($"Failed to send test alert: {ex.Message}", "Email Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}