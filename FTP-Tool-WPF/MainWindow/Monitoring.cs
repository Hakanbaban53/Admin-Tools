using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net;

namespace FTP_Tool
{
    public partial class MainWindow
    {
        private Task? _monitoringTask;

        private async Task StartMonitoringLoopAsync(int seconds, CancellationToken token)
        {
            if (seconds <= 0) seconds = 30;
            try
            {
                try
                {
                    if (token.IsCancellationRequested) return;
                    Log("Scheduled check starting...", LogLevel.Info);
                    await DownloadOnceAsync(token, "Scheduled");
                    Log("Scheduled check finished.", LogLevel.Info);
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
            int found = 0, downloaded = 0, skipped = 0, errors = 0;

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

                var files = await _ftpService.ListFilesAsync(host2, port, creds, remoteFolder, token);
                found = files.Length;
                _currentFilesInRemote = found;
                Log($"{source} run: found {found} entries", LogLevel.Info);
                if (files.Length == 0)
                {
                    Log("No files found.", LogLevel.Info);
                    if (errors > 0) _errorCount += errors;
                    UpdateSidebarStats();
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
                            Log($"Already exists, skipping: {file}", LogLevel.Debug);
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

                _totalFilesMonitored += downloaded;
                if (errors > 0) _errorCount += errors;

                if (downloaded > 0 || found > 0)
                {
                    _lastSuccessfulCheck = DateTime.Now;
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
                Log($"{source} run finished: found={found} downloaded={downloaded} skipped={skipped} errors={errors} duration={duration}", LogLevel.Info);

                UpdateSidebarStats();
            }

            return anyDownloaded;
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
