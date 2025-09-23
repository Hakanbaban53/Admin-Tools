using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Linq;
using System.Net.Mail;

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
                // explicitly save as 'ftp' category
                _credentialService.Save(_settings.Host ?? string.Empty, _settings.Username ?? string.Empty, txtPassword.Password ?? string.Empty, "ftp");
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

        private void BtnNavAlerts_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("Alerts");
            SetFloatingSidebarActive("Alerts");
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
                var btnL = FindName("btnFloatingNavAlerts") as System.Windows.Controls.Button; // optional

                if (btnM != null && btnS != null && btnA != null)
                {
                    // reset all to NavButton
                    btnM.Style = (Style)FindResource("NavButton");
                    btnS.Style = (Style)FindResource("NavButton");
                    btnA.Style = (Style)FindResource("NavButton");

                    if (btnL != null) btnL.Style = (Style)FindResource("NavButton");

                    switch (page)
                    {
                        case "Monitor":
                            btnM.Style = (Style)FindResource("NavButtonActive");
                            break;
                        case "Settings":
                            btnS.Style = (Style)FindResource("NavButtonActive");
                            break;
                        case "Alerts":
                            if (btnL != null) btnL.Style = (Style)FindResource("NavButtonActive");
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
                // show both ftp and smtp credentials grouped
                var ftpList = _credentialService.ListSavedCredentials("ftp");
                foreach (var item in ftpList)
                {
                    lb.Items.Add($"[FTP] {item.Host} : {item.Username}");
                }

                var smtpList = _credentialService.ListSavedCredentials("smtp");
                foreach (var item in smtpList)
                {
                    lb.Items.Add($"[SMTP] {item.Host} : {item.Username}");
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to refresh credentials: {ex.Message}", LogLevel.Error);
            }
        }

        // Test email click handler added (implementation in MainWindow)
        private async void BtnTestEmail_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await this.SendTestEmailAsync();
            }
            catch (Exception ex)
            {
                Log($"Test email failed: {ex.Message}", LogLevel.Error);
                global::System.Windows.MessageBox.Show($"Failed to send test email: {ex.Message}", "Email Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteSavedCredential_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var lb = this.FindName("lstSavedCredentials") as System.Windows.Controls.ListBox;
                if (lb == null || lb.SelectedItem == null) return;

                var sel = lb.SelectedItem.ToString();
                if (string.IsNullOrEmpty(sel)) return;

                // expected format: "[CATEGORY] host : username"
                var category = "ftp";
                var host = string.Empty;
                var user = string.Empty;

                try
                {
                    if (sel.StartsWith("[SMTP]", StringComparison.OrdinalIgnoreCase)) category = "smtp";
                    else if (sel.StartsWith("[FTP]", StringComparison.OrdinalIgnoreCase)) category = "ftp";

                    // remove prefix
                    var rest = sel;
                    var idx = sel.IndexOf(']');
                    if (idx >= 0 && idx + 1 < sel.Length) rest = sel.Substring(idx + 1).Trim();

                    var partsArr = rest.Split(new[] { ':' }, 2);
                    host = partsArr.Length >= 1 ? partsArr[0].Trim() : string.Empty;
                    user = partsArr.Length >= 2 ? partsArr[1].Trim() : string.Empty;
                }
                catch { }

                var res = System.Windows.MessageBox.Show($"Delete saved credential for '{category.ToUpper()} {host} : {user}'?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;

                _credentialService.Delete(host, user, category);
                lb.Items.Remove(lb.SelectedItem);

                try
                {
                    // Only clear the appropriate password field depending on category
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (string.Equals(category, "ftp", StringComparison.OrdinalIgnoreCase))
                            {
                                txtPassword.Password = string.Empty;
                            }
                            else if (string.Equals(category, "smtp", StringComparison.OrdinalIgnoreCase))
                            {
                                try { txtSmtpPass.Password = string.Empty; } catch { }
                            }
                        }
                        catch { }
                    });
                    try { var _ = _settings_service?.SaveAsync(_settings); } catch { }
                }
                catch { }

                Log($"Deleted credential for {category.ToUpper()} {host} : {user}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"Failed to delete credential: {ex.Message}", LogLevel.Error);
            }
        }

        private void BtnAddRecipient_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = txtNewRecipient.Text?.Trim();
                if (string.IsNullOrEmpty(text)) return;

                // use MailAddress for validation
                try
                {
                    var m = new MailAddress(text);
                }
                catch
                {
                    System.Windows.MessageBox.Show("Please enter a valid email address.", "Invalid", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // prevent duplicates (case-insensitive)
                var exists = lstEmailRecipients.Items.Cast<object>().Any(o => string.Equals(o?.ToString()?.Trim(), text, StringComparison.OrdinalIgnoreCase));
                if (exists)
                {
                    System.Windows.MessageBox.Show("Recipient already exists in the list.", "Duplicate", MessageBoxButton.OK, MessageBoxImage.Information);
                    txtNewRecipient.Text = string.Empty;
                    return;
                }

                lstEmailRecipients.Items.Add(text);
                txtNewRecipient.Text = string.Empty;

                // select newly added item
                lstEmailRecipients.SelectedIndex = lstEmailRecipients.Items.Count - 1;

                // persist into settings as semicolon separated
                SaveRecipientsToSettings();
            }
            catch (Exception ex)
            {
                Log($"Failed to add recipient: {ex.Message}", LogLevel.Error);
            }
        }

        private void BtnRemoveRecipient_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int count = lstEmailRecipients.Items.Count;
                if (count == 0) return;

                int removeIndex = -1;
                if (lstEmailRecipients.SelectedIndex >= 0)
                {
                    removeIndex = lstEmailRecipients.SelectedIndex;
                }
                else
                {
                    // nothing selected (e.g. focus moved to button) - remove the last item as a convenience
                    removeIndex = count - 1;
                }

                if (removeIndex >= 0 && removeIndex < count)
                {
                    lstEmailRecipients.Items.RemoveAt(removeIndex);

                    // restore a sensible selection
                    if (lstEmailRecipients.Items.Count > 0)
                    {
                        var sel = Math.Min(removeIndex, lstEmailRecipients.Items.Count - 1);
                        lstEmailRecipients.SelectedIndex = sel;
                    }
                }

                SaveRecipientsToSettings();
            }
            catch (Exception ex)
            {
                Log($"Failed to remove recipient: {ex.Message}", LogLevel.Error);
            }
        }

        private void SaveRecipientsToSettings()
        {
            try
            {
                var items = lstEmailRecipients.Items.Cast<object>().Select(o => o.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
                _settings.EmailRecipients = string.Join(";", items);
                var _ = _settings_service.SaveAsync(_settings);
            }
            catch { }
        }

        private void LoadRecipientsFromSettings()
        {
            try
            {
                lstEmailRecipients.Items.Clear();
                if (string.IsNullOrWhiteSpace(_settings.EmailRecipients)) return;
                var parts = _settings.EmailRecipients.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var p in parts) lstEmailRecipients.Items.Add(p);
            }
            catch { }
        }

        private void PersistWeekdays()
        {
            try
            {
                var days = new[]
                {
                    chkMon.IsChecked == true ? "Mon" : null,
                    chkTue.IsChecked == true ? "Tue" : null,
                    chkWed.IsChecked == true ? "Wed" : null,
                    chkThu.IsChecked == true ? "Thu" : null,
                    chkFri.IsChecked == true ? "Fri" : null,
                    chkSat.IsChecked == true ? "Sat" : null,
                    chkSun.IsChecked == true ? "Sun" : null,
                }.Where(s => s != null).ToArray();

                _settings.AlertWeekdays = string.Join(",", days);
                var _ = _settings_service.SaveAsync(_settings);
            }
            catch { }
        }

        // Ensure LoadRecipientsFromSettings is called during MainWindow_Loaded in WindowLifecycle.cs
    }
}
