using Serilog;
using Serilog.Events;

namespace FTP_Tool.Services
{
    public class LoggingService : IDisposable
    {
        private ILogger? _logger;
        private bool _enabled;
        private readonly object _sync = new();

        public LoggingService(string filePath, Models.AppSettings settings)
        {
            ApplySettings(filePath, settings);
        }

        /// <summary>
        /// Apply new settings at runtime and recreate the Serilog logger accordingly.
        /// Thread-safe and disposes previous logger if any.
        /// </summary>
        public void ApplySettings(string filePath, Models.AppSettings? settings)
        {
            lock (_sync)
            {
                _enabled = settings?.LogToFile ?? true;
                var minLevel = MapLevel(settings?.MinimumLogLevel);
                var retention = Math.Max(1, settings?.LogRetentionDays ?? 30);

                // dispose existing logger if any
                try { (_logger as IDisposable)?.Dispose(); } catch { }

                if (_enabled)
                {
                    var cfg = new LoggerConfiguration()
                        .MinimumLevel.Is(minLevel);

                    try
                    {
                        cfg = cfg.WriteTo.Async(a => a.File(
                            path: filePath,
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: retention,
                            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}] {Message}{NewLine}"));
                    }
                    catch
                    {
                        cfg = cfg.WriteTo.File(
                            path: filePath,
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: retention,
                            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}] {Message}{NewLine}");
                    }

                    _logger = cfg.CreateLogger();
                }
                else
                {
                    _logger = new LoggerConfiguration()
                        .MinimumLevel.Is(minLevel)
                        .CreateLogger();
                }
            }
        }

        public void Log(Models.AppSettings settings, FTP_Tool.LogLevel level, string message)
        {
            // if logging disabled, no-op
            if (!_enabled) return;

            var ev = level switch
            {
                FTP_Tool.LogLevel.Debug => LogEventLevel.Debug,
                FTP_Tool.LogLevel.Info => LogEventLevel.Information,
                FTP_Tool.LogLevel.Warning => LogEventLevel.Warning,
                FTP_Tool.LogLevel.Error => LogEventLevel.Error,
                _ => LogEventLevel.Information
            };

            try
            {
                // logger may be null if not configured
                _logger?.Write(ev, message);
            }
            catch
            {
                // swallow logging exceptions
            }
        }

        public bool ShouldLog(Models.AppSettings settings, FTP_Tool.LogLevel level)
        {
            var min = MapLevel(settings?.MinimumLogLevel);
            var ev = level switch
            {
                FTP_Tool.LogLevel.Debug => LogEventLevel.Debug,
                FTP_Tool.LogLevel.Info => LogEventLevel.Information,
                FTP_Tool.LogLevel.Warning => LogEventLevel.Warning,
                FTP_Tool.LogLevel.Error => LogEventLevel.Error,
                _ => LogEventLevel.Information
            };

            return ev >= min;
        }

        private LogEventLevel MapLevel(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return LogEventLevel.Information;
            return s.Trim().ToLowerInvariant() switch
            {
                "debug" => LogEventLevel.Debug,
                "info" => LogEventLevel.Information,
                "warning" => LogEventLevel.Warning,
                "error" => LogEventLevel.Error,
                _ => LogEventLevel.Information,
            };
        }

        public void Dispose()
        {
            lock (_sync)
            {
                try { (_logger as IDisposable)?.Dispose(); } catch { }
                _logger = null;
            }
        }
    }
}
