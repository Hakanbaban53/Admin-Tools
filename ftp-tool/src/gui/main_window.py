"""Main application window."""

import sys
import os
import platform
import subprocess
from datetime import datetime

from PySide6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QTabWidget, QLabel, QPushButton, QMessageBox, QSystemTrayIcon, QMenu
)
from PySide6.QtCore import QThread, QTimer
from PySide6.QtGui import QAction, QIcon, QPixmap, QPainter, QColor, QBrush, QPen
from ftplib import FTP
import smtplib
from email.mime.text import MIMEText

from ..config.constants import APP_NAME, APP_DESCRIPTION
from ..config.themes import ThemeType
from ..core.ftp_worker import FtpWorker
from ..utils.settings_manager import SettingsManager
from ..utils.theme_manager import ThemeManager
from ..utils.logger import (
    get_logger, configure_logging, log_app_start, log_app_stop,
    set_gui_callback, log_monitoring_event, log_connection_event, log_error_details,
    get_current_log_file
)
from .ftp_settings_tab import FTPSettingsTab
from .email_settings_tab import EmailSettingsTab
from .application_settings_tab import ApplicationSettingsTab


class FTPDownloaderApp(QMainWindow):
    """Main FTP downloader application window."""

    def __init__(self):
        super().__init__()
        self.setWindowTitle(APP_DESCRIPTION)
        self.setGeometry(100, 100, 900, 800)

        # Core components
        self.settings_manager = SettingsManager()
        self.theme_manager = ThemeManager(self.settings_manager)
        self.worker_thread = None
        self.ftp_worker = None
        
        # Initialize logger
        self.logger = get_logger()
        
        # Setup UI
        self._create_widgets()
        
        # Connect GUI logging callback
        set_gui_callback(self._on_log_message)
        
        # Track dirty settings
        self._dirty_settings = set()
        
        self._create_system_tray_icon()
        self._load_settings()

        # Apply theme
        self.theme_manager.apply_current_theme()
        
        # Configure logging with loaded settings
        app_settings = self.settings_manager.get_all_settings().get('Application', {})
        configure_logging(app_settings)
        
        # Log application start
        log_app_start()
        self.logger.info("FTP Tool GUI initialized")

        if app_settings.get('AutoStartMonitoring', False):
            try:
                self.start_monitor()
            except Exception:
                self.logger.exception("Failed to auto-start monitoring")

    def _create_widgets(self):
        """Create the main UI widgets."""
        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        main_layout = QVBoxLayout(central_widget)

        # Create tabs
        self.tabs = QTabWidget()

        # FTP Settings Tab
        self.ftp_tab = FTPSettingsTab()
        self.ftp_tab.setting_changed.connect(self._on_setting_changed)
        self.ftp_tab.test_connection_requested.connect(self._test_ftp_connection)
        self.tabs.addTab(self.ftp_tab, "FTP Settings")

        # Email Settings Tab
        self.email_tab = EmailSettingsTab()
        self.email_tab.setting_changed.connect(self._on_setting_changed)
        self.email_tab.test_email_requested.connect(self._test_send_email)
        self.tabs.addTab(self.email_tab, "Email Settings")

        # Application Settings Tab
        self.app_tab = ApplicationSettingsTab()
        self.app_tab.setting_changed.connect(self._on_setting_changed)
        self.app_tab.apply_autostart_requested.connect(self._apply_autostart)
        self.app_tab.open_logs_folder_requested.connect(self._open_logs_folder)
        self.app_tab.clear_old_logs_requested.connect(self._clear_old_logs)
        self.tabs.addTab(self.app_tab, "Application Settings")

        main_layout.addWidget(self.tabs)

        # Control buttons and status
        self._create_control_panel(main_layout)

    def _create_control_panel(self, parent_layout):
        """Create the control panel with buttons and status."""
        control_layout = QVBoxLayout()

        # Start/Stop buttons
        button_layout = QHBoxLayout()

        self.start_button = QPushButton("Start Monitoring")
        self.start_button.setObjectName("StartButton")
        self.start_button.clicked.connect(self.start_monitor)

        self.stop_button = QPushButton("Stop Monitoring")
        self.stop_button.setObjectName("StopButton")
        self.stop_button.setEnabled(False)
        self.stop_button.clicked.connect(self.stop_monitor)

        self.save_button = QPushButton("Save Settings")
        self.save_button.setObjectName("SaveButton")
        self.save_button.clicked.connect(self._save_settings)

        self.theme_button = QPushButton("🌙 Dark")
        self.theme_button.clicked.connect(self._toggle_theme)
        self._update_theme_button()

        button_layout.addWidget(self.start_button)
        button_layout.addWidget(self.stop_button)
        button_layout.addWidget(self.save_button)
        button_layout.addWidget(self.theme_button)
        button_layout.addStretch()

        control_layout.addLayout(button_layout)

        # Status labels
        status_layout = QHBoxLayout()

        self.status_label = QLabel("Ready")
        self.download_count_label = QLabel("Downloaded: 0 files")

        status_layout.addWidget(QLabel("Status:"))
        status_layout.addWidget(self.status_label)
        status_layout.addStretch()
        status_layout.addWidget(self.download_count_label)

        control_layout.addLayout(status_layout)
        parent_layout.addLayout(control_layout)

    def _create_system_tray_icon(self):
        """Create system tray icon and menu."""
        if not QSystemTrayIcon.isSystemTrayAvailable():
            return

        self.tray_icon = QSystemTrayIcon(self)
        # Initialize generated icons and blink timer
        try:
            self._init_tray_icons()
        except Exception:
            # If icon generation fails, fall back to standard icons
            pass
        # Use a more compatible icon
        try:
            self.tray_icon.setIcon(self.style().standardIcon(self.style().StandardPixmap.SP_ComputerIcon))
        except AttributeError:
            # Fallback to a simple icon if SP_ComputerIcon is not available
            self.tray_icon.setIcon(self.style().standardIcon(self.style().StandardPixmap.SP_DriveHDIcon))
        # Create tray menu
        tray_menu = QMenu()

        # Status label (disabled QAction used as label)
        self.tray_status_action = QAction("Status: Idle", self)
        self.tray_status_action.setEnabled(False)
        tray_menu.addAction(self.tray_status_action)
        tray_menu.addSeparator()

        show_action = QAction("Show", self)
        show_action.triggered.connect(self.show)
        tray_menu.addAction(show_action)

        # Keep references so we can enable/disable them later
        self.tray_start_action = QAction("Start Monitoring", self)
        self.tray_start_action.triggered.connect(self.start_monitor)
        tray_menu.addAction(self.tray_start_action)

        self.tray_stop_action = QAction("Stop Monitoring", self)
        self.tray_stop_action.triggered.connect(self.stop_monitor)
        # Initially disabled because monitoring isn't running
        self.tray_stop_action.setEnabled(False)
        tray_menu.addAction(self.tray_stop_action)

        tray_menu.addSeparator()

        # Quick log access
        open_logs_action = QAction("Open Logs Folder", self)
        open_logs_action.triggered.connect(self._open_logs_folder)
        tray_menu.addAction(open_logs_action)

        open_current_log_action = QAction("Open Current Log File", self)
        open_current_log_action.triggered.connect(self._open_current_log_file)
        tray_menu.addAction(open_current_log_action)

        tray_menu.addSeparator()

        quit_action = QAction("Quit", self)
        quit_action.triggered.connect(self.quit_app)
        tray_menu.addAction(quit_action)

        self.tray_icon.setContextMenu(tray_menu)
        self.tray_icon.activated.connect(self._on_tray_activated)
        self.tray_icon.show()

        # Initialize tray status
        self._update_tray_status("Idle")

    def _open_current_log_file(self):
        """Open the current log file in the system's default viewer."""
        try:
            log_path = get_current_log_file()
            if os.path.exists(log_path):
                import platform, subprocess
                system = platform.system()
                if system == "Windows":
                    os.startfile(log_path)
                elif system == "Darwin":
                    subprocess.run(["open", log_path])
                else:
                    subprocess.run(["xdg-open", log_path])
            else:
                QMessageBox.information(self, "Open Log", "Current log file not found.")
        except Exception as e:
            QMessageBox.warning(self, "Open Log", f"Failed to open log file: {e}")

    def _update_tray_status(self, status: str):
        """Update the tray icon tooltip and status action text."""
        try:
            text = f"{APP_NAME} - {status}"
            # Tooltip
            if hasattr(self, 'tray_icon'):
                try:
                    self.tray_icon.setToolTip(text)
                except Exception:
                    pass

            # Status text in menu
            if hasattr(self, 'tray_status_action'):
                try:
                    self.tray_status_action.setText(f"Status: {status}")
                except Exception:
                    pass

            # Visual icon changes for quick recognition (use generated icons if available)
            if hasattr(self, 'tray_icon'):
                try:
                    st = status.lower() if isinstance(status, str) else ''

                    # Stop any blinking unless explicitly required
                    try:
                        if hasattr(self, '_tray_blink_timer'):
                            self._tray_blink_timer.stop()
                    except Exception:
                        pass

                    # Choose icon and behavior
                    if 'monitor' in st or 'running' in st:
                        # Start blinking between two monitoring icons for visibility
                        if hasattr(self, '_tray_icons') and 'monitor_on' in self._tray_icons:
                            # Ensure blink state starts
                            try:
                                self._tray_blink_state = False
                                if hasattr(self, '_tray_blink_timer'):
                                    self._tray_blink_timer.start()
                            except Exception:
                                pass
                        else:
                            # Fallback to a standard icon
                            try:
                                sp = self.style().standardIcon(self.style().StandardPixmap.SP_ComputerIcon)
                                self.tray_icon.setIcon(sp)
                            except Exception:
                                pass

                    elif 'error' in st or 'failed' in st:
                        # Show an error icon and a brief notification
                        try:
                            if hasattr(self, '_tray_icons') and 'error' in self._tray_icons:
                                self.tray_icon.setIcon(self._tray_icons['error'])
                            else:
                                sp = self.style().standardIcon(self.style().StandardPixmap.SP_MessageBoxCritical)
                                self.tray_icon.setIcon(sp)
                        except Exception:
                            pass
                        # Optionally pop up a notification for errors
                        try:
                            app_settings = self.app_tab.get_settings()
                            if app_settings.get('EnableStatusNotifications', False):
                                self.tray_icon.showMessage(APP_NAME, f"Error: {status}", QSystemTrayIcon.Critical, 4000)
                        except Exception:
                            pass

                    else:
                        # Idle/Stopped/default icon
                        try:
                            if hasattr(self, '_tray_icons') and 'idle' in self._tray_icons:
                                self.tray_icon.setIcon(self._tray_icons['idle'])
                            else:
                                sp = self.style().standardIcon(self.style().StandardPixmap.SP_DriveHDIcon)
                                self.tray_icon.setIcon(sp)
                        except Exception:
                            pass
                except Exception:
                    pass
        except Exception:
            pass

    def _init_tray_icons(self):
        """Generate small tray icons programmatically to avoid external assets.

        Creates: idle, monitor_on, monitor_off, error
        """
        # small square pixmap size appropriate for system tray
        size = 32
        icons = {}

        def make_pix(color: QColor, mark: str = None):
            pix = QPixmap(size, size)
            pix.fill(QColor(0, 0, 0, 0))
            p = QPainter(pix)
            p.setRenderHint(QPainter.Antialiasing)
            # background circle
            p.setBrush(QBrush(color))
            p.setPen(QPen(QColor(0, 0, 0, 0)))
            r = 4
            p.drawEllipse(r, r, size - 2 * r, size - 2 * r)
            # optional small mark (e.g., exclamation)
            if mark == '!':
                p.setPen(QPen(QColor('white')))
                p.setBrush(QBrush(QColor('white')))
                # draw small rectangle as exclamation base
                p.drawRect(size // 2 - 2, size // 4, 4, size // 2 - 6)
                p.drawRect(size // 2 - 2, size - size // 6 - 2, 4, 4)
            p.end()
            return QIcon(pix)

        # Idle: gray
        icons['idle'] = make_pix(QColor('#6c757d'))
        # Monitoring on: green
        icons['monitor_on'] = make_pix(QColor('#28a745'))
        # Monitoring off (alternate): light green / transparent
        icons['monitor_off'] = make_pix(QColor('#74d67a'))
        # Error: red with exclamation
        icons['error'] = make_pix(QColor('#dc3545'), mark='!')

        self._tray_icons = icons
        # Blink timer to toggle monitoring icon
        self._tray_blink_timer = QTimer(self)
        self._tray_blink_timer.setInterval(700)
        self._tray_blink_timer.timeout.connect(self._on_tray_blink)
        self._tray_blink_state = False

    def _on_tray_blink(self):
        """Toggle tray icon between monitor_on and monitor_off icons."""
        try:
            if not hasattr(self, '_tray_icons'):
                return
            self._tray_blink_state = not getattr(self, '_tray_blink_state', False)
            icon_key = 'monitor_on' if self._tray_blink_state else 'monitor_off'
            icon = self._tray_icons.get(icon_key)
            if icon:
                try:
                    self.tray_icon.setIcon(icon)
                except Exception:
                    pass
        except Exception:
            pass

    def _on_tray_activated(self, reason):
        """Handle tray icon activation."""
        if reason == QSystemTrayIcon.DoubleClick:
            self.show()
            self.raise_()
            self.activateWindow()

    def _on_setting_changed(self, key: str, value):
        """Handle setting changes."""
        self._dirty_settings.add(key)
        # Auto-save or mark as dirty for manual save
    
    def _on_log_message(self, message: str):
        """Handle log messages from the logger and display in FTP tab."""
        if hasattr(self, 'ftp_tab'):
            self.ftp_tab.log_message(message)

    def _load_settings(self):
        """Load settings from file and populate UI."""
        all_settings = self.settings_manager.get_all_settings()

        # Load FTP settings
        ftp_settings = all_settings.get('FTPConnection', {})
        self.ftp_tab.set_settings(ftp_settings)

        # Load email settings
        email_settings = all_settings.get('EmailSettings', {})
        schedule_settings = all_settings.get('WorkSchedule', {})
        email_settings.update(schedule_settings)
        self.email_tab.set_settings(email_settings)

        # Load application settings
        app_settings = all_settings.get('Application', {})
        self.app_tab.set_settings(app_settings)

        # Clear dirty flags after loading
        self._dirty_settings.clear()

    def _save_settings(self):
        """Save current settings to file."""
        try:
            # Collect settings from all tabs
            all_settings = {
                'FTPConnection': self.ftp_tab.get_settings(),
                'EmailSettings': {},
                'WorkSchedule': {},
                'Application': self.app_tab.get_settings()
            }

            # Split email tab settings between EmailSettings and WorkSchedule
            email_settings = self.email_tab.get_settings()
            email_keys = ['smtp_host', 'smtp_port', 'smtp_user', 'smtp_pass', 'smtp_ssl',
                         'email_from', 'email_to', 'notify_threshold']
            schedule_keys = ['work_start', 'work_end', 'lunch_start', 'lunch_end', 'weekdays']

            for key, value in email_settings.items():
                if key in email_keys:
                    all_settings['EmailSettings'][key] = value
                elif key in schedule_keys:
                    all_settings['WorkSchedule'][key] = value

            # Save to file
            if self.settings_manager.save_settings(all_settings):
                self._dirty_settings.clear()
                
                # Reconfigure logging with new settings
                configure_logging(all_settings['Application'])
                self.logger.info("Settings saved successfully")
                
                QMessageBox.information(self, "Settings", "Settings saved successfully!")
            else:
                self.logger.error("Failed to save settings")
                QMessageBox.warning(self, "Settings", "Failed to save settings!")

        except Exception as e:
            self.logger.error(f"Error saving settings: {e}")
            QMessageBox.critical(self, "Error", f"Error saving settings: {e}")

    def _test_ftp_connection(self):
        """Test FTP connection with current settings."""
        try:
            settings = self.ftp_tab.get_settings()

            if not all([settings.get('ftp_host'), settings.get('ftp_user')]):
                QMessageBox.warning(self, "Test Connection", "Please fill in host and username first.")
                return

            host = settings['ftp_host']
            log_connection_event("TEST_START", host, "INITIATED")

            with FTP() as ftp:
                ftp.connect(settings['ftp_host'], int(settings.get('ftp_port', 21)), timeout=10)
                log_connection_event("CONNECT", host, "SUCCESS")
                
                ftp.login(settings['ftp_user'], settings.get('ftp_pass', ''))
                log_connection_event("LOGIN", host, "SUCCESS")
                
                ftp.set_pasv(settings.get('passive_mode', True))

                # Test remote path if provided
                if settings.get('remote_path'):
                    ftp.cwd(settings['remote_path'])
                    log_connection_event("CHANGE_DIR", f"{host}:{settings['remote_path']}", "SUCCESS")

                log_connection_event("TEST_COMPLETE", host, "SUCCESS")
                QMessageBox.information(self, "Test Connection", "FTP connection successful!")

        except Exception as e:
            error_msg = f"FTP connection failed: {e}"
            log_error_details("FTP_CONNECTION_TEST", str(e), f"Host: {settings.get('ftp_host', 'unknown')}")
            QMessageBox.warning(self, "Test Connection", error_msg)

    def _test_send_email(self):
        """Test email sending with current settings."""
        try:
            settings = self.email_tab.get_settings()

            if not all([settings.get('smtp_host'), settings.get('email_from'), settings.get('email_to')]):
                QMessageBox.warning(self, "Test Email", "Please fill in SMTP host, from address, and recipients first.")
                return

            subject = "FTP Monitor Test Email"
            body = f"This is a test email sent from FTP Monitor at {datetime.now()}"

            self._send_email(settings, subject, body)
            QMessageBox.information(self, "Test Email", "Test email sent successfully!")

        except Exception as e:
            QMessageBox.warning(self, "Test Email", f"Failed to send test email: {e}")

    def _send_email(self, settings: dict, subject: str, body: str):
        """Send an email with the given settings."""
        host = settings.get('smtp_host')
        port = int(settings.get('smtp_port', 587))
        user = settings.get('smtp_user')
        pwd = settings.get('smtp_pass')
        use_ssl = bool(settings.get('smtp_ssl', False))
        from_addr = settings.get('email_from')
        to_addrs = [a.strip() for a in (settings.get('email_to') or '').split(';') if a.strip()]

        if not host or not from_addr or not to_addrs:
            raise RuntimeError('Email settings incomplete')

        msg = MIMEText(body)
        msg['Subject'] = subject
        msg['From'] = from_addr
        msg['To'] = ', '.join(to_addrs)

        if use_ssl:
            server = smtplib.SMTP_SSL(host, port, timeout=20)
        else:
            server = smtplib.SMTP(host, port, timeout=20)

        server.ehlo()
        if not use_ssl:
            try:
                server.starttls()
            except Exception:
                pass

        if user:
            server.login(user, pwd)

        server.sendmail(from_addr, to_addrs, msg.as_string())
        server.quit()

    def _apply_autostart(self):
        """Apply autostart settings using Windows Registry."""
        import platform
        from pathlib import Path

        try:
            app_settings = self.app_tab.get_settings()
            enabled = bool(app_settings.get('StartWithWindows', False))

            system = platform.system()

            # Resolve target executable and script for autostart
            main_script = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', 'main.py'))
            
            if getattr(sys, 'frozen', False):
                # Running as compiled executable
                target_executable = sys.executable
                args_str = '--tray'
                display_name = "FTP Monitor"
            else:
                # Running as Python script
                target_executable = sys.executable or 'python'
                args_str = f'"{main_script}" --tray'
                display_name = "FTP Monitor (Dev)"

            if system == 'Windows':
                self._apply_registry_autostart(enabled, target_executable, args_str, display_name)

            elif system == 'Linux':
                # Use XDG autostart .desktop entry
                autostart_dir = Path.home() / '.config' / 'autostart'
                autostart_dir.mkdir(parents=True, exist_ok=True)
                desktop_path = autostart_dir / 'ftp-monitor.desktop'

                if enabled:
                    exec_cmd = f"{target_executable} '{main_script}' --tray"
                    content = (
                        "[Desktop Entry]\n"
                        "Type=Application\n"
                        f"Name=FTP Monitor\n"
                        f"Exec={exec_cmd}\n"
                        "X-GNOME-Autostart-enabled=true\n"
                    )
                    try:
                        desktop_path.write_text(content, encoding='utf-8')
                        QMessageBox.information(self, 'Autostart', 'Autostart entry created.')
                        self.logger.info('Autostart .desktop created')
                    except Exception as e:
                        self.logger.exception('Failed to create autostart .desktop')
                        QMessageBox.warning(self, 'Autostart', f'Failed to create autostart entry: {e}')
                else:
                    try:
                        if desktop_path.exists():
                            desktop_path.unlink()
                            QMessageBox.information(self, 'Autostart', 'Autostart entry removed.')
                            self.logger.info('Autostart .desktop removed')
                        else:
                            QMessageBox.information(self, 'Autostart', 'No autostart entry found to remove.')
                    except Exception as e:
                        self.logger.exception('Failed to remove autostart .desktop')
                        QMessageBox.warning(self, 'Autostart', f'Failed to remove autostart entry: {e}')

            elif system == 'Darwin':
                # macOS: create/remove a LaunchAgent plist in ~/Library/LaunchAgents
                launch_dir = Path.home() / 'Library' / 'LaunchAgents'
                launch_dir.mkdir(parents=True, exist_ok=True)
                plist_path = launch_dir / 'com.ftpmonitor.startup.plist'

                if enabled:
                    plist = f"""<?xml version='1.0' encoding='UTF-8'?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>Label</key>
    <string>com.ftpmonitor.startup</string>
    <key>ProgramArguments</key>
    <array>
      <string>{target_executable}</string>
      <string>{main_script}</string>
      <string>--tray</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
  </dict>
</plist>
"""
                    try:
                        plist_path.write_text(plist, encoding='utf-8')
                        QMessageBox.information(self, 'Autostart', 'LaunchAgent created. You may need to load it with launchctl load.')
                        self.logger.info('LaunchAgent plist created')
                    except Exception as e:
                        self.logger.exception('Failed to create LaunchAgent plist')
                        QMessageBox.warning(self, 'Autostart', f'Failed to create LaunchAgent plist: {e}')
                else:
                    try:
                        if plist_path.exists():
                            plist_path.unlink()
                            QMessageBox.information(self, 'Autostart', 'LaunchAgent removed.')
                            self.logger.info('LaunchAgent plist removed')
                        else:
                            QMessageBox.information(self, 'Autostart', 'No LaunchAgent found to remove.')
                    except Exception as e:
                        self.logger.exception('Failed to remove LaunchAgent plist')
                        QMessageBox.warning(self, 'Autostart', f'Failed to remove LaunchAgent plist: {e}')

            else:
                QMessageBox.information(self, 'Autostart', f'Autostart not implemented for platform: {system}')

        except Exception as e:
            self.logger.exception('Failed to apply autostart')
            QMessageBox.warning(self, 'Autostart', f'Failed to apply autostart: {e}')

    def _apply_registry_autostart(self, enabled: bool, target_executable: str, args_str: str, display_name: str):
        """Apply autostart using Windows Registry (Run key)."""
        import winreg
        
        try:
            # Open the Run key in HKEY_CURRENT_USER
            key_path = r"Software\Microsoft\Windows\CurrentVersion\Run"
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, key_path, 0, winreg.KEY_ALL_ACCESS) as key:
                if enabled:
                    # Create registry entry
                    if args_str:
                        command = f'"{target_executable}" {args_str}'
                    else:
                        command = f'"{target_executable}"'
                    
                    winreg.SetValueEx(key, display_name, 0, winreg.REG_SZ, command)
                    self.logger.info(f'Registry autostart entry created: {command}')
                    QMessageBox.information(self, 'Autostart', 'Registry startup entry created successfully!')
                else:
                    # Remove registry entry
                    try:
                        winreg.DeleteValue(key, display_name)
                        self.logger.info('Registry autostart entry removed')
                        QMessageBox.information(self, 'Autostart', 'Registry startup entry removed successfully!')
                    except FileNotFoundError:
                        QMessageBox.information(self, 'Autostart', 'No registry startup entry found to remove.')
                        
        except Exception as e:
            self.logger.exception('Failed to modify registry autostart')
            QMessageBox.warning(self, 'Autostart', f'Failed to modify registry startup: {e}')

    def _open_logs_folder(self):
        """Open the logs folder in the system file manager."""
        import os
        import subprocess
        import platform
        
        try:
            # Create logs directory if it doesn't exist
            logs_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'logs')
            logs_dir = os.path.abspath(logs_dir)
            os.makedirs(logs_dir, exist_ok=True)
            
            # Open folder based on operating system
            system = platform.system()
            if system == "Windows":
                os.startfile(logs_dir)
            elif system == "Darwin":  # macOS
                subprocess.run(["open", logs_dir])
            else:  # Linux and others
                subprocess.run(["xdg-open", logs_dir])
        except Exception as e:
            QMessageBox.warning(self, "Open Logs Folder", f"Failed to open logs folder: {e}")

    def _clear_old_logs(self):
        """Clear old log files based on the keep logs setting."""
        import os
        import glob
        from datetime import datetime, timedelta
        
        try:
            # Get the keep logs days setting
            app_settings = self.app_tab.get_settings()
            keep_days = int(app_settings.get('KeepLogsDays', 30))
            
            # Calculate cutoff date
            cutoff_date = datetime.now() - timedelta(days=keep_days)
            
            # Find logs directory
            logs_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'logs')
            logs_dir = os.path.abspath(logs_dir)
            
            if not os.path.exists(logs_dir):
                QMessageBox.information(self, "Clear Logs", "No logs directory found.")
                return
            
            # Find and delete old log files
            log_files = glob.glob(os.path.join(logs_dir, "*.log*"))
            deleted_count = 0
            
            for log_file in log_files:
                try:
                    # Protect the active log file from accidental deletion
                    try:
                        current_log = os.path.abspath(__import__('..utils.logger', fromlist=['get_current_log_file']).get_current_log_file())
                    except Exception:
                        current_log = None

                    file_time = datetime.fromtimestamp(os.path.getmtime(log_file))
                    if file_time < cutoff_date and os.path.abspath(log_file) != current_log:
                        os.remove(log_file)
                        deleted_count += 1
                except Exception as e:
                    print(f"Error deleting {log_file}: {e}")
            
            if deleted_count > 0:
                QMessageBox.information(self, "Clear Logs", f"Deleted {deleted_count} old log files.")
            else:
                QMessageBox.information(self, "Clear Logs", "No old log files found to delete.")
                
        except Exception as e:
            QMessageBox.warning(self, "Clear Old Logs", f"Failed to clear old logs: {e}")

    def _is_thread_running(self) -> bool:
        """Safely check if worker thread is running."""
        try:
            return self.worker_thread and self.worker_thread.isRunning()
        except RuntimeError:
            # Thread object was deleted
            return False

    def start_monitor(self):
        """Start the FTP monitoring."""
        self.logger.info("Starting FTP monitoring...")
        log_monitoring_event("MONITORING_START", "FTP monitoring service initiated")

        # Check if already running
        if self._is_thread_running():
            log_monitoring_event("MONITORING_ALREADY_RUNNING", "Monitor already active")
            return

        # Update button states
        self.start_button.setEnabled(False)
        self.stop_button.setEnabled(True)

        # Collect settings from all tabs
        ftp_settings = self.ftp_tab.get_settings()
        email_settings = self.email_tab.get_settings()
        app_settings = self.app_tab.get_settings()
        
        # Log configuration details
        host = ftp_settings.get('ftp_host', 'unknown')
        interval = ftp_settings.get('interval', 60)
        log_monitoring_event("CONFIG_LOADED", f"Host: {host}, Interval: {interval}s")

        # Combine all settings for worker
        current_settings = {}
        current_settings.update(ftp_settings)
        current_settings.update(email_settings)
        current_settings.update(app_settings)

        # Create worker thread
        self.worker_thread = QThread()
        self.ftp_worker = FtpWorker(current_settings)
        self.ftp_worker.moveToThread(self.worker_thread)

        # Connect signals
        self.ftp_worker.log_message.connect(self.ftp_tab.log_message)
        self.ftp_worker.status_update.connect(self.status_label.setText)
        self.ftp_worker.status_update.connect(self._on_status_update)
        self.ftp_worker.download_count_update.connect(
            lambda count: self.download_count_label.setText(f"Downloaded: {count} files")
        )

        self.worker_thread.started.connect(self.ftp_worker.run)
        # Ensure we quit the thread when worker finishes and clean up references
        self.ftp_worker.finished.connect(self.worker_thread.quit)
        self.ftp_worker.finished.connect(self.ftp_worker.deleteLater)
        # When thread finishes, clear object references to avoid using deleted C++ objects
        self.worker_thread.finished.connect(self._on_thread_finished)
        # Keep a safe deleteLater on the thread as well
        self.worker_thread.finished.connect(self.worker_thread.deleteLater)

        # Start the worker
        self.worker_thread.start()
        log_monitoring_event("MONITORING_STARTED", "Worker thread started successfully")
        # Update tray menu and status
        try:
            if hasattr(self, 'tray_start_action'):
                self.tray_start_action.setEnabled(False)
            if hasattr(self, 'tray_stop_action'):
                self.tray_stop_action.setEnabled(True)
            self._update_tray_status("Monitoring")
            self.status_label.setText("Monitoring")
        except Exception:
            pass

    def stop_monitor(self):
        """Stop the FTP monitoring."""
        if self.ftp_worker:
            self.logger.info("Stopping FTP monitoring...")
            log_monitoring_event("MONITORING_STOP_REQUESTED", "Stop signal sent to worker")
            try:
                self.ftp_worker.stop()
            except RuntimeError:
                # Worker or underlying C++ object may already be deleted; ignore
                log_error_details("STOP_MONITOR", "Worker object already deleted", "FTP worker cleanup")

        # Also request the thread to quit and wait for it to finish to avoid destroying running QThreads
        if self.worker_thread:
            try:
                if self.worker_thread.isRunning():
                    try:
                        # Ask the thread event loop to quit
                        self.worker_thread.quit()
                    except Exception:
                        pass
                    # Wait a short time for graceful shutdown
                    try:
                        self.worker_thread.wait(5000)  # wait up to 5s
                    except Exception:
                        pass
            except Exception:
                pass

        # Update UI state regardless
        self.start_button.setEnabled(True)
        self.stop_button.setEnabled(False)
        self.status_label.setText("Stopped.")
        self.logger.info("FTP monitoring stopped")
        log_monitoring_event("MONITORING_STOPPED", "Service stopped and UI updated")
        # Update tray menu and status
        try:
            if hasattr(self, 'tray_start_action'):
                self.tray_start_action.setEnabled(True)
            if hasattr(self, 'tray_stop_action'):
                self.tray_stop_action.setEnabled(False)
            self._update_tray_status("Idle")
        except Exception:
            pass

    def _on_thread_finished(self):
        """Clean up thread/worker references after thread finishes to avoid accessing deleted C++ objects."""
        # Disconnect signals safely and clear references
        try:
            # It's safe to set to None; PySide will handle C++ deletion
            self.ftp_worker = None
        except Exception:
            self.ftp_worker = None

        try:
            self.worker_thread = None
        except Exception:
            self.worker_thread = None
        # Ensure tray/menu reflect stopped state
        try:
            if hasattr(self, 'tray_start_action'):
                self.tray_start_action.setEnabled(True)
            if hasattr(self, 'tray_stop_action'):
                self.tray_stop_action.setEnabled(False)
            self._update_tray_status("Idle")
            # Make sure status label is not left in 'Monitoring'
            try:
                self.status_label.setText("Stopped.")
            except Exception:
                pass
        except Exception:
            pass

    def _on_status_update(self, text: str):
        """Handle status updates for notifications."""
        try:
            app_settings = self.app_tab.get_settings()
            enabled = bool(app_settings.get('EnableStatusNotifications', False))
        except Exception:
            enabled = False

        if enabled and hasattr(self, 'tray_icon'):
            self.tray_icon.showMessage("FTP Monitor", str(text), QSystemTrayIcon.Information, 3000)

    def closeEvent(self, event):
        """Handle window close event."""
        try:
            app_settings = self.app_tab.get_settings()
            minimize = bool(app_settings.get('MinimizeOnClose', True))
        except Exception:
            minimize = True

        if minimize:
            event.ignore()
            self.hide()
            if hasattr(self, 'tray_icon'):
                app_settings = self.app_tab.get_settings()
                if app_settings.get('EnableStatusNotifications', False):
                    self.tray_icon.showMessage(
                        APP_NAME,
                        "Application was minimized to the system tray.",
                        QSystemTrayIcon.Information,
                        2000
                    )
        else:
            # Stop monitoring and wait briefly for threads to finish before quitting
            self.stop_monitor()
            try:
                if self.worker_thread and self.worker_thread.isRunning():
                    try:
                        self.worker_thread.quit()
                    except Exception:
                        pass
                    try:
                        self.worker_thread.wait(5000)
                    except Exception:
                        pass
            except Exception:
                pass

            event.accept()
            QApplication.quit()

    def quit_app(self):
        """Quit the application completely."""
        self.logger.info("Application quit requested")
        # Stop monitor and ensure threads are stopped before quitting
        self.stop_monitor()
        try:
            if self.worker_thread and self.worker_thread.isRunning():
                try:
                    self.worker_thread.quit()
                except Exception:
                    pass
                try:
                    self.worker_thread.wait(5000)
                except Exception:
                    pass
        except Exception:
            pass

        log_app_stop()
        QApplication.quit()

    def _toggle_theme(self):
        """Toggle between light and dark themes."""
        # Cycle order: Dark -> Light -> Auto -> Dark
        cur = self.theme_manager.get_current_theme()
        if cur == ThemeType.DARK:
            new_theme = ThemeType.LIGHT
        elif cur == ThemeType.LIGHT:
            new_theme = ThemeType.DARK

        # Apply and persist
        try:
            self.theme_manager.set_theme(new_theme)
        except Exception:
            # Fallback to toggle if something goes wrong
            new_theme = self.theme_manager.toggle_theme()

        self._update_theme_button()

        self.logger.info(f"Theme switched to {self.theme_manager.get_theme_display_name(new_theme)}")

    def _update_theme_button(self):
        """Update the theme button text and icon."""
        cur = self.theme_manager.get_current_theme()
        if cur == ThemeType.DARK:
            self.theme_button.setText("☀️ Light")
            self.theme_button.setToolTip("Switch to Light theme")
        elif cur == ThemeType.LIGHT:
            self.theme_button.setText("🌙 Dark")
            self.theme_button.setToolTip("Switch to Dark theme")

