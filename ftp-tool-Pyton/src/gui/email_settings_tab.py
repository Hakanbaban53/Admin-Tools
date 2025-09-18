"""Email settings and monitoring tab widget."""

from PySide6.QtWidgets import (
    QVBoxLayout, QGridLayout, QLabel, QLineEdit, 
    QPushButton, QCheckBox, QSpinBox
)
from PySide6.QtCore import Signal

from .base_widgets import BaseTabWidget, SettingsGroupBox, create_time_edit


class EmailSettingsTab(BaseTabWidget):
    """Email alerts and monitoring schedule settings tab."""
    
    test_email_requested = Signal()
    
    def __init__(self, parent=None):
        super().__init__(parent)
        
    def setup_ui(self):
        """Setup the email settings UI."""
        layout = QVBoxLayout(self)
        
        # SMTP Settings
        self._create_smtp_group(layout)
        
        # Schedule and Notifications
        self._create_schedule_group(layout)
        
        layout.addStretch()
    
    def _create_smtp_group(self, parent_layout):
        """Create SMTP/Email settings group."""
        smtp_group = SettingsGroupBox("SMTP / Email Settings")
        smtp_layout = QGridLayout()
        
        # SMTP Host
        self.smtp_host = QLineEdit()
        smtp_group.add_setting_control('smtp_host', self.smtp_host)
        smtp_layout.addWidget(QLabel("SMTP Host:"), 0, 0)
        smtp_layout.addWidget(self.smtp_host, 0, 1)
        
        # SMTP Port
        self.smtp_port = QSpinBox(minimum=1, maximum=65535, value=587)
        smtp_group.add_setting_control('smtp_port', self.smtp_port)
        smtp_layout.addWidget(QLabel("Port:"), 0, 2)
        smtp_layout.addWidget(self.smtp_port, 0, 3)
        
        # Username
        self.smtp_user = QLineEdit()
        smtp_group.add_setting_control('smtp_user', self.smtp_user)
        smtp_layout.addWidget(QLabel("Username:"), 1, 0)
        smtp_layout.addWidget(self.smtp_user, 1, 1)
        
        # Password
        self.smtp_pass = QLineEdit(echoMode=QLineEdit.Password)
        smtp_group.add_setting_control('smtp_pass', self.smtp_pass)
        smtp_layout.addWidget(QLabel("Password:"), 1, 2)
        smtp_layout.addWidget(self.smtp_pass, 1, 3)
        
        # SSL/TLS
        self.smtp_ssl = QCheckBox("Enable SSL/TLS")
        smtp_group.add_setting_control('smtp_ssl', self.smtp_ssl)
        smtp_layout.addWidget(self.smtp_ssl, 2, 0)
        
        # From address
        self.email_from = QLineEdit()
        smtp_group.add_setting_control('email_from', self.email_from)
        smtp_layout.addWidget(QLabel("From address:"), 3, 0)
        smtp_layout.addWidget(self.email_from, 3, 1, 1, 3)
        
        # To addresses
        self.email_to = QLineEdit()
        smtp_group.add_setting_control('email_to', self.email_to)
        smtp_layout.addWidget(QLabel("Recipients (semicolon separated):"), 4, 0)
        smtp_layout.addWidget(self.email_to, 4, 1, 1, 3)
        
        # Test email button
        test_email_btn = QPushButton("Test Send Email")
        test_email_btn.clicked.connect(self.test_email_requested.emit)
        smtp_layout.addWidget(test_email_btn, 5, 3)
        
        smtp_group.setLayout(smtp_layout)
        parent_layout.addWidget(smtp_group)
        
        # Connect signals
        smtp_group.setting_changed.connect(lambda key, value: self.setting_changed.emit(key, value))
        
        self.smtp_group = smtp_group
    
    def _create_schedule_group(self, parent_layout):
        """Create monitoring schedule settings group."""
        sched_group = SettingsGroupBox("Download monitoring schedule & notifications")
        sched_layout = QGridLayout()
        
        # Weekdays
        self.weekday_checks = {}
        days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]
        for i, day in enumerate(days):
            cb = QCheckBox(day)
            self.weekday_checks[day] = cb
            cb.toggled.connect(lambda checked, d=day: self._on_weekday_changed(d, checked))
            sched_layout.addWidget(cb, 0, i)
        
        # Working hours start
        self.work_start = create_time_edit()
        sched_group.add_setting_control('work_start', self.work_start)
        sched_layout.addWidget(QLabel("Working hours start:"), 1, 0)
        sched_layout.addWidget(self.work_start, 1, 1)
        
        # Working hours end
        self.work_end = create_time_edit()
        sched_group.add_setting_control('work_end', self.work_end)
        sched_layout.addWidget(QLabel("Working hours end:"), 1, 2)
        sched_layout.addWidget(self.work_end, 1, 3)
        
        # Lunch start
        self.lunch_start = create_time_edit()
        sched_group.add_setting_control('lunch_start', self.lunch_start)
        sched_layout.addWidget(QLabel("Lunch start:"), 2, 0)
        sched_layout.addWidget(self.lunch_start, 2, 1)
        
        # Lunch end
        self.lunch_end = create_time_edit()
        sched_group.add_setting_control('lunch_end', self.lunch_end)
        sched_layout.addWidget(QLabel("Lunch end:"), 2, 2)
        sched_layout.addWidget(self.lunch_end, 2, 3)
        
        # Notification threshold
        self.notify_threshold = QSpinBox(minimum=1, maximum=1440, value=15)
        sched_group.add_setting_control('notify_threshold', self.notify_threshold)
        sched_layout.addWidget(QLabel("Notify if no download within (minutes):"), 3, 0)
        sched_layout.addWidget(self.notify_threshold, 3, 1)
        
        sched_group.setLayout(sched_layout)
        parent_layout.addWidget(sched_group)
        
        # Connect signals
        sched_group.setting_changed.connect(lambda key, value: self.setting_changed.emit(key, value))
        
        self.sched_group = sched_group
    
    def _on_weekday_changed(self, day: str, checked: bool):
        """Handle weekday checkbox changes."""
        weekdays = [day for day, cb in self.weekday_checks.items() if cb.isChecked()]
        self.setting_changed.emit('weekdays', weekdays)
    
    def get_settings(self) -> dict:
        """Get all email and schedule settings."""
        settings = {}
        settings.update(self.smtp_group.get_all_values())
        settings.update(self.sched_group.get_all_values())
        
        # Add weekdays separately
        weekdays = [day for day, cb in self.weekday_checks.items() if cb.isChecked()]
        settings['weekdays'] = weekdays
        
        return settings
    
    def set_settings(self, settings: dict):
        """Set email and schedule settings."""
        # Set SMTP and schedule settings
        for key, value in settings.items():
            if key != 'weekdays':
                self.smtp_group.set_setting_value(key, value)
                self.sched_group.set_setting_value(key, value)
        
        # Set weekdays
        weekdays = settings.get('weekdays', [])
        for day, cb in self.weekday_checks.items():
            cb.setChecked(day in weekdays)
