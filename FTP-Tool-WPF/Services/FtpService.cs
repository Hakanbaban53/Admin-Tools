using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using FluentFTP;

namespace FTP_Tool.Services
{
    /// <summary>
    /// Thread-safe, persistent FTP client wrapper using FluentFTP.
    /// Provides automatic connection management, retry logic, and configurable options.
    /// </summary>
    public sealed class FtpService : IDisposable, IAsyncDisposable
    {
        private FtpClient? _client;
        private string? _host;
        private int _port;
        private NetworkCredential? _creds;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private bool _disposed;

        // Configurable options
        private int _connectionTimeoutSeconds = 30;
        private int _maxAttempts = 3;
        private bool _usePassive = true;
        private bool _validateServerCertificate = false; // Default to false for compatibility

        // Cached reflection info for performance
        private static PropertyInfo[]? _timeoutProperties;
        private static PropertyInfo? _dataConnectionTypeProperty;
        private static Type? _dataConnectionEnumType;
        private static bool _reflectionInitialized;
        private static readonly object _reflectionLock = new object();

        /// <summary>
        /// Optional telemetry callback (message, level)
        /// </summary>
        public Action<string, FTP_Tool.LogLevel>? Logger { get; set; }

        /// <summary>
        /// Gets whether the FTP client is currently connected
        /// </summary>
        public bool IsConnected => _client?.IsConnected ?? false;

        /// <summary>
        /// Gets or sets whether to validate server SSL/TLS certificates.
        /// Default is false for backward compatibility. Set to true for production.
        /// </summary>
        public bool ValidateServerCertificate
        {
            get => _validateServerCertificate;
            set => _validateServerCertificate = value;
        }

        /// <summary>
        /// Apply connection options
        /// </summary>
        /// <param name="connectionTimeoutSeconds">Connection timeout in seconds (minimum 1)</param>
        /// <param name="maxAttempts">Maximum connection/operation attempts (minimum 1)</param>
        /// <param name="usePassive">Whether to use passive mode for data connections</param>
        public void ApplyOptions(int connectionTimeoutSeconds, int maxAttempts, bool usePassive)
        {
            _connectionTimeoutSeconds = Math.Max(1, connectionTimeoutSeconds);
            _maxAttempts = Math.Max(1, maxAttempts);
            _usePassive = usePassive;
        }

        /// <summary>
        /// Ensures the FTP client is connected with the specified credentials
        /// </summary>
        public async Task EnsureConnectedAsync(string host, int port, NetworkCredential creds, CancellationToken token)
        {
            await _lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_disposed) throw new ObjectDisposedException(nameof(FtpService));

                // Validate inputs early
                if (string.IsNullOrWhiteSpace(host))
                    throw new ArgumentException("Host is required", nameof(host));
                if (creds == null)
                    throw new ArgumentNullException(nameof(creds));
                if (string.IsNullOrWhiteSpace(creds.UserName))
                    throw new ArgumentException("Username is required", nameof(creds));
                if (creds.Password == null)
                    throw new ArgumentException("Password must be provided (can be empty string)", nameof(creds));

                // Determine if we need a new client
                bool needNew = _client == null ||
                               !string.Equals(_host, host, StringComparison.OrdinalIgnoreCase) ||
                               _port != port ||
                               _creds?.UserName != creds.UserName ||
                               _creds?.Password != creds.Password;

                if (needNew)
                {
                    DisconnectAndDisposeClient();

                    _host = host;
                    _port = port;
                    _creds = new NetworkCredential(creds.UserName, creds.Password);

                    _client = new FtpClient(host)
                    {
                        Port = port,
                        Credentials = _creds
                    };

                    // Initialize reflection cache once
                    InitializeReflectionCache();

                    // Apply timeout settings
                    ApplyTimeoutSettings(_client);

                    // Apply passive/active mode
                    ApplyDataConnectionType(_client);

                    // Certificate validation
                    _client.ValidateCertificate += OnValidateCertificate;
                }

                // Check connection state and connect if needed
                if (_client == null || !_client.IsConnected)
                {
                    await ConnectWithRetriesAsync(_client!, token).ConfigureAwait(false);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Lists files in the specified remote folder (returns names only, may include directories)
        /// </summary>
        public async Task<string[]> ListFilesAsync(string host, int port, NetworkCredential creds, string remoteFolder, CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            await EnsureConnectedAsync(host, port, creds, token).ConfigureAwait(false);
            var client = _client;
            if (client == null) return Array.Empty<string>();

            var result = await Task.Run<string[]>(() =>
            {
                var folder = NormalizeRemoteFolder(remoteFolder);
                var listing = client.GetNameListing(folder) ?? Array.Empty<string>();
                return listing.Select(p => Path.GetFileName(p.TrimEnd('/'))).Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
            }, token).ConfigureAwait(false);

            sw.Stop();
            Logger?.Invoke($"ListFiles({remoteFolder}) returned {result.Length} entries in {sw.ElapsedMilliseconds}ms", FTP_Tool.LogLevel.Debug);
            return result;
        }

        /// <summary>
        /// Lists entries in the specified remote folder with file/directory information
        /// </summary>
        /// <returns>Array of tuples (Name, IsDirectory)</returns>
        public async Task<(string Name, bool IsDirectory)[]> ListEntriesAsync(string host, int port, NetworkCredential creds, string remoteFolder, CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            await EnsureConnectedAsync(host, port, creds, token).ConfigureAwait(false);
            var client = _client;
            if (client == null) return Array.Empty<(string, bool)>();

            var result = await Task.Run<(string, bool)[]>(() =>
            {
                var folder = NormalizeRemoteFolder(remoteFolder);
                try
                {
                    // Try to get detailed listing (FluentFTP GetListing)
                    var listing = client.GetListing(folder);
                    if (listing != null)
                    {
                        return listing
                            .Where(item => !string.IsNullOrWhiteSpace(item.Name) && item.Name != "." && item.Name != "..")
                            .Select(item => (item.Name, item.Type == FtpObjectType.Directory))
                            .ToArray();
                    }
                }
                catch (Exception ex)
                {
                    Logger?.Invoke($"GetListing failed for {folder}, falling back to name listing: {ex.Message}", FTP_Tool.LogLevel.Debug);
                }

                // Fallback: use name listing and guess based on extension
                try
                {
                    var nameListing = client.GetNameListing(folder) ?? Array.Empty<string>();
                    return nameListing
                        .Select(p => Path.GetFileName(p.TrimEnd('/')))
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(name =>
                        {
                            // Guess: if no extension, likely a directory
                            bool likelyDir = !name.Contains('.');
                            return (name, likelyDir);
                        })
                        .ToArray();
                }
                catch (Exception ex)
                {
                    Logger?.Invoke($"Name listing also failed for {folder}: {ex.Message}", FTP_Tool.LogLevel.Warning);
                    return Array.Empty<(string, bool)>();
                }
            }, token).ConfigureAwait(false);

            sw.Stop();
            var files = result.Count(e => !e.Item2);
            var dirs = result.Count(e => e.Item2);
            Logger?.Invoke($"ListEntries({remoteFolder}) returned {result.Length} entries ({files} files, {dirs} directories) in {sw.ElapsedMilliseconds}ms", FTP_Tool.LogLevel.Debug);
            return result;
        }

        /// <summary>
        /// Gets the size of a remote file
        /// </summary>
        /// <returns>File size in bytes, or null if unable to determine</returns>
        public async Task<long?> GetFileSizeAsync(string host, int port, NetworkCredential creds, string remotePath, CancellationToken token)
        {
            await EnsureConnectedAsync(host, port, creds, token).ConfigureAwait(false);
            var client = _client;
            if (client == null) return null;

            return await Task.Run<long?>(() =>
            {
                try
                {
                    return client.GetFileSize(remotePath);
                }
                catch (Exception ex)
                {
                    Logger?.Invoke($"GetFileSize failed for {remotePath}: {ex.Message}", FTP_Tool.LogLevel.Debug);
                    return null;
                }
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks if a remote directory exists
        /// </summary>
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
                catch (Exception ex)
                {
                    Logger?.Invoke($"DirectoryExists check failed for {folder}, trying alternate method: {ex.Message}", FTP_Tool.LogLevel.Debug);
                    try
                    {
                        var listing = client.GetNameListing(folder);
                        return listing != null && listing.Length >= 0; // Empty directory returns empty array
                    }
                    catch (Exception ex2)
                    {
                        Logger?.Invoke($"Alternate DirectoryExists check also failed for {folder}: {ex2.Message}", FTP_Tool.LogLevel.Debug);
                        return false;
                    }
                }
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Downloads a file from the FTP server
        /// </summary>
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
                string? tempPath = null;

                try
                {
                    var folder = NormalizeRemoteFolder(remoteFolder);
                    var remotePath = (folder == "/") ? $"/{fileName}" : $"{folder}/{fileName}";

                    long? remoteSize = null;
                    try
                    {
                        remoteSize = client.GetFileSize(remotePath);
                    }
                    catch (Exception ex)
                    {
                        Logger?.Invoke($"Could not get remote size for {fileName}: {ex.Message}", FTP_Tool.LogLevel.Debug);
                    }

                    var finalPath = Path.Combine(localFolder, fileName);

                    // Skip if file already exists with matching size
                    if (File.Exists(finalPath) && remoteSize.HasValue)
                    {
                        try
                        {
                            var localSize = new FileInfo(finalPath).Length;
                            if (localSize == remoteSize.Value)
                            {
                                overallSw.Stop();
                                Logger?.Invoke($"Skipping download for {fileName}: already exists (size match). duration={overallSw.ElapsedMilliseconds}ms", FTP_Tool.LogLevel.Debug);
                                return (true, finalPath, remotePath, false, null, true);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger?.Invoke($"Could not check local file size for {fileName}: {ex.Message}", FTP_Tool.LogLevel.Debug);
                        }
                    }

                    tempPath = finalPath + ".part";
                    try
                    {
                        if (File.Exists(tempPath)) File.Delete(tempPath);
                    }
                    catch (Exception ex)
                    {
                        Logger?.Invoke($"Could not delete existing temp file {tempPath}: {ex.Message}", FTP_Tool.LogLevel.Debug);
                    }

                    int attempts = 0;
                    Exception? lastEx = null;

                    for (int attempt = 1; attempt <= _maxAttempts && !token.IsCancellationRequested; attempt++)
                    {
                        attempts = attempt;
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
                                    catch (Exception delEx)
                                    {
                                        Logger?.Invoke($"Could not delete remote file {remotePath}: {delEx.Message}", FTP_Tool.LogLevel.Warning);
                                    }
                                }

                                overallSw.Stop();
                                Logger?.Invoke($"Downloaded {fileName} ({remoteSize ?? 0} bytes) in {overallSw.ElapsedMilliseconds}ms attempts={attempts} deleted={deleted}", FTP_Tool.LogLevel.Info);
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

                        // Small backoff before retry (check cancellation)
                        if (attempt < _maxAttempts && !token.IsCancellationRequested)
                        {
                            try { Thread.Sleep(500); } catch { }
                        }
                    }

                    overallSw.Stop();

                    if (token.IsCancellationRequested)
                    {
                        Logger?.Invoke($"Download cancelled for {fileName} after {attempts} attempt(s)", FTP_Tool.LogLevel.Warning);
                        return (false, null, null, false, "Operation cancelled", false);
                    }

                    Logger?.Invoke($"Failed to download {fileName} after {attempts} attempt(s): {lastEx?.Message} duration={overallSw.ElapsedMilliseconds}ms", FTP_Tool.LogLevel.Error);
                    return (false, null, null, false, lastEx?.Message ?? "Failed to download", false);
                }
                catch (Exception ex)
                {
                    overallSw.Stop();
                    Logger?.Invoke($"Download error for {fileName}: {ex.Message} duration={overallSw.ElapsedMilliseconds}ms", FTP_Tool.LogLevel.Error);
                    return (false, null, null, false, ex.Message, false);
                }
                finally
                {
                    // Cleanup temp file on failure
                    if (tempPath != null)
                    {
                        try
                        {
                            if (File.Exists(tempPath))
                                File.Delete(tempPath);
                        }
                        catch (Exception ex)
                        {
                            Logger?.Invoke($"Could not cleanup temp file {tempPath}: {ex.Message}", FTP_Tool.LogLevel.Debug);
                        }
                    }
                }
            }, token).ConfigureAwait(false);
        }

        #region Private Helper Methods

        private static string NormalizeRemoteFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || folder == "/") return "/";
            folder = folder.Trim().Replace('\\', '/'); // Handle Windows-style paths
            if (!folder.StartsWith("/")) folder = "/" + folder;
            if (folder.EndsWith("/") && folder.Length > 1) folder = folder.TrimEnd('/');
            return folder;
        }

        private void DisconnectAndDisposeClient()
        {
            if (_client != null)
            {
                try { _client.Disconnect(); }
                catch (Exception ex)
                {
                    Logger?.Invoke($"Error disconnecting client: {ex.Message}", FTP_Tool.LogLevel.Debug);
                }
                try { _client.Dispose(); }
                catch (Exception ex)
                {
                    Logger?.Invoke($"Error disposing client: {ex.Message}", FTP_Tool.LogLevel.Debug);
                }
                _client = null;
            }
        }

        private void OnValidateCertificate(object? control, FluentFTP.FtpSslValidationEventArgs e)
        {
            if (!_validateServerCertificate)
            {
                e.Accept = true;
            }
            // If _validateServerCertificate is true, let FluentFTP's default validation occur
        }

        private async Task ConnectWithRetriesAsync(FtpClient client, CancellationToken token)
        {
            int attempts = 0;
            Exception? lastEx = null;
            var sw = Stopwatch.StartNew();

            for (int attempt = 1; attempt <= _maxAttempts && !token.IsCancellationRequested; attempt++)
            {
                attempts = attempt;
                try
                {
                    // Try to use async connect if available
                    var clientType = client.GetType();
                    var connectAsyncMethod = clientType.GetMethod("ConnectAsync", new[] { typeof(CancellationToken) });

                    if (connectAsyncMethod != null)
                    {
                        var task = (Task?)connectAsyncMethod.Invoke(client, new object[] { token });
                        if (task != null) await task.ConfigureAwait(false);
                    }
                    else
                    {
                        // Fallback to synchronous Connect
                        await Task.Run(() => client.Connect(), token).ConfigureAwait(false);
                    }

                    if (client.IsConnected)
                    {
                        sw.Stop();
                        Logger?.Invoke($"Connected to {_host}:{_port} in {sw.ElapsedMilliseconds}ms after {attempts} attempt(s)", FTP_Tool.LogLevel.Info);
                        return;
                    }
                }
                catch (FluentFTP.FtpAuthenticationException fae)
                {
                    Logger?.Invoke($"Authentication failed for {_host}:{_port}: {fae.Message}", FTP_Tool.LogLevel.Error);
                    throw new InvalidOperationException($"Authentication failed: {fae.Message}", fae);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    Logger?.Invoke($"Connect attempt {attempts} failed: {ex.Message}", FTP_Tool.LogLevel.Warning);

                    // Delay before retry (can be interrupted by token)
                    if (attempt < _maxAttempts)
                    {
                        try { await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { throw; }
                    }
                }
            }

            sw.Stop();
            Logger?.Invoke($"Failed to connect to {_host}:{_port} after {attempts} attempt(s): {lastEx?.Message}", FTP_Tool.LogLevel.Error);
            throw new InvalidOperationException($"Failed to connect to FTP server after {attempts} attempts", lastEx);
        }

        private static void InitializeReflectionCache()
        {
            if (_reflectionInitialized) return;

            lock (_reflectionLock)
            {
                if (_reflectionInitialized) return;

                try
                {
                    var ftpClientType = typeof(FtpClient);

                    // Cache timeout properties
                    var timeoutPropNames = new[] { "ConnectTimeout", "ReadTimeout", "DataConnectionConnectTimeout", "DataConnectionReadTimeout" };
                    _timeoutProperties = timeoutPropNames
                        .Select(name => ftpClientType.GetProperty(name))
                        .Where(prop => prop != null && prop.CanWrite && prop.PropertyType == typeof(int))
                        .ToArray()!;

                    // Cache DataConnectionType property
                    _dataConnectionTypeProperty = ftpClientType.GetProperty("DataConnectionType");
                    if (_dataConnectionTypeProperty != null && _dataConnectionTypeProperty.PropertyType.IsEnum)
                    {
                        _dataConnectionEnumType = _dataConnectionTypeProperty.PropertyType;
                    }
                }
                catch
                {
                    // Reflection failed - not critical, just means timeouts won't be set
                }

                _reflectionInitialized = true;
            }
        }

        private void ApplyTimeoutSettings(FtpClient client)
        {
            if (_timeoutProperties == null || _timeoutProperties.Length == 0) return;

            var timeoutMs = _connectionTimeoutSeconds * 1000;
            foreach (var prop in _timeoutProperties)
            {
                try
                {
                    prop.SetValue(client, timeoutMs);
                }
                catch (Exception ex)
                {
                    Logger?.Invoke($"Could not set timeout property {prop.Name}: {ex.Message}", FTP_Tool.LogLevel.Debug);
                }
            }
        }

        private void ApplyDataConnectionType(FtpClient client)
        {
            if (_dataConnectionTypeProperty == null || _dataConnectionEnumType == null) return;

            try
            {
                var name = _usePassive ? "PASV" : "PORT";
                try
                {
                    var enumVal = Enum.Parse(_dataConnectionEnumType, name, ignoreCase: true);
                    _dataConnectionTypeProperty.SetValue(client, enumVal);
                    return;
                }
                catch
                {
                    // Try alternate names
                    name = _usePassive ? "Passive" : "Active";
                    var enumVal = Enum.Parse(_dataConnectionEnumType, name, ignoreCase: true);
                    _dataConnectionTypeProperty.SetValue(client, enumVal);
                }
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"Could not set DataConnectionType: {ex.Message}", FTP_Tool.LogLevel.Debug);
            }
        }

        #endregion

        #region IDisposable / IAsyncDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisconnectAndDisposeClient();
            try { _lock.Dispose(); } catch { }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                DisconnectAndDisposeClient();
            }
            finally
            {
                _lock.Release();
                _lock.Dispose();
            }
        }

        #endregion
    }
}