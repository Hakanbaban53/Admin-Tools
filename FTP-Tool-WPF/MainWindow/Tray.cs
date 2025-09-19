using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Forms;

namespace FTP_Tool
{
    public partial class MainWindow
    {
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
                cm.Items.Add("Show").Click += (s, e) => ShowFromTray();
                cm.Items.Add("Start Monitoring").Click += (s, e) => Dispatcher.Invoke(() => BtnStart_Click(this, new RoutedEventArgs()));
                cm.Items.Add("Stop Monitoring").Click += (s, e) => Dispatcher.Invoke(() => BtnStop_Click(this, new RoutedEventArgs()));
                cm.Items.Add(new ToolStripSeparator());
                cm.Items.Add("Exit").Click += (s, e) => Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown());
                _trayIcon.ContextMenuStrip = cm;
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
