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

                if (downloaded > 0 || fileCount > 0)
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