#!/bin/bash
set -e

# MAUI Sherpa Linux Installer
# Usage: sudo ./install.sh [--uninstall]

INSTALL_DIR="/opt/maui-sherpa"
BIN_LINK="/usr/local/bin/maui-sherpa"
DESKTOP_FILE="codes.redth.mauisherpa.desktop"
ICON_FILE="maui-sherpa.png"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

if [ "$1" = "--uninstall" ]; then
    echo "Uninstalling MAUI Sherpa..."
    rm -f "$BIN_LINK"
    rm -f "/usr/share/applications/$DESKTOP_FILE"
    rm -f "/usr/share/icons/hicolor/512x512/apps/$ICON_FILE"
    rm -rf "$INSTALL_DIR"
    echo "MAUI Sherpa has been uninstalled."
    exit 0
fi

# Check for root
if [ "$(id -u)" -ne 0 ]; then
    echo "This installer requires root privileges. Please run with sudo."
    exit 1
fi

echo "Installing MAUI Sherpa..."

# Create install directory
mkdir -p "$INSTALL_DIR"

# Copy application files (everything except the installer itself and linux metadata)
echo "Copying application files to $INSTALL_DIR..."
cp -r "$SCRIPT_DIR"/* "$INSTALL_DIR/" 2>/dev/null || true

# Make the main executable runnable
chmod +x "$INSTALL_DIR/MauiSherpa.LinuxGtk"

# Create symlink
echo "Creating symlink at $BIN_LINK..."
ln -sf "$INSTALL_DIR/MauiSherpa.LinuxGtk" "$BIN_LINK"

# Install desktop entry
echo "Installing desktop entry..."
mkdir -p /usr/share/applications
if [ -f "$SCRIPT_DIR/linux/$DESKTOP_FILE" ]; then
    sed "s|Exec=maui-sherpa|Exec=$INSTALL_DIR/MauiSherpa.LinuxGtk|g" \
        "$SCRIPT_DIR/linux/$DESKTOP_FILE" > "/usr/share/applications/$DESKTOP_FILE"
elif [ -f "$INSTALL_DIR/linux/$DESKTOP_FILE" ]; then
    sed "s|Exec=maui-sherpa|Exec=$INSTALL_DIR/MauiSherpa.LinuxGtk|g" \
        "$INSTALL_DIR/linux/$DESKTOP_FILE" > "/usr/share/applications/$DESKTOP_FILE"
fi

# Install icon
echo "Installing application icon..."
mkdir -p /usr/share/icons/hicolor/512x512/apps
if [ -f "$SCRIPT_DIR/linux/$ICON_FILE" ]; then
    cp "$SCRIPT_DIR/linux/$ICON_FILE" "/usr/share/icons/hicolor/512x512/apps/$ICON_FILE"
elif [ -f "$INSTALL_DIR/linux/$ICON_FILE" ]; then
    cp "$INSTALL_DIR/linux/$ICON_FILE" "/usr/share/icons/hicolor/512x512/apps/$ICON_FILE"
fi

# Update icon cache
gtk-update-icon-cache /usr/share/icons/hicolor/ 2>/dev/null || true

echo ""
echo "✅ MAUI Sherpa installed successfully!"
echo "   Run with: maui-sherpa"
echo "   Uninstall with: sudo $INSTALL_DIR/install.sh --uninstall"
echo ""
echo "Prerequisites: GTK4 must be installed on your system."
echo "   Ubuntu/Debian: sudo apt install libgtk-4-1 libwebkitgtk-6.0-4"
echo "   Fedora:        sudo dnf install gtk4 webkit2gtk6.0"
echo "   Arch:          sudo pacman -S gtk4 webkit2gtk-6.0"
