using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace FTP_Tool
{
    public partial class MainWindow
    {
        // tray menu items we update dynamically
        private ToolStripMenuItem? _trayStatusItem;
        private ToolStripMenuItem? _trayHostItem;
        private ToolStripMenuItem? _trayProcessedItem;
        private ToolStripMenuItem? _trayRemoteItem;
        private ToolStripMenuItem? _trayErrorsItem;
        private ToolStripMenuItem? _trayLastCheckItem;
        private ToolStripMenuItem? _trayLastAlertItem;
        private ToolStripMenuItem? _trayStartItem;
        private ToolStripMenuItem? _trayStopItem;

        private void InitializeTray()
        {
            try
            {
                _trayIcon = new NotifyIcon();
                try
                {
                    _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule?.FileName ?? "");
                }
                catch { }
                _trayIcon.Visible = false;
                _trayIcon.DoubleClick += (s, e) => ShowFromTray();

                var cm = new System.Windows.Forms.ContextMenuStrip();

                // Info group (disabled headers)
                _trayStatusItem = new ToolStripMenuItem("Status: -") { Enabled = false };
                _trayHostItem = new ToolStripMenuItem("Host: -") { Enabled = false };
                _trayProcessedItem = new ToolStripMenuItem("Processed: 0 files") { Enabled = false };
                _trayRemoteItem = new ToolStripMenuItem("Remote: 0 files") { Enabled = false };
                _trayErrorsItem = new ToolStripMenuItem("Errors: 0") { Enabled = false };
                _trayLastCheckItem = new ToolStripMenuItem("Last: -") { Enabled = false };
                _trayLastAlertItem = new ToolStripMenuItem("Last alert: -") { Enabled = false };

                cm.Items.Add(_trayStatusItem);
                cm.Items.Add(_trayHostItem);
                cm.Items.Add(_trayProcessedItem);
                cm.Items.Add(_trayRemoteItem);
                cm.Items.Add(_trayErrorsItem);
                cm.Items.Add(_trayLastCheckItem);
                cm.Items.Add(_trayLastAlertItem);

                cm.Items.Add(new ToolStripSeparator());

                var showItem = cm.Items.Add("Show");
                showItem.ToolTipText = "Restore the application window from the tray";
                showItem.Click += (s, e) => ShowFromTray();

                _trayStartItem = new ToolStripMenuItem("Start Monitoring")
                {
                    ToolTipText = "Begin monitoring immediately"
                };
                _trayStartItem.Click += (s, e) => Dispatcher.Invoke(() => BtnStart_Click(this, new RoutedEventArgs()));
                cm.Items.Add(_trayStartItem);

                _trayStopItem = new ToolStripMenuItem("Stop Monitoring")
                {
                    ToolTipText = "Stop monitoring"
                };
                _trayStopItem.Click += (s, e) => Dispatcher.Invoke(() => BtnStop_Click(this, new RoutedEventArgs()));
                cm.Items.Add(_trayStopItem);

                cm.Items.Add(new ToolStripSeparator());
                var exitItem = cm.Items.Add("Exit");
                exitItem.ToolTipText = "Exit the application";
                // Instead of calling Shutdown directly (which triggers Window_Closing and may hide to tray),
                // set a flag to allow a real exit and then close the main window.
                exitItem.Click += (s, e) => Dispatcher.Invoke(() =>
                {
                    try
                    {
                        _suppressHideOnClose = true;
                        // ensure tray icon removed before closing to avoid extra balloon
                        if (_trayIcon != null) { _trayIcon.Visible = false; }
                        this.Close();
                    }
                    catch
                    {
                        try { System.Windows.Application.Current.Shutdown(); } catch { }
                    }
                });
                _trayIcon.ContextMenuStrip = cm;

                // tooltip shown on hover
                _trayIcon.Text = "FTP Monitor";

                // initial update of enabled state
                UpdateTray();
            }
            catch { }
        }

        // Call to refresh tray menu contents from UI code
        public void UpdateTray()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (_trayIcon == null || _trayIcon.ContextMenuStrip == null) return;

                    // status
                    var isMonitoring = (_cts != null && !_cts.IsCancellationRequested);
                    if (_trayStatusItem != null) _trayStatusItem.Text = isMonitoring ? "Status: Monitoring" : "Status: Idle";

                    // host
                    if (_trayHostItem != null)
                    {
                        var host = string.IsNullOrWhiteSpace(txtHost?.Text) ? "-" : txtHost.Text.Trim();
                        _trayHostItem.Text = $"Host: {host}";
                    }

                    // processed / downloaded
                    if (_trayProcessedItem != null) _trayProcessedItem.Text = $"Processed: {_totalFilesMonitored} files";
                    if (_trayRemoteItem != null) _trayRemoteItem.Text = $"Remote: {_currentFilesInRemote} files";
                    if (_trayErrorsItem != null) _trayErrorsItem.Text = $"Errors: {_errorCount}";

                    if (_trayLastCheckItem != null)
                    {
                        if (_lastSuccessfulCheck != DateTime.MinValue)
                            _trayLastCheckItem.Text = $"Last: {_lastSuccessfulCheck:yyyy-MM-dd HH:mm:ss}";
                        else
                            _trayLastCheckItem.Text = "Last: Never";
                    }

                    if (_trayLastAlertItem != null)
                    {
                        if (_lastAlertSent != DateTime.MinValue)
                            _trayLastAlertItem.Text = $"Last alert: {_lastAlertSent:yyyy-MM-dd HH:mm:ss}";
                        else
                            _trayLastAlertItem.Text = "Last alert: -";
                    }

                    // enable/disable start/stop based on monitoring state
                    if (_trayStartItem != null) _trayStartItem.Enabled = !isMonitoring;
                    if (_trayStopItem != null) _trayStopItem.Enabled = isMonitoring;

                    // update tooltip to include summary
                    try
                    {
                        var tooltip = isMonitoring ? "FTP Monitor - Monitoring" : "FTP Monitor - Idle";
                        tooltip += $" | Host: {(string.IsNullOrWhiteSpace(txtHost?.Text) ? "-" : txtHost.Text.Trim())}";
                        tooltip += $" | Processed: {_totalFilesMonitored}";
                        tooltip += $" | Errors: {_errorCount}";

                        // NotifyIcon.Text limited to 63 chars on Windows; truncate if too long
                        if (tooltip.Length > 60) tooltip = tooltip[..60];
                        _trayIcon.Text = tooltip;
                    }
                    catch { }
                });
            }
            catch { }
        }

        private void HideToTray()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    Hide();
                    if (_trayIcon != null)
                    {
                        _trayIcon.Visible = true;
                        _trayIcon.BalloonTipTitle = "FTP Monitor";
                        _trayIcon.BalloonTipText = "Application minimized to tray.";
                        _trayIcon.ShowBalloonTip(1000);
                        _trayIcon.Text = "FTP Monitor - Running in tray (double-click to open)";
                        // refresh values when hidden
                        UpdateTray();
                    }
                });
            }
            catch { }
        }

        private void ShowFromTray()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                    if (_trayIcon != null) _trayIcon.Visible = false;
                });
            }
            catch { }
        }
    }
}
