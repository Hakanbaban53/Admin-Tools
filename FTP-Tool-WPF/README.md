# FTP Monitor - Automated File Transfer Tool

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Platform-Windows-0078D4?style=flat-square)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-GPL-green?style=flat-square)](LICENSE)

A powerful, user-friendly Windows desktop application for monitoring and automatically downloading files from FTP servers. Perfect for automated file transfer workflows between Linux servers and Windows machines.

## ✨ Features

### 🚀 Core Functionality
- **Automated Monitoring**: Continuously monitor FTP servers at configurable intervals
- **Smart Downloads**: Automatically download new files as they appear
- **File Management**: Option to delete files from server after successful download
- **Duplicate Handling**: Skip existing files to avoid redundant downloads
- **Real-time Activity Log**: View all operations with color-coded log levels

### 📧 Advanced Alert System
- **Email Notifications**: Configurable SMTP email alerts for various events
- **Alert Types**: 
  - Error alerts (sent immediately)
  - Warning alerts (sent immediately)
  - Info summaries (batched at configurable intervals)
  - Download/no-activity alerts
- **Flexible Scheduling**:
  - 24/7 monitoring or scheduled hours
  - Multi-shift support (overnight shifts)
  - Weekday selection
  - Work hours and break time configuration
  - Excluded time intervals
- **Background Alerts**: Optional alerts even when monitoring is not running

### ⚙️ Configuration Options
- **FTP Settings**:
  - Connection timeout and retry attempts
  - Passive/Active FTP mode
  - Secure credential storage (Windows Credential Manager)
- **Download Settings**:
  - Custom local download folder
  - Configurable check intervals
  - Post-download actions
- **Logging**:
  - File-based activity logging
  - Configurable log levels (Debug, Info, Warning, Error)
  - Log retention policies
  - UI log line limits

### 🖥️ User Interface
- **Modern Design**: Clean, intuitive WPF interface with Fluent Design elements
- **Responsive Layout**: Adapts to different window sizes
- **System Tray Support**: Minimize to tray for background operation
- **Multi-page Navigation**: Organized sections (Monitor, Alerts, Settings, About)
- **Real-time Status**: Live updates on monitoring status and statistics

### 🔧 System Integration
- **Auto-start**: Optional Windows startup integration
- **Startup Modes**: Open window or start minimized to tray
- **Tray Icon**: Quick access and status monitoring from system tray
- **Credential Management**: Secure storage of FTP and SMTP credentials

## 📋 Requirements

- **Operating System**: Windows 10 or later
- **.NET Runtime**: .NET 8.0 Desktop Runtime ([Download](https://dotnet.microsoft.com/download/dotnet/8.0))
- **FTP Server**: Access to an FTP server (passive mode recommended)
- **Email (Optional)**: SMTP server for email notifications

## 🚀 Installation

### Option 1: Using Release Binary (Recommended - But not releasing. Currently I use manual deploying.)
1. Download the latest release from the [Releases](https://github.com/Hakanbaban53/Admin-Tools/releases) page
2. Extract the ZIP file to your desired location
3. Run `FTP-Tool.exe`

### Option 2: Building from Source
1. Clone the repository:
   ```bash
   git clone https://github.com/Hakanbaban53/Admin-Tools.git
   cd Admin-Tools/FTP-Tool-WPF
   ```

2. Build the project:
   ```bash
   dotnet build -c Release
   ```

3. Run the application:
   ```bash
   dotnet run --project FTP-Tool.csproj
   ```

## 📖 Usage

### Quick Start

1. **Configure FTP Connection**:
   - Enter your FTP server address and port
   - Provide username and password
   - Specify the remote folder path
   - Click "Test" to verify connection (credentials are saved on success)

2. **Set Download Location**:
   - Choose a local folder for downloaded files
   - Set the check interval (in seconds)
   - Optionally enable "Delete files after download"

3. **Start Monitoring**:
   - Click "▶ Start Monitor" to begin
   - Monitor activity in the real-time log
   - View statistics in the sidebar

### Configuring Email Alerts

1. Navigate to the **Alerts** page
2. Configure SMTP settings:
   - SMTP server address and port
   - Enable SSL/TLS if required
   - Enter credentials and from address
   - Add recipient email addresses

3. Set up alert schedule:
   - Enable email alerts
   - Choose alert types (Errors, Warnings, Info)
   - Select active days of the week
   - Configure work hours and break times
   - Set alert threshold for no-activity notifications

4. Test your configuration:
   - Use "Test Email Connection" to verify SMTP settings
   - Use "Send Test Alert" to test end-to-end delivery

### Advanced Features

#### Multi-Shift Support
For 24-hour operations or multiple shifts:
1. Enable "Use multi-shift mode"
2. Add shift intervals in HH:mm-HH:mm format (e.g., "22:00-06:00" for overnight)
3. Add excluded intervals for breaks

#### Background Alerts
To receive alerts even when monitoring is stopped:
1. Enable "Send download/no-activity alerts"
2. Enable "Send alerts even when monitoring is not running"
3. Configure the alert threshold (minutes)

#### Auto-Start Configuration
To run automatically at Windows startup:
1. Go to **Settings** page
2. Enable "Start application with Windows"
3. Choose startup behavior:
   - Open window
   - Start minimized to tray
4. Optionally enable "Start monitoring automatically"

## 🔒 Security

- **Credential Storage**: FTP and SMTP credentials are securely stored in Windows Credential Manager
- **No Plain Text**: Passwords are never stored in configuration files
- **Local Processing**: All operations are performed locally on your machine
- **Secure Protocols**: Supports SSL/TLS for SMTP connections

## 📁 File Locations

- **Settings**: `%AppData%\FTP_Tool\settings.json`
- **Logs**: `%AppData%\FTP_Tool\logs\activity-YYYY-MM-DD.log`
- **Credentials**: Windows Credential Manager (secure vault)

## 🛠️ Technology Stack

- **Framework**: .NET 8.0 (WPF)
- **FTP Library**: FluentFTP 41.0.0
- **Email**: MailKit 3.5.0
- **Logging**: Serilog 3.0.1
- **Dependency Injection**: Microsoft.Extensions.Hosting 8.0.0
- **UI**: Windows Presentation Foundation (WPF) with custom styling

## 🐛 Troubleshooting

### Connection Issues
- Verify FTP server address and credentials
- Check if passive FTP mode is enabled
- Ensure firewall allows FTP connections
- Try increasing connection timeout in Settings

### Email Not Sending
- Test SMTP settings using "Test Email Connection"
- Verify SSL/TLS settings match your server
- Check if firewall blocks SMTP port
- Review activity log for error messages

### Monitoring Not Starting
- Verify all required fields are filled
- Check if local download folder exists and is writable
- Review the activity log for specific error messages

### Performance Issues
- Reduce check interval if monitoring too frequently
- Decrease max log lines in Settings
- Check FTP server response time

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📝 License

This project is licensed under the GPL-3 License - see the [LICENSE](LICENSE) file for details.

## 👤 Author

**Hakan İSMAİL**
- GitHub: [@Hakanbaban53](https://github.com/Hakanbaban53)

## 🙏 Acknowledgments

- FluentFTP team for the excellent FTP library
- MailKit team for robust email functionality
- Microsoft for the .NET platform

## 📧 Support

If you encounter any issues or have questions:
- Open an issue on [GitHub Issues](https://github.com/Hakanbaban53/Admin-Tools/issues)
- Check the troubleshooting section above
- Review the activity logs for detailed error messages

---

Made with ❤️ for automated file transfers
