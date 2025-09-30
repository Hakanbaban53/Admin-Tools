using FTP_Tool.Services;
using Microsoft.Win32;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Threading;

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
            catch (Exception ex)
            {
                // Log failure to create logging service so we have diagnostics
                try { Log($"Failed to initialize logging service: {ex.Message}", LogLevel.Warning); } catch { }
            }

            // Apply FTP options and forward ftp logging
            try
            {
                _ftpService.ApplyOptions(_settings.ConnectionTimeoutSeconds, _settings.MaxRetryAttempts, _settings.UsePassiveMode);
                try { _ftpService.Logger = (msg, lvl) => Log(msg, lvl); } catch (Exception ex) { try { Log($"Failed to attach FTP logger: {ex.Message}", LogLevel.Debug); } catch { } }
            }
            catch (Exception ex)
            {
                try { Log($"Failed to apply FTP options: {ex.Message}", LogLevel.Warning); } catch { }
            }

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
                try { chkAutoStart.IsChecked = _settings.StartWithWindows; } catch (Exception ex) { try { Log($"Failed to set chkAutoStart: {ex.Message}", LogLevel.Debug); } catch { } }

                try
                {
                    if (cmbStartupMode != null)
                    {
                        cmbStartupMode.SelectedIndex = _settings.StartMinimizedOnStartup ? 1 : 0;
                    }
                }
                catch (Exception ex)
                {
                    try { Log($"Failed to set startup mode: {ex.Message}", LogLevel.Debug); } catch { }
                }

                chkMinimizeToTray.IsChecked = _settings.MinimizeToTray;
                try { chkStartMonitoringOnLaunch.IsChecked = _settings.StartMonitoringOnLaunch; } catch (Exception ex) { try { Log($"Failed to set StartMonitoringOnLaunch: {ex.Message}", LogLevel.Debug); } catch { } }

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
            catch (Exception ex)
            {
                try { Log($"Failed to load saved FTP credential: {ex.Message}", LogLevel.Warning); } catch { }
            }

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
                catch (Exception ex)
                {
                    try { Log($"Failed to load SMTP credential: {ex.Message}", LogLevel.Warning); } catch { }
                }

                txtEmailFrom.Text = _settings.EmailFrom ?? string.Empty;

                LoadRecipientsFromSettings();

                // Weekday selections
                try
                {
                    var days = (_settings.AlertWeekdays ?? string.Empty).Split(separatorArray0, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());
                    chkMon.IsChecked = days.Contains("Mon");
                    chkTue.IsChecked = days.Contains("Tue");
                    chkWed.IsChecked = days.Contains("Wed");
                    chkThu.IsChecked = days.Contains("Thu");
                    chkFri.IsChecked = days.Contains("Fri");
                    chkSat.IsChecked = days.Contains("Sat");
                    chkSun.IsChecked = days.Contains("Sun");
                }
                catch (Exception ex)
                {
                    try { Log($"Failed to apply weekday selections: {ex.Message}", LogLevel.Debug); } catch { }
                }

                txtWorkStart.Text = _settings.WorkStart ?? "08:00";
                txtWorkEnd.Text = _settings.WorkEnd ?? "17:00";
                txtLunchStart.Text = _settings.LunchStart ?? "12:00";
                txtLunchEnd.Text = _settings.LunchEnd ?? "13:00";
                // New: all-day checkbox state
                try { chkAllDay.IsChecked = _settings.AllDay; } catch { }
                txtAlertThreshold.Text = (_settings.AlertThresholdMinutes > 0 ? _settings.AlertThresholdMinutes : 15).ToString();

                // New email options
                try
                {
                    chkEmailInfo.IsChecked = _settings.EmailOnInfo;
                    chkEmailWarnings.IsChecked = _settings.EmailOnWarnings;
                    chkEmailErrors.IsChecked = _settings.EmailOnErrors;
                    txtEmailSummaryInterval.Text = _settings.EmailSummaryIntervalMinutes.ToString();

                    // new send-download-alerts checkbox
                    try { chkSendDownloadAlerts.IsChecked = _settings.SendDownloadAlerts; } catch { }

                    // reflect master alerts enabled state
                    try { chkSendDownloadAlerts.IsEnabled = _settings.AlertsEnabled; } catch { }

                    // enable/disable threshold input based on both master alerts and send-downloads setting
                    try { txtAlertThreshold.IsEnabled = _settings.AlertsEnabled && _settings.SendDownloadAlerts; } catch { }
                }
                catch { }

                // Alert master switches
                try
                {
                    if (chkAlertsEnabled != null) chkAlertsEnabled.IsChecked = _settings.AlertsEnabled;
                    if (chkAlertAlways != null) chkAlertAlways.IsChecked = _settings.AlertAlways;

                    var alertsEnabled = chkAlertsEnabled?.IsChecked == true;
                    var alertAlways = chkAlertAlways?.IsChecked == true;

                    if (chkMon != null) chkMon.IsEnabled = alertsEnabled && !alertAlways;
                    if (chkTue != null) chkTue.IsEnabled = alertsEnabled && !alertAlways;
                    if (chkWed != null) chkWed.IsEnabled = alertsEnabled && !alertAlways;
                    if (chkThu != null) chkThu.IsEnabled = alertsEnabled && !alertAlways;
                    if (chkFri != null) chkFri.IsEnabled = alertsEnabled && !alertAlways;
                    if (chkSat != null) chkSat.IsEnabled = alertsEnabled && !alertAlways;
                    if (chkSun != null) chkSun.IsEnabled = alertsEnabled && !alertAlways;
                    if (txtWorkStart != null) txtWorkStart.IsEnabled = alertsEnabled && !alertAlways;
                    if (txtWorkEnd != null) txtWorkEnd.IsEnabled = alertsEnabled && !alertAlways;
                    if (txtLunchStart != null) txtLunchStart.IsEnabled = alertsEnabled && !alertAlways;
                    if (txtLunchEnd != null) txtLunchEnd.IsEnabled = alertsEnabled && !alertAlways;
                    if (txtAlertThreshold != null) txtAlertThreshold.IsEnabled = alertsEnabled;
                }
                catch { }
            }
            catch (Exception ex)
            {
                try { Log($"Error while loading email/settings UI: {ex.Message}", LogLevel.Warning); } catch { }
            }

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
                        try { if (cmbStartupMode != null) cmbStartupMode.IsEnabled = true; } catch { }
                    };

                    chkAutoStart.Unchecked += (s, ev) =>
                    {
                        _settings.StartWithWindows = false;
                        _ = _settings_service.SaveAsync(_settings);
                        DisableAutoStart();
                        try { if (cmbStartupMode != null) cmbStartupMode.IsEnabled = false; } catch { }
                    };
                }

                if (cmbStartupMode != null)
                {
                    cmbStartupMode.SelectionChanged += (s, ev) =>
                    {
                        _settings.StartMinimizedOnStartup = cmbStartupMode.SelectedIndex == 1;
                        _ = _settings_service.SaveAsync(_settings);
                        try { if (chkAutoStart?.IsChecked == true) EnableAutoStart(); } catch { }
                    };
                }

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
                    if (cmbMinimumLogLevel.SelectedItem is ComboBoxItem selected)
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
                void persist() => PersistWeekdays();
                if (chkMon != null) { chkMon.Checked += (s, ev) => persist(); chkMon.Unchecked += (s, ev) => persist(); }
                if (chkTue != null) { chkTue.Checked += (s, ev) => persist(); chkTue.Unchecked += (s, ev) => persist(); }
                if (chkWed != null) { chkWed.Checked += (s, ev) => persist(); chkWed.Unchecked += (s, ev) => persist(); }
                if (chkThu != null) { chkThu.Checked += (s, ev) => persist(); chkThu.Unchecked += (s, ev) => persist(); }
                if (chkFri != null) { chkFri.Checked += (s, ev) => persist(); chkFri.Unchecked += (s, ev) => persist(); }
                if (chkSat != null) { chkSat.Checked += (s, ev) => persist(); chkSat.Unchecked += (s, ev) => persist(); }
                if (chkSun != null) { chkSun.Checked += (s, ev) => persist(); chkSun.Unchecked += (s, ev) => persist(); }

                // Alerts master switches
                var alertsEnabledCb = chkAlertsEnabled;
                var alertAlwaysCb = chkAlertAlways;
                var sendDownloadCb = chkSendDownloadAlerts;
                var monCb = chkMon; var tueCb = chkTue; var wedCb = chkWed; var thuCb = chkThu; var friCb = chkFri; var satCb = chkSat; var sunCb = chkSun;
                var workStartTb = txtWorkStart; var workEndTb = txtWorkEnd; var lunchStartTb = txtLunchStart; var lunchEndTb = txtLunchEnd; var alertThresholdTb = txtAlertThreshold;

                // initialize states
                if (alertsEnabledCb != null) alertsEnabledCb.IsChecked = _settings.AlertsEnabled;
                if (alertAlwaysCb != null) alertAlwaysCb.IsChecked = _settings.AlertAlways;
                if (sendDownloadCb != null) sendDownloadCb.IsChecked = _settings.SendDownloadAlerts;
                try { if (chkAllDay != null) chkAllDay.IsChecked = _settings.AllDay; } catch { }

                // Apply UI state for alerts and schedule
                void ApplyAlertUiState()
                {
                    var enabled = alertsEnabledCb?.IsChecked == true;
                    var always = alertAlwaysCb?.IsChecked == true;
                    var allDay = chkAllDay?.IsChecked == true;

                    if (monCb != null) monCb.IsEnabled = enabled && !always;
                    if (tueCb != null) tueCb.IsEnabled = enabled && !always;
                    if (wedCb != null) wedCb.IsEnabled = enabled && !always;
                    if (thuCb != null) thuCb.IsEnabled = enabled && !always;
                    if (friCb != null) friCb.IsEnabled = enabled && !always;
                    if (satCb != null) satCb.IsEnabled = enabled && !always;
                    if (sunCb != null) sunCb.IsEnabled = enabled && !always;

                    // Disable hours inputs when AllDay or AlertAlways is enabled
                    var disableHours = allDay || always;
                    if (workStartTb != null) workStartTb.IsEnabled = enabled && !disableHours;
                    if (workEndTb != null) workEndTb.IsEnabled = enabled && !disableHours;
                    if (lunchStartTb != null) lunchStartTb.IsEnabled = enabled && !disableHours;
                    if (lunchEndTb != null) lunchEndTb.IsEnabled = enabled && !disableHours;

                    if (alertThresholdTb != null) alertThresholdTb.IsEnabled = enabled && (sendDownloadCb?.IsChecked == true);

                    if (sendDownloadCb != null) sendDownloadCb.IsEnabled = enabled;
                }

                ApplyAlertUiState();

                // Alerts enable/disable handlers
                if (alertsEnabledCb != null)
                {
                    alertsEnabledCb.Checked += (s, ev) => { _settings.AlertsEnabled = true; _ = _settings_service.SaveAsync(_settings); ApplyAlertUiState(); _ = MaybeSendNoDownloadAlertAsync(CancellationToken.None); };
                    alertsEnabledCb.Unchecked += (s, ev) => { _settings.AlertsEnabled = false; _ = _settings_service.SaveAsync(_settings); ApplyAlertUiState(); };
                }

                // AlertAlways (7/24) - when checked, clear AllDay and disable hours
                if (alertAlwaysCb != null)
                {
                    alertAlwaysCb.Checked += (s, ev) =>
                    {
                        // When AlertAlways (7/24) is checked, disable the custom AllDay and hours
                        _settings.AlertAlways = true;
                        try
                        {
                            if (this.FindName("chkAllDay") is System.Windows.Controls.CheckBox ad) { ad.IsChecked = false; }
                        }
                        catch { }
                        // Ensure alerts master switch is enabled when AlertAlways is checked
                        try
                        {
                            _settings.AlertsEnabled = true;
                            if (this.FindName("chkAlertsEnabled") is System.Windows.Controls.CheckBox master) master.IsChecked = true;
                        }
                        catch { }
                        _ = _settings_service.SaveAsync(_settings);
                        ApplyAlertUiState();
                    };
                    alertAlwaysCb.Unchecked += (s, ev) => { _settings.AlertAlways = false; _ = _settings_service.SaveAsync(_settings); ApplyAlertUiState(); };
                }

                // All-day handlers - when checked, clear AlertAlways
                if (chkAllDay != null)
                {
                    chkAllDay.Checked += (s, ev) =>
                    {
                        _settings.AllDay = true;
                        try { if (alertAlwaysCb != null) alertAlwaysCb.IsChecked = false; } catch { }
                        _ = _settings_service.SaveAsync(_settings);
                        ApplyAlertUiState();
                    };
                    chkAllDay.Unchecked += (s, ev) => { _settings.AllDay = false; _ = _settings_service.SaveAsync(_settings); ApplyAlertUiState(); };
                }

                // Send-downloads checkbox handlers
                if (sendDownloadCb != null)
                {
                    sendDownloadCb.Checked += (s, ev) => { _settings.SendDownloadAlerts = true; _ = _settings_service.SaveAsync(_settings); ApplyAlertUiState(); _ = MaybeSendNoDownloadAlertAsync(CancellationToken.None); };
                    sendDownloadCb.Unchecked += (s, ev) => { _settings.SendDownloadAlerts = false; _ = _settings_service.SaveAsync(_settings); ApplyAlertUiState(); };
                }

                // Email option handlers
                if (chkEmailInfo != null) { chkEmailInfo.Checked += (s, ev) => { _settings.EmailOnInfo = true; _ = _settings_service.SaveAsync(_settings); }; chkEmailInfo.Unchecked += (s, ev) => { _settings.EmailOnInfo = false; _ = _settings_service.SaveAsync(_settings); }; }
                if (chkEmailWarnings != null) { chkEmailWarnings.Checked += (s, ev) => { _settings.EmailOnWarnings = true; _ = _settings_service.SaveAsync(_settings); }; chkEmailWarnings.Unchecked += (s, ev) => { _settings.EmailOnWarnings = false; _ = _settings_service.SaveAsync(_settings); }; }
                if (chkEmailErrors != null) { chkEmailErrors.Checked += (s, ev) => { _settings.EmailOnErrors = true; _ = _settings_service.SaveAsync(_settings); }; chkEmailErrors.Unchecked += (s, ev) => { _settings.EmailOnErrors = false; _ = _settings_service.SaveAsync(_settings); }; }

                if (txtEmailSummaryInterval != null)
                {
                    txtEmailSummaryInterval.LostFocus += (s, ev) =>
                    {
                        if (int.TryParse(txtEmailSummaryInterval.Text, out var val))
                        {
                            _settings.EmailSummaryIntervalMinutes = Math.Max(1, val);
                            _ = _settings_service.SaveAsync(_settings);
                        }
                        else
                        {
                            txtEmailSummaryInterval.Text = _settings.EmailSummaryIntervalMinutes.ToString();
                        }
                    };
                }

                // Persist alert threshold on focus loss
                if (txtAlertThreshold != null)
                {
                    txtAlertThreshold.LostFocus += (s, ev) =>
                    {
                        if (int.TryParse(txtAlertThreshold.Text, out var val))
                        {
                            _settings.AlertThresholdMinutes = Math.Max(1, val);
                            _ = _settings_service.SaveAsync(_settings);
                        }
                        else
                        {
                            try { txtAlertThreshold.Text = (_settings.AlertThresholdMinutes > 0 ? _settings.AlertThresholdMinutes : 15).ToString(); } catch { }
                        }
                    };
                }

                // Persist work / lunch times when user edits the textboxes (validate HH:mm)
                if (txtWorkStart != null)
                {
                    txtWorkStart.LostFocus += (s, ev) =>
                    {
                        try
                        {
                            if (TimeSpan.TryParse(txtWorkStart.Text, out var ts))
                            {
                                _settings.WorkStart = ts.ToString(@"hh\:mm");
                                _ = _settings_service.SaveAsync(_settings);
                            }
                            else
                            {
                                txtWorkStart.Text = _settings.WorkStart ?? "08:00";
                            }
                        }
                        catch { }
                    };
                }

                if (txtWorkEnd != null)
                {
                    txtWorkEnd.LostFocus += (s, ev) =>
                    {
                        try
                        {
                            if (TimeSpan.TryParse(txtWorkEnd.Text, out var ts))
                            {
                                _settings.WorkEnd = ts.ToString(@"hh\:mm");
                                _ = _settings_service.SaveAsync(_settings);
                            }
                            else
                            {
                                txtWorkEnd.Text = _settings.WorkEnd ?? "17:00";
                            }
                        }
                        catch { }
                    };
                }

                if (txtLunchStart != null)
                {
                    txtLunchStart.LostFocus += (s, ev) =>
                    {
                        try
                        {
                            if (TimeSpan.TryParse(txtLunchStart.Text, out var ts))
                            {
                                _settings.LunchStart = ts.ToString(@"hh\:mm");
                                _ = _settings_service.SaveAsync(_settings);
                            }
                            else
                            {
                                txtLunchStart.Text = _settings.LunchStart ?? "12:00";
                            }
                        }
                        catch { }
                    };
                }

                if (txtLunchEnd != null)
                {
                    txtLunchEnd.LostFocus += (s, ev) =>
                    {
                        try
                        {
                            if (TimeSpan.TryParse(txtLunchEnd.Text, out var ts))
                            {
                                _settings.LunchEnd = ts.ToString(@"hh\:mm");
                                _ = _settings_service.SaveAsync(_settings);
                            }
                            else
                            {
                                txtLunchEnd.Text = _settings.LunchEnd ?? "13:00";
                            }
                        }
                        catch { }
                    };
                }

                // Final initialization steps
                ApplyAlertUiState();
            }
            catch { }

            // Populate saved credentials list (best-effort)
            try
            {
                var list = _credentialService.ListSavedCredentials();
                if (this.FindName("lstSavedCredentials") is System.Windows.Controls.ListBox lb)
                {
                    lb.Items.Clear();
                    foreach (var (Category, Host, Username) in list)
                    {
                        var label = string.Equals(Category, "smtp", StringComparison.OrdinalIgnoreCase) ? "[SMTP]" : "[FTP]";
                        lb.Items.Add($"{label} {Host} : {Username}");
                    }
                }
            }
            catch (Exception ex)
            {
                try { Log($"Failed to populate saved credentials: {ex.Message}", LogLevel.Warning); } catch { }
            }

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
                // ensure alert threshold persisted
                try { _settings.AlertThresholdMinutes = int.TryParse(txtAlertThreshold.Text, out var t) ? Math.Max(1, t) : _settings.AlertThresholdMinutes; } catch { }

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
                    if (this.FindName("chkAutoStart") is System.Windows.Controls.CheckBox autoCb) _settings.StartWithWindows = autoCb.IsChecked == true;
                }
                catch { }

                // persist StartMinimizedOnStartup from UI control
                try
                {
                    if (this.FindName("cmbStartupMode") is System.Windows.Controls.ComboBox cmb) _settings.StartMinimizedOnStartup = cmb.SelectedIndex == 1;
                }
                catch { }

                if (cmbMinimumLogLevel.SelectedItem is ComboBoxItem selected) _settings.MinimumLogLevel = selected.Content?.ToString() ?? "Info";

                if (int.TryParse(txtMaxLogLines.Text, out var maxLines)) _settings.MaxLogLines = Math.Max(0, maxLines);

                // advanced settings
                if (int.TryParse(txtConnectionTimeout.Text, out var timeout)) _settings.ConnectionTimeoutSeconds = Math.Max(1, timeout);
                if (int.TryParse(txtMaxRetries.Text, out var retries)) _settings.MaxRetryAttempts = Math.Max(0, retries);
                try { _settings.UsePassiveMode = chkUsePassiveMode.IsChecked == true; } catch { }
                if (int.TryParse(txtLogRetentionDays.Text, out var days)) _settings.LogRetentionDays = Math.Max(1, days);

                // persist alert settings
                try { _settings.AlertsEnabled = chkAlertsEnabled.IsChecked == true; } catch { }
                try { _settings.AlertAlways = chkAlertAlways.IsChecked == true; } catch { }

                // ensure send-downloads persisted
                try { _settings.SendDownloadAlerts = chkSendDownloadAlerts.IsChecked == true; } catch { }

                // persist new email options
                try { _settings.EmailOnInfo = chkEmailInfo.IsChecked == true; } catch { }
                try { _settings.EmailOnWarnings = chkEmailWarnings.IsChecked == true; } catch { }
                try { _settings.EmailOnErrors = chkEmailErrors.IsChecked == true; } catch { }
                try { _settings.EmailSummaryIntervalMinutes = int.TryParse(txtEmailSummaryInterval.Text, out var iv) ? Math.Max(1, iv) : _settings.EmailSummaryIntervalMinutes; } catch { }

                // Persist work and lunch times so they are not lost on exit
                try
                {
                    _settings.WorkStart = txtWorkStart.Text.Trim();
                    _settings.WorkEnd = txtWorkEnd.Text.Trim();
                    _settings.LunchStart = txtLunchStart.Text.Trim();
                    _settings.LunchEnd = txtLunchEnd.Text.Trim();
                    try { _settings.AllDay = chkAllDay.IsChecked == true; } catch { }
                }
                catch { }

                if (_settings_service != null) await _settings_service.SaveAsync(_settings);

                try
                {
                    // explicitly save as 'ftp' category
                    _credentialService.Save(_settings.Host ?? string.Empty, _settings.Username ?? string.Empty, txtPassword.Password ?? string.Empty, "ftp");
                }
                catch (Exception ex)
                {
                    try { Log($"Failed to save ftp credential on exit: {ex.Message}", LogLevel.Warning); } catch { }
                }
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
        private static readonly char[] separatorArray0 = [',', ';'];

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

        private static void DisableAutoStart()
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
        private static string ProcessExecutablePathFallback()
        {
            try
            {
                return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            }
            catch { return string.Empty; }
        }
    }
}
