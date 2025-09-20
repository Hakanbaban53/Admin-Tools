using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FTP_Tool.Services;
using Microsoft.Win32;
using System.Reflection;

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

            // Apply FTP options
            try
            {
                _ftpService.ApplyOptions(_settings.ConnectionTimeoutSeconds, _settings.MaxRetryAttempts, _settings.UsePassiveMode);
                // forward telemetry from FtpService into the app logging pipeline
                try { _ftpService.Logger = (msg, lvl) => Log(msg, lvl); } catch { }
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
                try
                {
                    var cb = this.FindName("chkAutoStart") as System.Windows.Controls.CheckBox;
                    if (cb != null) cb.IsChecked = _settings.StartWithWindows;
                }
                catch { }

                try
                {
                    var cmb = this.FindName("cmbStartupMode") as System.Windows.Controls.ComboBox;
                    if (cmb != null)
                    {
                        // 0 = Open window, 1 = Start minimized to tray
                        cmb.SelectedIndex = _settings.StartMinimizedOnStartup ? 1 : 0;
                    }
                }
                catch { }

                chkMinimizeToTray.IsChecked = _settings.MinimizeToTray;
                // new checkbox for start monitoring on launch
                try { chkStartMonitoringOnLaunch.IsChecked = _settings.StartMonitoringOnLaunch; } catch { }

                // advanced settings mapping
                try
                {
                    txtConnectionTimeout.Text = _settings.ConnectionTimeoutSeconds.ToString();
                    txtMaxRetries.Text = _settings.MaxRetryAttempts.ToString();
                    chkUsePassiveMode.IsChecked = _settings.UsePassiveMode;
                    txtLogRetentionDays.Text = _settings.LogRetentionDays.ToString();
                }
                catch { }

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
                var autoCb = this.FindName("chkAutoStart") as System.Windows.Controls.CheckBox;
                var cmbStartup = this.FindName("cmbStartupMode") as System.Windows.Controls.ComboBox;

                // initialize startup mode enabled state based on AutoStart
                try
                {
                    if (autoCb != null && cmbStartup != null)
                    {
                        cmbStartup.IsEnabled = autoCb.IsChecked == true;
                    }
                }
                catch { }

                if (autoCb != null)
                {
                    autoCb.Checked += (s, ev) =>
                    {
                        try
                        {
                            _settings.StartWithWindows = true;
                            var _ = _settings_service.SaveAsync(_settings);
                            EnableAutoStart();
                            // enable startup mode selection when autostart enabled
                            try { if (cmbStartup != null) cmbStartup.IsEnabled = true; } catch { }
                        }
                        catch { }
                    };

                    autoCb.Unchecked += (s, ev) =>
                    {
                        try
                        {
                            _settings.StartWithWindows = false;
                            var _ = _settings_service.SaveAsync(_settings);
                            DisableAutoStart();
                            // disable startup mode selection when autostart disabled
                            try { if (cmbStartup != null) cmbStartup.IsEnabled = false; } catch { }
                        }
                        catch { }
                    };
                }

                if (cmbStartup != null)
                {
                    cmbStartup.SelectionChanged += (s, ev) =>
                    {
                        try
                        {
                            // SelectedIndex: 0 = Open window, 1 = Start minimized
                            _settings.StartMinimizedOnStartup = (cmbStartup.SelectedIndex == 1);
                            var _ = _settings_service.SaveAsync(_settings);
                            // refresh Run entry when autostart enabled
                            try { if (autoCb != null && autoCb.IsChecked == true) EnableAutoStart(); } catch { }
                        }
                        catch { }
                    };
                }

                chkMinimizeToTray.Checked += (s, ev) => { _settings.MinimizeToTray = true; var _ = _settings_service.SaveAsync(_settings); };
                chkMinimizeToTray.Unchecked += (s, ev) => { _settings.MinimizeToTray = false; var _ = _settings_service.SaveAsync(_settings); };

                // persist change for StartMonitoringOnLaunch
                try
                {
                    chkStartMonitoringOnLaunch.Checked += (s, ev) => { _settings.StartMonitoringOnLaunch = true; var _ = _settings_service.SaveAsync(_settings); };
                    chkStartMonitoringOnLaunch.Unchecked += (s, ev) => { _settings.StartMonitoringOnLaunch = false; var _ = _settings_service.SaveAsync(_settings); };
                }
                catch { }

                // advanced settings handlers
                try
                {
                    txtConnectionTimeout.LostFocus += (s, ev) =>
                    {
                        if (int.TryParse(txtConnectionTimeout.Text, out var val))
                        {
                            _settings.ConnectionTimeoutSeconds = Math.Max(1, val);
                            _ftpService.ApplyOptions(_settings.ConnectionTimeoutSeconds, _settings.MaxRetryAttempts, _settings.UsePassiveMode);
                            var _ = _settings_service.SaveAsync(_settings);
                        }
                        else
                        {
                            txtConnectionTimeout.Text = _settings.ConnectionTimeoutSeconds.ToString();
                        }
                    };

                    txtMaxRetries.LostFocus += (s, ev) =>
                    {
                        if (int.TryParse(txtMaxRetries.Text, out var val))
                        {
                            _settings.MaxRetryAttempts = Math.Max(0, val);
                            _ftpService.ApplyOptions(_settings.ConnectionTimeoutSeconds, _settings.MaxRetryAttempts, _settings.UsePassiveMode);
                            var _ = _settings_service.SaveAsync(_settings);
                        }
                        else
                        {
                            txtMaxRetries.Text = _settings.MaxRetryAttempts.ToString();
                        }
                    };

                    chkUsePassiveMode.Checked += (s, ev) => { _settings.UsePassiveMode = true; _ftpService.ApplyOptions(_settings.ConnectionTimeoutSeconds, _settings.MaxRetryAttempts, _settings.UsePassiveMode); var _ = _settings_service.SaveAsync(_settings); };
                    chkUsePassiveMode.Unchecked += (s, ev) => { _settings.UsePassiveMode = false; _ftpService.ApplyOptions(_settings.ConnectionTimeoutSeconds, _settings.MaxRetryAttempts, _settings.UsePassiveMode); var _ = _settings_service.SaveAsync(_settings); };

                    txtLogRetentionDays.LostFocus += (s, ev) =>
                    {
                        if (int.TryParse(txtLogRetentionDays.Text, out var val))
                        {
                            _settings.LogRetentionDays = Math.Max(1, val);
                            _logging_service?.ApplySettings(_logFilePath, _settings);
                            var _ = _settings_service.SaveAsync(_settings);
                        }
                        else
                        {
                            txtLogRetentionDays.Text = _settings.LogRetentionDays.ToString();
                        }
                    };
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

            // If application launched with --startup, and user wants start minimized, hide to tray.
            try
            {
                var args = Environment.GetCommandLineArgs();
                var startedFromRun = false;
                foreach (var a in args)
                {
                    if (string.Equals(a, "--startup", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "/startup", StringComparison.OrdinalIgnoreCase))
                    {
                        startedFromRun = true;
                        break;
                    }
                }

                if (startedFromRun && _settings.StartMinimizedOnStartup)
                {
                    HideToTray();
                }
            }
            catch { }
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // If minimize to tray is enabled, cancel close and hide instead
            if (!_suppressHideOnClose && _settings.MinimizeToTray && WindowState != WindowState.Minimized)
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

                // persist StartWithWindows from UI control
                try
                {
                    var autoCb = this.FindName("chkAutoStart") as System.Windows.Controls.CheckBox;
                    if (autoCb != null) _settings.StartWithWindows = autoCb.IsChecked == true;
                }
                catch { }

                // persist StartMinimizedOnStartup from UI control
                try
                {
                    var cmb = this.FindName("cmbStartupMode") as System.Windows.Controls.ComboBox;
                    if (cmb != null) _settings.StartMinimizedOnStartup = cmb.SelectedIndex == 1;
                }
                catch { }

                var selected = cmbMinimumLogLevel.SelectedItem as ComboBoxItem;
                if (selected != null) _settings.MinimumLogLevel = selected.Content?.ToString() ?? "Info";

                if (int.TryParse(txtMaxLogLines.Text, out var maxLines)) _settings.MaxLogLines = Math.Max(0, maxLines);

                // advanced settings
                if (int.TryParse(txtConnectionTimeout.Text, out var timeout)) _settings.ConnectionTimeoutSeconds = Math.Max(1, timeout);
                if (int.TryParse(txtMaxRetries.Text, out var retries)) _settings.MaxRetryAttempts = Math.Max(0, retries);
                try { _settings.UsePassiveMode = chkUsePassiveMode.IsChecked == true; } catch { }
                if (int.TryParse(txtLogRetentionDays.Text, out var days)) _settings.LogRetentionDays = Math.Max(1, days);

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

        // Add/Remove Run registry entries to enable start-with-windows
        private void EnableAutoStart()
        {
            try
            {
                var runKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (runKey == null) return;

                var entryPath = Assembly.GetEntryAssembly()?.Location ?? ProcessExecutablePathFallback();
                if (string.IsNullOrEmpty(entryPath)) return;

                // wrap in quotes and add startup arg; include --minimized when requested
                var args = "--startup" + (_settings.StartMinimizedOnStartup ? " --minimized" : string.Empty);

                string value;
                if (entryPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    // The app is running as a framework-dependent deployment (dotnet <app>.dll).
                    // Register the host (dotnet) and pass the DLL path so Windows opens the DLL with dotnet.
                    var hostExe = ProcessExecutablePathFallback();
                    if (string.IsNullOrEmpty(hostExe) || hostExe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        // If we couldn't find a host path, fall back to 'dotnet' on PATH.
                        hostExe = "dotnet";
                    }

                    value = $"\"{hostExe}\" \"{entryPath}\" {args}";
                }
                else
                {
                    // Normal executable
                    value = $"\"{entryPath}\" {args}";
                }

                runKey.SetValue("FTP_Tool", value, RegistryValueKind.String);
                runKey.Close();
            }
            catch { }
        }

        private void DisableAutoStart()
        {
            try
            {
                var runKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (runKey == null) return;
                try { runKey.DeleteValue("FTP_Tool", false); } catch { }
                runKey.Close();
            }
            catch { }
        }

        // Fallback for executable path
        private string ProcessExecutablePathFallback()
        {
            try
            {
                return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            }
            catch { return string.Empty; }
        }
    }
}
