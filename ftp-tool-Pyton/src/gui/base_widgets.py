"""Base widgets and common GUI components."""

from PySide6.QtWidgets import (
    QWidget, QGroupBox, 
    QLineEdit, QCheckBox, QSpinBox, QTimeEdit, QComboBox
)
from PySide6.QtCore import Signal


class BaseTabWidget(QWidget):
    """Base class for tab widgets with common functionality."""
    
    setting_changed = Signal(str, object)  # setting_key, value
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self._dirty = set()
        self.setup_ui()
        
    def setup_ui(self):
        """Override this method to setup the UI."""
        pass
    
    def mark_dirty(self, key: str):
        """Mark a setting as dirty (modified)."""
        self._dirty.add(key)
        
    def is_dirty(self, key: str = None) -> bool:
        """Check if a setting is dirty or if any setting is dirty."""
        if key:
            return key in self._dirty
        return len(self._dirty) > 0
    
    def clear_dirty(self):
        """Clear all dirty flags."""
        self._dirty.clear()


class SettingsGroupBox(QGroupBox):
    """A group box with settings change tracking."""
    
    setting_changed = Signal(str, object)
    
    def __init__(self, title: str, parent=None):
        super().__init__(title, parent)
        self._controls = {}
        
    def add_setting_control(self, key: str, control):
        """Add a control and connect its change signal."""
        self._controls[key] = control
        
        # Connect appropriate signals based on control type
        if isinstance(control, QLineEdit):
            control.textChanged.connect(lambda text: self.setting_changed.emit(key, text))
        elif isinstance(control, QSpinBox):
            control.valueChanged.connect(lambda value: self.setting_changed.emit(key, value))
        elif isinstance(control, QCheckBox):
            control.toggled.connect(lambda checked: self.setting_changed.emit(key, checked))
        elif isinstance(control, QTimeEdit):
            control.timeChanged.connect(lambda time: self.setting_changed.emit(key, time.toString('HH:mm')))
        elif isinstance(control, QComboBox):
            control.currentTextChanged.connect(lambda text: self.setting_changed.emit(key, text))
    
    def get_setting_value(self, key: str):
        """Get the current value of a setting control."""
        control = self._controls.get(key)
        if not control:
            return None
            
        if isinstance(control, QLineEdit):
            return control.text()
        elif isinstance(control, QSpinBox):
            return control.value()
        elif isinstance(control, QCheckBox):
            return control.isChecked()
        elif isinstance(control, QTimeEdit):
            return control.time().toString('HH:mm')
        elif isinstance(control, QComboBox):
            return control.currentText()
        
        return None
    
    def set_setting_value(self, key: str, value):
        """Set the value of a setting control."""
        control = self._controls.get(key)
        if not control:
            return False
            
        try:
            if isinstance(control, QLineEdit):
                control.setText(str(value) if value is not None else "")
            elif isinstance(control, QSpinBox):
                control.setValue(int(value) if value is not None else 0)
            elif isinstance(control, QCheckBox):
                control.setChecked(bool(value) if value is not None else False)
            elif isinstance(control, QTimeEdit):
                if value:
                    from PySide6.QtCore import QTime
                    time_parts = str(value).split(':')
                    if len(time_parts) == 2:
                        control.setTime(QTime(int(time_parts[0]), int(time_parts[1])))
            elif isinstance(control, QComboBox):
                if value is not None:
                    index = control.findText(str(value))
                    if index >= 0:
                        control.setCurrentIndex(index)
                    else:
                        control.setCurrentText(str(value))
            return True
        except Exception:
            return False
    
    def get_all_values(self) -> dict:
        """Get all setting values as a dictionary."""
        return {key: self.get_setting_value(key) for key in self._controls}


def create_time_edit() -> QTimeEdit:
    """Create a standardized time edit widget."""
    time_edit = QTimeEdit()
    time_edit.setDisplayFormat('HH:mm')
    return time_edit
