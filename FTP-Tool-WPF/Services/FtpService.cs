using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;

namespace FTP_Tool.Services
{
    // Simple thread-safe, persistent FtpClient wrapper.
    // Uses FluentFTP synchronous APIs inside Task.Run for compatibility.
    public sealed class FtpService : IDisposable
    {
        private FtpClient? _client;
        private string? _host;
        private int _port;
        private NetworkCredential? _creds;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private bool _disposed;

        public async Task EnsureConnectedAsync(string host, int port, NetworkCredential creds, CancellationToken token)
        {
            await _lock.WaitAsync(token);
            try
            {
                if (_disposed) throw new ObjectDisposedException(nameof(FtpService));

                // Validate inputs early to avoid null-deref and give clearer error messages
                if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required", nameof(host));
                if (creds == null) throw new ArgumentNullException(nameof(creds));
                if (string.IsNullOrWhiteSpace(creds.UserName)) throw new ArgumentException("Username is required", nameof(creds));
                if (creds.Password == null) throw new ArgumentException("Password must be provided (can be empty string)", nameof(creds));

                // Safely determine whether we need a new client
                var needNew = _client == null || !string.Equals(_host, host, StringComparison.OrdinalIgnoreCase) || _port != port || _creds?.UserName != creds.UserName || _creds?.Password != creds.Password;
                if (needNew)
                {
                    try { _client?.Disconnect(); } catch { }
                    try { _client?.Dispose(); } catch { }

                    _host = host;
                    _port = port;
                    _creds = new NetworkCredential(creds.UserName, creds.Password);

                    _client = new FtpClient(host)
                    {
                        Port = port,
                        Credentials = new NetworkCredential(creds.UserName, creds.Password)
                    };

                    // Accept all certificates for now (useful for debugging/self-signed servers). Do not use in production without proper validation.
                    _client.ValidateCertificate += (control, e) => { e.Accept = true; };
                }

                // Only check IsConnected if _client is not null
                if (_client == null || !_client.IsConnected)
                {
                    try
                    {
                        // Connect on background thread (FluentFTP synchronous API)
                        await Task.Run(() => _client!.Connect(), token);
                    }
                    catch (FluentFTP.FtpAuthenticationException fae)
                    {
                        // Try to read additional server reply info via reflection (not all versions expose LastReply)
                        string lastReply = "(unknown)";
                        try
                        {
                            var c = _client;
                            if (c != null)
                            {
                                var prop = c.GetType().GetProperty("LastReply");
                                if (prop != null)
                                {
                                    var val = prop.GetValue(c);
                                    if (val != null) lastReply = val.ToString() ?? lastReply;
                                }
                            }
                        }
                        catch { /* ignore reflection failures */ }

                        throw new InvalidOperationException($"Authentication failed: {fae.Message}. Server reply: {lastReply}", fae);
                    }
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<string[]> ListFilesAsync(string host, int port, NetworkCredential creds, string remoteFolder, CancellationToken token)
        {
            await EnsureConnectedAsync(host, port, creds, token);
            return await Task.Run<string[]>(() =>
            {
                var folder = NormalizeRemoteFolder(remoteFolder);
                try
                {
                    var listing = _client!.GetNameListing(folder) ?? Array.Empty<string>();
                    return listing.Select(p => Path.GetFileName(p.TrimEnd('/'))).Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
                }
                catch
                {
                    return Array.Empty<string>();
                }
            }, token);
        }

        public async Task<long> GetFileSizeAsync(string host, int port, NetworkCredential creds, string remotePath, CancellationToken token)
        {
            await EnsureConnectedAsync(host, port, creds, token);
            return await Task.Run<long>(() =>
            {
                try
                {
                    return _client!.GetFileSize(remotePath);
                }
                catch
                {
                    return -1L;
                }
            }, token);
        }

        public async Task<bool> DirectoryExistsAsync(string host, int port, NetworkCredential creds, string remoteFolder, CancellationToken token)
        {
            await EnsureConnectedAsync(host, port, creds, token);
            return await Task.Run<bool>(() =>
            {
                var folder = NormalizeRemoteFolder(remoteFolder);
                try
                {
                    return _client!.DirectoryExists(folder);
                }
                catch
                {
                    try
                    {
                        var listing = _client!.GetNameListing(folder);
                        return listing != null && listing.Length > 0;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }, token);
        }

        public async Task<(bool Success, string? LocalPath, string? RemotePath, bool Deleted, string? ErrorMessage, bool Skipped)> DownloadFileAsync(
            string host, int port, NetworkCredential creds,
            string remoteFolder, string fileName, string localFolder, bool deleteAfter, CancellationToken token)
        {
            await EnsureConnectedAsync(host, port, creds, token);
            return await Task.Run<(bool, string?, string?, bool, string?, bool)>(() =>
            {
                try
                {
                    var folder = NormalizeRemoteFolder(remoteFolder);
                    var remotePath = (folder == "/") ? $"/{fileName}" : $"{folder}/{fileName}";

                    long remoteSize = -1;
                    try { remoteSize = _client!.GetFileSize(remotePath); } catch { }

                    var finalPath = Path.Combine(localFolder, fileName);

                    if (File.Exists(finalPath) && remoteSize >= 0)
                    {
                        try
                        {
                            var localSize = new FileInfo(finalPath).Length;
                            if (localSize == remoteSize)
                            {
                                return (true, finalPath, remotePath, false, null, true);
                            }
                        }
                        catch { }
                    }

                    var tempPath = finalPath + ".part";
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

                    var status = _client!.DownloadFile(tempPath, remotePath);
                    if (status == FtpStatus.Success || status == FtpStatus.Skipped)
                    {
                        try
                        {
                            File.Move(tempPath, finalPath, true);
                        }
                        catch (Exception mvEx)
                        {
                            return (false, null, null, false, $"Move temp failed: {mvEx.Message}", false);
                        }

                        bool deleted = false;
                        if (deleteAfter)
                        {
                            try
                            {
                                _client.DeleteFile(remotePath);
                                deleted = true;
                            }
                            catch { }
                        }

                        return (true, finalPath, remotePath, deleted, null, false);
                    }

                    return (false, null, null, false, $"FTP status: {status}", false);
                }
                catch (Exception ex)
                {
                    return (false, null, null, false, ex.Message, false);
                }
            }, token);
        }

        private static string NormalizeRemoteFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || folder == "/") return "/";
            folder = folder.Trim();
            if (!folder.StartsWith("/")) folder = "/" + folder;
            if (folder.EndsWith("/") && folder.Length > 1) folder = folder.TrimEnd('/');
            return folder;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _client?.Disconnect(); } catch { }
            try { _client?.Dispose(); } catch { }
            try { _lock.Dispose(); } catch { }
        }
    }
}
