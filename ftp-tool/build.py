#!/usr/bin/env python3
"""
Cross-platform build script for FTP Tool
Builds standalone executables for Windows, macOS, and Linux
"""

import os
import sys
import shutil
import subprocess
import platform
import argparse
from pathlib import Path


class CrossPlatformBuilder:
    """Cross-platform builder for FTP Tool."""
    
    def __init__(self):
        self.project_root = Path(__file__).parent
        self.dist_dir = self.project_root / "dist"
        self.build_dir = self.project_root / "build"
        self.spec_file = self.project_root / "ftp_tool.spec"
        
        # Platform info
        self.current_platform = platform.system().lower()
        self.platform_map = {
            'windows': 'win',
            'darwin': 'mac', 
            'linux': 'linux'
        }
        
    def clean_build(self):
        """Clean previous build artifacts."""
        print("🧹 Cleaning previous build artifacts...")
        
        dirs_to_clean = [self.dist_dir, self.build_dir]
        files_to_clean = [self.spec_file, self.project_root / "*.pyc"]
        
        for dir_path in dirs_to_clean:
            if dir_path.exists():
                shutil.rmtree(dir_path)
                print(f"   Removed: {dir_path}")
                
        # Clean __pycache__ directories
        for pycache in self.project_root.rglob("__pycache__"):
            shutil.rmtree(pycache)
            print(f"   Removed: {pycache}")
            
    def check_dependencies(self):
        """Check if required build dependencies are installed."""
        print("📋 Checking build dependencies...")
        
        # Check PyInstaller by trying to run it
        try:
            result = subprocess.run([sys.executable, '-m', 'PyInstaller', '--version'], 
                                  capture_output=True, text=True, timeout=10)
            if result.returncode == 0:
                print(f"   ✅ PyInstaller {result.stdout.strip()}")
            else:
                print(f"   ❌ PyInstaller")
                return False
        except (subprocess.CalledProcessError, FileNotFoundError, subprocess.TimeoutExpired):
            print(f"   ❌ PyInstaller")
            return False
        
        # Check PySide6
        try:
            import PySide6
            print(f"   ✅ PySide6 {PySide6.__version__}")
        except ImportError:
            try:
                # Alternative check using importlib.metadata (Python 3.8+)
                import importlib.metadata
                version = importlib.metadata.version('PySide6')
                print(f"   ✅ PySide6 {version}")
            except:
                print(f"   ❌ PySide6")
                return False
                
        return True
        
    def create_spec_file(self, console_mode=False):
        """Create PyInstaller spec file."""
        print("📝 Creating PyInstaller spec file...")
        
        icon_path = ""
        if (self.project_root / "icon.ico").exists():
            icon_path = str(self.project_root / "icon.ico")
        elif (self.project_root / "icon.png").exists():
            icon_path = str(self.project_root / "icon.png")
            
        spec_content = f'''# -*- mode: python ; coding: utf-8 -*-

block_cipher = None

a = Analysis(
    ['main.py'],
    pathex=[r'{self.project_root}'],
    binaries=[],
    datas=[
        ('src', 'src'),
        ('AppSettings.json', '.'),
        ('icon.ico', '.'),
    ],
    hiddenimports=[
        'PySide6.QtCore',
        'PySide6.QtWidgets', 
        'PySide6.QtGui',
        'src.gui.main_window',
        'src.core.ftp_worker',
        'src.utils.logger',
        'src.utils.settings_manager',
        'src.utils.theme_manager',
        'src.utils.email_manager',
        'src.config.constants',
        'src.config.themes',
    ],
    hookspath=[],
    hooksconfig={{}},
    runtime_hooks=[],
    excludes=[
        'tkinter',
        'matplotlib',
        'numpy',
        'pandas',
        'scipy',
        'jupyter',
        'IPython',
        'notebook',
    ],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name='FTPTool',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console={'True' if console_mode else 'False'},
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=r'{icon_path}',
)
'''
        
        with open(self.spec_file, 'w') as f:
            f.write(spec_content)
            
        print(f"   Created: {self.spec_file}")
        
    def build_executable(self, console_mode=False, debug=False):
        """Build the executable using PyInstaller."""
        print(f"🔨 Building executable for {self.current_platform}...")
        
        cmd = [
            sys.executable, '-m', 'PyInstaller',
            '--clean',
            '--noconfirm',
        ]
        
        if debug:
            cmd.append('--debug=all')
        else:
            cmd.append('--log-level=WARN')
            
        cmd.append(str(self.spec_file))
        
        print(f"   Running: {' '.join(cmd)}")
        
        try:
            result = subprocess.run(cmd, cwd=self.project_root, check=True, 
                                  capture_output=True, text=True)
            print("   ✅ Build completed successfully!")
            return True
        except subprocess.CalledProcessError as e:
            print(f"   ❌ Build failed with error code {e.returncode}")
            print(f"   Error output: {e.stderr}")
            return False
            
    def create_package(self):
        """Create distribution package."""
        print("📦 Creating distribution package...")
        
        platform_name = self.platform_map.get(self.current_platform, self.current_platform)
        
        # Find the built executable
        exe_name = "FTPTool"
        if self.current_platform == "windows":
            exe_name += ".exe"
            
        exe_path = self.dist_dir / exe_name
        
        if not exe_path.exists():
            print(f"   ❌ Executable not found: {exe_path}")
            return False
            
        # Create package directory
        package_name = f"FTPTool-{platform_name}"
        package_dir = self.dist_dir / package_name
        package_dir.mkdir(exist_ok=True)
        
        # Copy executable
        shutil.copy2(exe_path, package_dir)
        
        # Copy additional files
        additional_files = [
            "README.md",
            "LICENSE",
            "AppSettings.json",
        ]
        
        for file_name in additional_files:
            src_file = self.project_root / file_name
            if src_file.exists():
                shutil.copy2(src_file, package_dir)
                print(f"   Added: {file_name}")
                
        # Create logs directory
        (package_dir / "logs").mkdir(exist_ok=True)
        
        # Create archive
        archive_name = f"{package_name}-v1.0"
        
        if self.current_platform == "windows":
            archive_path = shutil.make_archive(
                str(self.dist_dir / archive_name), 
                'zip', 
                str(package_dir)
            )
        else:
            archive_path = shutil.make_archive(
                str(self.dist_dir / archive_name), 
                'gztar', 
                str(package_dir)
            )
            
        print(f"   ✅ Package created: {Path(archive_path).name}")
        return True
        
    def create_installer_script(self):
        """Create platform-specific installer/launcher scripts."""
        print("📜 Creating installer scripts...")
        
        package_name = f"FTPTool-{self.platform_map.get(self.current_platform, self.current_platform)}"
        package_dir = self.dist_dir / package_name
        
        if self.current_platform == "windows":
            # Create Windows batch launcher
            launcher_content = '''@echo off
echo Starting FTP Tool...
cd /d "%~dp0"
FTPTool.exe
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo FTP Tool exited with error code %ERRORLEVEL%
    echo Press any key to close...
    pause >nul
)
'''
            with open(package_dir / "start_ftp_tool.bat", 'w') as f:
                f.write(launcher_content)
                
        else:
            # Create Unix shell launcher
            launcher_content = '''#!/bin/bash
echo "Starting FTP Tool..."
cd "$(dirname "$0")"
./FTPTool
if [ $? -ne 0 ]; then
    echo
    echo "FTP Tool exited with error code $?"
    echo "Press Enter to close..."
    read
fi
'''
            launcher_path = package_dir / "start_ftp_tool.sh"
            with open(launcher_path, 'w') as f:
                f.write(launcher_content)
            # Make executable
            os.chmod(launcher_path, 0o755)
            
        print("   ✅ Launcher script created")
        
    def build(self, console_mode=False, debug=False, clean=True):
        """Run the complete build process."""
        print(f"🚀 Building FTP Tool for {self.current_platform}")
        print("=" * 50)
        
        # Clean previous builds
        if clean:
            self.clean_build()
            
        # Check dependencies
        if not self.check_dependencies():
            return False
            
        # Create spec file
        self.create_spec_file(console_mode)
        
        # Build executable
        if not self.build_executable(console_mode, debug):
            return False
            
        # Create package
        if not self.create_package():
            return False
            
        # Create installer scripts
        self.create_installer_script()
        
        print("\n" + "=" * 50)
        print("✅ Build completed successfully!")
        print(f"📁 Output directory: {self.dist_dir}")
        
        return True


def main():
    """Main entry point."""
    parser = argparse.ArgumentParser(description="Build FTP Tool for distribution")
    parser.add_argument('--console', action='store_true', 
                       help='Build console version (shows terminal window)')
    parser.add_argument('--debug', action='store_true',
                       help='Enable debug mode for troubleshooting')
    parser.add_argument('--no-clean', action='store_true',
                       help='Skip cleaning previous build artifacts')
    
    args = parser.parse_args()
    
    builder = CrossPlatformBuilder()
    success = builder.build(
        console_mode=args.console,
        debug=args.debug,
        clean=not args.no_clean
    )
    
    sys.exit(0 if success else 1)


if __name__ == "__main__":
    main()
