#!/usr/bin/env python3
"""
Build script for creating a directory-based distribution of FTP Tool.
This method is recommended to avoid antivirus false positives.
"""

import os
import sys
import shutil
import subprocess
from pathlib import Path

# --- Configuration ---
APP_NAME = "FTPTool"
APP_VERSION = "1.0.0"
MAIN_SCRIPT = "main.py"
# --- End Configuration ---

class AppBuilder:
    """Handles the entire build and packaging process."""

    def __init__(self):
        self.project_root = Path(__file__).parent.resolve()
        self.build_dir = self.project_root / "build"
        self.dist_dir = self.project_root / "dist"
        self.output_dir = self.dist_dir / APP_NAME # Final directory for distribution

        self.current_os = sys.platform
        print(f"🚀 Initializing build for '{APP_NAME}' on '{self.current_os}'")

    def clean(self):
        """Removes previous build artifacts."""
        print("\n🧹 Cleaning previous build directories...")
        for path in [self.build_dir, self.dist_dir]:
            if path.exists():
                print(f"   - Removing {path}")
                shutil.rmtree(path)
        print("   Clean complete.")

    def check_pyinstaller(self):
        """Checks if PyInstaller is installed."""
        print("\n📋 Checking for PyInstaller...")
        try:
            subprocess.run(
                [sys.executable, "-m", "PyInstaller", "--version"],
                check=True, capture_output=True
            )
            print("   ✅ PyInstaller is installed.")
            return True
        except (subprocess.CalledProcessError, FileNotFoundError):
            print("   ❌ PyInstaller not found.")
            print("      Please install it with: pip install pyinstaller")
            return False

    def build(self):
        """Runs PyInstaller to create a directory-based build."""
        print(f"\n🔨 Building executable from '{MAIN_SCRIPT}'...")

        # PyInstaller command for a directory-based build (--onedir is default)
        # We specify the output directory name with --name
        command = [
            sys.executable, "-m", "PyInstaller",
            MAIN_SCRIPT,
            "--noconfirm",         # Overwrite previous builds
            "--name", APP_NAME,    # Name of the output folder and .exe
            "--windowed",          # Use --console for a terminal window
            "--add-data", f"AppSettings.json{os.pathsep}.", # Include settings file
            "--add-data", f"src{os.pathsep}src",             # Include the entire src folder
            "--icon", "icon.ico"   # Application icon
        ]

        print(f"   Running command: {' '.join(command)}")

        try:
            subprocess.run(command, check=True, cwd=self.project_root)
            print("   ✅ PyInstaller build successful.")
            return True
        except subprocess.CalledProcessError as e:
            print(f"   ❌ PyInstaller build failed with error code {e.returncode}.")
            return False
        except FileNotFoundError:
            print(f"   ❌ Could not run PyInstaller. Is it in your PATH?")
            return False

    def package(self):
        """Packages the final build directory into a distributable archive."""
        print("\n📦 Packaging the application...")

        if not self.output_dir.exists():
            print(f"   ❌ Output directory '{self.output_dir}' not found. Cannot package.")
            return False

        # Copy any final files like README or LICENSE into the built directory
        files_to_add = ["README.md", "LICENSE"]
        for file in files_to_add:
            src_path = self.project_root / file
            if src_path.exists():
                print(f"   - Adding {file} to package.")
                shutil.copy(src_path, self.output_dir)

        # Create a distributable archive (zip for Windows, tar.gz for others)
        platform_tag = "win" if self.current_os == "win32" else "nix"
        archive_name = f"{APP_NAME}-v{APP_VERSION}-{platform_tag}"
        
        archive_format = 'zip' if self.current_os == "win32" else 'gztar'

        print(f"   Creating archive: {archive_name}.{archive_format}")
        archive_path = shutil.make_archive(
            base_name=str(self.dist_dir / archive_name), # Full path for archive name
            format=archive_format,
            root_dir=self.dist_dir,       # Start archiving from the 'dist' folder
            base_dir=APP_NAME             # The specific folder to archive
        )

        print(f"   ✅ Package created successfully: {archive_path}")
        return True

def main():
    """Main execution function."""
    builder = AppBuilder()

    # 1. Clean previous builds
    builder.clean()

    # 2. Check for dependencies
    if not builder.check_pyinstaller():
        sys.exit(1)

    # 3. Run the build process
    if not builder.build():
        sys.exit(1)

    # 4. Package the result into an archive
    if not builder.package():
        sys.exit(1)

    print("\n" + "="*50)
    print("🎉 Build process completed successfully!")
    print(f"Find your distributable archive in: {builder.dist_dir}")
    print("="*50)

if __name__ == "__main__":
    main()