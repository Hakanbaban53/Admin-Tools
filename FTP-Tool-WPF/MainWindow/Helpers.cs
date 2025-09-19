using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Net;

namespace FTP_Tool
{
    public partial class MainWindow
    {
        private void MainWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveSidebar();
        }

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(txtHost.Text))
            {
                System.Windows.MessageBox.Show("FTP Host girin.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtUsername.Text))
            {
                System.Windows.MessageBox.Show("FTP kullanici adini girin.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtPassword.Password))
            {
                System.Windows.MessageBox.Show("FTP sifresini girin.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtLocalFolder.Text) || !System.IO.Directory.Exists(txtLocalFolder.Text))
            {
                System.Windows.MessageBox.Show("Geçerli bir local klasör seçin.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private async Task<bool> TestFtpConnectionAsync()
        {
            var host = Dispatcher.Invoke(() => txtHost.Text.Trim());
            var port = Dispatcher.Invoke(() => int.TryParse(txtPort.Text, out var p) ? p : 21);
            var creds = GetCredentials();
            var remoteFolder = Dispatcher.Invoke(() => txtRemoteFolder.Text);

            try
            {
                return await _ftpService.DirectoryExistsAsync(host, port, creds, remoteFolder, System.Threading.CancellationToken.None);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => txtLastError.Text = ex.Message);
                Log($"Test connection failed: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        private async Task RefreshRemoteFileCount()
        {
            try
            {
                var host = Dispatcher.Invoke(() => txtHost.Text.Trim());
                var port = Dispatcher.Invoke(() => int.TryParse(txtPort.Text, out var p) ? p : 21);
                var creds = GetCredentials();
                var remoteFolder = Dispatcher.Invoke(() => txtRemoteFolder.Text);

                var files = await _ftpService.ListFilesAsync(host, port, creds, remoteFolder, System.Threading.CancellationToken.None);
                _currentFilesInRemote = files.Length;
                UpdateSidebarStats();
            }
            catch
            {
                _currentFilesInRemote = 0;
                UpdateSidebarStats();
            }
        }

        private void ShowPage(string page, bool animate = true)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_isLoaded) animate = false;

                UIElement toShow = MonitorPage;
                switch (page)
                {
                    case "Settings": toShow = SettingsPage; break;
                    case "History": toShow = HistoryPage; break;
                    case "Schedule": toShow = SchedulePage; break;
                    case "Logs": toShow = LogsPage; break;
                    case "About": toShow = AboutPage; break;
                    default: toShow = MonitorPage; break;
                }

                MonitorPage.Visibility = Visibility.Collapsed;
                SettingsPage.Visibility = Visibility.Collapsed;
                HistoryPage.Visibility = Visibility.Collapsed;
                SchedulePage.Visibility = Visibility.Collapsed;
                LogsPage.Visibility = Visibility.Collapsed;
                AboutPage.Visibility = Visibility.Collapsed;

                if (animate)
                {
                    toShow.Opacity = 0;
                    toShow.Visibility = Visibility.Visible;
                    var da = new System.Windows.Media.Animation.DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(300)));
                    toShow.BeginAnimation(UIElement.OpacityProperty, da);
                }
                else
                {
                    toShow.Visibility = Visibility.Visible;
                }

                var active = (Style)FindResource("NavButtonActive");
                var normal = (Style)FindResource("NavButton");
                btnNavMonitor.Style = page == "Monitor" ? active : normal;
                btnNavSettings.Style = page == "Settings" ? active : normal;
                btnNavAbout.Style = page == "About" ? active : normal;

                txtNavStatus.Text = page;
                _settings.LastPage = page;
            });
        }
    }
}
