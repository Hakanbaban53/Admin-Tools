@echo off
REM Cross-platform build script for Windows

setlocal EnableDelayedExpansion

REM Colors for output (Windows 10+ with ANSI support)
set "BLUE=[94m"
set "GREEN=[92m"
set "YELLOW=[93m"
set "RED=[91m"
set "NC=[0m"

REM Function to print status
echo %BLUE%[INFO]%NC% Starting FTP Tool build process...

REM Check if Python is available
python --version >nul 2>&1
if %ERRORLEVEL% neq 0 (
    python3 --version >nul 2>&1
    if %ERRORLEVEL% neq 0 (
        echo %RED%[ERROR]%NC% Python is not installed or not in PATH
        pause
        exit /b 1
    ) else (
        set PYTHON_CMD=python3
    )
) else (
    set PYTHON_CMD=python
)

echo %BLUE%[INFO]%NC% Using Python: %PYTHON_CMD%

REM Check if we're in the right directory
if not exist main.py (
    echo %RED%[ERROR]%NC% main.py not found. Please run this script from the FTP Tool root directory
    pause
    exit /b 1
)

if not exist build.py (
    echo %RED%[ERROR]%NC% build.py not found. Please run this script from the FTP Tool root directory
    pause
    exit /b 1
)

echo %BLUE%[INFO]%NC% Setting up build environment...

REM Install build dependencies
echo %BLUE%[INFO]%NC% Installing build dependencies...
%PYTHON_CMD% -m pip install --upgrade pip --break-system-packages
%PYTHON_CMD% -m pip install PyInstaller PySide6 --break-system-packages

REM Run the Python build script with all arguments
echo %BLUE%[INFO]%NC% Starting build process...
%PYTHON_CMD% build.py %*

if %ERRORLEVEL% equ 0 (
    echo.
    echo %GREEN%[SUCCESS]%NC% Build completed successfully!
    echo %BLUE%[INFO]%NC% Built files are in the 'dist' directory
    
    REM List built files
    if exist dist (
        echo.
        echo %BLUE%[INFO]%NC% Built packages:
        dir dist\*.zip 2>nul
        dir dist\*.tar.gz 2>nul
        echo.
        echo %BLUE%[INFO]%NC% Executable directory:
        dir dist\FTPTool-* /AD 2>nul
    )
    echo.
    echo Press any key to continue...
    pause >nul
) else (
    echo.
    echo %RED%[ERROR]%NC% Build failed!
    echo Press any key to continue...
    pause >nul
    exit /b 1
)
