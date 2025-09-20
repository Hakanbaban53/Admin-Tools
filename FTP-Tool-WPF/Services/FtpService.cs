using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
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

        // configurable options
        private int _connectionTimeoutSeconds = 30;
        private int _maxRetries = 3;
        private bool _usePassive = true;

        // Optional telemetry callback (message, level)
        public Action<string, FTP_Tool.LogLevel>? Logger { get; set; }

        public void ApplyOptions(int connectionTimeoutSeconds, int maxRetries, bool usePassive)
        {
            _connectionTimeoutSeconds = Math.Max(1, connectionTimeoutSeconds);
            _maxRetries = Math.Max(0, maxRetries);
            _usePassive = usePassive;
        }

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

                    // Try to set optional properties (timeouts, data connection type) via reflection so code remains compatible
                    try
                    {
                        // set milliseconds timeouts if properties available
                        var ms = _connectionTimeoutSeconds * 1000;
                        var t = _client.GetType();
                        var propNames = new[] { "ConnectTimeout", "ReadTimeout", "DataConnectionConnectTimeout", "DataConnectionReadTimeout" };
                        foreach (var pn in propNames)
                        {
                            try
                            {
                                var prop = t.GetProperty(pn);
                                if (prop != null && prop.CanWrite && prop.PropertyType == typeof(int))
                                {
                                    prop.SetValue(_client, ms);
                                }
                            }
                            catch { }
                        }

                        // set passive/active via DataConnectionType if present
                        try
                        {
                            var prop = t.GetProperty("DataConnectionType");
                            if (prop != null && prop.CanWrite)
                            {
                                var enumType = prop.PropertyType;
                                if (enumType.IsEnum)
                                {
                                    // try parse names used in different FluentFTP versions
                                    var name = _usePassive ? "PASV" : "PORT";
                                    try
                                    {
                                        var enumVal = Enum.Parse(enumType, name, ignoreCase: true);
                                        prop.SetValue(_client, enumVal);
                                    }
                                    catch
                                    {
                                        // try alternate name
                                        name = _usePassive ? "Passive" : "Active";
                                        try { var enumVal = Enum.Parse(enumType, name, ignoreCase: true); prop.SetValue(_client, enumVal); } catch { }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                    catch { }

                    // Accept all certificates for now (useful for debugging/self-signed servers). Do not use in production without proper validation.
                    _client.ValidateCertificate += (control, e) => { e.Accept = true; };
                }

                // Only check IsConnected if _client is not null
                if (_client == null || !_client.IsConnected)
                {
                    int attempts = 0;
                    Exception? lastEx = null;
                    var sw = Stopwatch.StartNew();

                    while (attempts <= _maxRetries && !token.IsCancellationRequested)
                    {
                        attempts++;
                        try
                        {
                            // Prefer an async connect method when available (FluentFTP newer versions) so cancellation is supported
                            var client = _client; // capture for thread-safety
                            if (client == null) throw new InvalidOperationException("FTP client not initialized");

                            var clientType = client.GetType();
                            var connectAsyncMethod = clientType.GetMethod("ConnectAsync", new[] { typeof(CancellationToken) });
                            if (connectAsyncMethod != null)
                            {
                                // call ConnectAsync(CancellationToken)
                                var task = (Task?)connectAsyncMethod.Invoke(client, new object[] { token });
                                if (task != null) await task.ConfigureAwait(false);
                            }
                            else
                            {
                                // fallback to synchronous Connect on a background thread
                                await Task.Run(() => client.Connect(), token).ConfigureAwait(false);
                            }

                            // if connected, emit telemetry and return
                            if (client.IsConnected)
                            {
                                sw.Stop();
                                Logger?.Invoke($"Connected to {host}:{port} in {sw.ElapsedMilliseconds}ms after {attempts} attempt(s)", FTP_Tool.LogLevel.Info);
                                return;
                            }
                        }
                        catch (FluentFTP.FtpAuthenticationException fae)
                        {
                            // authentication failure: do not retry
                            Logger?.Invoke($"Authentication failed for {host}:{port}: {fae.Message}", FTP_Tool.LogLevel.Error);
                            throw new InvalidOperationException($"Authentication failed: {fae.Message}", fae);
                        }
                        catch (OperationCanceledException)
                        {
                            // bubble up cancellation
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lastEx = ex;
                            Logger?.Invoke($"Connect attempt {attempts} failed: {ex.Message}", FTP_Tool.LogLevel.Warning);
                            // small delay before retrying (can be interrupted by token)
                            try { await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false); } catch { }
                        }
                    }

                    sw.Stop();
                    if (_client == null || !_client.IsConnected)
                    {
                        Logger?.Invoke($"Failed to connect to {host}:{port} after {attempts} attempt(s): {lastEx?.Message}", FTP_Tool.LogLevel.Error);
                        throw new InvalidOperationException("Failed to connect to FTP server", lastEx);
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
            var sw = Stopwatch.StartNew();
            await EnsureConnectedAsync(host, port, creds, token).ConfigureAwait(false);
            var client = _client;
            if (client == null) return Array.Empty<string>();

            var result = await Task.Run<string[]>(() =>
            {
                var folder = NormalizeRemoteFolder(remoteFolder);
                try
                {
                    var listing = client.GetNameListing(folder) ?? Array.Empty<string>();
                    return listing.Select(p => Path.GetFileName(p.TrimEnd('/'))).Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
                }
                catch
                {
                    return Array.Empty<string>();
                }
            }, token).ConfigureAwait(false);

            sw.Stop();
            Logger?.Invoke($"ListFiles({remoteFolder}) returned {result.Length} entries in {sw.ElapsedMilliseconds}ms", FTP_Tool.LogLevel.Debug);
            return result;
        }

        public async Task<long> GetFileSizeAsync(string host, int port, NetworkCredential creds, string remotePath, CancellationToken token)
        {
            await EnsureConnectedAsync(host, port, creds, token).ConfigureAwait(false);
            var client = _client;
            if (client == null) return -1L;

            return await Task.Run<long>(() =>
            {
                try
                {
                    return client.GetFileSize(remotePath);
                }
                catch
                {
                    return -1L;
                }
            }, token).ConfigureAwait(false);
        }

        public async Task<bool> DirectoryExistsAsync(string host, int port, NetworkCredential creds, string remoteFolder, CancellationToken token)
        {
            await EnsureConnectedAsync(host, port, creds, token).ConfigureAwait(false);
            var client = _client;
            if (client == null) return false;

            return await Task.Run<bool>(() =>
            {
                var folder = NormalizeRemoteFolder(remoteFolder);
                try
                {
                    return client.DirectoryExists(folder);
                }
                catch
                {
                    try
                    {
                        var listing = client.GetNameListing(folder);
                        return listing != null && listing.Length > 0;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }, token).ConfigureAwait(false);
        }

        public async Task<(bool Success, string? LocalPath, string? RemotePath, bool Deleted, string? ErrorMessage, bool Skipped)> DownloadFileAsync(
            string host, int port, NetworkCredential creds,
            string remoteFolder, string fileName, string localFolder, bool deleteAfter, CancellationToken token)
        {
            await EnsureConnectedAsync(host, port, creds, token).ConfigureAwait(false);
            var client = _client;
            if (client == null) return (false, null, null, false, "FTP client not initialized", false);

            return await Task.Run<(bool, string?, string?, bool, string?, bool)>(() =>
            {
                var overallSw = Stopwatch.StartNew();
                try
                {
                    var folder = NormalizeRemoteFolder(remoteFolder);
                    var remotePath = (folder == "/") ? $"/{fileName}" : $"{folder}/{fileName}";

                    long remoteSize = -1;
                    try { remoteSize = client.GetFileSize(remotePath); } catch { }

                    var finalPath = Path.Combine(localFolder, fileName);

                    if (File.Exists(finalPath) && remoteSize >= 0)
                    {
                        try
                        {
                            var localSize = new FileInfo(finalPath).Length;
                            if (localSize == remoteSize)
                            {
                                overallSw.Stop();
                                Logger?.Invoke($"Skipping download for {fileName}: already exists (size match). duration={overallSw.ElapsedMilliseconds}ms", FTP_Tool.LogLevel.Debug);
                                return (true, finalPath, remotePath, false, null, true);
                            }
                        }
                        catch { }
                    }

                    var tempPath = finalPath + ".part";
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

                    int attempts = 0;
                    Exception? lastEx = null;

                    while (attempts <= _maxRetries)
                    {
                        attempts++;
                        try
                        {
                            var status = client.DownloadFile(tempPath, remotePath);
                            if (status == FtpStatus.Success || status == FtpStatus.Skipped)
                            {
                                try
                                {
                                    File.Move(tempPath, finalPath, true);
                                }
                                catch (Exception mvEx)
                                {
                                    overallSw.Stop();
                                    Logger?.Invoke($"Move temp failed for {fileName}: {mvEx.Message} attempts={attempts} duration={overallSw.ElapsedMilliseconds}ms", FTP_Tool.LogLevel.Error);
                                    return (false, null, null, false, $"Move temp failed: {mvEx.Message}", false);
                                }

                                bool deleted = false;
                                if (deleteAfter)
                                {
                                    try
                                    {
                                        client.DeleteFile(remotePath);
                                        deleted = true;
                                    }
                                    catch { }
                                }

                                overallSw.Stop();
                                Logger?.Invoke($"Downloaded {fileName} ({remoteSize} bytes) in {overallSw.ElapsedMilliseconds}ms attempts={attempts} deleted={deleted}", FTP_Tool.LogLevel.Info);
                                return (true, finalPath, remotePath, deleted, null, false);
                            }

                            lastEx = new InvalidOperationException($"FTP status: {status}");
                            Logger?.Invoke($"Download attempt {attempts} for {fileName} returned status {status}", FTP_Tool.LogLevel.Warning);
                        }
                        catch (Exception ex)
                        {
                            lastEx = ex;
                            Logger?.Invoke($"Download attempt {attempts} for {fileName} failed: {ex.Message}", FTP_Tool.LogLevel.Warning);
                        }

                        // small backoff
                        try { Thread.Sleep(500); } catch { }
                    }

                    overallSw.Stop();
                    Logger?.Invoke($"Failed to download {fileName} after {attempts} attempt(s): {lastEx?.Message} duration={overallSw.ElapsedMilliseconds}ms", FTP_Tool.LogLevel.Error);
                    return (false, null, null, false, lastEx?.Message ?? "Failed to download", false);
                }
                catch (Exception ex)
                {
                    overallSw.Stop();
                    Logger?.Invoke($"Download error for {fileName}: {ex.Message} duration={overallSw.ElapsedMilliseconds}ms", FTP_Tool.LogLevel.Error);
                    return (false, null, null, false, ex.Message, false);
                }
            }, token).ConfigureAwait(false);
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
