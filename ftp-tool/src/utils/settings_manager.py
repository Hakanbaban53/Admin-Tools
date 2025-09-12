"""Settings management utilities."""

import json
import os
from typing import Dict, Any

from ..config.constants import CONFIG_FILE


class SettingsManager:
    """Manages application settings with JSON persistence."""
    
    def __init__(self, config_file: str = None):
        self.config_file = config_file or CONFIG_FILE
        # Keep a canonical defaults tree and a small overrides tree saved to disk.
        self._defaults = self._get_default_settings()
        self._overrides: Dict[str, Any] = {}
        # Effective settings is defaults merged with overrides
        self._settings = {}
        self.load_settings()
    
    def load_settings(self) -> Dict[str, Any]:
        """Load settings from JSON file."""
        try:
            if os.path.exists(self.config_file):
                with open(self.config_file, 'r', encoding='utf-8') as f:
                    # File is treated as an overrides file (only user-changed values)
                    data = json.load(f)
                    if isinstance(data, dict):
                        self._overrides = data
                    else:
                        self._overrides = {}
            else:
                self._overrides = {}

            # Merge defaults with overrides to produce effective settings
            self._settings = self._deep_merge_dicts(self._defaults, self._overrides)
        except Exception as e:
            print(f"Error loading settings: {e}")
            # On error fall back to defaults only
            self._overrides = {}
            self._settings = self._defaults.copy()
        return self._settings
    
    def save_settings(self, settings: Dict[str, Any] = None) -> bool:
        """Save settings to JSON file."""
        try:
            # If caller provided a settings dict, treat it as full settings and compute overrides
            if settings is not None:
                # Compute overrides = differences between provided settings and defaults
                overrides = self._compute_overrides(self._defaults, settings)
                self._overrides = overrides
            # Persist only overrides (small JSON containing changed values)
            # Write to a temporary file and atomically replace to avoid corruption if the
            # application crashes or is terminated while writing settings.
            import tempfile
            dirpath = os.path.dirname(os.path.abspath(self.config_file)) or '.'
            os.makedirs(dirpath, exist_ok=True)
            fd, tmp_path = tempfile.mkstemp(prefix='.tmp_settings_', dir=dirpath)
            try:
                with os.fdopen(fd, 'w', encoding='utf-8') as f:
                    json.dump(self._overrides, f, indent=2, ensure_ascii=False)
                os.replace(tmp_path, self.config_file)
            finally:
                # Ensure no stray temp file remains
                try:
                    if os.path.exists(tmp_path):
                        os.remove(tmp_path)
                except Exception:
                    pass
            # Recompute effective settings
            self._settings = self._deep_merge_dicts(self._defaults, self._overrides)
            return True
        except Exception as e:
            print(f"Error saving settings: {e}")
            return False
    
    def get_setting(self, key: str, default: Any = None) -> Any:
        """Get a specific setting value."""
        # Support dotted keys like 'Application.Theme'
        if not isinstance(key, str) or '.' not in key:
            return self._settings.get(key, default)
        return self._get_from_dict(self._settings, key, default)
    
    def set_setting(self, key: str, value: Any) -> None:
        """Set a specific setting value."""
        # Support dotted keys and keep overrides small.
        if isinstance(key, str) and '.' in key:
            # Update overrides
            self._set_in_dict(self._overrides, key, value)
        else:
            # top-level key
            self._overrides[key] = value

        # Update effective settings immediately
        self._settings = self._deep_merge_dicts(self._defaults, self._overrides)

        # Recompute minimal overrides (this will remove entries that equal defaults)
        self._overrides = self._compute_overrides(self._defaults, self._settings)
    
    def get_all_settings(self) -> Dict[str, Any]:
        """Get all settings."""
        return self._settings.copy()
    
    def update_settings(self, new_settings: Dict[str, Any]) -> None:
        """Update multiple settings."""
        # Merge into effective settings then recompute overrides
        # Start with current effective settings
        merged = self._deep_merge_dicts(self._settings, new_settings)
        # Compute new overrides relative to defaults
        self._overrides = self._compute_overrides(self._defaults, merged)
        # Update effective settings
        self._settings = self._deep_merge_dicts(self._defaults, self._overrides)

    # --- Helper utilities for dotted keys, merging and diffing ---
    def _deep_merge_dicts(self, base: Dict[str, Any], override: Dict[str, Any]) -> Dict[str, Any]:
        """Return a new dict with override values merged into base recursively."""
        result = {}
        for k in set(base.keys()) | set(override.keys()):
            if k in override:
                if isinstance(base.get(k), dict) and isinstance(override.get(k), dict):
                    result[k] = self._deep_merge_dicts(base.get(k, {}), override.get(k, {}))
                else:
                    result[k] = override.get(k)
            else:
                result[k] = base.get(k)
        return result

    def _compute_overrides(self, defaults: Dict[str, Any], full: Dict[str, Any]) -> Dict[str, Any]:
        """Compute a minimal overrides dict where values differ from defaults.

        For nested dicts, recurses. Keys present in `full` but equal to defaults are omitted.
        """
        overrides: Dict[str, Any] = {}
        for k, v in full.items():
            dval = defaults.get(k)
            if isinstance(v, dict) and isinstance(dval, dict):
                sub = self._compute_overrides(dval, v)
                if sub:
                    overrides[k] = sub
            else:
                # If key missing in defaults or value differs (tolerant comparison), include it
                if k not in defaults or not self._values_equal(dval, v):
                    overrides[k] = v
        return overrides

    def _values_equal(self, a: Any, b: Any) -> bool:
        """Tolerant equality check between default value a and candidate b.

        Tries to avoid treating values as different due only to type differences
        (for example, numeric strings vs ints). Falls back to strict equality.
        """
        # Direct equality first
        if a == b:
            return True

        # If either is None, already handled by direct equality
        try:
            # If types differ, attempt to coerce b to type(a) for comparison
            if a is not None and b is not None and type(a) != type(b):
                ta = type(a)
                tb = type(b)
                # Try numeric coercion
                if (isinstance(a, (int, float)) and isinstance(b, str)):
                    try:
                        return float(b) == float(a)
                    except Exception:
                        pass
                if (isinstance(a, str) and isinstance(b, (int, float))):
                    try:
                        return float(a) == float(b)
                    except Exception:
                        pass
                # Try bool-string variants
                if isinstance(a, bool) and isinstance(b, str):
                    low = b.strip().lower()
                    if low in ('true', 'false'):
                        return a == (low == 'true')
                if isinstance(b, bool) and isinstance(a, str):
                    low = a.strip().lower()
                    if low in ('true', 'false'):
                        return b == (low == 'true')
                # Fallback: compare string forms
                if str(a) == str(b):
                    return True
        except Exception:
            pass

        return False

    def _get_from_dict(self, data: Dict[str, Any], dotted_key: str, default: Any = None) -> Any:
        parts = dotted_key.split('.')
        cur = data
        for p in parts:
            if isinstance(cur, dict) and p in cur:
                cur = cur[p]
            else:
                return default
        return cur

    def _set_in_dict(self, data: Dict[str, Any], dotted_key: str, value: Any) -> None:
        parts = dotted_key.split('.')
        cur = data
        for p in parts[:-1]:
            if p not in cur or not isinstance(cur[p], dict):
                cur[p] = {}
            cur = cur[p]
        cur[parts[-1]] = value
    
    def _get_default_settings(self) -> Dict[str, Any]:
        """Get default application settings."""
        return {
            "FTPConnection": {
                "ftp_host": "",
                "ftp_port": "21",
                "ftp_user": "",
                "ftp_pass": "",
                "remote_path": "",
                "local_path": "",
                "passive_mode": True,
                "delete_after_download": False,
                "interval": 60,
                "EnableFileStability": False,
                "FileStabilityWait": 10
            },
            "EmailSettings": {
                "smtp_host": "",
                "smtp_port": "587",
                "smtp_user": "",
                "smtp_pass": "",
                "smtp_ssl": False,
                "email_from": "",
                "email_to": "",
                "notify_threshold": 15
            },
            "WorkSchedule": {
                "work_start": "08:00",
                "work_end": "17:00",
                "lunch_start": "12:00",
                "lunch_end": "13:00",
                "weekdays": ["Mon", "Tue", "Wed", "Thu", "Fri"]
            },
            "Application": {
                "MinimizeOnClose": True,
                "AutoStartMonitoring": False,
                "StartWithWindows": False,
                "Theme": "dark",
                # File stability and status notification defaults
                "EnableFileStability": False,
                "FileStabilityWait": 10,
                "EnableStatusNotifications": False,
                "LogLevel": "INFO",
                "MaxLogSize": 5,
                "KeepLogsDays": 30,
                "EnableFileLogging": True,
                # Long-term stability settings
                "ConnectionPooling": True,
                "MaxConnectionRetries": 3,
                "PeriodicCleanupInterval": 3600,  # 1 hour
                "ForcedConnectionReset": False,
                "MemoryOptimization": True
            }
        }
