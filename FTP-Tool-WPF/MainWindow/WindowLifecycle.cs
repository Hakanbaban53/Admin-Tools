using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FTP_Tool.Services;
using Microsoft.Win32;
using System.Reflection;
using System.Linq;

namespace FTP_Tool
{
    public partial class MainWindow
    {
        private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            // Load settings first
            try
            {
                _settings = await _settings_service.LoadAsync();
            }
            catch
            {
                _settings = new Models.AppSettings();
            }

            // Initialize logging service (best-effort)
            try
            {
                _logging_service = new LoggingService(_logFilePath, _settings);
            }
            catch { }

            // Apply FTP options and forward ftp logging
            try
            {
                _ftpService.ApplyOptions(_settings.ConnectionTimeoutSeconds, _settings.MaxRetryAttempts, _settings.UsePassiveMode);
                try { _ftpService.Logger = (msg, lvl) => Log(msg, lvl); } catch { }
            }
            catch { }

            // Populate common UI fields from settings
            try
            {
                txtHost.Text = _settings.Host ?? string.Empty;
                txtPort.Text = (_settings.Port > 0) ? _settings.Port.ToString() : "21";
                txtUsername.Text = _settings.Username ?? string.Empty;
                txtRemoteFolder.Text = string.IsNullOrWhiteSpace(_settings.RemoteFolder) ? "/" : _settings.RemoteFolder!;
                txtLocalFolder.Text = _settings.LocalFolder ?? string.Empty;
                txtInterval.Text = (_settings.IntervalSeconds > 0) ? _settings.IntervalSeconds.ToString() : "30";
                chkDeleteAfterDownload.IsChecked = _settings.DeleteAfterDownload;

                chkLogToFile.IsChecked = _settings.LogToFile;
                try { chkAutoStart.IsChecked = _settings.StartWithWindows; } catch { }

                try
                {
                    cmbStartupMode.SelectedIndex = _settings.StartMinimizedOnStartup ? 1 : 0;
                }
                catch { }

                chkMinimizeToTray.IsChecked = _settings.MinimizeToTray;
                try { chkStartMonitoringOnLaunch.IsChecked = _settings.StartMonitoringOnLaunch; } catch { }

                // advanced
                txtConnectionTimeout.Text = _settings.ConnectionTimeoutSeconds.ToString();
                txtMaxRetries.Text = _settings.MaxRetryAttempts.ToString();
                chkUsePassiveMode.IsChecked = _settings.UsePassiveMode;
                txtLogRetentionDays.Text = _settings.LogRetentionDays.ToString();

                // logging
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

            // Load saved FTP password from credential store (best-effort)
            try
            {
                var cred = _credentialService.Load(_settings.Host ?? string.Empty, _settings.Username ?? string.Empty);
                if (cred.HasValue && !string.IsNullOrEmpty(cred.Value.Password)) txtPassword.Password = cred.Value.Password;
            }
            catch { }

            // Load email related settings
            try
            {
                txtSmtpHost.Text = _settings.SmtpHost ?? string.Empty;
                txtSmtpPort.Text = _settings.SmtpPort.ToString();
                chkSmtpSsl.IsChecked = _settings.SmtpEnableSsl;
                txtSmtpUser.Text = _settings.SmtpUsername ?? string.Empty;

                try
                {
                    var smtpCred = _credentialService.Load(_settings.SmtpHost ?? string.Empty, _settings.SmtpUsername ?? string.Empty);
                    if (smtpCred.HasValue) txtSmtpPass.Password = smtpCred.Value.Password ?? string.Empty;
                }
                catch { }

                txtEmailFrom.Text = _settings.EmailFrom ?? string.Empty;

                LoadRecipientsFromSettings();

                // Weekday selections
                try
                {
                    var days = (_settings.AlertWeekdays ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());
                    chkMon.IsChecked = days.Contains("Mon");
                    chkTue.IsChecked = days.Contains("Tue");
                    chkWed.IsChecked = days.Contains("Wed");
                    chkThu.IsChecked = days.Contains("Thu");
                    chkFri.IsChecked = days.Contains("Fri");
                    chkSat.IsChecked = days.Contains("Sat");
                    chkSun.IsChecked = days.Contains("Sun");
                }
                catch { }

                txtWorkStart.Text = _settings.WorkStart ?? "08:00";
                txtWorkEnd.Text = _settings.WorkEnd ?? "17:00";
                txtLunchStart.Text = _settings.LunchStart ?? "12:00";
                txtLunchEnd.Text = _settings.LunchEnd ?? "13:00";
                txtAlertThreshold.Text = (_settings.AlertThresholdMinutes > 0 ? _settings.AlertThresholdMinutes : 15).ToString();

                // Alert master switches
                try
                {
                    chkAlertsEnabled.IsChecked = _settings.AlertsEnabled;
                    chkAlertAlways.IsChecked = _settings.AlertAlways;

                    var alertsEnabled = chkAlertsEnabled.IsChecked == true;
                    var alertAlways = chkAlertAlways.IsChecked == true;

                    chkMon.IsEnabled = alertsEnabled && !alertAlways;
                    chkTue.IsEnabled = alertsEnabled && !alertAlways;
                    chkWed.IsEnabled = alertsEnabled && !alertAlways;
                    chkThu.IsEnabled = alertsEnabled && !alertAlways;
                    chkFri.IsEnabled = alertsEnabled && !alertAlways;
                    chkSat.IsEnabled = alertsEnabled && !alertAlways;
                    chkSun.IsEnabled = alertsEnabled && !alertAlways;
                    txtWorkStart.IsEnabled = alertsEnabled && !alertAlways;
                    txtWorkEnd.IsEnabled = alertsEnabled && !alertAlways;
                    txtLunchStart.IsEnabled = alertsEnabled && !alertAlways;
                    txtLunchEnd.IsEnabled = alertsEnabled && !alertAlways;
                    txtAlertThreshold.IsEnabled = alertsEnabled;
                }
                catch { }
            }
            catch { }

            // Wire handlers (only once during load)
            try
            {
                // Auto start handlers
                if (chkAutoStart != null)
                {
                    chkAutoStart.Checked += (s, ev) =>
                    {
                        _settings.StartWithWindows = true;
                        _ = _settings_service.SaveAsync(_settings);
                        EnableAutoStart();
                        try { cmbStartupMode.IsEnabled = true; } catch { }
                    };

                    chkAutoStart.Unchecked += (s, ev) =>
                    {
                        _settings.StartWithWindows = false;
                        _ = _settings_service.SaveAsync(_settings);
                        DisableAutoStart();
                        try { cmbStartupMode.IsEnabled = false; } catch { }
                    };
                }

                cmbStartupMode.SelectionChanged += (s, ev) =>
                {
                    _settings.StartMinimizedOnStartup = cmbStartupMode.SelectedIndex == 1;
                    _ = _settings_service.SaveAsync(_settings);
                    try { if (chkAutoStart.IsChecked == true) EnableAutoStart(); } catch { }
                };

                chkMinimizeToTray.Checked += (s, ev) => { _settings.MinimizeToTray = true; _ = _settings_service.SaveAsync(_settings); };
                chkMinimizeToTray.Unchecked += (s, ev) => { _settings.MinimizeToTray = false; _ = _settings_service.SaveAsync(_settings); };

                try
                {
                    chkStartMonitoringOnLaunch.Checked += (s, ev) => { _settings.StartMonitoringOnLaunch = true; _ = _settings_service.SaveAsync(_settings); };
                    chkStartMonitoringOnLaunch.Unchecked += (s, ev) => { _settings.StartMonitoringOnLaunch = false; _ = _settings_service.SaveAsync(_settings); };
                }
                catch { }

                // Advanced options
                txtConnectionTimeout.LostFocus += (s, ev) =>
                {
                    if (int.TryParse(txtConnectionTimeout.Text, out var val))
                    {
                        _settings.ConnectionTimeoutSeconds = Math.Max(1, val);
                        _ftpService.ApplyOptions(_settings.ConnectionTimeoutSeconds, _settings.MaxRetryAttempts, _settings.UsePassiveMode);
                        _ = _settings_service.SaveAsync(_settings);
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
                        _ = _settings_service.SaveAsync(_settings);
                    }
                    else
                    {
                        txtMaxRetries.Text = _settings.MaxRetryAttempts.ToString();
                    }
                };

                chkUsePassiveMode.Checked += (s, ev) => { _settings.UsePassiveMode = true; _ftpService.ApplyOptions(_settings.ConnectionTimeoutSeconds, _settings.MaxRetryAttempts, _settings.UsePassiveMode); _ = _settings_service.SaveAsync(_settings); };
                chkUsePassiveMode.Unchecked += (s, ev) => { _settings.UsePassiveMode = false; _ftpService.ApplyOptions(_settings.ConnectionTimeoutSeconds, _settings.MaxRetryAttempts, _settings.UsePassiveMode); _ = _settings_service.SaveAsync(_settings); };

                txtLogRetentionDays.LostFocus += (s, ev) =>
                {
                    if (int.TryParse(txtLogRetentionDays.Text, out var val))
                    {
                        _settings.LogRetentionDays = Math.Max(1, val);
                        _logging_service?.ApplySettings(_logFilePath, _settings);
                        _ = _settings_service.SaveAsync(_settings);
                    }
                    else
                    {
                        txtLogRetentionDays.Text = _settings.LogRetentionDays.ToString();
                    }
                };

                // Logging handlers
                chkLogToFile.Checked += (s, ev) => { _settings.LogToFile = true; _logging_service?.ApplySettings(_logFilePath, _settings); _ = _settings_service.SaveAsync(_settings); };
                chkLogToFile.Unchecked += (s, ev) => { _settings.LogToFile = false; _logging_service?.ApplySettings(_logFilePath, _settings); _ = _settings_service.SaveAsync(_settings); };

                cmbMinimumLogLevel.SelectionChanged += (s, ev) =>
                {
                    var selected = cmbMinimumLogLevel.SelectedItem as ComboBoxItem;
                    if (selected != null)
                    {
                        _settings.MinimumLogLevel = selected.Content?.ToString() ?? "Info";
                        _logging_service?.ApplySettings(_logFilePath, _settings);
                        _ = _settings_service.SaveAsync(_settings);
                    }
                };

                txtMaxLogLines.LostFocus += (s, ev) =>
                {
                    if (int.TryParse(txtMaxLogLines.Text, out var maxLines))
                    {
                        _settings.MaxLogLines = Math.Max(0, maxLines);
                        _ = _settings_service.SaveAsync(_settings);
                    }
                    else
                    {
                        txtMaxLogLines.Text = _settings.MaxLogLines.ToString();
                    }
                };

                // Credentials list actions
                btnRefreshCredentials.Click += (s, ev) => BtnRefreshCredentials_Click(s, ev);
                btnDeleteCredential.Click += (s, ev) => BtnDeleteSavedCredential_Click(s, ev);

                // Recipient management
                btnAddRecipient.Click += (s, ev) => BtnAddRecipient_Click(s, ev);
                btnRemoveRecipient.Click += (s, ev) => BtnRemoveRecipient_Click(s, ev);

                // Weekday changes persist
                Action persist = () => PersistWeekdays();
                chkMon.Checked += (s, ev) => persist(); chkMon.Unchecked += (s, ev) => persist();
                chkTue.Checked += (s, ev) => persist(); chkTue.Unchecked += (s, ev) => persist();
                chkWed.Checked += (s, ev) => persist(); chkWed.Unchecked += (s, ev) => persist();
                chkThu.Checked += (s, ev) => persist(); chkThu.Unchecked += (s, ev) => persist();
                chkFri.Checked += (s, ev) => persist(); chkFri.Unchecked += (s, ev) => persist();
                chkSat.Checked += (s, ev) => persist(); chkSat.Unchecked += (s, ev) => persist();
                chkSun.Checked += (s, ev) => persist(); chkSun.Unchecked += (s, ev) => persist();

                // Alerts master switches
                chkAlertsEnabled.Checked += (s, ev) =>
                {
                    _settings.AlertsEnabled = true; _ = _settings_service.SaveAsync(_settings);
                    var always = chkAlertAlways.IsChecked == true;
                    chkMon.IsEnabled = !always; chkTue.IsEnabled = !always; chkWed.IsEnabled = !always; chkThu.IsEnabled = !always; chkFri.IsEnabled = !always; chkSat.IsEnabled = !always; chkSun.IsEnabled = !always;
                    txtWorkStart.IsEnabled = !always; txtWorkEnd.IsEnabled = !always; txtLunchStart.IsEnabled = !always; txtLunchEnd.IsEnabled = !always; txtAlertThreshold.IsEnabled = true;
                };

                chkAlertsEnabled.Unchecked += (s, ev) =>
                {
                    _settings.AlertsEnabled = false; _ = _settings_service.SaveAsync(_settings);
                    chkMon.IsEnabled = chkTue.IsEnabled = chkWed.IsEnabled = chkThu.IsEnabled = chkFri.IsEnabled = chkSat.IsEnabled = chkSun.IsEnabled = false;
                    txtWorkStart.IsEnabled = txtWorkEnd.IsEnabled = txtLunchStart.IsEnabled = txtLunchEnd.IsEnabled = txtAlertThreshold.IsEnabled = false;
                };

                chkAlertAlways.Checked += (s, ev) =>
                {
                    _settings.AlertAlways = true; _ = _settings_service.SaveAsync(_settings);
                    chkMon.IsEnabled = chkTue.IsEnabled = chkWed.IsEnabled = chkThu.IsEnabled = chkFri.IsEnabled = chkSat.IsEnabled = chkSun.IsEnabled = false;
                    txtWorkStart.IsEnabled = txtWorkEnd.IsEnabled = txtLunchStart.IsEnabled = txtLunchEnd.IsEnabled = false;
                };

                chkAlertAlways.Unchecked += (s, ev) =>
                {
                    _settings.AlertAlways = false; _ = _settings_service.SaveAsync(_settings);
                    var enabled = chkAlertsEnabled.IsChecked == true;
                    chkMon.IsEnabled = chkTue.IsEnabled = chkWed.IsEnabled = chkThu.IsEnabled = chkFri.IsEnabled = chkSat.IsEnabled = chkSun.IsEnabled = enabled;
                    txtWorkStart.IsEnabled = txtWorkEnd.IsEnabled = txtLunchStart.IsEnabled = txtLunchEnd.IsEnabled = enabled;
                };
            }
            catch { }

            // Populate saved credentials list (best-effort)
            try
            {
                var list = _credentialService.ListSavedCredentials();
                var lb = this.FindName("lstSavedCredentials") as System.Windows.Controls.ListBox;
                if (lb != null)
                {
                    lb.Items.Clear();
                    foreach (var item in list)
                    {
                        var label = string.Equals(item.Category, "smtp", StringComparison.OrdinalIgnoreCase) ? "[SMTP]" : "[FTP]";
                        lb.Items.Add($"{label} {item.Host} : {item.Username}");
                    }
                }
            }
            catch { }

            // Final initialization steps
            UpdateResponsiveSidebar();
            ClearLogIfNeeded();
            UpdateSidebarStatus(false);
            UpdateSidebarStats();

            // Start log flush timer
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

            // Restore last page without animation
            try
            {
                var page = string.IsNullOrWhiteSpace(_settings.LastPage) ? "Monitor" : _settings.LastPage!;
                ShowPage(page, animate: false);
            }
            catch { }

            _isLoaded = true;

            // Auto-start monitoring if requested and inputs valid
            try
            {
                if (_settings.StartMonitoringOnLaunch && ValidateInputs())
                {
                    BtnStart_Click(this, new RoutedEventArgs());
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to auto-start monitoring: {ex.Message}", LogLevel.Error);
            }

            // Handle startup args (minimize to tray when launched by Windows)
            try
            {
                var args = Environment.GetCommandLineArgs();
                bool startedFromRun = args.Any(a => string.Equals(a, "--startup", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "/startup", StringComparison.OrdinalIgnoreCase));
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

                // persist alert settings
                try { _settings.AlertsEnabled = chkAlertsEnabled.IsChecked == true; } catch { }
                try { _settings.AlertAlways = chkAlertAlways.IsChecked == true; } catch { }

                if (_settings_service != null) await _settings_service.SaveAsync(_settings);

                try
                {
                    // explicitly save as 'ftp' category
                    _credentialService.Save(_settings.Host ?? string.Empty, _settings.Username ?? string.Empty, txtPassword.Password ?? string.Empty, "ftp");
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
