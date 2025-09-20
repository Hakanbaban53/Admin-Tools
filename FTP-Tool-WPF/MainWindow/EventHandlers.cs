using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace FTP_Tool
{
    public partial class MainWindow
    {
        private NetworkCredential GetCredentials()
        {
            return new NetworkCredential(txtUsername.Text.Trim(), txtPassword.Password);
        }

        private void UpdateDownloadedLabel()
        {
            Dispatcher.Invoke(() => lblDownloaded.Text = $"Downloaded: {_downloadedCount} files");
            try { UpdateTray(); } catch { }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;

            _settings.Host = txtHost.Text.Trim();
            _settings.Port = int.TryParse(txtPort.Text, out var p) ? p : 21;
            _settings.Username = txtUsername.Text.Trim();
            _settings.RemoteFolder = txtRemoteFolder.Text.Trim();
            _settings.LocalFolder = txtLocalFolder.Text.Trim();
            _settings.IntervalSeconds = int.TryParse(txtInterval.Text, out var s) ? s : 30;
            _settings.DeleteAfterDownload = chkDeleteAfterDownload.IsChecked == true;
            _ = _settings_service.SaveAsync(_settings);

            try
            {
                _credentialService.Save(_settings.Host ?? string.Empty, _settings.Username ?? string.Empty, txtPassword.Password ?? string.Empty);
            }
            catch { }

            UpdateUiState(true);
            UpdateSidebarStatus(true);
            btnCheckNow.IsEnabled = false;

            _cts = new System.Threading.CancellationTokenSource();
            int seconds = _settings.IntervalSeconds > 0 ? _settings.IntervalSeconds : 30;

            // Start the monitoring loop as an async task (do not run the whole method on a thread-pool thread).
            // FTP/network operations inside the loop are already offloaded to background threads by FtpService.
            _monitoringTask = StartMonitoringLoopAsync(seconds, _cts.Token);

            SetStatus("Monitoring started");
            Log("Monitor started", LogLevel.Info);

            try { UpdateTray(); } catch { }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopMonitoring("Monitoring stopped by user");
            try { UpdateTray(); } catch { }
        }

        private async void BtnCheckNow_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;
            btnCheckNow.IsEnabled = false;
            SetStatus("Checking now...");

            using var localCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? System.Threading.CancellationToken.None);
            try
            {
                Log("Manual check starting...", LogLevel.Info);
                var res = await DownloadOnceAsync(localCts.Token, "Manual");
                Log("Manual check finished.", LogLevel.Info);
                if (res) Dispatcher.Invoke(() => txtLastCheck.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            finally
            {
                btnCheckNow.IsEnabled = true;
                SetStatus("Ready");
                try { UpdateTray(); } catch { }
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = "Select download folder";
            dlg.ShowNewFolderButton = true;
            var result = dlg.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                txtLocalFolder.Text = dlg.SelectedPath;
                _settings.LocalFolder = dlg.SelectedPath;
                try { var _ = _settings_service?.SaveAsync(_settings); } catch { }
            }
        }

        private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;
            SetStatus("Testing connection...");
            bool ok = await TestFtpConnectionAsync();
            SetStatus(ok ? "Connection successful" : "Connection failed");
            Log($"Test connection: {(ok ? "OK" : "FAILED")} ", LogLevel.Info);
            if (ok)
            {
                Dispatcher.Invoke(() =>
                {
                    txtLastCheck.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    txtLastError.Text = "-";
                    _lastSuccessfulCheck = DateTime.Now;
                });

                await RefreshRemoteFileCount();
            }
            UpdateSidebarStats();
            try { UpdateTray(); } catch { }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _displayedLogEntries.Clear();
                _downloadedCount = 0;
                UpdateDownloadedLabel();
                try { UpdateTray(); } catch { }
            }
            catch { }
        }

        private void BtnOpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = Path.GetDirectoryName(_logFilePath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    System.Windows.MessageBox.Show("Log folder not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var psi = new ProcessStartInfo { FileName = dir, UseShellExecute = true };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                try { System.Windows.MessageBox.Show($"Failed to open log folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
            }
        }

        private void BtnNavMonitor_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("Monitor");
            SetFloatingSidebarActive("Monitor");
            if (FloatingSidebar.Visibility == Visibility.Visible) HideFloatingSidebar();
        }

        private void BtnNavSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("Settings");
            SetFloatingSidebarActive("Settings");
            if (FloatingSidebar.Visibility == Visibility.Visible) HideFloatingSidebar();
        }

        private void BtnNavAbout_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("About");
            SetFloatingSidebarActive("About");
            if (FloatingSidebar.Visibility == Visibility.Visible) HideFloatingSidebar();
        }

        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            if (FloatingSidebar.Visibility == Visibility.Visible) HideFloatingSidebar(); else ShowFloatingSidebar();
        }

        // Simple show/hide for floating sidebar
        private void HideFloatingSidebar()
        {
            try
            {
                // animate off-screen using TranslateTransform
                double width = (FloatingSidebar.ActualWidth > 0) ? FloatingSidebar.ActualWidth : 260;
                var tt = FloatingSidebar.RenderTransform as System.Windows.Media.TranslateTransform;
                if (tt == null)
                {
                    tt = new System.Windows.Media.TranslateTransform(0, 0);
                    FloatingSidebar.RenderTransform = tt;
                }

                var anim = new DoubleAnimation
                {
                    To = -width,
                    Duration = TimeSpan.FromMilliseconds(220),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };
                anim.Completed += (s, e) => FloatingSidebar.Visibility = Visibility.Collapsed;
                tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
            }
            catch { }
        }

        private void ShowFloatingSidebar()
        {
            try
            {
                // ensure visible then animate into view
                FloatingSidebar.Visibility = Visibility.Visible;

                // give layout a chance to measure so ActualWidth is valid
                FloatingSidebar.UpdateLayout();

                var tt = FloatingSidebar.RenderTransform as System.Windows.Media.TranslateTransform;
                if (tt == null)
                {
                    tt = new System.Windows.Media.TranslateTransform(-FloatingSidebar.ActualWidth, 0);
                    FloatingSidebar.RenderTransform = tt;
                }

                var anim = new DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(220),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };
                tt.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
            }
            catch { }
        }

        // update floating sidebar button styles so the active page is visually selected
        private void SetFloatingSidebarActive(string page)
        {
            try
            {
                var btnM = FindName("btnFloatingNavMonitor") as System.Windows.Controls.Button;
                var btnS = FindName("btnFloatingNavSettings") as System.Windows.Controls.Button;
                var btnA = FindName("btnFloatingNavAbout") as System.Windows.Controls.Button;

                if (btnM != null && btnS != null && btnA != null)
                {
                    // reset all to NavButton
                    btnM.Style = (Style)FindResource("NavButton");
                    btnS.Style = (Style)FindResource("NavButton");
                    btnA.Style = (Style)FindResource("NavButton");

                    switch (page)
                    {
                        case "Monitor":
                            btnM.Style = (Style)FindResource("NavButtonActive");
                            break;
                        case "Settings":
                            btnS.Style = (Style)FindResource("NavButtonActive");
                            break;
                        case "About":
                            btnA.Style = (Style)FindResource("NavButtonActive");
                            break;
                    }
                }
            }
            catch { }
        }

        // update saved credentials list
        private void BtnRefreshCredentials_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var lb = this.FindName("lstSavedCredentials") as System.Windows.Controls.ListBox;
                if (lb == null) return;

                lb.Items.Clear();
                var list = _credentialService.ListSavedCredentials();
                foreach (var item in list)
                {
                    lb.Items.Add($"{item.Host} : {item.Username}");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to refresh credentials: {ex.Message}", LogLevel.Error);
            }
        }

        private void BtnDeleteSavedCredential_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var lb = this.FindName("lstSavedCredentials") as System.Windows.Controls.ListBox;
                if (lb == null) return;
                if (lb.SelectedItem == null) return;

                var sel = lb.SelectedItem.ToString();
                if (string.IsNullOrEmpty(sel)) return;

                // expect format host : username
                var partsArr = sel.Split(new[] { ':' }, 2);
                var host = partsArr.Length >= 1 ? partsArr[0].Trim() : string.Empty;
                var user = partsArr.Length >= 2 ? partsArr[1].Trim() : string.Empty;

                var res = System.Windows.MessageBox.Show($"Delete saved credential for '{host} : {user}'?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;

                _credentialService.Delete(host, user);
                lb.Items.Remove(lb.SelectedItem);

                // clear password field in UI if it matches the deleted credential context
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        try { txtPassword.Password = string.Empty; } catch { }
                    });

                    // best-effort: save settings (username/host unchanged) so future loads won't populate password
                    try { var _ = _settings_service?.SaveAsync(_settings); } catch { }
                }
                catch { }

                Log($"Deleted credential for {host} : {user}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"Failed to delete credential: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
