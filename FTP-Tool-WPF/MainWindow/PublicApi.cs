using System.Windows;

namespace FTP_Tool
{
    public partial class MainWindow
    {
        // Called from App to request application shutdown in fatal situations
        public void RequestShutdown(string message)
        {
            try
            {
                StopMonitoring("Shutting down due to fatal error");
            }
            catch { }

            try
            {
                System.Windows.MessageBox.Show(message, "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }

            try { System.Windows.Application.Current.Shutdown(); } catch { }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            try
            {
                // Do not hide to tray on minimize. Closing behavior (hide to tray when closing) is handled in Window_Closing.
                // This prevents accidental minimize from sending the app to the tray.
            }
            catch { }
        }
    }
}
