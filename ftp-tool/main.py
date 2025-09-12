"""New modular main launcher for FTP tool."""
import sys
import signal
from PySide6.QtWidgets import QApplication

try:
    # Try modular import first
    from src.gui.main_window import FTPDownloaderApp
except ImportError as e:
    print(f"Error importing GUI: {e}")
    sys.exit(1)


def main(argv=None):
    """Main application entry point."""
    argv = argv or sys.argv
    # Simple arg handling for tray mode
    tray_mode = False
    if '-tray' in argv or '--tray' in argv:
        tray_mode = True
        # Remove from args passed to QApplication to avoid confusion
        argv = [a for a in argv if a not in ('-tray', '--tray')]

    app = QApplication(argv)

    # Install simple signal handlers to ensure graceful shutdown in long-running deployments
    def _handle_signal(signum, frame):
        # Ask Qt to quit the event loop which will trigger application cleanup
        try:
            QApplication.quit()
        except Exception:
            pass

    for s in (signal.SIGINT, signal.SIGTERM):
        try:
            signal.signal(s, _handle_signal)
        except Exception:
            # Some platforms or embedding contexts might not allow setting handlers; ignore
            pass

    window = FTPDownloaderApp()
    if not tray_mode:
        window.show()
    return app.exec()


if __name__ == "__main__":
    sys.exit(main())
