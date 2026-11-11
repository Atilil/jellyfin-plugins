#!/bin/bash
# Build script for WatchSync Jellyfin plugin
set -e

PLUGIN_DIR="Jellyfin.Plugin.WatchSync"
OUTPUT_DIR="output"

echo "=== Building WatchSync Plugin ==="

# Clean output directory
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

# Build plugin
echo "Compiling plugin..."
cd "$PLUGIN_DIR"
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ~/.dotnet/dotnet restore
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ~/.dotnet/dotnet build -c Release

# Copy necessary files
echo "Copying files..."
cd ..
cp "$PLUGIN_DIR/bin/Release/net10.0/Jellyfin.Plugin.WatchSync.dll" "$OUTPUT_DIR/"

# Create meta.json for the plugin
cat > "$OUTPUT_DIR/meta.json" << 'EOF'
{
    "guid": "aa4fe598-07fd-41b5-bf1d-65abab9980db",
    "name": "WatchSync",
    "overview": "Sync watch history between libraries of different qualities (4K/HD)",
    "description": "Automatically synchronizes watch history between libraries of different qualities (4K/HD). When a movie is watched in 4K, the HD version is also marked as watched (and vice versa).",
    "owner": "Atilili",
    "category": "General",
    "version": "1.0.1.0",
    "targetAbi": "12.0.0.0",
    "timestamp": "2026-11-11T00:00:00Z"
}
EOF

# Create ZIP archive
echo "Creating ZIP archive..."
cd "$OUTPUT_DIR"
zip -r "watchsync-1.0.1.0.zip" *.dll meta.json
cd ..

echo ""
echo "=== Build complete ==="
echo "Output files in: $OUTPUT_DIR/"
echo ""
echo "To install:"
echo "1. Copy the DLL to your Jellyfin plugins folder:"
echo "   mkdir -p /path/to/jellyfin/plugins/WatchSync"
echo "   cp $OUTPUT_DIR/Jellyfin.Plugin.WatchSync.dll /path/to/jellyfin/plugins/WatchSync/"
echo "2. Restart Jellyfin"
