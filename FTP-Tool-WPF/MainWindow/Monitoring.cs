using System.Diagnostics;

namespace FTP_Tool
{
    public partial class MainWindow
    {
        private Task? _monitoringTask;
        // Track when monitoring started so threshold can be measured even before first download
        private DateTime _monitoringStartedAt = DateTime.MinValue;
        // Track when we last sent an alert
        private DateTime _lastAlertSent = DateTime.MinValue;

        private async Task StartMonitoringLoopAsync(int seconds, CancellationToken token)
        {
            if (seconds <= 0) seconds = 30;
            try
            {
                // mark monitoring start
                _monitoringStartedAt = DateTime.Now;

                try
                {
                    if (token.IsCancellationRequested) return;
                    Log("Scheduled check starting...", LogLevel.Info);
                    await DownloadOnceAsync(token, "Scheduled");
                    Log("Scheduled check finished.", LogLevel.Info);

                    // check alerts after the run
                    try { await MaybeSendNoDownloadAlertAsync(token); } catch { }
                }
                catch (OperationCanceledException)
                {
                    Log("Scheduled check cancelled.", LogLevel.Warning);
                }
                catch (Exception ex)
                {
                    Log($"Scheduled check error: {ex.Message}", LogLevel.Error);
                }

                using var pt = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
                while (await pt.WaitForNextTickAsync(token))
                {
                    try
                    {
                        if (token.IsCancellationRequested) break;
                        Log("Scheduled check starting...", LogLevel.Info);
                        await DownloadOnceAsync(token, "Scheduled");
                        Log("Scheduled check finished.", LogLevel.Info);

                        // check alerts after the run
                        try { await MaybeSendNoDownloadAlertAsync(token); } catch { }
                    }
                    catch (OperationCanceledException)
                    {
                        Log("Scheduled check cancelled.", LogLevel.Warning);
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log($"Scheduled check error: {ex.Message}", LogLevel.Error);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log("Monitoring loop cancelled.", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                Log($"Monitoring loop error: {ex.Message}", LogLevel.Error);
            }
        }

        private async Task<bool> DownloadOnceAsync(CancellationToken token, string source = "Manual")
        {
            bool anyDownloaded = false;
            var sw = Stopwatch.StartNew();
            int totalEntries = 0, fileCount = 0, dirCount = 0, downloaded = 0, skipped = 0, errors = 0;

            string host = Dispatcher.Invoke(() => txtHost.Text.Trim());
            string portText = Dispatcher.Invoke(() => txtPort.Text);
            string remoteFolderText = Dispatcher.Invoke(() => txtRemoteFolder.Text);
            string localFolderText = Dispatcher.Invoke(() => txtLocalFolder.Text);

            Log($"{source} run: connecting to {host}:{portText} folder={remoteFolderText}", LogLevel.Info);

            try
            {
                var host2 = host;
                var port = int.TryParse(portText, out var p) ? p : 21;
                var creds = GetCredentials();
                var remoteFolder = remoteFolderText;
                var localFolder = localFolderText;
                var deleteAfter = Dispatcher.Invoke(() => chkDeleteAfterDownload.IsChecked == true);

                // Use the new ListEntriesAsync to get file/directory information
                var entries = await _ftpService.ListEntriesAsync(host2, port, creds, remoteFolder, token);
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

                    var (Success, LocalPath, RemotePath, Deleted, ErrorMessage, Skipped) = await _ftpService.DownloadFileAsync(host2, port, creds, remoteFolder, name, localFolder, deleteAfter, token);

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
                            Dispatcher.Invoke(() =>
                            {
                                _downloadedCount++;
                                UpdateDownloadedLabel();
                                Log($"Downloaded: {name} -> {LocalPath}", LogLevel.Info);
                                txtLastCheck.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            });

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
                            Dispatcher.Invoke(() => txtLastError.Text = ErrorMessage);
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
                    Dispatcher.Invoke(() =>
                    {
                        try { txtLastAlert.Text = "-"; } catch { }
                    });
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
                Dispatcher.Invoke(() => txtLastError.Text = ex.Message);
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

                var now = DateTime.Now;

                // Use new helper for schedule check
                if (!IsAlertTime(now)) return;

                // Determine reference time for last activity: either last successful download or monitoring started time
                var lastActivity = (_lastSuccessfulCheck != DateTime.MinValue) ? _lastSuccessfulCheck : _monitoringStartedAt;
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
                    var subject = $"FTP Monitor - No downloads for {_settings.AlertThresholdMinutes} minutes";
                    var body = $"No downloads detected for at least {_settings.AlertThresholdMinutes} minutes.\nHost: {txtHost.Text.Trim()}\nRemote folder: {txtRemoteFolder.Text.Trim()}\nLocal folder: {txtLocalFolder.Text.Trim()}\nLast activity: {lastActivity:yyyy-MM-dd HH:mm:ss}\nNow: {now:yyyy-MM-dd HH:mm:ss}";

                    Log("Alert threshold exceeded, sending alert email...", LogLevel.Warning);
                    await _emailService.SendEmailAsync(subject, body);
                    _lastAlertSent = DateTime.Now;
                    Log("Alert email sent.", LogLevel.Info);

                    // Update UI with last alert time
                    Dispatcher.Invoke(() =>
                    {
                        try { txtLastAlert.Text = _lastAlertSent.ToString("yyyy-MM-dd HH:mm:ss"); } catch { }
                    });
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
        private static readonly char[] separator = [',', ';'];

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
            if (_settings.AlertAlways) return true;

            // Check weekday
            var days = (_settings.AlertWeekdays ?? string.Empty)
                .Split(separator, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim());
            var shortDay = now.ToString("ddd"); // Mon, Tue, ...
            if (!days.Contains(shortDay)) return false;

            // If AllDay is enabled, we honor the selected weekdays but ignore work hours / lunch
            if (_settings.AllDay)
            {
                return true;
            }

            // Check work hours
            if (TimeSpan.TryParse(_settings.WorkStart ?? "08:00", out var workStart) &&
                TimeSpan.TryParse(_settings.WorkEnd ?? "17:00", out var workEnd))
            {
                var t = now.TimeOfDay;
                var inWork = t >= workStart && t <= workEnd;
                if (!inWork) return false;

                // Check lunch break
                if (TimeSpan.TryParse(_settings.LunchStart ?? "12:00", out var lunchStart) &&
                    TimeSpan.TryParse(_settings.LunchEnd ?? "13:00", out var lunchEnd))
                {
                    var inLunch = t >= lunchStart && t <= lunchEnd;
                    if (inLunch) return false;
                }
            }
            return true;
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