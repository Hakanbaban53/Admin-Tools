using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FTP_Tool.Services;
using System.Net;

namespace FTP_Tool.ViewModels
{
    // Lightweight ViewModel: exposes status properties and provides a simple file-list cache.
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly FtpService _ftpService;
        private readonly ConcurrentDictionary<string, (string[] Files, DateTime LoadedAt)> _cache = new();

        public MainViewModel(FtpService ftpService)
        {
            _ftpService = ftpService ?? throw new ArgumentNullException(nameof(ftpService));
        }

        // Simple status properties for binding
        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _lastCheck = "-";
        public string LastCheck
        {
            get => _lastCheck;
            set => SetProperty(ref _lastCheck, value);
        }

        private string _lastError = "-";
        public string LastError
        {
            get => _lastError;
            set => SetProperty(ref _lastError, value);
        }

        private int _downloadedCount;
        public int DownloadedCount
        {
            get => _downloadedCount;
            set => SetProperty(ref _downloadedCount, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (!Equals(field, value))
            {
                field = value!;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        // Get cached file list; cache TTL in seconds
        public async Task<string[]> GetCachedFileListAsync(string host, int port, NetworkCredential creds, string remoteFolder, int ttlSeconds, CancellationToken token)
        {
            var key = GetCacheKey(host, port, creds, remoteFolder);
            if (_cache.TryGetValue(key, out var entry))
            {
                if ((DateTime.UtcNow - entry.LoadedAt).TotalSeconds < ttlSeconds)
                {
                    return entry.Files;
                }
            }

            var files = await _ftpService.ListFilesAsync(host, port, creds, remoteFolder, token);
            _cache[key] = (files, DateTime.UtcNow);
            return files;
        }

        public void InvalidateCache(string host, int port, NetworkCredential creds, string remoteFolder)
        {
            var key = GetCacheKey(host, port, creds, remoteFolder);
            _cache.TryRemove(key, out _);
        }

        private static string GetCacheKey(string host, int port, NetworkCredential creds, string remoteFolder)
        {
            return $"{host}:{port}|{creds.UserName}|{remoteFolder}".ToLowerInvariant();
        }
    }
}
