using FTP_Tool.Services;
using Microsoft.Win32;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using System.Linq;
using System;
using FTP_Tool.Models;
using System.Collections.ObjectModel;

namespace FTP_Tool
{
    public partial class MainWindow
    {
        private ObservableCollection<IntervalItem> _workShifts = new();
        private ObservableCollection<IntervalItem> _excludedIntervals = new();

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
                try { _ftpService.Logger = (msg, lvl) => Log(msg, lvl); } catch { }
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
                try { chkAutoStart.IsChecked = _settings.StartWithWindows; } catch { }

                try
                {
                    if (cmbStartupMode != null)
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
                var cred = _credentialService.Load(_settings.Host ?? string.Empty, _settings.Username ?? string.Empty, "ftp");
                if (cred.HasValue && !string.IsNullOrEmpty(cred.Value.Password)) txtPassword.Password = cred.Value.Password;
            }
            catch { }

            // Load email related settings and alert settings FIRST (before wiring handlers)
            try
            {
                txtSmtpHost.Text = _settings.SmtpHost ?? string.Empty;
                txtSmtpPort.Text = _settings.SmtpPort.ToString();
                chkSmtpSsl.IsChecked = _settings.SmtpEnableSsl;
                txtSmtpUser.Text = _settings.SmtpUsername ?? string.Empty;

                try
                {
                    var smtpCred = _credentialService.Load(_settings.SmtpHost ?? string.Empty, _settings.SmtpUsername ?? string.Empty, "smtp");
                    if (smtpCred.HasValue) txtSmtpPass.Password = smtpCred.Value.Password ?? string.Empty;
                }
                catch { }

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
                catch { }

                txtWorkStart.Text = _settings.WorkStart ?? "08:00";
                txtWorkEnd.Text = _settings.WorkEnd ?? "17:00";
                txtLunchStart.Text = _settings.LunchStart ?? "12:00";
                txtLunchEnd.Text = _settings.LunchEnd ?? "13:00";
                txtAlertThreshold.Text = (_settings.AlertThresholdMinutes > 0 ? _settings.AlertThresholdMinutes : 15).ToString();

                // CRITICAL: Load alert states BEFORE ApplyAlertUiState is called
                // Master alert settings
                if (chkAlertsEnabled != null) chkAlertsEnabled.IsChecked = _settings.AlertsEnabled;
                if (chkAlertAlways != null) chkAlertAlways.IsChecked = _settings.AlertAlways;
                if (chkAllDay != null) chkAllDay.IsChecked = _settings.AllDay;
                if (chkSendDownloadAlerts != null) chkSendDownloadAlerts.IsChecked = _settings.SendDownloadAlerts;
                if (chkSendAlertsWhenNotMonitoring != null) chkSendAlertsWhenNotMonitoring.IsChecked = _settings.SendAlertsWhenNotMonitoring;

                // Email type options
                if (chkEmailInfo != null) chkEmailInfo.IsChecked = _settings.EmailOnInfo;
                if (chkEmailWarnings != null) chkEmailWarnings.IsChecked = _settings.EmailOnWarnings;
                if (chkEmailErrors != null) chkEmailErrors.IsChecked = _settings.EmailOnErrors;
                if (txtEmailSummaryInterval != null) txtEmailSummaryInterval.Text = _settings.EmailSummaryIntervalMinutes.ToString();

                // Multi-shift UI population (load data AND set checkbox state)
                if (chkUseMultiShift != null) chkUseMultiShift.IsChecked = _settings.UseMultiShiftMode;

                _workShifts = new ObservableCollection<IntervalItem>();
                if (!string.IsNullOrWhiteSpace(_settings.WorkShifts))
                {
                    var parts = _settings.WorkShifts.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());
                    foreach (var p in parts) _workShifts.Add(IntervalItem.ParseFromString(p));
                }
                dgWorkShifts.ItemsSource = _workShifts;

                _excludedIntervals = new ObservableCollection<IntervalItem>();
                if (!string.IsNullOrWhiteSpace(_settings.ExcludedIntervals))
                {
                    var parts = _settings.ExcludedIntervals.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());
                    foreach (var p in parts) _excludedIntervals.Add(IntervalItem.ParseFromString(p));
                }
                dgExcluded.ItemsSource = _excludedIntervals;
            }
            catch { }

            // Wire handlers (only once during load)
            try
            {
                // Auto start handlers
                if (chkAutoStart != null)
                {
                    chkAutoStart.Checked += (s, ev) => { _settings.StartWithWindows = true; _ = _settings_service.SaveAsync(_settings); EnableAutoStart(); try { if (cmbStartupMode != null) cmbStartupMode.IsEnabled = true; } catch { } };
                    chkAutoStart.Unchecked += (s, ev) => { _settings.StartWithWindows = false; _ = _settings_service.SaveAsync(_settings); DisableAutoStart(); try { if (cmbStartupMode != null) cmbStartupMode.IsEnabled = false; } catch { } };
                }

                if (cmbStartupMode != null)
                {
                    cmbStartupMode.SelectionChanged += (s, ev) => { _settings.StartMinimizedOnStartup = cmbStartupMode.SelectedIndex == 1; _ = _settings_service.SaveAsync(_settings); try { if (chkAutoStart?.IsChecked == true) EnableAutoStart(); } catch { } };
                }

                chkMinimizeToTray.Checked += (s, ev) => { _settings.MinimizeToTray = true; _ = _settings_service.SaveAsync(_settings); };
                chkMinimizeToTray.Unchecked += (s, ev) => { _settings.MinimizeToTray = false; _ = _settings_service.SaveAsync(_settings); };

                try { chkStartMonitoringOnLaunch.Checked += (s, ev) => { _settings.StartMonitoringOnLaunch = true; _ = _settings_service.SaveAsync(_settings); }; chkStartMonitoringOnLaunch.Unchecked += (s, ev) => { _settings.StartMonitoringOnLaunch = false; _ = _settings_service.SaveAsync(_settings); }; } catch { }

                // Advanced options
                txtConnectionTimeout.LostFocus += (s, ev) =>
                {
                    if (int.TryParse(txtConnectionTimeout.Text, out var val))
                    {
                        _settings.ConnectionTimeoutSeconds = Math.Max(1, val);
                        _ftpService.ApplyOptions(_settings.ConnectionTimeoutSeconds, _settings.MaxRetryAttempts, _settings.UsePassiveMode);
                        _ = _settings_service.SaveAsync(_settings);
                    }
                    else { txtConnectionTimeout.Text = _settings.ConnectionTimeoutSeconds.ToString(); }
                };

                txtMaxRetries.LostFocus += (s, ev) =>
                {
                    if (int.TryParse(txtMaxRetries.Text, out var val))
                    {
                        _settings.MaxRetryAttempts = Math.Max(0, val);
                        _ftpService.ApplyOptions(_settings.ConnectionTimeoutSeconds, _settings.MaxRetryAttempts, _settings.UsePassiveMode);
                        _ = _settings_service.SaveAsync(_settings);
                    }
                    else { txtMaxRetries.Text = _settings.MaxRetryAttempts.ToString(); }
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
                    else { txtLogRetentionDays.Text = _settings.LogRetentionDays.ToString(); }
                };

                // Logging handlers
                chkLogToFile.Checked += (s, ev) => { _settings.LogToFile = true; _logging_service?.ApplySettings(_logFilePath, _settings); _ = _settings_service.SaveAsync(_settings); };
                chkLogToFile.Unchecked += (s, ev) => { _settings.LogToFile = false; _logging_service?.ApplySettings(_logFilePath, _settings); _ = _settings_service.SaveAsync(_settings); };

                cmbMinimumLogLevel.SelectionChanged += (s, ev) =>
                {
                    if (cmbMinimumLogLevel.SelectedItem is ComboBoxItem selected) { _settings.MinimumLogLevel = selected.Content?.ToString() ?? "Info"; _logging_service?.ApplySettings(_logFilePath, _settings); _ = _settings_service.SaveAsync(_settings); }
                };

                txtMaxLogLines.LostFocus += (s, ev) =>
                {
                    if (int.TryParse(txtMaxLogLines.Text, out var maxLines)) { _settings.MaxLogLines = Math.Max(0, maxLines); _ = _settings_service.SaveAsync(_settings); } else { txtMaxLogLines.Text = _settings.MaxLogLines.ToString(); }
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

                // Wire multi-shift toggle so the UI updates immediately when user changes it
                try
                {
                    if (chkUseMultiShift != null)
                    {
                        chkUseMultiShift.Checked += (s, ev) => ApplyMultiShiftState(true);
                        chkUseMultiShift.Unchecked += (s, ev) => ApplyMultiShiftState(false);
                        // initialize based on current checkbox state (or settings already applied earlier)
                        ApplyMultiShiftState(chkUseMultiShift.IsChecked == true);
                    }
                }
                catch { }

                // Note: Add/Edit dialog logic removed. Inline DataGrid editing and RowEditEnding validation are used instead.
                // Remove buttons are wired via XAML Click handlers to the methods implemented below.

                // Alerts master switches - properly wire the handlers
                try
                {
                    // Alerts enable/disable handlers
                    if (chkAlertsEnabled != null)
                    {
                        chkAlertsEnabled.Checked += (s, ev) => { _settings.AlertsEnabled = true; _ = _settings_service.SaveAsync(_settings); ApplyAlertUiState(); _ = MaybeSendNoDownloadAlertAsync(CancellationToken.None); };
                        chkAlertsEnabled.Unchecked += (s, ev) => { _settings.AlertsEnabled = false; _ = _settings_service.SaveAsync(_settings); ApplyAlertUiState(); };
                    }

                    // AlertAlways (7/24) - when checked, clear AllDay and disable hours
                    if (chkAlertAlways != null)
                    {
                        chkAlertAlways.Checked += (s, ev) =>
                        {
                            _settings.AlertAlways = true;
                            try { if (chkAllDay != null) chkAllDay.IsChecked = false; } catch { }
                            try { _settings.AlertsEnabled = true; if (chkAlertsEnabled != null) chkAlertsEnabled.IsChecked = true; } catch { }
                            _ = _settings_service.SaveAsync(_settings);
                            ApplyAlertUiState();
                        };
                        chkAlertAlways.Unchecked += (s, ev) => { _settings.AlertAlways = false; _ = _settings_service.SaveAsync(_settings); ApplyAlertUiState(); };
                    }

                    // All-day handlers - when checked, clear AlertAlways
                    if (chkAllDay != null)
                    {
                        chkAllDay.Checked += (s, ev) => { _settings.AllDay = true; try { if (chkAlertAlways != null) chkAlertAlways.IsChecked = false; } catch { } _ = _settings_service.SaveAsync(_settings); ApplyAlertUiState(); };
                        chkAllDay.Unchecked += (s, ev) => { _settings.AllDay = false; _ = _settings_service.SaveAsync(_settings); ApplyAlertUiState(); };
                    }

                    // Send-downloads checkbox handlers
                    if (chkSendDownloadAlerts != null)
                    {
                        chkSendDownloadAlerts.Checked += (s, ev) => { _settings.SendDownloadAlerts = true; _ = _settings_service.SaveAsync(_settings); ApplyAlertUiState(); _ = MaybeSendNoDownloadAlertAsync(CancellationToken.None); };
                        chkSendDownloadAlerts.Unchecked += (s, ev) => { _settings.SendDownloadAlerts = false; _ = _settings_service.SaveAsync(_settings); ApplyAlertUiState(); };
                    }

                    // Send alerts when not monitoring handlers
                    if (chkSendAlertsWhenNotMonitoring != null)
                    {
                        chkSendAlertsWhenNotMonitoring.Checked += (s, ev) => 
                        { 
                            _settings.SendAlertsWhenNotMonitoring = true; 
                            _ = _settings_service.SaveAsync(_settings);
                            // Start the background alert timer
                            if (_alertTimer != null && !_alertTimer.IsEnabled)
                            {
                                _alertTimer.Start();
                                Log("Background alert timer started (alerts enabled when not monitoring)", LogLevel.Info);
                            }
                        };
                        chkSendAlertsWhenNotMonitoring.Unchecked += (s, ev) => 
                        { 
                            _settings.SendAlertsWhenNotMonitoring = false; 
                            _ = _settings_service.SaveAsync(_settings);
                            // Stop the background alert timer
                            if (_alertTimer != null && _alertTimer.IsEnabled)
                            {
                                _alertTimer.Stop();
                                Log("Background alert timer stopped", LogLevel.Info);
                            }
                        };
                    }
				
                    // Email option handlers
                    if (chkEmailInfo != null) { chkEmailInfo.Checked += (s, ev) => { _settings.EmailOnInfo = true; _ = _settings_service.SaveAsync(_settings); }; chkEmailInfo.Unchecked += (s, ev) => { _settings.EmailOnInfo = false; _ = _settings_service.SaveAsync(_settings); }; }
                    if (chkEmailWarnings != null) { chkEmailWarnings.Checked += (s, ev) => { _settings.EmailOnWarnings = true; _ = _settings_service.SaveAsync(_settings); }; chkEmailWarnings.Unchecked += (s, ev) => { _settings.EmailOnWarnings = false; _ = _settings_service.SaveAsync(_settings); }; }
                    if (chkEmailErrors != null) { chkEmailErrors.Checked += (s, ev) => { _settings.EmailOnErrors = true; _ = _settings_service.SaveAsync(_settings); }; chkEmailErrors.Unchecked += (s, ev) => { _settings.EmailOnErrors = false; _ = _settings_service.SaveAsync(_settings); }; }

                    if (txtEmailSummaryInterval != null)
                    {
                        txtEmailSummaryInterval.LostFocus += (s, ev) =>
                        {
                            if (int.TryParse(txtEmailSummaryInterval.Text, out var val)) { _settings.EmailSummaryIntervalMinutes = Math.Max(1, val); _ = _settings_service.SaveAsync(_settings); } else { txtEmailSummaryInterval.Text = _settings.EmailSummaryIntervalMinutes.ToString(); }
                        };
                    }

                    // Persist alert threshold on focus loss
                    if (txtAlertThreshold != null)
                    {
                        txtAlertThreshold.LostFocus += (s, ev) =>
                        {
                            if (int.TryParse(txtAlertThreshold.Text, out var val)) { _settings.AlertThresholdMinutes = Math.Max(1, val); _ = _settings_service.SaveAsync(_settings); } else { try { txtAlertThreshold.Text = (_settings.AlertThresholdMinutes > 0 ? _settings.AlertThresholdMinutes : 15).ToString(); } catch { } }
                        };
                    }

                    // Persist work / lunch times when user edits the textboxes (validate HH:mm)
                    if (txtWorkStart != null)
                    {
                        txtWorkStart.LostFocus += (s, ev) => { try { if (TimeSpan.TryParse(txtWorkStart.Text, out var ts)) { _settings.WorkStart = ts.ToString(@"hh\:mm"); _ = _settings_service.SaveAsync(_settings); } else { txtWorkStart.Text = _settings.WorkStart ?? "08:00"; } } catch { } };
                    }

                    if (txtWorkEnd != null)
                    {
                        txtWorkEnd.LostFocus += (s, ev) => { try { if (TimeSpan.TryParse(txtWorkEnd.Text, out var ts)) { _settings.WorkEnd = ts.ToString(@"hh\:mm"); _ = _settings_service.SaveAsync(_settings); } else { txtWorkEnd.Text = _settings.WorkEnd ?? "17:00"; } } catch { } };
                    }

                    if (txtLunchStart != null)
                    {
                        txtLunchStart.LostFocus += (s, ev) => { try { if (TimeSpan.TryParse(txtLunchStart.Text, out var ts)) { _settings.LunchStart = ts.ToString(@"hh\:mm"); _ = _settings_service.SaveAsync(_settings); } else { txtLunchStart.Text = _settings.LunchStart ?? "12:00"; } } catch { } };
                    }

                    if (txtLunchEnd != null)
                    {
                        txtLunchEnd.LostFocus += (s, ev) => { try { if (TimeSpan.TryParse(txtLunchEnd.Text, out var ts)) { _settings.LunchEnd = ts.ToString(@"hh\:mm"); _ = _settings_service.SaveAsync(_settings); } else { txtLunchEnd.Text = _settings.LunchEnd ?? "13:00"; } } catch { } };
                    }

                    // Final initialization step for alerts UI
                    ApplyAlertUiState();
                }
                catch { }
            }
            catch { }

            // Final initialization steps
            try
            {
                // Populate saved credentials list (best-effort)
                var ftpList = _credentialService.ListSavedCredentials("ftp");
                var smtpList = _credentialService.ListSavedCredentials("smtp");
                if (this.FindName("lstSavedCredentials") is System.Windows.Controls.ListBox lb)
                {
                    lb.Items.Clear();
                    foreach (var item in ftpList) lb.Items.Add($"[FTP] {item.Host} : {item.Username}");
                    foreach (var item in smtpList) lb.Items.Add($"[SMTP] {item.Host} : {item.Username}");
                }

                // Final initialization steps
                UpdateResponsiveSidebar();
                ClearLogIfNeeded();
                UpdateSidebarStatus(false);
                UpdateSidebarStats();

                // Start log flush timer
                _logFlushTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
                _logFlushTimer.Tick += (s, ev) => FlushPendingLogs();
                _logFlushTimer.Start();
                
                // Initialize background alert timer (checks every minute)
                _alertTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
                _alertTimer.Tick += async (s, ev) =>
                {
                    try
                    {
                        // Only run if SendAlertsWhenNotMonitoring is enabled and monitoring is NOT active
                        if (_settings?.SendAlertsWhenNotMonitoring == true && !IsMonitoringActive)
                        {
                            await MaybeSendNoDownloadAlertAsync(CancellationToken.None);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Alert timer error: {ex.Message}", LogLevel.Debug);
                    }
                };
                
                // Start the alert timer if the setting is enabled
                if (_settings?.SendAlertsWhenNotMonitoring == true)
                {
                    _alertTimer.Start();
                    Log("Background alert timer started (monitoring not required for alerts)", LogLevel.Info);
                }

                // Restore last page without animation
                try { var page = string.IsNullOrWhiteSpace(_settings.LastPage) ? "Monitor" : _settings.LastPage!; ShowPage(page, animate: false); } catch { }

                _isLoaded = true;

                // Auto-start monitoring if requested and inputs valid
                try { if (_settings.StartMonitoringOnLaunch && ValidateInputs()) BtnStart_Click(this, new RoutedEventArgs()); } catch (Exception ex) { Log($"Failed to auto-start monitoring: {ex.Message}", LogLevel.Error); }

                // Handle startup args
                try
                {
                    var args = Environment.GetCommandLineArgs();
                    bool startedFromRun = args.Any(a => string.Equals(a, "--startup", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "/startup", StringComparison.OrdinalIgnoreCase));
                    if (startedFromRun && _settings.StartMinimizedOnStartup) HideToTray();
                }
                catch { }
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
                try { _settings.SendAlertsWhenNotMonitoring = chkSendAlertsWhenNotMonitoring.IsChecked == true; } catch { }

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

                // Persist multi-shift and excluded intervals
                try
                {
                    try { _settings.UseMultiShiftMode = chkUseMultiShift.IsChecked == true; } catch { }

                    if (_workShifts != null)
                    {
                        var items = _workShifts.Select(i => i.IntervalString);
                        _settings.WorkShifts = string.Join(';', items);
                    }

                    if (_excludedIntervals != null)
                    {
                        var items = _excludedIntervals.Select(i => i.IntervalString);
                        _settings.ExcludedIntervals = string.Join(';', items);
                    }
                }
                catch { }

                if (_settings_service != null) await _settings_service.SaveAsync(_settings);

                try { _credentialService.Save(_settings.Host ?? string.Empty, _settings.Username ?? string.Empty, txtPassword.Password ?? string.Empty, "ftp"); } catch { }
            }
            catch { }

            // stop log flush timer
            try { if (_logFlushTimer != null) { _logFlushTimer.Stop(); _logFlushTimer = null; } } catch { }
            
            // stop alert timer
            try { if (_alertTimer != null) { _alertTimer.Stop(); _alertTimer = null; } } catch { }

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

        private static readonly char[] separatorArray0 = new[] { ',', ';' };

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
                    if (string.IsNullOrEmpty(hostExe) || hostExe.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) hostExe = "dotnet";
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

        private static string ProcessExecutablePathFallback()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty; } catch { return string.Empty; }
        }

        // XAML Click handlers - ensure these are present for XAML wiring
        private void BtnRemoveWorkShift_Click(object sender, RoutedEventArgs e) => BtnRemoveWorkShift_Click_Core();
        private void BtnRemoveExcludedInterval_Click(object sender, RoutedEventArgs e) => BtnRemoveExcludedInterval_Click_Core();

        // Core implementations (used both from dynamic wiring and XAML handlers)
        private void BtnRemoveWorkShift_Click_Core()
        {
            try
            {
                if (dgWorkShifts.SelectedItem is IntervalItem it)
                {
                    _workShifts.Remove(it);
                    PersistWorkShifts();
                }
            }
            catch (Exception ex) { Log($"RemoveWorkShift error: {ex.Message}", LogLevel.Debug); }
        }

        private void BtnRemoveExcludedInterval_Click_Core()
        {
            try
            {
                if (dgExcluded.SelectedItem is IntervalItem it)
                {
                    _excludedIntervals.Remove(it);
                    PersistExcludedIntervals();
                }
            }
            catch (Exception ex) { Log($"RemoveExcludedInterval error: {ex.Message}", LogLevel.Debug); }
        }

        // RowEditEnding handlers for inline validation and persistence
        private void DgWorkShifts_RowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Row.Item is IntervalItem item)
                {
                    if (!item.IsValid())
                    {
                        System.Windows.MessageBox.Show("Invalid interval. Please use HH:mm format for start and end.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(item.Start) && string.IsNullOrWhiteSpace(item.End))
                    {
                        _workShifts.Remove(item);
                    }
                    PersistWorkShifts();
                }
            }));
        }

        private void DgExcluded_RowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Row.Item is IntervalItem item)
                {
                    if (!item.IsValid())
                    {
                        System.Windows.MessageBox.Show("Invalid interval. Please use HH:mm format for start and end.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(item.Start) && string.IsNullOrWhiteSpace(item.End))
                    {
                        _excludedIntervals.Remove(item);
                    }
                    PersistExcludedIntervals();
                }
            }));
        }

        // Persist helpers
        private void PersistWorkShifts()
        {
            try
            {
                var items = _workShifts.Select(i => i.IntervalString);
                _settings.WorkShifts = string.Join(';', items);
                _ = _settings_service.SaveAsync(_settings);
            }
            catch { }
        }

        private void PersistExcludedIntervals()
        {
            try
            {
                var items = _excludedIntervals.Select(i => i.IntervalString);
                _settings.ExcludedIntervals = string.Join(';', items);
                _ = _settings_service.SaveAsync(_settings);
            }
            catch { }
        }

        private void ApplyMultiShiftState(bool useMulti)
        {
            try
            {
                // Hide or disable single-period inputs when multi-shift active
                if (brdWorkHours != null) brdWorkHours.Visibility = useMulti ? Visibility.Collapsed : Visibility.Visible;
                if (brdLunchBreak != null) brdLunchBreak.Visibility = useMulti ? Visibility.Collapsed : Visibility.Visible;

                // Save setting
                try { _settings.UseMultiShiftMode = useMulti; _ = _settings_service.SaveAsync(_settings); } catch { }
                
                // Apply alert UI state after multi-shift state change to ensure proper enabling/disabling
                ApplyAlertUiState();
            }
            catch { }
        }

        // Add ApplyAlertUiState method that properly manages the enabled/disabled state of controls
        private void ApplyAlertUiState()
        {
            try
            {
                var enabled = chkAlertsEnabled?.IsChecked == true;
                var always = chkAlertAlways?.IsChecked == true;
                var allDay = chkAllDay?.IsChecked == true;
                var useMultiShift = chkUseMultiShift?.IsChecked == true;

                // Weekday checkboxes - disabled when AlertAlways is on
                if (chkMon != null) chkMon.IsEnabled = enabled && !always;
                if (chkTue != null) chkTue.IsEnabled = enabled && !always;
                if (chkWed != null) chkWed.IsEnabled = enabled && !always;
                if (chkThu != null) chkThu.IsEnabled = enabled && !always;
                if (chkFri != null) chkFri.IsEnabled = enabled && !always;
                if (chkSat != null) chkSat.IsEnabled = enabled && !always;
                if (chkSun != null) chkSun.IsEnabled = enabled && !always;

                // Hour inputs - disabled when AllDay, AlertAlways, or UseMultiShift is enabled
                var disableHours = allDay || always || useMultiShift;
                if (txtWorkStart != null) txtWorkStart.IsEnabled = enabled && !disableHours;
                if (txtWorkEnd != null) txtWorkEnd.IsEnabled = enabled && !disableHours;
                if (txtLunchStart != null) txtLunchStart.IsEnabled = enabled && !disableHours;
                if (txtLunchEnd != null) txtLunchEnd.IsEnabled = enabled && !disableHours;

                // Send-download checkbox - enabled when alerts are enabled
                if (chkSendDownloadAlerts != null) chkSendDownloadAlerts.IsEnabled = enabled;

                // Threshold textbox - enabled only when alerts enabled AND send-downloads checked
                if (txtAlertThreshold != null) txtAlertThreshold.IsEnabled = enabled && (chkSendDownloadAlerts?.IsChecked == true);
            }
            catch { }
        }
    }
}
