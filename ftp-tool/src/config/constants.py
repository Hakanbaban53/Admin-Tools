"""Application constants and settings.

This module computes paths in a way that works when the app is run from the
repository (development) and when it is packaged as a frozen executable
(PyInstaller / similar). The configuration file is stored next to the
application entry point (script or exe) so that launching from Explorer or
via startup will still find the settings file.
"""

import os
import sys
from pathlib import Path


# Resolve the base application directory robustly. When frozen (PyInstaller,
# cx_Freeze, etc.) use the executable location; otherwise place config next to
# the project root (two levels up from this file: src/config -> ftp-tool).
def _get_app_base_dir() -> str:
	try:
		if getattr(sys, "frozen", False):
			# Running from a bundled executable
			return os.path.dirname(sys.executable)
	except Exception:
		pass
	# Not frozen: return the repository/app folder (two levels up)
	return str(Path(__file__).resolve().parents[2])


# Configuration file location (absolute path)
CONFIG_FILE = os.path.join(_get_app_base_dir(), "AppSettings.json")

# Application information
APP_NAME = "FTP File Monitor"
APP_DESCRIPTION = "FTP File Monitor"
APP_VERSION = "1.0.0"
