#!/bin/bash
# Build script for JellyTag Jellyfin plugin
set -e

PLUGIN_DIR="Jellyfin.Plugin.JellyTag"
OUTPUT_DIR="output"

echo "=== Building JellyTag Plugin ==="

# Clean output directory
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

# Build and publish plugin
echo "Compiling plugin..."
cd "$PLUGIN_DIR"
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ~/.dotnet/dotnet restore
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ~/.dotnet/dotnet publish -c Release -o publish_out

# Copy necessary files
echo "Copying files..."
cd ..
cp "$PLUGIN_DIR/publish_out/Jellyfin.Plugin.JellyTag.dll" "$OUTPUT_DIR/"
cp "$PLUGIN_DIR/publish_out/SixLabors.ImageSharp.dll" "$OUTPUT_DIR/"

# Create meta.json for the plugin
cat > "$OUTPUT_DIR/meta.json" << 'EOF'
{
    "guid": "f4a2e8c1-9b3d-4f7a-b6c5-2d8e1a3f9b04",
    "name": "JellyTag",
    "overview": "Adds quality badges (4K, 1080p, etc.) to media posters and thumbnails.",
    "description": "JellyTag automatically adds quality resolution badges to your media posters and thumbnails. Badges are visible on all clients including web, mobile, TV, and Kodi.",
    "owner": "Atilili",
    "category": "General",
    "version": "1.0.0.0",
    "targetAbi": "10.11.0.0",
    "timestamp": "2025-01-29T00:00:00Z"
}
EOF

# Create ZIP archive
echo "Creating ZIP archive..."
cd "$OUTPUT_DIR"
zip -r "jellytag-1.0.0.0.zip" *.dll meta.json
cd ..

echo ""
echo "=== Build complete ==="
echo "Output files in: $OUTPUT_DIR/"
echo ""
echo "To install:"
echo "1. Copy the DLLs to your Jellyfin plugins folder:"
echo "   mkdir -p /path/to/jellyfin/plugins/JellyTag"
echo "   cp $OUTPUT_DIR/*.dll /path/to/jellyfin/plugins/JellyTag/"
echo "2. Restart Jellyfin"
