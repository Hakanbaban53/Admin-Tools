using FTP_Tool.Models;
using System.IO;
using System.Text.Json;

namespace FTP_Tool.Services
{
    public class SettingsService
    {
        private readonly string _path;

        public SettingsService(string filePath)
        {
            _path = filePath;
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        public async Task<AppSettings> LoadAsync()
        {
            if (!File.Exists(_path)) return new AppSettings();
            try
            {
                var s = await File.ReadAllTextAsync(_path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<AppSettings>(s, opts) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public async Task SaveAsync(AppSettings settings)
        {
            var s = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_path, s);
        }
    }
}