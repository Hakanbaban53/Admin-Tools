using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace FTP_Tool.Services
{
    public static class ThemeService
    {
        public enum Theme { Light, Dark, System }

        private static bool _listeningToSystem = false;
        private static UserPreferenceChangedEventHandler? _sysHandler;

        public static void ApplyTheme(Theme theme)
        {
            // If requested theme is System we resolve to Light/Dark based on registry
            if (theme == Theme.System)
            {
                // Start monitoring system theme changes
                StartSystemThemeMonitoring();
                theme = GetSystemTheme() == true ? Theme.Light : Theme.Dark;
            }
            else
            {
                // Stop monitoring when a specific theme is chosen
                StopSystemThemeMonitoring();
            }

            var mergedDictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;

            // Find and remove any existing theme dictionary to prevent conflicts
            var oldTheme = mergedDictionaries.FirstOrDefault(d =>
                d.Source != null && (d.Source.OriginalString.Contains("LightTheme.xaml") || d.Source.OriginalString.Contains("DarkTheme.xaml")));

            if (oldTheme != null)
            {
                mergedDictionaries.Remove(oldTheme);
            }

            // Determine the new theme URI and add it to the merged dictionaries
            string themeUri = theme == Theme.Light ? "Theme/LightTheme.xaml" : "Theme/DarkTheme.xaml";
            var newTheme = new ResourceDictionary { Source = new Uri(themeUri, UriKind.Relative) };
            mergedDictionaries.Add(newTheme);
        }

        private static void StartSystemThemeMonitoring()
        {
            if (_listeningToSystem) return;
            try
            {
                _sysHandler = new UserPreferenceChangedEventHandler(OnUserPreferenceChanged);
                SystemEvents.UserPreferenceChanged += _sysHandler;
                _listeningToSystem = true;
            }
            catch { }
        }

        private static void StopSystemThemeMonitoring()
        {
            if (!_listeningToSystem) return;
            try
            {
                if (_sysHandler != null) SystemEvents.UserPreferenceChanged -= _sysHandler;
            }
            catch { }
            _listeningToSystem = false;
            _sysHandler = null;
        }

        private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            // When user preference changes, re-check the system theme and re-apply
            try
            {
                var isLight = GetSystemTheme();
                var themeToApply = (isLight == true) ? Theme.Light : Theme.Dark;
                // Re-apply on UI thread
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => ApplyTheme(themeToApply)));
            }
            catch { }
        }

        /// <summary>
        /// Returns true when system/apps theme is light, false for dark, null when unknown.
        /// </summary>
        private static bool? GetSystemTheme()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
                if (key == null) return null;
                var val = key.GetValue("AppsUseLightTheme");
                if (val is int i) return i != 0;
                if (val is byte b) return b != 0;
                return null;
            }
            catch { return null; }
        }
    }
}
