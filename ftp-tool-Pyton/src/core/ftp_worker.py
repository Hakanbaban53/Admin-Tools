"""FTP Worker module for handling FTP operations in background threads."""

import os
import time
import threading
from ftplib import FTP, error_perm
import smtplib
from email.mime.text import MIMEText
from datetime import datetime, time as dt_time

from PySide6.QtCore import QObject, Signal

from ..utils.logger import (
    log_monitoring_event,
    log_connection_event,
    log_file_operation,
    log_error_details,
)


class FtpWorker(QObject):
    """
    Worker thread for handling FTP operations to prevent GUI freezing.
    Emits signals to update the GUI safely from the background thread.
    """
    log_message = Signal(str)
    status_update = Signal(str)
    download_count_update = Signal(int)
    finished = Signal()

    def __init__(self, settings):
        super().__init__()
        self.settings = settings
        self.is_running = False
        self.stop_event = threading.Event()
        self.last_download_time = time.time()
        self.notified_no_download = False
        self.last_notification_time = None
        
        # Long-term stability optimizations
        self._ftp_connection = None
        self._connection_failures = 0
        self._max_connection_failures = 3
        self._last_cleanup_time = time.time()
        self._cleanup_interval = 3600  # 1 hour cleanup cycle
        self._cycle_count = 0
        # Cache whether MLSD is supported by the connected server.
        # None = unknown, True = supported, False = not supported
        self._mlsd_supported = None
        # Event set when the worker has completed cleanup and is fully stopped
        self._stopped_event = threading.Event()

    def run(self):
        """The main loop for the background download thread."""
        self.is_running = True
        while self.is_running:
            try:
                self._cycle_count += 1
                interval_seconds = int(self.settings.get("interval", 60))
                msg = f"Starting download cycle #{self._cycle_count}. Next run in {interval_seconds} seconds."
                self.log_message.emit(msg)
                log_monitoring_event("MONITORING_CYCLE_START", msg)
                self.status_update.emit("Downloading...")

                count = self.perform_ftp_download()
                self.download_count_update.emit(count)
                if count > 0:
                    self.last_download_time = time.time()
                    self.notified_no_download = False
                    self._connection_failures = 0  # Reset failure counter on success

                msg = f"Download cycle #{self._cycle_count} finished. Waiting for next interval."
                self.log_message.emit(msg)
                log_monitoring_event("MONITORING_CYCLE_END", msg)
                self.status_update.emit("Waiting for next run...")

                # Periodic cleanup for long-term stability
                self._perform_periodic_cleanup()

                self.stop_event.wait(interval_seconds)
            except Exception as e:
                self._connection_failures += 1
                err = f"An error occurred in download cycle #{self._cycle_count}: {e}"
                self.log_message.emit(f"ERROR: {err}")
                log_error_details("DOWNLOAD_LOOP", str(e))
                
                # Auto-recovery logic
                if self._connection_failures >= self._max_connection_failures:
                    self._reset_connections()
                
                self.stop_event.wait(60)
            finally:
                if self.is_running:
                    self.check_monitoring_and_notify()

        # Cleanup on exit
        self._cleanup_connections()
        # Signal any waiters that we are fully stopped
        try:
            self._stopped_event.set()
        except Exception:
            pass
        self.finished.emit()

    def stop(self):
        """Stops the monitoring loop."""
        self.is_running = False
        self.stop_event.set()
        self._cleanup_connections()

    def stop_and_wait(self, timeout: float = None) -> bool:
        """Signal the worker to stop and wait for cleanup to finish.

        Args:
            timeout: seconds to wait (None = block indefinitely)

        Returns:
            True if the worker finished cleanup within timeout, False otherwise.
        """
        try:
            # Ensure the stopped event is cleared before stopping
            try:
                self._stopped_event.clear()
            except Exception:
                pass

            # Signal stop
            self.is_running = False
            self.stop_event.set()

            # Wait for the worker to signal that cleanup is complete
            return self._stopped_event.wait(timeout)
        except Exception:
            return False

    def perform_ftp_download(self):
        """Connects to FTP and downloads files. Returns the count of downloaded files."""
        download_count = 0
        ftp = None
        try:
            # Try to reuse existing connection first
            ftp = self._get_or_create_ftp_connection()
            if not ftp:
                return 0
                
            log_connection_event("CONNECT", self.settings['ftp_host'], "SUCCESS")
            download_count = self.download_dir_recursive(ftp, self.settings['remote_path'], self.settings['local_path'])
            
        except error_perm as e:
            self.log_message.emit(f"FTP Permission Error: {e}. Check path and permissions.")
            log_error_details("FTP_PERMISSION", str(e))
            self._reset_connections()  # Reset on permission errors
        except Exception as e:
            self.log_message.emit(f"FTP ERROR: {e}")
            log_error_details("FTP_GENERAL_ERROR", str(e))
            self._reset_connections()  # Reset on general errors
        return download_count

    def download_dir_recursive(self, ftp, remote_path, local_path):
        """Recursively downloads a directory from FTP."""
        count = 0
        try:
            ftp.cwd(remote_path)
            if not os.path.exists(local_path):
                os.makedirs(local_path)
            # Try MLSD if we haven't determined server capability yet or if known supported
            items = []
            if self._mlsd_supported is None or self._mlsd_supported:
                try:
                    items = list(ftp.mlsd())
                    # If MLSD succeeded, record support
                    self._mlsd_supported = True
                except Exception as e:
                    # MLSD not supported by this server; cache and fall back to NLST
                    self._mlsd_supported = False
                    # Log the MLSD failure only once to avoid repeated spam
                    self.log_message.emit(f"MLSD not supported, falling back to NLST (first observed: {e})")
                    log_monitoring_event("MLSD_FAILED", str(e))

            if not items:
                try:
                    names = ftp.nlst()
                except Exception as e2:
                    self.log_message.emit(f"NLST also failed: {e2}")
                    log_error_details("NLST_FAILED", str(e2))
                    names = []
                for name in names:
                    if name in ['.', '..']:
                        continue
                    facts = {'type': 'file'}
                    try:
                        ftp.cwd(name)
                        facts['type'] = 'dir'
                        ftp.cwd('..')
                    except Exception:
                        facts['type'] = 'file'
                    items.append((name, facts))

            # Throttle per-cycle skip logs to avoid spamming logs when many files are present
            skip_log_limit = int(self.settings.get('skip_log_limit', 5)) if self.settings else 5
            skip_logs_shown = 0
            skip_total = 0

            for name, facts in items:
                if name in [".", ".."]:
                    continue
                remote_item_path = f"{remote_path}/{name}".replace('//', '/')
                local_item_path = os.path.join(local_path, name)

                if facts.get('type') == 'dir':
                    count += self.download_dir_recursive(ftp, remote_item_path, local_item_path)
                elif facts.get('type') == 'file':
                    # Determine remote size when possible so we can skip already-downloaded files
                    remote_size = None
                    try:
                        remote_size = ftp.size(remote_item_path)
                    except Exception:
                        remote_size = None

                    # If local file exists and size matches remote, skip download
                    try:
                        if os.path.exists(local_item_path) and remote_size is not None:
                            local_size = os.path.getsize(local_item_path)
                            if local_size == remote_size:
                                skip_total += 1
                                # Only emit a small number of skip messages per cycle
                                if skip_logs_shown < skip_log_limit:
                                    skip_msg = f"Skipping (exists): {remote_item_path}"
                                    self.log_message.emit(skip_msg)
                                    log_file_operation("DOWNLOAD_SKIPPED", remote_item_path, remote_size, "SKIPPED")
                                    skip_logs_shown += 1
                                # If configured, delete the remote file even when skipping
                                if self.settings.get('delete_after_download'):
                                    try:
                                        ftp.delete(remote_item_path)
                                        self.log_message.emit(f"DELETED remote file (skipped): {remote_item_path}")
                                        log_file_operation("DELETE_REMOTE", remote_item_path, None, "SUCCESS")
                                    except Exception as e:
                                        self.log_message.emit(f"ERROR deleting remote file {remote_item_path}: {e}")
                                        log_error_details("DELETE_REMOTE", str(e), remote_item_path)
                                continue
                    except Exception:
                        # If any error checking sizes, proceed to download (safer default)
                        pass

                    self.log_message.emit(f"Downloading: {remote_item_path}")
                    log_file_operation("DOWNLOAD_START", remote_item_path, None, "INIT")
                    try:
                        # If configured, wait until file is stable before downloading.
                        try:
                            enable_stability = bool(self.settings.get('EnableFileStability', False))
                            wait_seconds = int(self.settings.get('FileStabilityWait', 0))
                        except Exception:
                            enable_stability = False
                            wait_seconds = 0

                        if enable_stability and wait_seconds > 0:
                            # Adaptive per-file polling: require two consecutive equal size readings
                            # Prefer MLSD 'size' fact when available, else use SIZE command.
                            try:
                                initial_size = None
                                if facts and isinstance(facts, dict) and facts.get('size') is not None:
                                    try:
                                        initial_size = int(facts.get('size'))
                                    except Exception:
                                        initial_size = None
                                else:
                                    try:
                                        initial_size = ftp.size(remote_item_path)
                                    except Exception:
                                        initial_size = None
                            except Exception:
                                initial_size = None

                            # If we can't obtain any size info, skip stability check (assume stable)
                            if initial_size is None:
                                pass
                            else:
                                start_t = time.time()
                                stable = False
                                prev_size = initial_size
                                stable_count = 1
                                check_interval = 1.0
                                required_stable_count = 2
                                while time.time() - start_t < wait_seconds:
                                    try:
                                        cur_size = ftp.size(remote_item_path)
                                    except Exception:
                                        cur_size = None

                                    if cur_size is not None and cur_size == prev_size:
                                        stable_count += 1
                                    else:
                                        stable_count = 1 if cur_size is not None else 0
                                    prev_size = cur_size

                                    if stable_count >= required_stable_count:
                                        stable = True
                                        break

                                    time.sleep(check_interval)

                                if not stable:
                                    # Timeout waiting for stability — skip this file this cycle
                                    msg = f"Unstable file skipped after {wait_seconds}s: {remote_item_path}"
                                    self.log_message.emit(msg)
                                    log_monitoring_event("FILE_UNSTABLE_TIMEOUT", f"{remote_item_path}")
                                    continue
                                else:
                                    log_monitoring_event("FILE_STABLE", f"{remote_item_path} size={prev_size}")

                        # Download to a temporary file first to avoid partial-file races
                        temp_local = local_item_path + '.part'
                        with open(temp_local, 'wb') as f:
                                    # Per-file watchdog
                                    per_file_timeout = int(self.settings.get('per_file_timeout', 120)) if self.settings else 120
                                    last_progress = {'t': time.time()}

                                    def _write_and_count(data):
                                        # Update last progress timestamp
                                        last_progress['t'] = time.time()
                                        # Allow transfer to be aborted promptly when stop() is requested
                                        if self.stop_event.is_set():
                                            raise InterruptedError("Transfer aborted by stop request")
                                        f.write(data)

                                    # Set a socket-level timeout if available to avoid indefinite blocking
                                    try:
                                        sock = getattr(ftp, 'sock', None)
                                        if sock is not None:
                                            try:
                                                sock.settimeout(max(10, per_file_timeout // 2))
                                            except Exception:
                                                pass
                                    except Exception:
                                        pass

                                    # Start retrbinary in a way that we can watchdog progress
                                    # Use a small loop to check for stalls: retrbinary will call our callback
                                    # but some FTP implementations block; we rely on socket timeout + callback progress
                                    try:
                                        ftp.retrbinary(f'RETR {remote_item_path}', _write_and_count)
                                    except Exception:
                                        # If retrbinary raised due to socket timeout or our InterruptedError, we re-raise
                                        raise
                        # Atomically move into place
                        try:
                            os.replace(temp_local, local_item_path)
                        except Exception:
                            # fallback to rename
                            os.rename(temp_local, local_item_path)
                        # determine size if possible
                        try:
                            size = os.path.getsize(local_item_path)
                        except Exception:
                            size = None
                        count += 1
                        log_file_operation("DOWNLOAD_COMPLETE", remote_item_path, size, "SUCCESS")
                        if self.settings.get('delete_after_download'):
                            try:
                                ftp.delete(remote_item_path)
                                self.log_message.emit(f"DELETED remote file: {remote_item_path}")
                                log_file_operation("DELETE_REMOTE", remote_item_path, None, "SUCCESS")
                            except Exception as e:
                                self.log_message.emit(f"ERROR deleting remote file {remote_item_path}: {e}")
                                log_error_details("DELETE_REMOTE", str(e), remote_item_path)
                    except Exception as e:
                        self.log_message.emit(f"Error downloading {remote_item_path}: {e}")
                        log_error_details("DOWNLOAD_ERROR", str(e), remote_item_path)
        except Exception as e:
            self.log_message.emit(f"Error processing directory {remote_path}: {e}")
            log_error_details("PROCESS_DIR_ERROR", str(e), remote_path)
        return count

    def send_email_notification(self, subject, body):
        """Send email notification."""
        try:
            host = self.settings.get('smtp_host')
            port = int(self.settings.get('smtp_port', 587))
            user = self.settings.get('smtp_user')
            pwd = self.settings.get('smtp_pass')
            use_ssl = bool(self.settings.get('smtp_ssl', False))
            from_addr = self.settings.get('email_from')
            to_addrs = [a.strip() for a in (self.settings.get('email_to') or '').split(';') if a.strip()]
            if not host or not from_addr or not to_addrs:
                self.log_message.emit('Email settings incomplete, cannot send notification.')
                log_monitoring_event('EMAIL_NOTIFICATION_SKIPPED', 'Email settings incomplete')
                return

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
            self.log_message.emit('Notification email sent to: ' + ','.join(to_addrs))
            # Also write structured log to file
            log_monitoring_event('EMAIL_SENT', f"Recipients: {','.join(to_addrs)}")
            self.last_notification_time = time.time()
            self.notified_no_download = True
        except Exception as e:
            self.log_message.emit('Failed to send notification email: ' + str(e))
            log_error_details('EMAIL_SEND_FAILED', str(e))

    def check_monitoring_and_notify(self):
        """Check monitoring conditions and send notifications if needed."""
        try:
            now = datetime.now()
            weekday_map = {0: 'Mon', 1: 'Tue', 2: 'Wed', 3: 'Thu', 4: 'Fri', 5: 'Sat', 6: 'Sun'}
            allowed_days = set(self.settings.get('weekdays', []))
            today = weekday_map[now.weekday()]
            if allowed_days and today not in allowed_days:
                return

            def parse_time_str(s):
                try:
                    h, m = map(int, s.split(':'))
                    return dt_time(h, m)
                except Exception:
                    return None

            ws = parse_time_str(self.settings.get('work_start', '00:00'))
            we = parse_time_str(self.settings.get('work_end', '23:59'))
            ls = parse_time_str(self.settings.get('lunch_start', '00:00'))
            le = parse_time_str(self.settings.get('lunch_end', '00:00'))

            cur_t = now.time()
            in_work = True
            if ws and we:
                in_work = (ws <= cur_t <= we) if ws <= we else (cur_t >= ws or cur_t <= we)
            in_lunch = False
            if ls and le:
                in_lunch = (ls <= cur_t <= le) if ls <= le else (cur_t >= ls or cur_t <= le)

            if not in_work or in_lunch:
                return

            threshold_min = int(self.settings.get('notify_threshold', 15))
            elapsed = time.time() - self.last_download_time
            if elapsed > (threshold_min * 60) and not self.notified_no_download:
                subj = 'FTP Monitor: No downloads detected'
                body = f'No downloads have occurred in the last {threshold_min} minutes (elapsed {int(elapsed/60)} min).'
                self.send_email_notification(subj, body)
        except Exception as e:
            self.log_message.emit('Error in monitoring check: ' + str(e))

    def _get_or_create_ftp_connection(self):
        """Get existing FTP connection or create new one with retry logic."""
        max_retries = 3
        for attempt in range(max_retries):
            try:
                # Test existing connection
                if self._ftp_connection:
                    try:
                        # Test if connection is still alive
                        self._ftp_connection.pwd()
                        return self._ftp_connection
                    except Exception:
                        # Connection is dead, clean it up
                        self._cleanup_connections()
                
                # Create new connection
                conn_msg = f"Connecting to {self.settings['ftp_host']} (attempt {attempt + 1}/{max_retries})..."
                self.log_message.emit(conn_msg)
                log_connection_event("CONNECT_START", self.settings['ftp_host'], f"ATTEMPT_{attempt + 1}")
                
                ftp = FTP()
                ftp.connect(self.settings['ftp_host'], int(self.settings['ftp_port']), timeout=30)
                ftp.login(self.settings['ftp_user'], self.settings['ftp_pass'])
                ftp.set_pasv(self.settings.get('passive_mode', True))
                ftp.encoding = "utf-8"
                
                self._ftp_connection = ftp
                self.log_message.emit("FTP connection successful.")
                return ftp
                
            except Exception as e:
                self.log_message.emit(f"FTP connection attempt {attempt + 1} failed: {e}")
                log_error_details("FTP_CONNECTION", str(e), f"Attempt {attempt + 1}")
                if attempt < max_retries - 1:
                    time.sleep(5)  # Wait before retry
                    
        return None

    def _reset_connections(self):
        """Reset FTP connections due to errors."""
        self.log_message.emit("Resetting FTP connections due to errors...")
        log_monitoring_event("CONNECTION_RESET", f"After {self._connection_failures} failures")
        self._cleanup_connections()
        self._connection_failures = 0

    def _cleanup_connections(self):
        """Clean up FTP connections."""
        if self._ftp_connection:
            try:
                self._ftp_connection.quit()
            except Exception:
                try:
                    self._ftp_connection.close()
                except Exception:
                    pass
            finally:
                self._ftp_connection = None

    def _perform_periodic_cleanup(self):
        """Perform periodic cleanup for long-term stability."""
        current_time = time.time()
        if current_time - self._last_cleanup_time >= self._cleanup_interval:
            self.log_message.emit(f"Performing periodic cleanup (cycle #{self._cycle_count})...")
            log_monitoring_event("PERIODIC_CLEANUP", f"Cycle {self._cycle_count}, Runtime: {int((current_time - self._last_cleanup_time)/3600)}h")
            
            # Reset connections periodically to prevent stale connections
            self._cleanup_connections()
            
            # Force garbage collection to manage memory
            import gc
            gc.collect()
            
            self._last_cleanup_time = current_time
