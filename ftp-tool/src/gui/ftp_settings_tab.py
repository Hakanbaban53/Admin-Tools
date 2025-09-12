"""FTP settings tab widget."""

from PySide6.QtWidgets import (
    QVBoxLayout, QGridLayout, QLabel, QLineEdit, 
    QPushButton, QCheckBox, QSpinBox, QTextEdit, QFileDialog
)
from PySide6.QtCore import Signal
from PySide6.QtGui import QPalette, QColor

from .base_widgets import BaseTabWidget, SettingsGroupBox


class FTPSettingsTab(BaseTabWidget):
    """FTP connection and download settings tab."""
    
    test_connection_requested = Signal()
    browse_local_path_requested = Signal()
    
    def __init__(self, parent=None):
        super().__init__(parent)
        
    def setup_ui(self):
        """Setup the FTP settings UI."""
        layout = QVBoxLayout(self)

        # FTP Connection Settings
        self._create_connection_group(layout)

        # Download Settings
        self._create_download_group(layout)

        # Log Output (colored)
        self.log_box = QTextEdit()
        self.log_box.setReadOnly(True)
        self.log_box.setAcceptRichText(True)
        self.log_box.setStyleSheet("")
        layout.addWidget(self.log_box, 1)

        layout.addStretch()
    
    def _create_connection_group(self, parent_layout):
        """Create FTP connection settings group."""
        conn_group = SettingsGroupBox("FTP Connection Settings")
        conn_layout = QGridLayout()
        
        # Host
        self.ftp_host = QLineEdit()
        conn_group.add_setting_control('ftp_host', self.ftp_host)
        conn_layout.addWidget(QLabel("Host:"), 0, 0)
        conn_layout.addWidget(self.ftp_host, 0, 1)
        
        # Port
        self.ftp_port = QSpinBox(minimum=1, maximum=65535, value=21)
        conn_group.add_setting_control('ftp_port', self.ftp_port)
        conn_layout.addWidget(QLabel("Port:"), 0, 2)
        conn_layout.addWidget(self.ftp_port, 0, 3)
        
        # Username
        self.ftp_user = QLineEdit()
        conn_group.add_setting_control('ftp_user', self.ftp_user)
        conn_layout.addWidget(QLabel("Username:"), 1, 0)
        conn_layout.addWidget(self.ftp_user, 1, 1)
        
        # Password
        self.ftp_pass = QLineEdit(echoMode=QLineEdit.Password)
        conn_group.add_setting_control('ftp_pass', self.ftp_pass)
        conn_layout.addWidget(QLabel("Password:"), 1, 2)
        conn_layout.addWidget(self.ftp_pass, 1, 3)
        
        # Remote path
        self.remote_path = QLineEdit()
        conn_group.add_setting_control('remote_path', self.remote_path)
        conn_layout.addWidget(QLabel("Remote folder:"), 2, 0)
        conn_layout.addWidget(self.remote_path, 2, 1, 1, 2)
        
        # Test connection button
        test_btn = QPushButton("Test Connection")
        test_btn.clicked.connect(self.test_connection_requested.emit)
        conn_layout.addWidget(test_btn, 2, 3)
        
        conn_group.setLayout(conn_layout)
        parent_layout.addWidget(conn_group)
        
        # Connect signals
        conn_group.setting_changed.connect(lambda key, value: self.setting_changed.emit(key, value))
        
        self.conn_group = conn_group
    
    def _create_download_group(self, parent_layout):
        """Create download settings group."""
        dl_group = SettingsGroupBox("Download Settings")
        dl_layout = QGridLayout()
        
        # Local path
        self.local_path = QLineEdit()
        dl_group.add_setting_control('local_path', self.local_path)
        browse_btn = QPushButton("Browse...")
        browse_btn.clicked.connect(self._browse_local_path)
        dl_layout.addWidget(QLabel("Download to:"), 0, 0)
        dl_layout.addWidget(self.local_path, 0, 1)
        dl_layout.addWidget(browse_btn, 0, 2)
        
        # Check interval
        self.interval = QSpinBox(minimum=1, maximum=86400, value=60)
        dl_group.add_setting_control('interval', self.interval)
        dl_layout.addWidget(QLabel("Check every:"), 1, 0)
        dl_layout.addWidget(self.interval, 1, 1)
        dl_layout.addWidget(QLabel("seconds"), 1, 2)
        
        # Delete after download
        self.delete_after_download = QCheckBox("Delete files from server after download")
        dl_group.add_setting_control('delete_after_download', self.delete_after_download)
        dl_layout.addWidget(self.delete_after_download, 2, 0, 1, 2)
        
        # Passive mode
        self.passive_mode = QCheckBox("Use Passive FTP Mode")
        self.passive_mode.setChecked(True)
        dl_group.add_setting_control('passive_mode', self.passive_mode)
        dl_layout.addWidget(self.passive_mode, 2, 2, 1, 1)
        
        dl_group.setLayout(dl_layout)
        parent_layout.addWidget(dl_group)
        
        # Connect signals
        dl_group.setting_changed.connect(lambda key, value: self.setting_changed.emit(key, value))
        
        self.dl_group = dl_group
    
    def _browse_local_path(self):
        """Browse for local download directory."""
        folder = QFileDialog.getExistingDirectory(
            self, 
            "Select Download Directory", 
            self.local_path.text()
        )
        if folder:
            self.local_path.setText(folder)
            self.browse_local_path_requested.emit()
    
    def log_message(self, message: str):
        """Add a message to the log box."""
        # Simple tag-based coloring. Messages are expected to start with
        # a timestamp like: [HH:MM:SS] TAG: details
        color = "white"
        tag = None
        try:
            # naive parse: find token between '] ' and ':'
            if '] ' in message and ':' in message:
                after = message.split('] ', 1)[1]
                tag = after.split(':', 1)[0].strip()
        except Exception:
            tag = None

        if tag:
            tag_upper = tag.upper()
            if 'ERROR' in tag_upper or tag_upper.startswith('ERROR'):
                color = 'red'
            elif 'DOWNLOAD' in tag_upper or 'FILE' in tag_upper:
                color = 'blue'
            elif 'CONNECT' in tag_upper or 'MONITOR' in tag_upper:
                color = 'green'
            elif 'MLSD' in tag_upper or 'NLST' in tag_upper:
                color = 'orange'
            else:
                color = 'black'

        # Render as HTML to apply color. Use theme-aware colors: for default
        # (black/white) use the widget palette so text remains readable in both
        # light and dark themes. Convert named colors to hex where possible.
        safe_message = (message.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;'))

        # Determine final color hex
        try:
            if color in ('black', 'white'):
                # Use the text color from the widget palette
                palette_color = self.log_box.palette().color(QPalette.Text)
                color_hex = palette_color.name()
            else:
                color_hex = QColor(color).name()
        except Exception:
            # Fallback to palette text color on any error
            color_hex = self.log_box.palette().color(QPalette.Text).name()

        html = f'<pre style="color: {color_hex}; margin:0;">{safe_message}</pre>'
        self.log_box.append(html)
    
    def clear_log(self):
        """Clear the log box."""
        self.log_box.clear()
    
    def get_settings(self) -> dict:
        """Get all FTP settings."""
        settings = {}
        settings.update(self.conn_group.get_all_values())
        settings.update(self.dl_group.get_all_values())
        return settings
    
    def set_settings(self, settings: dict):
        """Set FTP settings."""
        for key, value in settings.items():
            self.conn_group.set_setting_value(key, value)
            self.dl_group.set_setting_value(key, value)
