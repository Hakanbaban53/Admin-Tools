"""UI styling and themes with light and dark mode support."""

from enum import Enum


class ThemeType(Enum):
    """Available theme types."""
    LIGHT = "light"
    DARK = "dark"


# Color schemes for each theme
THEME_COLORS = {
    ThemeType.DARK: {
        'background': '#2E2E2E',
        'background_alt': '#3C3C3C',
        'background_window': '#242424',
        'background_input': '#1E1E1E',
        'background_hover': '#484848',
        'background_pressed': '#4A4A4A',
        'text': '#FFFFFF',
        'text_secondary': '#CCCCCC',
        'text_disabled': '#808080',
        'border': '#555555',
        'border_light': '#444444',
        'border_focus': '#0078D7',
        'accent': '#0078D7',
        'success': '#28A745',
        'success_hover': '#218838',
        'danger': '#DC3545',
        'danger_hover': '#C82333',
        'warning': '#FFC107',
        'warning_hover': '#E0A800',
        'info': '#17A2B8',
        'info_hover': '#138496',
    },
    ThemeType.LIGHT: {
        'background': '#FFFFFF',
        'background_alt': '#F5F5F5',
        'background_window': '#FAFAFA',
        'background_input': '#FFFFFF',
        'background_hover': '#E6E6E6',
        'background_pressed': '#D4D4D4',
        'text': '#212529',
        'text_secondary': '#6C757D',
        'text_disabled': '#ADB5BD',
        'border': '#CED4DA',
        'border_light': '#DEE2E6',
        'border_focus': '#0078D7',
        'accent': '#0078D7',
        'success': '#28A745',
        'success_hover': '#218838',
        'danger': '#DC3545',
        'danger_hover': '#C82333',
        'warning': '#FFC107',
        'warning_hover': '#E0A800',
        'info': '#17A2B8',
        'info_hover': '#138496',
    }
}


def generate_stylesheet(theme_type: ThemeType) -> str:
    """Generate stylesheet for the specified theme."""
    colors = THEME_COLORS[theme_type]
    
    return f"""
    /* Main Application Styling */
    QWidget {{
        background-color: {colors['background']};
        color: {colors['text']};
        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
        font-size: 10pt;
        selection-background-color: {colors['accent']};
        selection-color: white;
    }}
    
    QMainWindow {{
        background-color: {colors['background_window']};
    }}
    
    /* Tab Widget Styling */
    QTabWidget::pane {{
        border: 1px solid {colors['border_light']};
        border-radius: 6px;
        margin-top: -1px;
        background-color: {colors['background']};
    }}
    
    QTabBar::tab {{
        background: {colors['background_alt']};
        color: {colors['text_secondary']};
        padding: 10px 16px;
        margin-right: 2px;
        border-top-left-radius: 6px;
        border-top-right-radius: 6px;
        border: 1px solid {colors['border_light']};
        border-bottom: none;
        min-width: 80px;
    }}
    
    QTabBar::tab:selected {{
        background: {colors['background']};
        color: {colors['text']};
        border-color: {colors['border_light']};
        margin-bottom: -1px;
    }}
    
    QTabBar::tab:!selected:hover {{
        background: {colors['background_hover']};
        color: {colors['text']};
    }}
    
    /* Group Box Styling */
    QGroupBox {{
        background-color: {colors['background']};
        border: 1px solid {colors['border_light']};
        border-radius: 8px;
        margin-top: 12px;
        padding-top: 6px;
        font-weight: 600;
        color: {colors['text']};
    }}
    
    QGroupBox::title {{
        subcontrol-origin: margin;
        subcontrol-position: top left;
        padding: 4px 8px;
        left: 12px;
        background-color: {colors['background']};
        border-radius: 4px;
    }}
    
    /* Input Controls Styling */
    QLineEdit, QPlainTextEdit, QSpinBox, QTimeEdit {{
        background-color: {colors['background_input']};
        border: 2px solid {colors['border']};
        border-radius: 6px;
        padding: 8px 12px;
        color: {colors['text']};
        font-size: 10pt;
    }}
    
    QLineEdit:focus, QPlainTextEdit:focus, QSpinBox:focus, QTimeEdit:focus {{
        border-color: {colors['border_focus']};
        outline: none;
    }}
    
    QLineEdit:disabled, QPlainTextEdit:disabled, QSpinBox:disabled, QTimeEdit:disabled {{
        background-color: {colors['background_alt']};
        color: {colors['text_disabled']};
        border-color: {colors['border_light']};
    }}
    
    QPlainTextEdit {{
        padding: 12px;
        line-height: 1.4;
    }}
    
    /* Spin Box Specific */
    QSpinBox::up-button, QSpinBox::down-button {{
        background-color: {colors['background_alt']};
        border: none;
        border-radius: 3px;
        width: 16px;
    }}
    
    QSpinBox::up-button:hover, QSpinBox::down-button:hover {{
        background-color: {colors['background_hover']};
    }}
    
    /* Button Styling */
    QPushButton {{
        background-color: {colors['background_alt']};
        border: 2px solid {colors['border']};
        color: {colors['text']};
        padding: 8px 16px;
        border-radius: 6px;
        font-weight: 500;
        min-width: 80px;
    }}
    
    QPushButton:hover {{
        background-color: {colors['background_hover']};
        border-color: {colors['border_focus']};
    }}
    
    QPushButton:pressed {{
        background-color: {colors['background_pressed']};
    }}
    
    QPushButton:disabled {{
        background-color: {colors['background_alt']};
        color: {colors['text_disabled']};
        border-color: {colors['border_light']};
    }}
    
    /* Special Button Styles */
    QPushButton#StartButton {{
        background-color: {colors['success']};
        border-color: {colors['success']};
        color: white;
        font-weight: 600;
    }}
    
    QPushButton#StartButton:hover {{
        background-color: {colors['success_hover']};
        border-color: {colors['success_hover']};
    }}
    
    QPushButton#StopButton {{
        background-color: {colors['danger']};
        border-color: {colors['danger']};
        color: white;
        font-weight: 600;
    }}
    
    QPushButton#StopButton:hover {{
        background-color: {colors['danger_hover']};
        border-color: {colors['danger_hover']};
    }}
    
    QPushButton#SaveButton {{
        background-color: {colors['info']};
        border-color: {colors['info']};
        color: white;
        font-weight: 600;
    }}
    
    QPushButton#SaveButton:hover {{
        background-color: {colors['info_hover']};
        border-color: {colors['info_hover']};
    }}
    
    /* Checkbox Styling */
    QCheckBox {{
        color: {colors['text']};
        spacing: 8px;
    }}
    
    QCheckBox::indicator {{
        width: 18px;
        height: 18px;
        border: 2px solid {colors['border']};
        border-radius: 4px;
        background-color: {colors['background_input']};
    }}
    
    QCheckBox::indicator:hover {{
        border-color: {colors['border_focus']};
    }}
    
    QCheckBox::indicator:checked {{
        background-color: {colors['accent']};
        border-color: {colors['accent']};
    }}
    
    QCheckBox::indicator:disabled {{
        background-color: {colors['background_alt']};
        border-color: {colors['border_light']};
    }}
    
    /* Label Styling */
    QLabel {{
        color: {colors['text']};
        background-color: transparent;
    }}
    
    QLabel[class="secondary"] {{
        color: {colors['text_secondary']};
        font-size: 9pt;
    }}
    
    /* Scrollbar Styling */
    QScrollBar:vertical {{
        border: none;
        background: {colors['background_alt']};
        width: 12px;
        border-radius: 6px;
    }}
    
    QScrollBar::handle:vertical {{
        background: {colors['border']};
        border-radius: 6px;
        min-height: 20px;
    }}
    
    QScrollBar::handle:vertical:hover {{
        background: {colors['text_secondary']};
    }}
    
    QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {{
        border: none;
        background: none;
    }}
    
    /* Status Bar Styling */
    QStatusBar {{
        background-color: {colors['background_alt']};
        color: {colors['text_secondary']};
        border-top: 1px solid {colors['border_light']};
    }}
    
    /* Menu Styling */
    QMenu {{
        background-color: {colors['background']};
        border: 1px solid {colors['border_light']};
        border-radius: 6px;
        padding: 4px;
    }}
    
    QMenu::item {{
        padding: 8px 16px;
        border-radius: 4px;
        color: {colors['text']};
    }}
    
    QMenu::item:selected {{
        background-color: {colors['background_hover']};
    }}
    
    QMenu::separator {{
        height: 1px;
        background-color: {colors['border_light']};
        margin: 4px 8px;
    }}
    """


# Pre-generated stylesheets
DARK_THEME_STYLESHEET = generate_stylesheet(ThemeType.DARK)
LIGHT_THEME_STYLESHEET = generate_stylesheet(ThemeType.LIGHT)

# Default stylesheet
DEFAULT_STYLESHEET = DARK_THEME_STYLESHEET

# Available themes dictionary
AVAILABLE_THEMES = {
    ThemeType.DARK: DARK_THEME_STYLESHEET,
    ThemeType.LIGHT: LIGHT_THEME_STYLESHEET,
}


def get_theme_stylesheet(theme_type: ThemeType) -> str:
    """Get stylesheet for the specified theme type."""
    return AVAILABLE_THEMES.get(theme_type, DEFAULT_STYLESHEET)


def get_theme_names() -> list:
    """Get list of available theme names."""
    return [theme.value for theme in ThemeType if theme != ThemeType.AUTO]
