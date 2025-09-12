"""Theme management utility."""

from typing import Optional
from PySide6.QtWidgets import QApplication

from ..config.themes import ThemeType, get_theme_stylesheet, get_theme_names


class ThemeManager:
    """Manages application theme switching and persistence."""
    
    def __init__(self, settings_manager=None):
        self.settings_manager = settings_manager
        self.current_theme = ThemeType.DARK
        self._load_saved_theme()
    
    def _load_saved_theme(self):
        """Load the saved theme from settings."""
        if self.settings_manager:
            theme_name = self.settings_manager.get_setting('Application.Theme', 'dark')
            try:
                self.current_theme = ThemeType(theme_name)
            except ValueError:
                self.current_theme = ThemeType.DARK

    # AUTO/system theme detection removed — only Light/Dark supported.
    
    def _save_theme(self):
        """Save the current theme to settings."""
        if self.settings_manager:
            self.settings_manager.set_setting('Application.Theme', self.current_theme.value)
            self.settings_manager.save_settings()
    
    def get_current_theme(self) -> ThemeType:
        """Get the current theme type."""
        return self.current_theme
    
    def set_theme(self, theme_type: ThemeType, save: bool = True):
        """
        Set the application theme.
        
        Args:
            theme_type: The theme type to apply
            save: Whether to save the theme to settings
        """
        self.current_theme = theme_type

        # Apply the stylesheet to the application (only light/dark supported)
        app = QApplication.instance()
        if app:
            stylesheet = get_theme_stylesheet(theme_type)
            app.setStyleSheet(stylesheet)
        
        if save:
            self._save_theme()
    
    def toggle_theme(self) -> ThemeType:
        """
        Toggle between light and dark themes.
        
        Returns:
            The new theme type
        """
        if self.current_theme == ThemeType.DARK:
            new_theme = ThemeType.LIGHT
        else:
            new_theme = ThemeType.DARK
        
        self.set_theme(new_theme)
        return new_theme
    
    def apply_current_theme(self):
        """Apply the current theme to the application."""
        app = QApplication.instance()
        if not app:
            return
        stylesheet = get_theme_stylesheet(self.current_theme)
        app.setStyleSheet(stylesheet)
    
    @staticmethod
    def get_available_themes() -> list:
        """Get list of available theme names."""
        return get_theme_names()
    
    def get_theme_display_name(self, theme_type: Optional[ThemeType] = None) -> str:
        """Get the display name for a theme type."""
        if theme_type is None:
            theme_type = self.current_theme
        
        display_names = {
            ThemeType.LIGHT: "Light",
            ThemeType.DARK: "Dark",
        }
        
        return display_names.get(theme_type, "Unknown")
    
    def is_dark_theme(self) -> bool:
        """Check if the current theme is dark."""
        return self.current_theme == ThemeType.DARK
    
    def is_light_theme(self) -> bool:
        """Check if the current theme is light."""
        return self.current_theme == ThemeType.LIGHT
