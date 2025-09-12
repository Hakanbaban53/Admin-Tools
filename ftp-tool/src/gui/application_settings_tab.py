"""Application settings tab widget."""

from PySide6.QtWidgets import (
    QVBoxLayout, QGridLayout, QLabel, QPushButton, QCheckBox, QSpinBox, QComboBox
)
from PySide6.QtCore import Signal

from .base_widgets import BaseTabWidget, SettingsGroupBox


class ApplicationSettingsTab(BaseTabWidget):
    """Application behavior and system integration settings tab."""
    
    apply_autostart_requested = Signal()
    open_logs_folder_requested = Signal()
    clear_old_logs_requested = Signal()
    
    def __init__(self, parent=None):
        super().__init__(parent)
        
    def setup_ui(self):
        """Setup the application settings UI."""
        layout = QVBoxLayout(self)
        
        # Application Settings
        self._create_app_group(layout)
        
        # Logging Settings
        self._create_logging_group(layout)
        
        layout.addStretch()
    
    def _create_app_group(self, parent_layout):
        """Create application settings group."""
        app_group = SettingsGroupBox('Application Settings')
        app_layout = QGridLayout()
        
        # File stability checking
        self.enable_file_stability = QCheckBox('Enable file stability checking')
        app_group.add_setting_control('EnableFileStability', self.enable_file_stability)
        app_layout.addWidget(self.enable_file_stability, 0, 0, 1, 2)
        
        # File stability wait time
        self.file_stability_wait = QSpinBox(minimum=0, maximum=3600, value=10)
        app_group.add_setting_control('FileStabilityWait', self.file_stability_wait)
        app_layout.addWidget(QLabel('File Stability Wait (sec):'), 1, 0)
        app_layout.addWidget(self.file_stability_wait, 1, 1)
        
        # Auto-start monitoring
        self.auto_start_monitoring = QCheckBox('Auto-start monitoring when application starts')
        app_group.add_setting_control('AutoStartMonitoring', self.auto_start_monitoring)
        app_layout.addWidget(self.auto_start_monitoring, 2, 0, 1, 2)
        
        # Start with system
        self.start_with_system = QCheckBox('Start with Windows')
        app_group.add_setting_control('StartWithWindows', self.start_with_system)
        app_layout.addWidget(self.start_with_system, 3, 0)
        
        # Enable status notifications
        self.enable_status_notifications = QCheckBox('Enable status notifications')
        app_group.add_setting_control('EnableStatusNotifications', self.enable_status_notifications)
        app_layout.addWidget(self.enable_status_notifications, 3, 1)
        
        # Minimize to tray when closing
        self.minimize_to_tray_on_close = QCheckBox('Minimize to system tray when closing')
        app_group.add_setting_control('MinimizeOnClose', self.minimize_to_tray_on_close)
        app_layout.addWidget(self.minimize_to_tray_on_close, 4, 0, 1, 2)
        
        # Apply autostart button
        autostart_btn = QPushButton('Apply Autostart')
        autostart_btn.clicked.connect(self.apply_autostart_requested.emit)
        app_layout.addWidget(autostart_btn, 5, 0)
        
        app_group.setLayout(app_layout)
        parent_layout.addWidget(app_group)
        
        # Connect signals
        app_group.setting_changed.connect(lambda key, value: self.setting_changed.emit(key, value))
        
        self.app_group = app_group
    
    def _create_logging_group(self, parent_layout):
        """Create logging settings group."""
        logging_group = SettingsGroupBox('Logging Settings')
        logging_layout = QGridLayout()

        # Log Level
        self.log_level = QComboBox()
        self.log_level.addItems(['DEBUG', 'INFO', 'WARNING', 'ERROR', 'CRITICAL'])
        self.log_level.setCurrentText('INFO')
        logging_group.add_setting_control('LogLevel', self.log_level)
        logging_layout.addWidget(QLabel('Log Level:'), 0, 0)
        logging_layout.addWidget(self.log_level, 0, 1)

        # Max Log Size (MB)
        self.max_log_size = QSpinBox(minimum=1, maximum=1000, value=5, suffix=' MB')
        logging_group.add_setting_control('MaxLogSize', self.max_log_size)
        logging_layout.addWidget(QLabel('Max Log Size (MB):'), 1, 0)
        logging_layout.addWidget(self.max_log_size, 1, 1)

        # Open Logs Folder button
        open_logs_btn = QPushButton('Open Logs Folder')
        open_logs_btn.clicked.connect(self.open_logs_folder_requested.emit)
        logging_layout.addWidget(open_logs_btn, 1, 2)

        # Keep Logs (days)
        self.keep_logs_days = QSpinBox(minimum=1, maximum=365, value=30, suffix=' days')
        logging_group.add_setting_control('KeepLogsDays', self.keep_logs_days)
        logging_layout.addWidget(QLabel('Keep Logs (days):'), 2, 0)
        logging_layout.addWidget(self.keep_logs_days, 2, 1)

        # Clear Old Logs button
        clear_logs_btn = QPushButton('Clear Old Logs')
        clear_logs_btn.clicked.connect(self.clear_old_logs_requested.emit)
        logging_layout.addWidget(clear_logs_btn, 2, 2)

        # Enable file logging
        self.enable_file_logging = QCheckBox('Enable file logging')
        self.enable_file_logging.setChecked(True)
        logging_group.add_setting_control('EnableFileLogging', self.enable_file_logging)
        logging_layout.addWidget(self.enable_file_logging, 3, 0, 1, 3)

        logging_group.setLayout(logging_layout)
        parent_layout.addWidget(logging_group)

        # Connect signals
        logging_group.setting_changed.connect(lambda key, value: self.setting_changed.emit(key, value))

        self.logging_group = logging_group
    
    def get_settings(self) -> dict:
        """Get all application settings."""
        settings = {}
        settings.update(self.app_group.get_all_values())
        settings.update(self.logging_group.get_all_values())
        return settings
    
    def set_settings(self, settings: dict):
        """Set application settings."""
        for key, value in settings.items():
            # Try app group first, then logging group
            if not self.app_group.set_setting_value(key, value):
                self.logging_group.set_setting_value(key, value)
