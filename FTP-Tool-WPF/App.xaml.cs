using System;
using System.Configuration;
using System.Data;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using FTP_Tool.Services;
using FTP_Tool.Models;
using System.IO;

namespace FTP_Tool
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private IHost? _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Install global handlers as early as possible
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            base.OnStartup(e);

            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, cfg) =>
                {
                    cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                })
                .ConfigureServices((context, services) =>
                {
                    services.Configure<AppSettings>(context.Configuration.GetSection("AppSettings"));
                    services.AddSingleton<SettingsService>(sp =>
                    {
                        var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FTP_Tool", "settings.json");
                        return new SettingsService(path);
                    });

                    services.AddSingleton<FtpService>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            _host.Start();

            var main = _host.Services.GetRequiredService<MainWindow>();
            main.Show();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                HandleException(e.Exception, "DispatcherUnhandledException");
            }
            catch { }
            // prevent default crash dialog to allow graceful handling
            e.Handled = true;
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                HandleException(e.Exception, "UnobservedTaskException");
            }
            catch { }
            // Mark as observed so it doesn't terminate process
            try { e.SetObserved(); } catch { }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                HandleException(ex, "DomainUnhandledException");
            }
            catch { }
        }

        private void HandleException(Exception? ex, string source)
        {
            try
            {
                var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FTP_Tool");
                var logDir = Path.Combine(baseDir, "logs");
                try { Directory.CreateDirectory(logDir); } catch { }

                var fatalPath = Path.Combine(logDir, "fatal.log");
                var activityPath = Path.Combine(logDir, $"activity-{DateTime.Now:yyyy-MM-dd}.log");

                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex?.ToString() ?? "<no exception>"}{Environment.NewLine}";
                try { File.AppendAllText(fatalPath, msg); } catch { }
                try { File.AppendAllText(activityPath, msg); } catch { }

                // Try to get main window from host and request graceful shutdown
                try
                {
                    if (_host != null)
                    {
                        var main = _host.Services.GetService<MainWindow>();
                        if (main != null)
                        {
                            // Request shutdown on UI thread
                            main.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    main.RequestShutdown("A fatal error occurred. The application will close.\nSee logs for details.");
                                }
                                catch { }
                            }));
                            return;
                        }
                    }
                }
                catch { }

                // Fallback: show messagebox
                try
                {
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { System.Windows.MessageBox.Show($"A fatal error occurred and the application needs to close. See {fatalPath} for details.", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
                        try { System.Windows.Application.Current.Shutdown(); } catch { }
                    }));
                }
                catch { }
            }
            catch { }
        }
    }
}
