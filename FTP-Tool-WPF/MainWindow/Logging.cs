using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using FTP_Tool.Models;

namespace FTP_Tool
{
    public partial class MainWindow
    {
        // pending log entries produced by background threads
        private readonly ConcurrentQueue<LogEntry> _pendingLogEntries = new();
        // displayed entries bound to ListBox (virtualized)
        private readonly ObservableCollection<LogEntry> _displayedLogEntries = new();
        private DispatcherTimer? _logFlushTimer;
        private int _newLogCount = 0;
        private bool _isUserAtBottom = true; // whether we should auto-scroll when new lines arrive

        private void SetStatus(string text)
        {
            Dispatcher.BeginInvoke(() => txtStatusText.Text = text);
        }

        private void Log(string text, LogLevel level = LogLevel.Info)
        {
            // Track errors
            if (level == LogLevel.Error)
            {
                _errorCount++;
            }

            // Filter by minimum log level from settings
            if (!ShouldLog(level)) return;

            var entry = new LogEntry { Time = DateTime.Now, Level = level, Message = text };

            try
            {
                // Update the small status/indicator immediately
                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        switch (level)
                        {
                            case LogLevel.Info:
                                ellipseStatus.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 182, 255));
                                break;
                            case LogLevel.Warning:
                                ellipseStatus.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 204, 0));
                                break;
                            case LogLevel.Error:
                                ellipseStatus.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 52, 56));
                                break;
                            case LogLevel.Debug:
                                ellipseStatus.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200));
                                break;
                        }

                        txtStatusText.Text = level == LogLevel.Error ? "Error" : (level == LogLevel.Warning ? "Warning" : "Running");
                    }
                    catch { }
                });
            }
            catch { }

            // enqueue for batched UI updates
            try
            {
                _pendingLogEntries.Enqueue(entry);
            }
            catch { }

            // also log to file (if enabled) immediately
            try
            {
                _logging_service?.Log(_settings, level, text);
            }
            catch { }
        }

        // Flush pending log lines to the UI. Should run on the UI thread.
        private void FlushPendingLogs()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke((Action)FlushPendingLogs);
                    return;
                }

                var toAdd = new List<LogEntry>();
                while (_pendingLogEntries.TryDequeue(out var ln))
                {
                    toAdd.Add(ln);
                }

                if (toAdd.Count == 0) return;

                // add to displayed collection in one batch
                foreach (var e in toAdd)
                {
                    _displayedLogEntries.Add(e);
                }

                // enforce MaxLogLines
                try
                {
                    var max = Math.Max(0, _settings?.MaxLogLines ?? 1000);
                    while (_displayedLogEntries.Count > max)
                    {
                        _displayedLogEntries.RemoveAt(0);
                    }
                }
                catch { }

                // Auto-scroll only if the user is at the bottom
                if (_isUserAtBottom)
                {
                    try
                    {
                        if (_displayedLogEntries.Count > 0)
                        {
                            var last = _displayedLogEntries[_displayedLogEntries.Count - 1];
                            lstLog.ScrollIntoView(last);
                        }

                        // hide jump button if visible
                        var btn = FindName("btnJumpToLatest") as System.Windows.Controls.Button;
                        if (btn != null) btn.Visibility = Visibility.Collapsed;
                        _newLogCount = 0;
                    }
                    catch { }
                }
                else
                {
                    // show jump-to-latest indicator / button
                    _newLogCount += toAdd.Count;
                    try
                    {
                        var btn = FindName("btnJumpToLatest") as System.Windows.Controls.Button;
                        if (btn != null)
                        {
                            btn.Visibility = Visibility.Visible;
                            btn.Content = $"Jump to latest ({_newLogCount})";
                        }
                    }
                    catch { }
                }

                UpdateSidebarStats();
            }
            catch { }
        }

        private void LogScrollViewer_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            try
            {
                // Consider user at bottom if within small threshold of the end
                var threshold = 10.0; // pixels
                var atBottom = false;
                if (e != null)
                {
                    atBottom = e.VerticalOffset >= (e.ExtentHeight - e.ViewportHeight - threshold);
                }

                _isUserAtBottom = atBottom;

                if (_isUserAtBottom)
                {
                    // clear new message indicator if present
                    _newLogCount = 0;
                    try
                    {
                        var btn = FindName("btnJumpToLatest") as System.Windows.Controls.Button;
                        if (btn != null) btn.Visibility = Visibility.Collapsed;
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void BtnJumpToLatest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isUserAtBottom = true;
                _newLogCount = 0;
                var btn = FindName("btnJumpToLatest") as System.Windows.Controls.Button;
                if (btn != null) btn.Visibility = Visibility.Collapsed;

                if (_displayedLogEntries.Count > 0)
                {
                    var last = _displayedLogEntries[_displayedLogEntries.Count - 1];
                    lstLog.ScrollIntoView(last);
                }
            }
            catch { }
        }

        private bool ShouldLog(LogLevel level)
        {
            if (_settings == null) return true;
            try
            {
                var min = ParseLogLevel(_settings.MinimumLogLevel);
                return level >= min;
            }
            catch
            {
                return true;
            }
        }

        private LogLevel ParseLogLevel(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return LogLevel.Info;
            return s.Trim().ToLowerInvariant() switch
            {
                "debug" => LogLevel.Debug,
                "info" => LogLevel.Info,
                "warning" => LogLevel.Warning,
                "error" => LogLevel.Error,
                _ => LogLevel.Info,
            };
        }
    }
}
