# FTP Tool Build Documentation

## Cross-Platform Building

This project includes a comprehensive build system that creates standalone executables for Windows, macOS, and Linux.

### Quick Start

#### Windows
```cmd
build.bat
```

#### Linux/macOS
```bash
chmod +x build.sh
./build.sh
```

#### Python (All Platforms)
```bash
python build.py
```

### Build Options

- `--console`: Build console version (shows terminal window)
- `--debug`: Enable debug mode for troubleshooting
- `--no-clean`: Skip cleaning previous build artifacts

Examples:
```bash
# Debug build with console
python build.py --console --debug

# Clean build (default)
python build.py

# Keep previous build artifacts
python build.py --no-clean
```

### Prerequisites

1. **Python 3.8+** with pip
2. **Required packages** (auto-installed by build script):
   - PyInstaller 5.0+
   - PySide6 6.4+

3. **Optional** (for smaller executables):
   - UPX compressor: https://upx.github.io/

### Build Process

The build script performs these steps:

1. **Clean**: Removes previous build artifacts
2. **Dependencies**: Checks and installs required packages
3. **Spec File**: Creates PyInstaller specification
4. **Build**: Compiles standalone executable
5. **Package**: Creates distribution archive
6. **Scripts**: Generates launcher scripts

### Output Structure

```
dist/
├── FTPTool-{platform}/
│   ├── FTPTool(.exe)           # Main executable
│   ├── AppSettings.json        # Default settings
│   ├── README.md              # Documentation
│   ├── logs/                  # Log directory
│   └── start_ftp_tool.{sh|bat} # Launcher script
└── FTPTool-{platform}-v1.0.{zip|tar.gz}  # Distribution archive
```

### Platform-Specific Notes

#### Windows
- Creates `.exe` executable
- Includes `.bat` launcher script
- Packages as `.zip` archive
- Requires Windows 10+ for best compatibility

#### macOS
- Creates native macOS executable
- Includes `.sh` launcher script  
- Packages as `.tar.gz` archive
- May require code signing for distribution

#### Linux
- Creates native Linux executable
- Includes `.sh` launcher script
- Packages as `.tar.gz` archive
- Works on most distributions with glibc 2.17+

### Troubleshooting

#### Build Fails
1. Ensure Python 3.8+ is installed
2. Check internet connectivity for package downloads
3. Run with `--debug` flag for detailed error information
4. Try `--no-clean` to preserve build artifacts for inspection

#### Large Executable Size
1. Install UPX compressor for automatic compression
2. Review `excludes` list in build script spec file
3. Consider using `--console` mode to reduce GUI overhead

#### Runtime Issues
1. Test with `--console` build to see error messages
2. Check log files in the `logs/` directory
3. Ensure all dependencies are included in PyInstaller spec

### Distribution

The generated archives are ready for distribution and include:
- Standalone executable (no Python installation required)
- All necessary dependencies bundled
- Default configuration files
- Launcher scripts for easy execution
- Documentation

### Development

To modify the build process:

1. **Build Script**: Edit `build.py` for main build logic
2. **Spec File**: Customize PyInstaller options in generated `.spec` file
3. **Platform Scripts**: Modify `build.sh` or `build.bat` for platform-specific behavior
4. **Dependencies**: Update `requirements-build.txt` for build requirements

### Continuous Integration

For automated builds, use:

```yaml
# Example GitHub Actions workflow
- name: Build FTP Tool
  run: |
    python -m pip install -r requirements-build.txt
    python build.py --console
```

The build system is designed to work in CI/CD environments with minimal configuration.
