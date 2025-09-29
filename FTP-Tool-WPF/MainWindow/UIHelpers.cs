using FTP_Tool.Models;
using System.Windows;
using System.Windows.Media;

namespace FTP_Tool
{
    public partial class MainWindow
    {
        private void UpdateUiState(bool monitoring)
        {
            Dispatcher.Invoke(() =>
            {
                btnStart.IsEnabled = !monitoring;
                btnStop.IsEnabled = monitoring;

                if (btnStart.IsEnabled)
                {
                    btnStart.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4));
                }
                else
                {
                    btnStart.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC8, 0xE6, 0xFA));
                }
            });
        }

        private void UpdateSidebarStatus(bool isMonitoring)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (FindName("ellipseNavStatus") is System.Windows.Shapes.Ellipse ellipse)
                {
                    ellipse.Fill = isMonitoring ? new SolidColorBrush(System.Windows.Media.Colors.Green) : new SolidColorBrush(System.Windows.Media.Colors.Orange);
                }

                if (FindName("txtNavStatus") is System.Windows.Controls.TextBlock txtStatus)
                {
                    txtStatus.Text = isMonitoring ? "Monitoring" : "Ready";
                    txtStatus.Foreground = isMonitoring ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)) : new SolidColorBrush(System.Windows.Media.Colors.Gray);
                }

                // Floating sidebar status
                try
                {
                    if (FindName("ellipseFloatingNavStatus") is System.Windows.Shapes.Ellipse fe)
                    {
                        fe.Fill = isMonitoring ? new SolidColorBrush(System.Windows.Media.Colors.Green) : new SolidColorBrush(System.Windows.Media.Colors.Orange);
                    }

                    if (FindName("txtFloatingNavStatus") is System.Windows.Controls.TextBlock ftxtStatus)
                    {
                        ftxtStatus.Text = isMonitoring ? "Monitoring" : "Ready";
                        ftxtStatus.Foreground = isMonitoring ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)) : new SolidColorBrush(System.Windows.Media.Colors.Gray);
                    }
                }
                catch { }
            });
        }

        private void UpdateSidebarStats()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (FindName("txtNavFiles") is System.Windows.Controls.TextBlock txtFiles) txtFiles.Text = $"Processed: {_totalFilesMonitored} files";
                if (FindName("txtNavRemote") is System.Windows.Controls.TextBlock txtRemote) txtRemote.Text = $"Remote: {_currentFilesInRemote} files";
                if (FindName("txtNavErrors") is System.Windows.Controls.TextBlock txtErrors) txtErrors.Text = $"Errors: {_errorCount}";

                if (FindName("txtNavLastCheck") is System.Windows.Controls.TextBlock txtLast)
                {
                    if (_lastSuccessfulCheck != DateTime.MinValue)
                    {
                        txtLast.Text = $"Last: {_lastSuccessfulCheck:HH:mm:ss}";
                    }
                    else
                    {
                        txtLast.Text = "Last: Never";
                    }
                }

                if (FindName("txtNavLastAlert") is System.Windows.Controls.TextBlock txtAlert)
                {
                    if (_lastAlertSent != DateTime.MinValue)
                    {
                        txtAlert.Text = $"Last alert: {_lastAlertSent:HH:mm:ss}";
                    }
                    else
                    {
                        txtAlert.Text = "Last alert: -";
                    }
                }

                // Floating sidebar stats
                try
                {
                    if (FindName("txtFloatingNavFiles") is System.Windows.Controls.TextBlock fFiles) fFiles.Text = $"Processed: {_totalFilesMonitored} files";
                    if (FindName("txtFloatingNavRemote") is System.Windows.Controls.TextBlock fRemote) fRemote.Text = $"Remote: {_currentFilesInRemote} files";
                    if (FindName("txtFloatingNavErrors") is System.Windows.Controls.TextBlock fErrors) fErrors.Text = $"Errors: {_errorCount}";

                    if (FindName("txtFloatingNavLastCheck") is System.Windows.Controls.TextBlock fLast)
                    {
                        if (_lastSuccessfulCheck != DateTime.MinValue)
                        {
                            fLast.Text = $"Last: {_lastSuccessfulCheck:HH:mm:ss}";
                        }
                        else
                        {
                            fLast.Text = "Last: Never";
                        }
                    }

                    if (FindName("txtFloatingNavLastAlert") is System.Windows.Controls.TextBlock fAlert)
                    {
                        if (_lastAlertSent != DateTime.MinValue)
                        {
                            fAlert.Text = $"Last alert: {_lastAlertSent:HH:mm:ss}";
                        }
                        else
                        {
                            fAlert.Text = "Last alert: -";
                        }
                    }
                }
                catch { }
            });
        }

        private void UpdateResponsiveSidebar()
        {
            Dispatcher.Invoke(() =>
            {
                if (ActualWidth < SidebarCollapseWidth)
                {
                    SidebarColumn.Width = new System.Windows.GridLength(0);
                    SidebarBorder.Visibility = Visibility.Collapsed;
                    btnToggleSidebar.Visibility = Visibility.Visible;
                }
                else
                {
                    SidebarColumn.Width = new System.Windows.GridLength(260);
                    SidebarBorder.Visibility = Visibility.Visible;
                    btnToggleSidebar.Visibility = Visibility.Collapsed;
                    HideFloatingSidebar();
                }
            });
        }

        private void ClearLogIfNeeded()
        {
            try
            {
                int maxLines = _settings?.MaxLogLines ?? 2000;
                if (maxLines <= 0) return;

                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        // Use the displayed collection count instead of reading a TextBox
                        int lineCount = _displayedLogEntries.Count;
                        if (lineCount == 0) return;

                        if (lineCount > maxLines)
                        {
                            // Clear the UI collection and add a notice entry
                            _displayedLogEntries.Clear();

                            var notice1 = new LogEntry
                            {
                                Time = DateTime.Now,
                                Level = LogLevel.Info,
                                Message = $"--- Log cleared (reached {maxLines} line limit) ---"
                            };

                            var notice2 = new LogEntry
                            {
                                Time = DateTime.Now,
                                Level = LogLevel.Info,
                                Message = $"--- View complete logs in: {System.IO.Path.GetDirectoryName(_logFilePath)} ---"
                            };

                            _displayedLogEntries.Add(notice1);
                            _displayedLogEntries.Add(notice2);

                            Log("UI log cleared to maintain performance. Complete logs are saved to file.", LogLevel.Info);
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }
    }
}
