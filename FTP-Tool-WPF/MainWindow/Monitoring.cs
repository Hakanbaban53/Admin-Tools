using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace FTP_Tool
{
    public partial class MainWindow
    {
        private Task? _monitoringTask;
        // Track when monitoring started so threshold can be measured even before first download
        private DateTime _monitoringStartedAt = DateTime.MinValue;
        // Track when we last sent an alert
        private DateTime _lastAlertSent = DateTime.MinValue;
        
        // Background alert timer for sending alerts when monitoring is not running
        private System.Windows.Threading.DispatcherTimer? _alertTimer;
        
        // Property to check if monitoring is currently active
        private bool IsMonitoringActive => _monitoringTask != null && !_monitoringTask.IsCompleted;

        private async Task<bool> DownloadOnceAsync(CancellationToken token, string source = "Manual")
        {
            bool anyDownloaded = false;
            var sw = Stopwatch.StartNew();
            int totalEntries = 0, fileCount = 0, dirCount = 0, downloaded = 0, skipped = 0, errors = 0;

            // Read UI inputs once at start to avoid repeated Dispatcher calls
            string host, portText, remoteFolderText, localFolderText;
            bool deleteAfter;

            try
            {
                // Use InvokeAsync and await so we don't block the background thread on UI operations
                var hostOp = Dispatcher.InvokeAsync(() => txtHost.Text.Trim());
                var portOp = Dispatcher.InvokeAsync(() => txtPort.Text);
                var remoteOp = Dispatcher.InvokeAsync(() => txtRemoteFolder.Text);
                var localOp = Dispatcher.InvokeAsync(() => txtLocalFolder.Text);
                var delOp = Dispatcher.InvokeAsync(() => chkDeleteAfterDownload.IsChecked == true);

                await Task.WhenAll(hostOp.Task, portOp.Task, remoteOp.Task, localOp.Task, delOp.Task).ConfigureAwait(false);

                host = hostOp.Task.Result;
                portText = portOp.Task.Result;
                remoteFolderText = remoteOp.Task.Result;
                localFolderText = localOp.Task.Result;
                deleteAfter = delOp.Task.Result;
            }
            catch
            {
                // On failure to read UI, fall back to using settings values (best-effort)
                host = _settings?.Host ?? string.Empty;
                portText = (_settings?.Port > 0) ? _settings.Port.ToString() : "21";
                remoteFolderText = _settings?.RemoteFolder ?? "/";
                localFolderText = _settings?.LocalFolder ?? string.Empty;
                deleteAfter = _settings?.DeleteAfterDownload == true;
            }

            Log($"{source} run: connecting to {host}:{portText} folder={remoteFolderText}", LogLevel.Info);

            try
            {
                var host2 = host;
                var port = int.TryParse(portText, out var p) ? p : 21;
                var creds = GetCredentials();
                var remoteFolder = remoteFolderText;
                var localFolder = localFolderText;

                // Use the new ListEntriesAsync to get file/directory information
                var entries = await _ftpService.ListEntriesAsync(host2, port, creds, remoteFolder, token).ConfigureAwait(false);
                totalEntries = entries.Length;

                // Count files and directories
                foreach (var (name, isDirectory) in entries)
                {
                    if (isDirectory)
                        dirCount++;
                    else
                        fileCount++;
                }

                _currentFilesInRemote = fileCount; // Only count actual files

                Log($"{source} run: found {totalEntries} entries ({fileCount} files, {dirCount} directories)", LogLevel.Info);

                if (fileCount == 0)
                {
                    if (dirCount > 0)
                        Log($"No files to download (found {dirCount} directories).", LogLevel.Info);
                    else
                        Log("No files found.", LogLevel.Info);

                    if (errors > 0) _errorCount += errors;
                    UpdateSidebarStats();
                    return false;
                }

                foreach (var (name, isDirectory) in entries)
                {
                    if (token.IsCancellationRequested) break;

                    if (string.IsNullOrWhiteSpace(name) || name == "." || name == "..") continue;

                    if (isDirectory)
                    {
                        Log($"Skipping directory: {name}", LogLevel.Debug);
                        continue;
                    }

                    var (Success, LocalPath, RemotePath, Deleted, ErrorMessage, Skipped) = await _ftpService.DownloadFileAsync(host2, port, creds, remoteFolder, name, localFolder, deleteAfter, token).ConfigureAwait(false);

                    if (Success)
                    {
                        if (Skipped)
                        {
                            skipped++;
                            Log($"Already exists, skipping: {name}", LogLevel.Debug);
                        }
                        else
                        {
                            downloaded++;
                            anyDownloaded = true;

                            // Schedule UI updates without blocking the background thread
                            try
                            {
                                await Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    try
                                    {
                                        _downloadedCount++;
                                        UpdateDownloadedLabel();
                                        Log($"Downloaded: {name} -> {LocalPath}", LogLevel.Info);
                                        txtLastCheck.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                                    }
                                    catch { }
                                }));
                            }
                            catch { }

                            if (Deleted)
                            {
                                Log($"Deleted remote file: {RemotePath}", LogLevel.Info);
                            }
                        }
                    }
                    else
                    {
                        errors++;
                        if (!string.IsNullOrEmpty(ErrorMessage))
                        {
                            Log($"DownloadFile error ({name}): {ErrorMessage}", LogLevel.Warning);
                            try { await Dispatcher.BeginInvoke(new Action(() => txtLastError.Text = ErrorMessage)); } catch { }
                        }
                    }
                }

                _totalFilesMonitored += downloaded;
                if (errors > 0) _errorCount += errors;

                // Only consider an actual file download as a successful check for alert purposes.
                if (downloaded > 0)
                {
                    _lastSuccessfulCheck = DateTime.Now;
                    // reset last alert sent so future threshold breaches can alert again
                    _lastAlertSent = DateTime.MinValue;

                    // update UI for last alert (cleared)
                    try { await Dispatcher.BeginInvoke(new Action(() => { try { txtLastAlert.Text = "-"; } catch { } })); } catch { }
                }
            }
            catch (OperationCanceledException)
            {
                Log("Download cancelled", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                _errorCount++;
                Log($"DownloadOnce error: {ex.Message}", LogLevel.Error);
                try { await Dispatcher.BeginInvoke(new Action(() => txtLastError.Text = ex.Message)); } catch { }
            }
            finally
            {
                sw.Stop();
                var duration = sw.Elapsed.ToString(@"mm\:ss");

                // Build a clear summary message
                var summary = $"{source} run finished: total={totalEntries}";
                if (dirCount > 0)
                    summary += $" (files={fileCount}, dirs={dirCount})";
                else
                    summary += $" files={fileCount}";

                summary += $", downloaded={downloaded}, skipped={skipped}";

                if (errors > 0)
                    summary += $", errors={errors}";

                summary += $", duration={duration}";

                Log(summary, LogLevel.Info);

                UpdateSidebarStats();
            }

            return anyDownloaded;
        }

        // Evaluate alert threshold and send alert emails repeatedly at AlertThresholdMinutes while no downloads occur.
        private async Task MaybeSendNoDownloadAlertAsync(CancellationToken token)
        {
            try
            {
                if (token.IsCancellationRequested) return;

                // Basic guard: alerts must be enabled and sending download alerts allowed
                if (_settings == null) return;
                if (!_settings.AlertsEnabled) return;
                if (!_settings.SendDownloadAlerts) return;
                if (_settings.AlertThresholdMinutes <= 0) return;

                // If monitoring is not active and SendAlertsWhenNotMonitoring is false, skip
                if (!IsMonitoringActive && !_settings.SendAlertsWhenNotMonitoring) return;

                var now = DateTime.Now;

                // Use new helper for schedule check
                if (!IsAlertTime(now)) return;

                // Determine reference time for last activity: either last successful download or monitoring started time
                var lastActivity = (_lastSuccessfulCheck != DateTime.MinValue) ? _lastSuccessfulCheck : _monitoringStartedAt;
                
                // If monitoring never started and SendAlertsWhenNotMonitoring is enabled, use app start time
                if (lastActivity == DateTime.MinValue && _settings.SendAlertsWhenNotMonitoring)
                {
                    // Use a reasonable fallback - app load time or current time minus threshold
                    lastActivity = DateTime.Now.AddMinutes(-_settings.AlertThresholdMinutes - 1);
                }
                
                if (lastActivity == DateTime.MinValue) return; // nothing to compare yet

                var minutesSince = (DateTime.Now - lastActivity).TotalMinutes;
                if (minutesSince < _settings.AlertThresholdMinutes) return;

                // Determine if we should send (first time after threshold OR repeat interval elapsed)
                var shouldSend = false;
                if (_lastAlertSent == DateTime.MinValue)
                {
                    shouldSend = true; // first alert
                }
                else
                {
                    // repeat every AlertThresholdMinutes while no downloads occur
                    var repeatMinutes = Math.Max(1, _settings.AlertThresholdMinutes);
                    if ((DateTime.Now - _lastAlertSent).TotalMinutes >= repeatMinutes)
                        shouldSend = true;
                }

                if (!shouldSend) return;

                // prepare and send email
                try
                {
                    _emailService ??= new Services.EmailService(_settings, _credentialService);

                    // Capture UI strings safely
                    string uiHost = string.Empty, uiRemote = string.Empty, uiLocal = string.Empty;
                    try
                    {
                        var hostOp = Dispatcher.InvokeAsync(() => txtHost.Text.Trim());
                        var remoteOp = Dispatcher.InvokeAsync(() => txtRemoteFolder.Text.Trim());
                        var localOp = Dispatcher.InvokeAsync(() => txtLocalFolder.Text.Trim());
                        await Task.WhenAll(hostOp.Task, remoteOp.Task, localOp.Task).ConfigureAwait(false);
                        uiHost = hostOp.Task.Result;
                        uiRemote = remoteOp.Task.Result;
                        uiLocal = localOp.Task.Result;
                    }
                    catch
                    {
                        uiHost = _settings.Host ?? string.Empty;
                        uiRemote = _settings.RemoteFolder ?? string.Empty;
                        uiLocal = _settings.LocalFolder ?? string.Empty;
                    }

                    var monitoringStatus = IsMonitoringActive ? "Monitoring is running" : "Monitoring is NOT running";
                    var subject = $"FTP Monitor - No downloads for {_settings.AlertThresholdMinutes} minutes";
                    var body = $"No downloads detected for at least {_settings.AlertThresholdMinutes} minutes.\n\nStatus: {monitoringStatus}\nHost: {uiHost}\nRemote folder: {uiRemote}\nLocal folder: {uiLocal}\nLast activity: {lastActivity:yyyy-MM-dd HH:mm:ss}\nNow: {now:yyyy-MM-dd HH:mm:ss}";

                    Log("Alert threshold exceeded, sending alert email...", LogLevel.Warning);
                    await _emailService.SendEmailAsync(subject, body).ConfigureAwait(false);
                    _lastAlertSent = DateTime.Now;
                    Log("Alert email sent.", LogLevel.Info);

                    // Update UI with last alert time
                    try { await Dispatcher.BeginInvoke(new Action(() => { try { txtLastAlert.Text = _lastAlertSent.ToString("yyyy-MM-dd HH:mm:ss"); } catch { } })); } catch { }
                }
                catch (Exception ex)
                {
                    Log($"Failed to send alert email: {ex.Message}", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                Log($"MaybeSendNoDownloadAlertAsync error: {ex.Message}", LogLevel.Debug);
            }
        }

        private static readonly char[] separator = new char[] { ',', ';' };

        // Determines if the current time is within the allowed alert schedule.
        /// <summary>
        /// Determines if the current time is within the allowed alert schedule.
        /// </summary>
        /// <param name="now">The time to check (usually DateTime.Now)</param>
        /// <returns>True if alerts are allowed at this time</returns>
        private bool IsAlertTime(DateTime now)
        {
            if (_settings == null) return false;
            if (!_settings.AlertsEnabled) return false;
            
            // AlertAlways (24/7 mode): Ignore ALL schedule settings
            if (_settings.AlertAlways) return true;

            // Check weekday - both AllDay and normal schedule respect selected days
            var days = (_settings.AlertWeekdays ?? string.Empty)
                .Split(separator, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim());
            var shortDay = now.ToString("ddd"); // Mon, Tue, ...
            if (!days.Contains(shortDay)) return false;

            // If AllDay is enabled, we honor the selected weekdays but ignore work hours
            if (_settings.AllDay)
            {
                // Still respect daily excluded intervals if provided
                return !IsInExcludedIntervals(now.TimeOfDay, _settings.ExcludedIntervals);
            }

            // Parse excluded intervals once
            var excludedIntervals = ParseIntervals(_settings.ExcludedIntervals);

            // Multi-shift mode: check the configured shifts
            if (!string.IsNullOrWhiteSpace(_settings.WorkShifts))
            {
                var shifts = ParseIntervals(_settings.WorkShifts);
                var t = now.TimeOfDay;

                // If any shift contains the current time and it's not inside an excluded interval, allow alerts
                var inAnyShift = shifts.Any(si => IsTimeInInterval(t, si.start, si.end));
                if (!inAnyShift) return false;

                if (excludedIntervals != null && excludedIntervals.Count > 0)
                {
                    if (IsInExcludedIntervals(t, _settings.ExcludedIntervals)) return false;
                }

                return true;
            }

            // If no WorkShifts configured and not in AllDay or AlertAlways, default to false (no schedule defined)
            return false;
        }

        // Helper: parse a semicolon/comma-separated list of HH:mm-HH:mm into list of intervals
        private static List<(TimeSpan start, TimeSpan end)> ParseIntervals(string? input)
        {
            var result = new List<(TimeSpan, TimeSpan)>();
            if (string.IsNullOrWhiteSpace(input)) return result;

            var parts = input.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts.Select(p => p.Trim()))
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                var range = part.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                if (range.Length != 2) continue;
                if (TimeSpan.TryParse(range[0], out var s) && TimeSpan.TryParse(range[1], out var e))
                {
                    result.Add((s, e));
                }
            }
            return result;
        }

        // Helper: check if a time is inside interval [start, end]. Supports overnight intervals (end <= start).
        private static bool IsTimeInInterval(TimeSpan time, TimeSpan start, TimeSpan end)
        {
            if (start == end)
            {
                // treat as full-day if both equal
                return true;
            }
            if (start < end)
            {
                return time >= start && time <= end;
            }
            else
            {
                // overnight interval, e.g., 22:00-06:00
                return time >= start || time <= end;
            }
        }

        // Helper: check excluded intervals string quickly
        private static bool IsInExcludedIntervals(TimeSpan time, string? excludedIntervals)
        {
            if (string.IsNullOrWhiteSpace(excludedIntervals)) return false;
            var intervals = ParseIntervals(excludedIntervals);
            foreach (var (start, end) in intervals)
            {
                if (IsTimeInInterval(time, start, end)) return true;
            }
            return false;
        }

        private void StopMonitoring(string message)
        {
            try
            {
                try { _cts?.Cancel(); } catch { }
                try { _cts?.Dispose(); } catch { }
                _cts = null;

                if (_monitoringTask != null)
                {
                    try { _monitoringTask.Wait(500); } catch { }
                    _monitoringTask = null;
                }
            }
            catch { }

            UpdateUiState(false);
            UpdateSidebarStatus(false);
            btnCheckNow.IsEnabled = true;
            SetStatus(message);
            Log(message, LogLevel.Info);
        }
    }
}