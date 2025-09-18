#!/bin/bash
# Cross-platform build script wrapper

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if Python is available
if ! command -v python3 &> /dev/null; then
    if ! command -v python &> /dev/null; then
        print_error "Python is not installed or not in PATH"
        exit 1
    else
        PYTHON_CMD="python"
    fi
else
    PYTHON_CMD="python3"
fi

print_status "Using Python: $PYTHON_CMD"

# Add user's local bin to PATH for PyInstaller
export PATH="$HOME/.local/bin:$PATH"

# Check if we're in the right directory
if [ ! -f "main.py" ] || [ ! -f "build.py" ]; then
    print_error "Please run this script from the FTP Tool root directory"
    exit 1
fi

print_status "Setting up build environment..."

# Install build dependencies if needed
print_status "Installing build dependencies..."
$PYTHON_CMD -m pip install --upgrade pip --break-system-packages
$PYTHON_CMD -m pip install PyInstaller PySide6 --break-system-packages

# Run the Python build script
print_status "Starting build process..."
$PYTHON_CMD build.py "$@"

if [ $? -eq 0 ]; then
    print_success "Build completed successfully!"
    print_status "Built files are in the 'dist' directory"
    
    # List built files
    if [ -d "dist" ]; then
        echo ""
        print_status "Built packages:"
        ls -la dist/*.{zip,tar.gz} 2>/dev/null || echo "No archives found"
        echo ""
        print_status "Executable directory:"
        ls -la dist/FTPTool-*/ 2>/dev/null || echo "No executable directory found"
    fi
else
    print_error "Build failed!"
    exit 1
fi
