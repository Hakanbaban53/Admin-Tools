using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FTP_Tool.Services;

namespace FTP_Tool
{
    public partial class MainWindow
    {
        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            _settings = await _settings_service.LoadAsync();

            try
            {
                _logging_service = new LoggingService(_logFilePath, _settings);
            }
            catch { }

            // populate UI (existing controls assumed to exist in XAML)
            try
            {
                txtHost.Text = _settings.Host;
                txtPort.Text = _settings.Port.ToString();
                txtUsername.Text = _settings.Username;
                txtRemoteFolder.Text = string.IsNullOrWhiteSpace(_settings.RemoteFolder) ? "/" : _settings.RemoteFolder;
                txtLocalFolder.Text = _settings.LocalFolder;
                txtInterval.Text = _settings.IntervalSeconds.ToString();
                chkDeleteAfterDownload.IsChecked = _settings.DeleteAfterDownload;

                chkLogToFile.IsChecked = _settings.LogToFile;
                //chkAutoStart.IsChecked = _settings.StartWithWindows;
                chkMinimizeToTray.IsChecked = _settings.MinimizeToTray;
                // new checkbox for start monitoring on launch
                try { chkStartMonitoringOnLaunch.IsChecked = _settings.StartMonitoringOnLaunch; } catch { }

                // logging controls - ensure UI shows saved values
                try
                {
                    if (!string.IsNullOrWhiteSpace(_settings.MinimumLogLevel))
                    {
                        foreach (var item in cmbMinimumLogLevel.Items)
                        {
                            if (item is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), _settings.MinimumLogLevel, StringComparison.OrdinalIgnoreCase))
                            {
                                cmbMinimumLogLevel.SelectedItem = item;
                                break;
                            }
                        }
                    }

                    txtMaxLogLines.Text = _settings.MaxLogLines.ToString();
                }
                catch { }

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
            }
            catch { }

            // wire simple handlers for settings controls
            try
            {
                //chkAutoStart.Checked += (s, ev) => EnableAutoStart(true);
                //chkAutoStart.Unchecked += (s, ev) => EnableAutoStart(false);

                chkMinimizeToTray.Checked += (s, ev) => { _settings.MinimizeToTray = true; var _ = _settings_service.SaveAsync(_settings); };
                chkMinimizeToTray.Unchecked += (s, ev) => { _settings.MinimizeToTray = false; var _ = _settings_service.SaveAsync(_settings); };

                // persist change for StartMonitoringOnLaunch
                try
                {
                    chkStartMonitoringOnLaunch.Checked += (s, ev) => { _settings.StartMonitoringOnLaunch = true; var _ = _settings_service.SaveAsync(_settings); };
                    chkStartMonitoringOnLaunch.Unchecked += (s, ev) => { _settings.StartMonitoringOnLaunch = false; var _ = _settings_service.SaveAsync(_settings); };
                }
                catch { }

                // logging handlers
                try
                {
                    chkLogToFile.Checked += (s, ev) => { _settings.LogToFile = true; _logging_service?.ApplySettings(_logFilePath, _settings); var _ = _settings_service.SaveAsync(_settings); };
                    chkLogToFile.Unchecked += (s, ev) => { _settings.LogToFile = false; _logging_service?.ApplySettings(_logFilePath, _settings); var _ = _settings_service.SaveAsync(_settings); };

                    cmbMinimumLogLevel.SelectionChanged += (s, ev) =>
                    {
                        try
                        {
                            var selected = cmbMinimumLogLevel.SelectedItem as ComboBoxItem;
                            if (selected != null)
                            {
                                _settings.MinimumLogLevel = selected.Content?.ToString() ?? "Info";
                                _logging_service?.ApplySettings(_logFilePath, _settings);
                                var _ = _settings_service.SaveAsync(_settings);
                            }
                        }
                        catch { }
                    };

                    txtMaxLogLines.LostFocus += (s, ev) =>
                    {
                        if (int.TryParse(txtMaxLogLines.Text, out var maxLines))
                        {
                            _settings.MaxLogLines = Math.Max(0, maxLines);
                            var _ = _settings_service.SaveAsync(_settings);
                        }
                        else
                        {
                            txtMaxLogLines.Text = _settings.MaxLogLines.ToString();
                        }
                    };
                }
                catch { }

            }
            catch { }

            // Populate saved credentials list in settings UI (if control exists)
            try
            {
                var list = _credentialService.ListSavedCredentials();
                // we will look for a ListBox named lstSavedCredentials in XAML
                try
                {
                    var lb = this.FindName("lstSavedCredentials") as System.Windows.Controls.ListBox;
                    if (lb != null)
                    {
                        lb.Items.Clear();
                        foreach (var item in list)
                        {
                            lb.Items.Add($"{item.Host} : {item.Username}");
                        }
                    }
                }
                catch { }
            }
            catch { }

            // other initialization
            UpdateResponsiveSidebar();
            ClearLogIfNeeded();
            UpdateSidebarStatus(false);
            UpdateSidebarStats();

            // start log flush timer
            try
            {
                _logFlushTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _logFlushTimer.Tick += (s, ev) => FlushPendingLogs();
                _logFlushTimer.Start();
            }
            catch { }

            // restore previously viewed page (avoid animation during initial load)
            try
            {
                var page = string.IsNullOrWhiteSpace(_settings.LastPage) ? "Monitor" : _settings.LastPage!;
                ShowPage(page, animate: false);
            }
            catch { }

            _isLoaded = true;

            // Auto-start monitoring if requested
            try
            {
                if (_settings.StartMonitoringOnLaunch)
                {
                    // validate inputs before starting
                    if (ValidateInputs())
                    {
                        BtnStart_Click(this, new RoutedEventArgs());
                    }
                    else
                    {
                        Log("StartMonitoringOnLaunch requested but inputs invalid; monitoring not started.", LogLevel.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to auto-start monitoring: {ex.Message}", LogLevel.Error);
            }
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // If minimize to tray is enabled, cancel close and hide instead
            if (_settings.MinimizeToTray && WindowState != WindowState.Minimized)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            // perform shutdown cleanup (call original close logic)
            _ = MainWindow_ClosingAsync();
        }

        private async Task MainWindow_ClosingAsync()
        {
            // save current settings
            try
            {
                _settings.Host = txtHost.Text.Trim();
                _settings.Port = int.TryParse(txtPort.Text, out var p) ? p : 21;
                _settings.Username = txtUsername.Text.Trim();
                _settings.RemoteFolder = txtRemoteFolder.Text.Trim();
                _settings.LocalFolder = txtLocalFolder.Text.Trim();
                _settings.IntervalSeconds = int.TryParse(txtInterval.Text, out var s) ? s : 30;
                _settings.DeleteAfterDownload = chkDeleteAfterDownload.IsChecked == true;
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;

                _settings.LogToFile = chkLogToFile.IsChecked == true;

                // persist StartMonitoringOnLaunch from UI control
                try { _settings.StartMonitoringOnLaunch = chkStartMonitoringOnLaunch.IsChecked == true; } catch { }

                var selected = cmbMinimumLogLevel.SelectedItem as ComboBoxItem;
                if (selected != null) _settings.MinimumLogLevel = selected.Content?.ToString() ?? "Info";

                if (int.TryParse(txtMaxLogLines.Text, out var maxLines)) _settings.MaxLogLines = Math.Max(0, maxLines);

                if (_settings_service != null) await _settings_service.SaveAsync(_settings);

                try
                {
                    _credentialService.Save(_settings.Host ?? string.Empty, _settings.Username ?? string.Empty, txtPassword.Password ?? string.Empty);
                }
                catch { }
            }
            catch { }

            // stop log flush timer
            try { if (_logFlushTimer != null) { _logFlushTimer.Stop(); _logFlushTimer = null; } } catch { }

            // Dispose ftp service when application is closing
            try { _ftpService?.Dispose(); } catch { }

            // ensure cancellation token disposed
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;

            // dispose logging service
            try { _logging_service?.Dispose(); } catch { }

            // dispose tray icon
            try { if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); _trayIcon = null; } } catch { }

            // finally close application
            try { System.Windows.Application.Current.Shutdown(); } catch { }
        }
    }
}
