"""Logging utility for the FTP tool."""

import os
import logging
from typing import Optional, Callable
from datetime import datetime
from PySide6.QtCore import QObject, Signal
import glob


class LogMessageRelay(QObject):
    """Relay log messages to GUI components."""
    
    message_logged = Signal(str)  # Signal for GUI updates
    
    def __init__(self):
        super().__init__()
        # Track the currently connected GUI callback so we avoid duplicate connects
        self._connected_callback: Optional[Callable] = None

    def set_gui_callback(self, callback: Callable):
        """Connect the Qt signal to a GUI callback.

        Using the Qt signal ensures that messages emitted from background threads
        are delivered to the GUI thread safely (queued connection).
        """
        # Connect signal to the provided callback. Make this idempotent so
        # repeated calls (e.g. on reconfigure) don't duplicate connections.
        try:
            if self._connected_callback is callback:
                return
            # If a different callback was previously connected, disconnect it.
            if self._connected_callback is not None:
                try:
                    self.message_logged.disconnect(self._connected_callback)
                except Exception:
                    # ignore any disconnect errors
                    pass
            self.message_logged.connect(callback)
            self._connected_callback = callback
        except Exception:
            # If anything goes wrong, clear the tracking reference to avoid stale state
            self._connected_callback = None

    def emit_message(self, message: str):
        """Emit log message to any connected GUI slots via Qt signal only."""
        # Emit the Qt signal; receivers will run on the appropriate thread.
        self.message_logged.emit(message)


class AppLogger:
    """Application logger with configurable file logging and GUI integration."""

    def __init__(self, name: str = "FTPTool"):
        self.name = name
        self.logger = logging.getLogger(name)
        self.logger.setLevel(logging.DEBUG)

        # GUI message relay
        self.message_relay = LogMessageRelay()

        # Prevent duplicate handlers
        if self.logger.handlers:
            self.logger.handlers.clear()
        # Create logs directory
        self.logs_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'logs')
        self.logs_dir = os.path.abspath(self.logs_dir)
        os.makedirs(self.logs_dir, exist_ok=True)

        # Base name for daily logs (date and suffixes handled by handler)
        self._log_basename = f"{self.name.lower()}"

        # Default settings
        self._file_logging_enabled = True
        self._log_level = logging.INFO
        # reduce default size to 5MB to save user's disk space
        self._max_log_size_mb = 5
        self._backup_count = 5
        self._console_logging_enabled = False  # Disabled for server deployment

        # Only setup file handler - no console logging for server deployment
        self._setup_file_handler()

    # --- Custom handler: daily files + size-based suffixes ---
    # Creates files like: <basename>_YYYY_MM_DD.log, and when that file
    # exceeds max size creates <basename>_YYYY_MM_DD_1.log, _2.log, etc.
    class _DailySizeRotatingHandler(logging.Handler):
        def __init__(self, logs_dir: str, base_name: str, maxBytes: int, backupCount: int = 5, encoding: Optional[str] = 'utf-8'):
            super().__init__()
            self.logs_dir = os.path.abspath(logs_dir)
            self.base_name = base_name
            self.maxBytes = maxBytes
            self.backupCount = backupCount
            self.encoding = encoding
            self.current_date = datetime.now().strftime('%Y_%m_%d')
            self.baseFilename = os.path.join(self.logs_dir, f"{self.base_name}_{self.current_date}.log")
            # Ensure file exists
            os.makedirs(self.logs_dir, exist_ok=True)
            self.stream = open(self.baseFilename, 'a', encoding=self.encoding)

        def emit(self, record: logging.LogRecord) -> None:
            try:
                msg = self.format(record)
                if self.shouldRollover(len(msg.encode(self.encoding or 'utf-8'))):
                    self.doRollover()
                self.stream.write(msg + '\n')
                self.stream.flush()
            except Exception:
                self.handleError(record)

        def shouldRollover(self, incoming_size: int) -> bool:
            try:
                # If the date rolled over, require rollover
                now_date = datetime.now().strftime('%Y_%m_%d')
                if now_date != self.current_date:
                    return True

                if not os.path.exists(self.baseFilename):
                    return False
                current_size = os.path.getsize(self.baseFilename)
                if current_size + incoming_size >= self.maxBytes:
                    return True
            except Exception:
                return False
            return False

        def doRollover(self) -> None:
            try:
                # Close current stream
                try:
                    self.stream.close()
                except Exception:
                    pass

                new_date = datetime.now().strftime('%Y_%m_%d')
                # If date changed, start a fresh dated file
                if new_date != self.current_date:
                    self.current_date = new_date
                    self.baseFilename = os.path.join(self.logs_dir, f"{self.base_name}_{self.current_date}.log")
                    self.stream = open(self.baseFilename, 'a', encoding=self.encoding)
                    return

                # Size-based rollover within same date: find next suffix
                pattern = os.path.join(self.logs_dir, f"{self.base_name}_{self.current_date}*.log")
                existing = sorted(glob.glob(pattern))
                # determine next index by parsing suffixes
                max_idx = 0
                for p in existing:
                    # match files ending with _N.log
                    base = os.path.basename(p)
                    parts = os.path.splitext(base)[0].split('_')
                    # parts example: [basename, YYYY, MM, DD] or [basename, YYYY, MM, DD, N]
                    if len(parts) >= 5:
                        try:
                            idx = int(parts[-1])
                            if idx > max_idx:
                                max_idx = idx
                        except Exception:
                            continue

                next_idx = max_idx + 1
                new_name = os.path.join(self.logs_dir, f"{self.base_name}_{self.current_date}_{next_idx}.log")
                try:
                    os.replace(self.baseFilename, new_name)
                except Exception:
                    # If replace fails, try rename
                    try:
                        os.rename(self.baseFilename, new_name)
                    except Exception:
                        pass

                # Re-open a fresh file for continued logging
                self.stream = open(self.baseFilename, 'a', encoding=self.encoding)

                # Cleanup older backups beyond backupCount (keep most recent N)
                if self.backupCount is not None and self.backupCount > 0:
                    # find all rotated files for today with suffix
                    rotated = []
                    for p in glob.glob(os.path.join(self.logs_dir, f"{self.base_name}_{self.current_date}_*.log")):
                        rotated.append((os.path.getmtime(p), p))
                    rotated.sort(reverse=True)
                    for _, oldp in rotated[self.backupCount:]:
                        try:
                            os.remove(oldp)
                        except Exception:
                            pass
            except Exception:
                # swallow errors to avoid crashing the app
                pass


    def set_gui_callback(self, callback: Callable):
        """Set GUI callback for log message display."""
        self.message_relay.set_gui_callback(callback)

    def configure(self, settings: dict):
        """Configure logger with settings from the application."""
        # Update settings
        self._file_logging_enabled = settings.get('EnableFileLogging', True)
        self._console_logging_enabled = False  # Always disabled for server deployment

        # Parse log level
        level_str = settings.get('LogLevel', 'INFO').upper()
        level_map = {
            'DEBUG': logging.DEBUG,
            'INFO': logging.INFO,
            'WARNING': logging.WARNING,
            'ERROR': logging.ERROR,
            'CRITICAL': logging.CRITICAL
        }
        self._log_level = level_map.get(level_str, logging.INFO)

        # Max log size in MB (default to 5 to reduce disk usage)
        self._max_log_size_mb = int(settings.get('MaxLogSize', 5))

        # Reconfigure handlers
        self._reconfigure_handlers()

    def _setup_file_handler(self):
        """Setup file logging handler with rotation."""
        if not self._file_logging_enabled:
            return
        # Use the daily+size rotating handler
        max_bytes = int(self._max_log_size_mb) * 1024 * 1024
        file_handler = self._DailySizeRotatingHandler(
            logs_dir=self.logs_dir,
            base_name=self._log_basename,
            maxBytes=max_bytes,
            backupCount=self._backup_count,
            encoding='utf-8'
        )

        # Enhanced formatter for enterprise logging
        file_formatter = logging.Formatter(
            '%(asctime)s | %(levelname)-8s | %(name)s | %(funcName)s:%(lineno)d | %(message)s',
            datefmt='%Y-%m-%d %H:%M:%S'
        )
        file_handler.setFormatter(file_formatter)
        file_handler.setLevel(self._log_level)
        self.logger.addHandler(file_handler)
        self._file_handler = file_handler
        
        # Long-term stability: Add periodic log cleanup
        self._setup_log_cleanup_timer()

    def get_current_log_file(self) -> str:
        """Return the full path to the current log file."""
        if hasattr(self, '_file_handler'):
            try:
                return os.path.abspath(self._file_handler.baseFilename)
            except Exception:
                pass
        return os.path.join(self.logs_dir, getattr(self, '_log_filename', f"{self.name.lower()}.log"))

    def _reconfigure_handlers(self):
        """Reconfigure existing handlers with new settings."""
        # Remove existing file handler if present
        if hasattr(self, '_file_handler'):
            self.logger.removeHandler(self._file_handler)
            self._file_handler.close()
            delattr(self, '_file_handler')

        # Add new file handler if enabled
        if self._file_logging_enabled:
            self._setup_file_handler()

        # Update logger level
        self.logger.setLevel(self._log_level)

    def get_logger(self) -> logging.Logger:
        """Get the configured logger instance."""
        return self.logger

    def log_app_start(self):
        """Log application startup."""
        self.logger.info("=" * 80)
        self.logger.info(f"FTP MONITORING SERVICE STARTED - {datetime.now()}")
        self.logger.info("=" * 80)

    def log_app_stop(self):
        """Log application shutdown."""
        self.logger.info("=" * 80)
        self.logger.info(f"FTP MONITORING SERVICE STOPPED - {datetime.now()}")
        self.logger.info("=" * 80)

    def log_ftp_operation(self, operation: str, details: str, status: str = "SUCCESS"):
        """Log FTP operations with structured format."""
        message = f"FTP_OP | {operation} | {status} | {details}"
        self.logger.info(message)

        # Send user-friendly message to GUI
        gui_message = f"[{datetime.now().strftime('%H:%M:%S')}] FTP {operation}: {details}"
        self.message_relay.emit_message(gui_message)

    def log_file_operation(self, action: str, filename: str, size: int = None, status: str = "SUCCESS"):
        """Log file operations with detailed information."""
        size_info = f" | Size: {size} bytes" if size is not None else ""
        message = f"FILE_OP | {action} | {status} | File: {filename}{size_info}"
        self.logger.info(message)

        # Send user-friendly message to GUI
        size_display = f" ({size} bytes)" if size is not None else ""
        gui_message = f"[{datetime.now().strftime('%H:%M:%S')}] {action}: {filename}{size_display}"
        self.message_relay.emit_message(gui_message)

    def log_monitoring_event(self, event: str, details: str = ""):
        """Log monitoring events."""
        message = f"MONITOR | {event} | {details}"
        self.logger.info(message)

        # Send user-friendly message to GUI
        gui_message = f"[{datetime.now().strftime('%H:%M:%S')}] {event}"
        if details:
            gui_message += f": {details}"
        self.message_relay.emit_message(gui_message)

    def log_connection_event(self, event: str, host: str, status: str = "SUCCESS"):
        """Log connection events."""
        message = f"CONNECT | {event} | {status} | Host: {host}"
        self.logger.info(message)

        # Send user-friendly message to GUI
        gui_message = f"[{datetime.now().strftime('%H:%M:%S')}] Connection {event}: {host} ({status})"
        self.message_relay.emit_message(gui_message)

    def log_error_details(self, operation: str, error_msg: str, context: str = ""):
        """Log detailed error information."""
        context_info = f" | Context: {context}" if context else ""
        message = f"ERROR | {operation} | {error_msg}{context_info}"
        self.logger.error(message)

        # Send user-friendly error message to GUI
        gui_message = f"[{datetime.now().strftime('%H:%M:%S')}] ERROR in {operation}: {error_msg}"
        self.message_relay.emit_message(gui_message)

    def _setup_log_cleanup_timer(self):
        """Setup periodic log cleanup for long-term operation."""
        import threading
        import glob
        import time
        from datetime import timedelta
        
        def cleanup_old_logs():
            """Clean up old log files beyond retention period."""
            try:
                # Clean logs older than 30 days by default
                keep_days = 30
                cutoff_time = time.time() - (keep_days * 24 * 3600)
                
                log_pattern = os.path.join(self.logs_dir, "*.log*")
                for log_file in glob.glob(log_pattern):
                    try:
                        if os.path.getmtime(log_file) < cutoff_time:
                            # Don't delete current active log
                            if log_file != self.get_current_log_file():
                                os.remove(log_file)
                                self.logger.info(f"Cleaned up old log file: {log_file}")
                    except Exception as e:
                        self.logger.warning(f"Failed to clean log file {log_file}: {e}")
                        
            except Exception as e:
                self.logger.error(f"Log cleanup failed: {e}")
                
            # Schedule next cleanup in 24 hours
            timer = threading.Timer(24 * 3600, cleanup_old_logs)
            timer.daemon = True
            timer.start()
        
        # Start cleanup timer
        timer = threading.Timer(3600, cleanup_old_logs)  # First cleanup after 1 hour
        timer.daemon = True
        timer.start()


# Global logger instance
_app_logger: Optional[AppLogger] = None


def get_logger(name: str = "FTPTool") -> logging.Logger:
    """Get the application logger instance."""
    global _app_logger
    if _app_logger is None:
        _app_logger = AppLogger(name)
    return _app_logger.get_logger()


def get_current_log_file() -> str:
    """Return the full path to the current log file for external callers.

    Ensures an AppLogger instance exists and delegates to it.
    """
    global _app_logger
    if _app_logger is None:
        _app_logger = AppLogger()
    return _app_logger.get_current_log_file()


def configure_logging(settings: dict):
    """Configure logging with application settings."""
    global _app_logger
    if _app_logger is None:
        _app_logger = AppLogger()
    _app_logger.configure(settings)


def log_app_start():
    """Log application startup."""
    global _app_logger
    if _app_logger is None:
        _app_logger = AppLogger()
    _app_logger.log_app_start()


def log_app_stop():
    """Log application shutdown."""
    global _app_logger
    if _app_logger is not None:
        _app_logger.log_app_stop()


def set_gui_callback(callback: Callable):
    """Set GUI callback for log messages."""
    global _app_logger
    if _app_logger is None:
        _app_logger = AppLogger()
    _app_logger.set_gui_callback(callback)


def log_ftp_operation(operation: str, details: str, status: str = "SUCCESS"):
    """Log FTP operation."""
    global _app_logger
    if _app_logger is None:
        _app_logger = AppLogger()
    _app_logger.log_ftp_operation(operation, details, status)


def log_file_operation(action: str, filename: str, size: int = None, status: str = "SUCCESS"):
    """Log file operation."""
    global _app_logger
    if _app_logger is None:
        _app_logger = AppLogger()
    _app_logger.log_file_operation(action, filename, size, status)


def log_monitoring_event(event: str, details: str = ""):
    """Log monitoring event."""
    global _app_logger
    if _app_logger is None:
        _app_logger = AppLogger()
    _app_logger.log_monitoring_event(event, details)


def log_connection_event(event: str, host: str, status: str = "SUCCESS"):
    """Log connection event."""
    global _app_logger
    if _app_logger is None:
        _app_logger = AppLogger()
    _app_logger.log_connection_event(event, host, status)


def log_error_details(operation: str, error_msg: str, context: str = ""):
    """Log error with details."""
    global _app_logger
    if _app_logger is None:
        _app_logger = AppLogger()
    _app_logger.log_error_details(operation, error_msg, context)
