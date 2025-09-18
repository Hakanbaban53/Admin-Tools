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
using System.Windows.Media.Animation;
using System.Collections.Generic;

namespace FTP_Tool
{
    public partial class MainWindow : Window
    {
        private System.Threading.Timer? _timer;
        private CancellationTokenSource? _cts;
        private int _downloadedCount = 0;

        private readonly FtpService _ftpService = new();
        private readonly SettingsService _settings_service;
        private readonly CredentialService _credentialService = new CredentialService(); // added
        private AppSettings _settings = new();

        private MainViewModel _viewModel;
        private bool _isLoaded = false; // track whether initial load completed

        private readonly string _logFilePath;

        public enum LogLevel { Info, Warning, Error }

        // threshold width below which sidebar becomes floating
        private const double SidebarCollapseWidth = 1000; // adjust as needed

        // Prevent overlapping scheduled runs
        private readonly SemaphoreSlim _scheduledSemaphore = new(1, 1);

        // Keep a bounded in-memory log buffer for the UI to avoid unbounded TextBox growth
        private readonly LinkedList<string> _logLines = new();
        private const int MaxLogLines = 2000; // keep this configurable if needed

        public MainWindow()
        {
            InitializeComponent();
            _settings_service = new SettingsService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FTP_Tool", "settings.json"));
            _viewModel = new MainViewModel(_ftpService);
            DataContext = _viewModel;
            UpdateUiState(false);
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;

            // react to size changes for responsive behavior
            SizeChanged += MainWindow_SizeChanged;

            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FTP_Tool");
            try { Directory.CreateDirectory(logDir); } catch { }
            _logFilePath = Path.Combine(logDir, "activity.log");
        }

        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            _settings = await _settings_service.LoadAsync();
            // populate UI
            txtHost.Text = _settings.Host;
            txtPort.Text = _settings.Port.ToString();
            txtUsername.Text = _settings.Username;
            txtRemoteFolder.Text = string.IsNullOrWhiteSpace(_settings.RemoteFolder) ? "/" : _settings.RemoteFolder;
            txtLocalFolder.Text = _settings.LocalFolder;
            txtInterval.Text = _settings.IntervalSeconds.ToString();
            chkDeleteAfterDownload.IsChecked = _settings.DeleteAfterDownload;

            // load saved password from Windows Credential Manager (if present)
            try
            {
                var cred = _credentialService.Load(_settings.Host ?? string.Empty, _settings.Username ?? string.Empty);
                if (cred.HasValue && !string.IsNullOrEmpty(cred.Value.Password))
                {
                    txtPassword.Password = cred.Value.Password;
                }
            }
            catch { /* ignore credential errors */ }

            if (_settings.WindowWidth.HasValue) Width = _settings.WindowWidth.Value;
            if (_settings.WindowHeight.HasValue) Height = _settings.WindowHeight.Value;

            // restore last page without animation to avoid flash
            var last = string.IsNullOrWhiteSpace(_settings.LastPage) ? "Monitor" : _settings.LastPage;
            ShowPage(last, animate: false);

            // mark loaded so subsequent ShowPage calls animate
            _isLoaded = true;

            // Bind UI to viewmodel properties
            lblStatus.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("StatusText"));
            txtLastCheck.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("LastCheck"));
            txtLastError.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("LastError"));
            lblDownloaded.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("DownloadedCount") { StringFormat = "Downloaded: {0} files" });

            // Ensure initial responsive state
            UpdateResponsiveSidebar();
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // save current settings
            _settings.Host = txtHost.Text.Trim();
            _settings.Port = int.TryParse(txtPort.Text, out var p) ? p : 21;
            _settings.Username = txtUsername.Text.Trim();
            _settings.RemoteFolder = txtRemoteFolder.Text.Trim();
            _settings.LocalFolder = txtLocalFolder.Text.Trim();
            _settings.IntervalSeconds = int.TryParse(txtInterval.Text, out var s) ? s : 30;
            _settings.DeleteAfterDownload = chkDeleteAfterDownload.IsChecked == true;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;

            try
            {
                // persist last page
                _settings.LastPage = MonitorPage.Visibility == Visibility.Visible ? "Monitor" :
                    (SettingsPage.Visibility == Visibility.Visible ? "Settings" :
                    (HistoryPage.Visibility == Visibility.Visible ? "History" :
                    (SchedulePage.Visibility == Visibility.Visible ? "Schedule" :
                    (LogsPage.Visibility == Visibility.Visible ? "Logs" :
                    (AboutPage.Visibility == Visibility.Visible ? "About" : "Monitor")))));

                if (_settings_service != null) await _settings_service.SaveAsync(_settings);

                // save password to credential manager (credential service will delete if password empty)
                try
                {
                    _credentialService.Save(_settings.Host ?? string.Empty, _settings.Username ?? string.Empty, txtPassword.Password ?? string.Empty);
                }
                catch { }
            }
            catch { /* ignore save errors on close */ }

            // Dispose ftp service when application is closing
            try { _ftpService?.Dispose(); } catch { }

            // ensure any cancellation tokens disposed
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
        }

        private void UpdateUiState(bool monitoring)
        {
            Dispatcher.Invoke(() =>
            {
                btnStart.IsEnabled = !monitoring;
                btnStop.IsEnabled = monitoring;

                // Visual: change Start button color when disabled
                if (btnStart.IsEnabled)
                {
                    btnStart.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4));
                }
                else
                {
                    btnStart.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC8, 0xE6, 0xFA));
                }
            });
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = "Select download folder";
            dlg.ShowNewFolderButton = true;
            var result = dlg.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                txtLocalFolder.Text = dlg.SelectedPath;
                _settings.LocalFolder = dlg.SelectedPath;
                try { var _ = _settings_service?.SaveAsync(_settings); } catch { }
            }
        }

        private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;

            SetStatus("Testing connection...");
            bool ok = await TestFtpConnectionAsync();
            SetStatus(ok ? "Connection successful" : "Connection failed");
            Log($"Test connection: {(ok ? "OK" : "FAILED")} ", LogLevel.Info);
            if (ok)
            {
                Dispatcher.Invoke(() =>
                {
                    txtLastCheck.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    txtLastError.Text = "-";
                });
            }
        }

        private async Task<bool> TestFtpConnectionAsync()
        {
            var host = Dispatcher.Invoke(() => txtHost.Text.Trim());
            var port = Dispatcher.Invoke(() => int.TryParse(txtPort.Text, out var p) ? p : 21);
            var creds = Dispatcher.Invoke(() => GetCredentials());
            var remoteFolder = Dispatcher.Invoke(() => txtRemoteFolder.Text);

            try
            {
                return await _ftpService.DirectoryExistsAsync(host, port, creds, remoteFolder, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => txtLastError.Text = ex.Message);
                Log($"Test connection failed: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;

            // persist settings on start
            _settings.Host = txtHost.Text.Trim();
            _settings.Port = int.TryParse(txtPort.Text, out var p) ? p : 21;
            _settings.Username = txtUsername.Text.Trim();
            _settings.RemoteFolder = txtRemoteFolder.Text.Trim();
            _settings.LocalFolder = txtLocalFolder.Text.Trim();
            _settings.IntervalSeconds = int.TryParse(txtInterval.Text, out var s) ? s : 30;
            _settings.DeleteAfterDownload = chkDeleteAfterDownload.IsChecked == true;
            _ = _settings_service.SaveAsync(_settings);

            // save password to credential manager for convenience
            try
            {
                _credentialService.Save(_settings.Host ?? string.Empty, _settings.Username ?? string.Empty, txtPassword.Password ?? string.Empty);
            }
            catch { }

            UpdateUiState(true);
            btnCheckNow.IsEnabled = false;

            // dispose previous token source if present (prevent leak)
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }

            _cts = new CancellationTokenSource();

            if (!int.TryParse(txtInterval.Text.Trim(), out int seconds) || seconds <= 0)
                seconds = 30;

            _timer = new System.Threading.Timer(_ =>
            {
                // Run the scheduled work on the threadpool and handle exceptions
                Task.Run(async () =>
                {
                    // prevent overlapping scheduled runs
                    if (!_scheduledSemaphore.Wait(0))
                    {
                        Log("Previous scheduled run still running, skipping...", LogLevel.Warning);
                        return;
                    }

                    try
                    {
                        Log("Scheduled check starting...", LogLevel.Info);
                        await DownloadOnceAsync(_cts!.Token, "Scheduled");
                        Log("Scheduled check finished.", LogLevel.Info);
                    }
                    catch (OperationCanceledException)
                    {
                        Log("Scheduled check cancelled.", LogLevel.Warning);
                    }
                    catch (Exception ex)
                    {
                        Log($"Timer error: {ex.Message}", LogLevel.Error);
                    }
                    finally
                    {
                        try { _scheduledSemaphore.Release(); } catch { }
                    }
                });
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(seconds));

            SetStatus("Monitoring started");
            Log("Monitor started", LogLevel.Info);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopMonitoring("Monitoring stopped by user");
        }

        private async void BtnCheckNow_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;
            btnCheckNow.IsEnabled = false;
            SetStatus("Checking now...");

            // If not currently monitoring, create a temporary CTS so Stop can cancel this check.
            var createdLocalCts = false;
            if (_cts == null)
            {
                _cts = new CancellationTokenSource();
                createdLocalCts = true;
            }

            try
            {
                Log("Manual check starting...", LogLevel.Info);
                var res = await DownloadOnceAsync(_cts.Token, "Manual");
                Log("Manual check finished.", LogLevel.Info);
                if (res)
                {
                    Dispatcher.Invoke(() => txtLastCheck.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }
            finally
            {
                // If we created the CTS for this single check, dispose and clear it now.
                if (createdLocalCts)
                {
                    try { _cts?.Dispose(); } catch { }
                    _cts = null;
                }

                btnCheckNow.IsEnabled = true;
                SetStatus("Ready");
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Clear();
            _logLines.Clear();
            _downloadedCount = 0;
            UpdateDownloadedLabel();
        }

        // Navigation button handlers
        private void BtnNavMonitor_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("Monitor");
            // if floating sidebar was open on small screen, close it after navigation
            if (FloatingSidebar.Visibility == Visibility.Visible) HideFloatingSidebar();
        }

        private void BtnNavSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("Settings");
            if (FloatingSidebar.Visibility == Visibility.Visible) HideFloatingSidebar();
        }

        private void BtnNavAbout_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("About");
            if (FloatingSidebar.Visibility == Visibility.Visible) HideFloatingSidebar();
        }

        private void ShowPage(string page, bool animate = true)
        {
            Dispatcher.Invoke(() =>
            {
                // if not loaded yet, force no animation
                if (!_isLoaded) animate = false;

                UIElement toShow = MonitorPage;
                switch (page)
                {
                    case "Settings": toShow = SettingsPage; break;
                    case "History": toShow = HistoryPage; break;
                    case "Schedule": toShow = SchedulePage; break;
                    case "Logs": toShow = LogsPage; break;
                    case "About": toShow = AboutPage; break;
                    default: toShow = MonitorPage; break;
                }

                // Set all collapsed first
                MonitorPage.Visibility = Visibility.Collapsed;
                SettingsPage.Visibility = Visibility.Collapsed;
                HistoryPage.Visibility = Visibility.Collapsed;
                SchedulePage.Visibility = Visibility.Collapsed;
                LogsPage.Visibility = Visibility.Collapsed;
                AboutPage.Visibility = Visibility.Collapsed;

                if (animate)
                {
                    // simple fade-in
                    toShow.Opacity = 0;
                    toShow.Visibility = Visibility.Visible;
                    var da = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(300)));
                    toShow.BeginAnimation(UIElement.OpacityProperty, da);
                }
                else
                {
                    toShow.Visibility = Visibility.Visible;
                }

                // Update nav button styles
                var active = (Style)FindResource("NavButtonActive");
                var normal = (Style)FindResource("NavButton");
                btnNavMonitor.Style = page == "Monitor" ? active : normal;
                btnNavSettings.Style = page == "Settings" ? active : normal;
                btnNavAbout.Style = page == "About" ? active : normal;

                // update nav status text
                txtNavStatus.Text = page;

                // remember last page in settings (persist on close)
                _settings.LastPage = page;
            });
        }

        private void StopMonitoring(string message)
        {
            _timer?.Dispose();
            _timer = null;

            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;

            // don't dispose _ftp_service here - keep instance for reuse
            UpdateUiState(false);
            btnCheckNow.IsEnabled = true;
            SetStatus(message);
            Log(message, LogLevel.Info);
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(txtHost.Text))
            {
                System.Windows.MessageBox.Show("FTP Host girin.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtUsername.Text))
            {
                System.Windows.MessageBox.Show("FTP kullanici adini girin.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtPassword.Password))
            {
                System.Windows.MessageBox.Show("FTP sifresini girin.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtLocalFolder.Text) || !Directory.Exists(txtLocalFolder.Text))
            {
                System.Windows.MessageBox.Show("Geçerli bir local klasör seçin.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private NetworkCredential GetCredentials()
        {
            return new NetworkCredential(txtUsername.Text.Trim(), txtPassword.Password);
        }

        private string NormalizeRemoteFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || folder == "/") return "/";
            folder = folder.Trim();
            if (!folder.StartsWith("/")) folder = "/" + folder;
            if (folder.EndsWith("/") && folder.Length > 1) folder = folder.TrimEnd('/');
            return folder;
        }

        private async Task<bool> DownloadOnceAsync(CancellationToken token, string source = "Manual")
        {
            bool anyDownloaded = false;
            var sw = Stopwatch.StartNew();
            int found = 0, downloaded = 0, skipped = 0, errors = 0;

            // Capture UI values on UI thread to avoid cross-thread access
            string host = Dispatcher.Invoke(() => txtHost.Text.Trim());
            string portText = Dispatcher.Invoke(() => txtPort.Text);
            string remoteFolderText = Dispatcher.Invoke(() => txtRemoteFolder.Text);
            string localFolderText = Dispatcher.Invoke(() => txtLocalFolder.Text);

            Log($"{source} run: connecting to {host}:{portText} folder={remoteFolderText}", LogLevel.Info);

            try
            {
                var host2 = host;
                var port = int.TryParse(portText, out var p) ? p : 21;
                var creds = Dispatcher.Invoke(() => GetCredentials());
                var remoteFolder = remoteFolderText;
                var localFolder = localFolderText;
                var deleteAfter = Dispatcher.Invoke(() => chkDeleteAfterDownload.IsChecked == true);

                var files = await _ftpService.ListFilesAsync(host2, port, creds, remoteFolder, token);
                found = files.Length;
                Log($"{source} run: found {found} entries", LogLevel.Info);
                if (files.Length == 0)
                {
                    Log("No files found.", LogLevel.Info);
                    return false;
                }

                foreach (var file in files)
                {
                    if (token.IsCancellationRequested) break;

                    if (string.IsNullOrWhiteSpace(file) || file == "." || file == "..") continue;
                    if (!file.Contains("."))
                    {
                        Log($"Skipping (looks like dir): {file}", LogLevel.Info);
                        continue;
                    }

                    var dl = await _ftpService.DownloadFileAsync(host2, port, creds, remoteFolder, file, localFolder, deleteAfter, token);
                    if (dl.Success)
                    {
                        if (dl.Skipped)
                        {
                            skipped++;
                            Log($"Already exists, skipping: {file}", LogLevel.Info);
                        }
                        else
                        {
                            downloaded++;
                            anyDownloaded = true;
                            Dispatcher.Invoke(() =>
                            {
                                _downloadedCount++;
                                UpdateDownloadedLabel();
                                Log($"Downloaded: {file} -> {dl.LocalPath}", LogLevel.Info);
                                txtLastCheck.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            });

                            if (dl.Deleted)
                            {
                                Log($"Deleted remote file: {dl.RemotePath}", LogLevel.Info);
                            }
                        }
                    }
                    else
                    {
                        errors++;
                        if (!string.IsNullOrEmpty(dl.ErrorMessage))
                        {
                            Log($"DownloadFile error ({file}): {dl.ErrorMessage}", LogLevel.Warning);
                            Dispatcher.Invoke(() => txtLastError.Text = dl.ErrorMessage);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log("Download cancelled", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                Log($"DownloadOnce error: {ex.Message}", LogLevel.Error);
                Dispatcher.Invoke(() => txtLastError.Text = ex.Message);
            }
            finally
            {
                sw.Stop();
                var duration = sw.Elapsed.ToString(@"mm\:ss");
                Log($"{source} run finished: found={found} downloaded={downloaded} skipped={skipped} errors={errors} duration={duration}", LogLevel.Info);
            }

            return anyDownloaded;
        }

        private void UpdateDownloadedLabel()
        {
            Dispatcher.Invoke(() => lblDownloaded.Text = $"Downloaded: {_downloadedCount} files");
        }

        private void SetStatus(string text)
        {
            Dispatcher.BeginInvoke(() => txtStatusText.Text = text);
        }

        private void Log(string text, LogLevel level = LogLevel.Info)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {text}{Environment.NewLine}";

            // Append to UI and update status indicator on UI thread
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        // Update status indicator color based on level
                        switch (level)
                        {
                            case LogLevel.Info:
                                ellipseStatus.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 182, 255));
                                break;
                            case LogLevel.Warning:
                                ellipseStatus.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 204, 0));
                                break;
                            case LogLevel.Error:
                                ellipseStatus.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 52, 56));
                                break;
                        }

                        txtStatusText.Text = level == LogLevel.Error ? "Error" : (level == LogLevel.Warning ? "Warning" : "Running");

                        // Maintain a bounded in-memory log to avoid unbounded growth of the TextBox
                        _logLines.AddLast(line);
                        while (_logLines.Count > MaxLogLines)
                        {
                            _logLines.RemoveFirst();
                        }

                        // Update UI with concatenated lines
                        // Using string.Concat on the LinkedList is reasonably efficient for this scale. If profiling shows cost here, consider a circular buffer + StringBuilder.
                        txtLog.Text = string.Concat(_logLines);
                        txtLog.CaretIndex = txtLog.Text.Length;
                        txtLog.ScrollToEnd();
                    }
                    catch { }
                });
            }
            catch { }

            // Persist to file asynchronously
            try
            {
                var path = _logFilePath;
                Task.Run(() =>
                {
                    try
                    {
                        File.AppendAllText(path, line);
                    }
                    catch { }
                });
            }
            catch { }
        }

        // Responsive sidebar helpers
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveSidebar();
        }

        private void UpdateResponsiveSidebar()
        {
            Dispatcher.Invoke(() =>
            {
                if (ActualWidth < SidebarCollapseWidth)
                {
                    // Hide fixed sidebar and show toggle button
                    SidebarColumn.Width = new GridLength(0);
                    SidebarBorder.Visibility = Visibility.Collapsed;
                    btnToggleSidebar.Visibility = Visibility.Visible;
                }
                else
                {
                    SidebarColumn.Width = new GridLength(260);
                    SidebarBorder.Visibility = Visibility.Visible;
                    btnToggleSidebar.Visibility = Visibility.Collapsed;
                    // ensure floating sidebar hidden
                    HideFloatingSidebar();
                }
            });
        }

        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            if (FloatingSidebar.Visibility == Visibility.Visible)
            {
                HideFloatingSidebar();
            }
            else
            {
                ShowFloatingSidebar();
            }
        }

        private void ShowFloatingSidebar()
        {
            FloatingSidebar.Visibility = Visibility.Visible;
            var tt = FloatingSidebar.RenderTransform as TranslateTransform;
            if (tt == null) return;
            var from = tt.X;
            // animate from negative width to 0
            var anim = new DoubleAnimation(from, 0, new Duration(TimeSpan.FromMilliseconds(250))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            tt.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void HideFloatingSidebar()
        {
            var tt = FloatingSidebar.RenderTransform as TranslateTransform;
            if (tt == null) return;
            // animate back to -ActualWidth so it moves off-screen to left
            var anim = new DoubleAnimation(tt.X, -FloatingSidebar.ActualWidth, new Duration(TimeSpan.FromMilliseconds(200))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            anim.Completed += (s, e) => FloatingSidebar.Visibility = Visibility.Collapsed;
            tt.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }
}