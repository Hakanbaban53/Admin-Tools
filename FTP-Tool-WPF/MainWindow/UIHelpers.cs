using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading.Tasks;
using FTP_Tool.Models;

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
                var ellipse = FindName("ellipseNavStatus") as System.Windows.Shapes.Ellipse;
                var txtStatus = FindName("txtNavStatus") as System.Windows.Controls.TextBlock;

                if (ellipse != null)
                {
                    ellipse.Fill = isMonitoring ? new SolidColorBrush(System.Windows.Media.Colors.Green) : new SolidColorBrush(System.Windows.Media.Colors.Orange);
                }

                if (txtStatus != null)
                {
                    txtStatus.Text = isMonitoring ? "Monitoring" : "Ready";
                    txtStatus.Foreground = isMonitoring ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)) : new SolidColorBrush(System.Windows.Media.Colors.Gray);
                }
            });
        }

        private void UpdateSidebarStats()
        {
            Dispatcher.BeginInvoke(() =>
            {
                var txtFiles = FindName("txtNavFiles") as System.Windows.Controls.TextBlock;
                var txtRemote = FindName("txtNavRemote") as System.Windows.Controls.TextBlock;
                var txtErrors = FindName("txtNavErrors") as System.Windows.Controls.TextBlock;
                var txtLast = FindName("txtNavLastCheck") as System.Windows.Controls.TextBlock;

                if (txtFiles != null) txtFiles.Text = $"Processed: {_totalFilesMonitored} files";
                if (txtRemote != null) txtRemote.Text = $"Remote: {_currentFilesInRemote} files";
                if (txtErrors != null) txtErrors.Text = $"Errors: {_errorCount}";

                if (txtLast != null)
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

        private int CountLinesFast(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int count = 1;
            int position = 0;
            while ((position = text.IndexOf('\n', position)) != -1)
            {
                count++;
                position++;
            }
            return count;
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
