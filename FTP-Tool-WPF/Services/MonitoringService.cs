using System;
using System.Threading;
using System.Threading.Tasks;
using FTP_Tool;

namespace FTP_Tool.Services
{
    public class MonitoringService
    {
        // onCheck: function that performs one check, returns true if any files downloaded
        // logger: optional logger callback
        public async Task StartMonitoringLoopAsync(int seconds, CancellationToken token, Func<CancellationToken, Task<bool>> onCheck, Action<string, LogLevel>? logger = null)
        {
            if (seconds <= 0) seconds = 30;

            try
            {
                // Run one immediate check on start
                try
                {
                    if (token.IsCancellationRequested) return;
                    logger?.Invoke("Scheduled check starting...", LogLevel.Info);
                    await onCheck(token).ConfigureAwait(false);
                    logger?.Invoke("Scheduled check finished.", LogLevel.Info);
                }
                catch (OperationCanceledException)
                {
                    logger?.Invoke("Scheduled check cancelled.", LogLevel.Warning);
                }
                catch (Exception ex)
                {
                    logger?.Invoke($"Scheduled check error: {ex.Message}", LogLevel.Error);
                }

                using var pt = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
                while (await pt.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    try
                    {
                        if (token.IsCancellationRequested) break;
                        logger?.Invoke("Scheduled check starting...", LogLevel.Info);
                        await onCheck(token).ConfigureAwait(false);
                        logger?.Invoke("Scheduled check finished.", LogLevel.Info);
                    }
                    catch (OperationCanceledException)
                    {
                        logger?.Invoke("Scheduled check cancelled.", LogLevel.Warning);
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger?.Invoke($"Scheduled check error: {ex.Message}", LogLevel.Error);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                logger?.Invoke("Monitoring loop cancelled.", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Monitoring loop error: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
