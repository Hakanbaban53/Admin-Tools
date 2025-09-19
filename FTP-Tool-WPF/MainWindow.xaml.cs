using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FluentFTP;
using FTP_Tool.Services;
using FTP_Tool.Models;
using FTP_Tool.ViewModels;
using System.Diagnostics;
using System.Windows.Media;
using System.Runtime.ExceptionServices;
using System.Windows.Forms; // for NotifyIcon
using Microsoft.Win32; // for registry
using System.Collections.ObjectModel;

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
        private readonly CredentialService _credentialService = new CredentialService(); // added
        private AppSettings _settings = new();

        private MainViewModel _viewModel;
        private bool _isLoaded = false; // track whether initial load completed

        private readonly string _logFilePath;
        private readonly object _logFileLock = new();

        // threshold width below which sidebar becomes floating
        private const double SidebarCollapseWidth = 1000; // adjust as needed

        private LoggingService? _logging_service;

        // Monitoring service (extracted)
        private readonly MonitoringService _monitoringService = new MonitoringService();

        // Tray icon
        private NotifyIcon? _trayIcon;

        // expose collection for XAML binding
        public ObservableCollection<LogEntry> _displayedLogEntriesPublic => _displayedLogEntries;

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
        }
    }
}