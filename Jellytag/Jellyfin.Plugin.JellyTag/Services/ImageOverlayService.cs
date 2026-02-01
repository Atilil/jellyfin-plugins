using System.Reflection;
using System.Xml.Linq;
using Jellyfin.Plugin.JellyTag.Configuration;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Svg.Skia;

namespace Jellyfin.Plugin.JellyTag.Services;

/// <summary>
/// Service for adding quality badge overlays to images.
/// </summary>
public class ImageOverlayService : IImageOverlayService, IDisposable
{
    private readonly ILogger<ImageOverlayService> _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _svgCache = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SKBitmap?> _rasterCache = new();
    private readonly SemaphoreSlim _badgeLock = new(1, 1);
    private bool _badgesLoaded;
    private bool _disposed;

    private const int MinBadgeSizePercent = 5;
    private const int MaxBadgeSizePercent = 50;
    private const float MinBadgeMarginPercent = 0f;
    private const float MaxBadgeMarginPercent = 20f;

    private static readonly Dictionary<string, string> BadgeDisplayText = new(StringComparer.OrdinalIgnoreCase)
    {
        { "4k", "4K" }, { "1080p", "1080p" }, { "720p", "720p" }, { "sd", "SD" },
        { "hdr10", "HDR10" }, { "hdr10plus", "HDR10+" }, { "dv", "DV" }, { "hlg", "HLG" },
        { "atmos", "ATMOS" }, { "dtsx", "DTS:X" }, { "truehd", "TrueHD" }, { "dtshdma", "DTS-HD MA" },
        { "7.1", "7.1" }, { "5.1", "5.1" }, { "stereo", "STEREO" },
        { "hdr", "HDR" }, { "3d", "3D" },
        { "UHD4K", "4K" }, { "FHD1080p", "1080p" }, { "HD720p", "720p" },
        { "fra", "FR" }, { "fre", "FR" }, { "eng", "EN" }, { "jpn", "JP" },
        { "deu", "DE" }, { "ger", "DE" }, { "spa", "ES" }, { "ita", "IT" },
        { "por", "PT" }, { "kor", "KR" }, { "zho", "ZH" }, { "chi", "ZH" },
        { "rus", "RU" }, { "nld", "NL" }, { "dut", "NL" }, { "ara", "AR" },
        { "hin", "HI" }, { "tha", "TH" }, { "pol", "PL" }, { "tur", "TR" },
        { "swe", "SV" }, { "dan", "DA" }, { "nor", "NO" }, { "fin", "FI" },
        { "ces", "CS" }, { "cze", "CS" }, { "hun", "HU" }, { "ron", "RO" },
        { "rum", "RO" }, { "ukr", "UK" }, { "vie", "VI" }, { "heb", "HE" },
        { "vostfra", "VOSTFR" }, { "vostfre", "VOSTFR" }, { "vosteng", "VOSTEN" },
        { "vostjpn", "VOSTJP" }, { "vostdeu", "VOSTDE" }, { "vostger", "VOSTDE" },
        { "vostspa", "VOSTES" }, { "vostita", "VOSTIT" }, { "vostpor", "VOSTPT" },
        { "vostkor", "VOSTKR" }, { "vostzho", "VOSTZH" }, { "vostchi", "VOSTZH" },
        { "vostrus", "VOSTR" }, { "vostnld", "VOSTNL" }, { "vostdut", "VOSTNL" },
        { "h264", "H.264" }, { "hevc", "HEVC" }, { "av1", "AV1" }, { "vp9", "VP9" },
        { "ell", "GR" }, { "gre", "GR" }, { "ind", "ID" }, { "msa", "MS" },
        { "tgl", "TL" }, { "fil", "TL" }, { "hrv", "HR" }, { "srp", "SR" },
        { "bul", "BG" }, { "slk", "SK" }, { "slo", "SK" }, { "lit", "LT" },
        { "lav", "LV" }, { "est", "ET" }, { "cat", "CA" }, { "eus", "EU" },
        { "baq", "EU" }, { "glg", "GL" }, { "cym", "CY" }, { "wel", "CY" },
        { "vostell", "VOSTGR" }, { "vostgre", "VOSTGR" }, { "vostind", "VOSTID" },
        { "vostmsa", "VOSTMS" }, { "vosttgl", "VOSTTL" }, { "vostfil", "VOSTTL" },
        { "vosthrv", "VOSTHR" }, { "vostsrp", "VOSTSR" }, { "vostbul", "VOSTBG" },
        { "vostslk", "VOSTSK" }, { "vostslo", "VOSTSK" }, { "vostlit", "VOSTLT" },
        { "vostlav", "VOSTLV" }, { "vostest", "VOSTET" }, { "vostcat", "VOSTCA" },
        { "vosteus", "VOSTEU" }, { "vostbaq", "VOSTEU" }, { "vostglg", "VOSTGL" },
        { "vostcym", "VOSTCY" }, { "vostwel", "VOSTCY" }
    };

    public ImageOverlayService(ILogger<ImageOverlayService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Maps a BadgeCategory to the corresponding panel in ImageTypeConfig.
    /// </summary>
    private static BadgePanelSettings GetPanelForCategory(BadgeCategory category, ImageTypeConfig imageConfig)
    {
        return category switch
        {
            BadgeCategory.Resolution => imageConfig.ResolutionPanel,
            BadgeCategory.Hdr or BadgeCategory.ThreeD => imageConfig.HdrPanel,
            BadgeCategory.VideoCodec => imageConfig.CodecPanel,
            BadgeCategory.Audio => imageConfig.AudioPanel,
            BadgeCategory.Language or BadgeCategory.Subtitle => imageConfig.LanguagePanel,
            _ => imageConfig.ResolutionPanel
        };
    }

    /// <inheritdoc />
    public bool ShouldShowBadge(BadgeInfo badge, ImageTypeConfig imageConfig)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null || !config.Enabled)
        {
            return false;
        }

        var panel = GetPanelForCategory(badge.Category, imageConfig);
        if (!panel.Enabled)
        {
            return false;
        }

        // For language/subtitle badges, EnabledBadges is empty = show all (language codes are dynamic)
        if (badge.Category is BadgeCategory.Language or BadgeCategory.Subtitle)
        {
            // Subtitle badges depend on ShowVostIndicator
            if (badge.Category == BadgeCategory.Subtitle && !imageConfig.ShowVostIndicator)
            {
                return false;
            }

            return true;
        }

        // For other categories, check EnabledBadges list
        if (panel.EnabledBadges.Count == 0)
        {
            return false;
        }

        return panel.EnabledBadges.Contains(badge.BadgeKey, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<(Stream Stream, string ContentType)> AddBadgeOverlaysAsync(Stream originalImage, List<BadgeInfo> badges, ImageTypeConfig imageConfig)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var jpegQuality = Math.Clamp(config.JpegQuality, 1, 100);

        originalImage.Position = 0;
        var sourceContentType = DetectImageContentType(originalImage);
        originalImage.Position = 0;

        using var image = SKBitmap.Decode(originalImage);
        if (image == null)
        {
            originalImage.Position = 0;
            var output = new MemoryStream();
            await originalImage.CopyToAsync(output).ConfigureAwait(false);
            output.Position = 0;
            return (output, sourceContentType);
        }

        // Get ordered panels
        var panels = GetOrderedPanels(imageConfig);

        // Group badges by panel, prepare and render each panel
        var allPanelGroups = new List<PanelRenderGroup>();
        var allOwnedBitmaps = new List<SKBitmap>();

        try
        {
            foreach (var (panel, category) in panels)
            {
                if (!panel.Enabled) continue;

                var panelBadges = badges.Where(b => GetPanelForCategory(b.Category, imageConfig) == panel).ToList();
                if (panelBadges.Count == 0) continue;

                // Apply ShowMode filter: Highest = keep only the first (highest priority) badge
                if (panel.ShowMode == BadgeDisplayMode.Highest && panelBadges.Count > 1)
                {
                    panelBadges = new List<BadgeInfo> { panelBadges[0] };
                }

                var sizePercent = Math.Clamp(panel.SizePercent, MinBadgeSizePercent, MaxBadgeSizePercent);
                var useText = panel.Style == BadgeStyle.Text;

                var sizes = new List<SKSizeI>();
                var sourceBitmaps = new List<SKBitmap>();
                var filtered = new List<BadgeInfo>();
                var ownedBitmaps = new List<SKBitmap>();

                await PrepareBadgeGroup(panelBadges, sizePercent, image.Width, useText, sizes, sourceBitmaps, filtered, ownedBitmaps).ConfigureAwait(false);
                allOwnedBitmaps.AddRange(ownedBitmaps);

                if (filtered.Count == 0) continue;

                allPanelGroups.Add(new PanelRenderGroup
                {
                    Panel = panel,
                    Filtered = filtered,
                    Sizes = sizes,
                    SourceBitmaps = sourceBitmaps,
                    ImageConfig = imageConfig
                });
            }

            if (allPanelGroups.Count == 0)
            {
                originalImage.Position = 0;
                var output = new MemoryStream();
                await originalImage.CopyToAsync(output).ConfigureAwait(false);
                output.Position = 0;
                return (output, sourceContentType);
            }

            // Calculate positions: panels at the same position stack on top of each other in Order order
            // Track cumulative vertical extent per position corner
            var priorExtents = new Dictionary<BadgePosition, int>
            {
                { BadgePosition.TopLeft, 0 }, { BadgePosition.TopRight, 0 },
                { BadgePosition.BottomLeft, 0 }, { BadgePosition.BottomRight, 0 }
            };

            foreach (var group in allPanelGroups)
            {
                var panel = group.Panel;
                var marginPercent = Math.Clamp(panel.MarginPercent, MinBadgeMarginPercent, MaxBadgeMarginPercent);
                var badgeMargin = (int)(image.Width * marginPercent / 100f);
                var gapPercent = Math.Max(0f, panel.GapPercent);
                var gap = group.Sizes.Count > 0 ? (int)(group.Sizes.Average(s => s.Height) * gapPercent / 100f) : 0;

                // Reverse order if needed
                if (ShouldReverseOrder(panel.Layout, panel.Position))
                {
                    group.Filtered.Reverse();
                    group.Sizes.Reverse();
                    group.SourceBitmaps.Reverse();
                }

                var priorExtent = priorExtents[panel.Position];
                group.Positions = CalculateStackedPositions(image.Width, image.Height, group.Sizes, panel.Position, badgeMargin, gap, panel.Layout, priorExtent);

                // Update prior extent for this corner
                var groupExtent = GroupVerticalExtent(group.Sizes, gap, panel.Layout);
                priorExtents[panel.Position] += groupExtent;
            }

            // Draw badges onto image
            using var surface = SKSurface.Create(new SKImageInfo(image.Width, image.Height));
            var canvas = surface.Canvas;
            canvas.DrawBitmap(image, 0, 0);

            using var paint = new SKPaint { IsAntialias = true };
            var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);

            foreach (var group in allPanelGroups)
            {
                var useText = group.Panel.Style == BadgeStyle.Text;
                RenderBadgeGroup(canvas, group.Filtered, group.SourceBitmaps, group.Positions, group.Sizes, useText, group.Panel, group.ImageConfig, paint, sampling);
            }

            canvas.Flush();

            using var resultImage = surface.Snapshot();
            var outputFormat = config.OutputFormat;
            var encodeFormat = outputFormat == OutputImageFormat.WebP ? SKEncodedImageFormat.Webp : SKEncodedImageFormat.Jpeg;
            var encodeQuality = outputFormat == OutputImageFormat.WebP ? Math.Clamp(config.WebPQuality, 1, 100) : jpegQuality;
            var contentType = outputFormat == OutputImageFormat.WebP ? "image/webp" : "image/jpeg";
            using var data = resultImage.Encode(encodeFormat, encodeQuality);

            var outputStream = new MemoryStream();
            data.SaveTo(outputStream);
            outputStream.Position = 0;
            return (outputStream, contentType);
        }
        finally
        {
            foreach (var bmp in allOwnedBitmaps) bmp.Dispose();
        }
    }

    private sealed class PanelRenderGroup
    {
        public BadgePanelSettings Panel { get; set; } = null!;
        public List<BadgeInfo> Filtered { get; set; } = new();
        public List<SKSizeI> Sizes { get; set; } = new();
        public List<SKBitmap> SourceBitmaps { get; set; } = new();
        public List<SKPointI> Positions { get; set; } = new();
        public ImageTypeConfig ImageConfig { get; set; } = null!;
    }

    private static List<(BadgePanelSettings Panel, string Name)> GetOrderedPanels(ImageTypeConfig imageConfig)
    {
        var panels = new List<(BadgePanelSettings Panel, string Name)>
        {
            (imageConfig.ResolutionPanel, "Resolution"),
            (imageConfig.HdrPanel, "HDR"),
            (imageConfig.CodecPanel, "Codec"),
            (imageConfig.AudioPanel, "Audio"),
            (imageConfig.LanguagePanel, "Language")
        };
        panels.Sort((a, b) => a.Panel.Order.CompareTo(b.Panel.Order));
        return panels;
    }

    private static int GroupVerticalExtent(List<SKSizeI> sizes, int gap, BadgeLayout layout)
    {
        if (sizes.Count == 0) return 0;
        return layout == BadgeLayout.Horizontal
            ? sizes.Max(s => s.Height) + gap
            : sizes.Sum(s => s.Height) + (sizes.Count - 1) * gap + gap;
    }

    /// <inheritdoc />
    public void ReloadBadges()
    {
        _badgeLock.Wait();
        try
        {
            foreach (var badge in _rasterCache.Values)
            {
                badge?.Dispose();
            }

            _rasterCache.Clear();
            _svgCache.Clear();
            _badgesLoaded = false;
        }
        finally
        {
            _badgeLock.Release();
        }
    }

    private async Task PrepareBadgeGroup(
        List<BadgeInfo> badges, int sizePercent, int imageWidth, bool useTextStyle,
        List<SKSizeI> sizes, List<SKBitmap> sourceBitmaps, List<BadgeInfo> filtered, List<SKBitmap> ownedBitmaps)
    {
        if (useTextStyle)
        {
            var badgeWidth = Math.Max(1, (int)(imageWidth * (sizePercent / 100.0)));
            var badgeHeight = Math.Max(1, (int)(badgeWidth * 0.5));

            foreach (var badgeInfo in badges)
            {
                var text = GetBadgeDisplayText(badgeInfo.BadgeKey);
                if (string.IsNullOrEmpty(text)) continue;

                filtered.Add(badgeInfo);
                sizes.Add(new SKSizeI(badgeWidth, badgeHeight));
            }
        }
        else
        {
            await EnsureBadgesLoaded().ConfigureAwait(false);

            foreach (var badgeInfo in badges)
            {
                var resourceFileName = badgeInfo.ResourceFileName;
                var badgeWidth = Math.Max(1, (int)(imageWidth * (sizePercent / 100.0)));

                if (string.IsNullOrEmpty(resourceFileName))
                {
                    var text = GetBadgeDisplayText(badgeInfo.BadgeKey);
                    if (string.IsNullOrEmpty(text)) continue;

                    var badgeHeight = Math.Max(1, (int)(badgeWidth * 0.5));
                    filtered.Add(badgeInfo);
                    sizes.Add(new SKSizeI(badgeWidth, badgeHeight));
                    continue;
                }

                if (_svgCache.TryGetValue(resourceFileName, out var svgBytes))
                {
                    var ratio = GetSvgAspectRatio(svgBytes);
                    var badgeHeight = Math.Max(1, (int)(badgeWidth / ratio));
                    var rasterized = RasterizeSvg(svgBytes, badgeWidth, badgeHeight);
                    if (rasterized != null)
                    {
                        sourceBitmaps.Add(rasterized);
                        ownedBitmaps.Add(rasterized);
                        filtered.Add(badgeInfo);
                        sizes.Add(new SKSizeI(badgeWidth, badgeHeight));
                        continue;
                    }

                    var fallbackText = GetBadgeDisplayText(badgeInfo.BadgeKey);
                    if (!string.IsNullOrEmpty(fallbackText))
                    {
                        var fbHeight = Math.Max(1, (int)(badgeWidth * 0.5));
                        var textBadge = new BadgeInfo { Category = badgeInfo.Category, BadgeKey = badgeInfo.BadgeKey, ResourceFileName = string.Empty };
                        filtered.Add(textBadge);
                        sizes.Add(new SKSizeI(badgeWidth, fbHeight));
                    }

                    continue;
                }

                if (_rasterCache.TryGetValue(resourceFileName, out var rasterBitmap) && rasterBitmap != null)
                {
                    var badgeHeight = Math.Max(1, (int)(rasterBitmap.Height * ((double)badgeWidth / rasterBitmap.Width)));
                    sourceBitmaps.Add(rasterBitmap);
                    filtered.Add(badgeInfo);
                    sizes.Add(new SKSizeI(badgeWidth, badgeHeight));
                }
                else
                {
                    // Resource not found in any cache — fall back to text badge
                    var fallbackText = GetBadgeDisplayText(badgeInfo.BadgeKey);
                    if (!string.IsNullOrEmpty(fallbackText))
                    {
                        var fbHeight = Math.Max(1, (int)(badgeWidth * 0.5));
                        var textBadge = new BadgeInfo { Category = badgeInfo.Category, BadgeKey = badgeInfo.BadgeKey, ResourceFileName = string.Empty };
                        filtered.Add(textBadge);
                        sizes.Add(new SKSizeI(badgeWidth, fbHeight));
                    }
                }
            }
        }
    }

    private async Task EnsureBadgesLoaded()
    {
        await _badgeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_badgesLoaded)
            {
                LoadBadges();
                _badgesLoaded = true;
            }
        }
        finally
        {
            _badgeLock.Release();
        }
    }

    private static SKBitmap? RasterizeSvg(byte[] svgBytes, int targetWidth, int targetHeight)
    {
        using var svg = new SKSvg();
        using var stream = new MemoryStream(svgBytes);
        svg.Load(stream);
        var picture = svg.Picture;
        if (picture == null) return null;

        var bitmap = new SKBitmap(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var scaleX = targetWidth / picture.CullRect.Width;
        var scaleY = targetHeight / picture.CullRect.Height;
        canvas.Scale((float)scaleX, (float)scaleY);
        canvas.DrawPicture(picture);
        canvas.Flush();

        return bitmap;
    }

    private static float GetSvgAspectRatio(byte[] svgBytes)
    {
        try
        {
            using var stream = new MemoryStream(svgBytes);
            var doc = XDocument.Load(stream);
            var root = doc.Root;
            if (root == null) return 2f;

            var viewBox = root.Attribute("viewBox")?.Value;
            if (!string.IsNullOrEmpty(viewBox))
            {
                var parts = viewBox.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4 &&
                    float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w) &&
                    float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) &&
                    h > 0)
                {
                    return w / h;
                }
            }

            var widthAttr = root.Attribute("width")?.Value;
            var heightAttr = root.Attribute("height")?.Value;
            if (!string.IsNullOrEmpty(widthAttr) && !string.IsNullOrEmpty(heightAttr))
            {
                widthAttr = widthAttr.Replace("px", string.Empty);
                heightAttr = heightAttr.Replace("px", string.Empty);
                if (float.TryParse(widthAttr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w2) &&
                    float.TryParse(heightAttr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h2) &&
                    h2 > 0)
                {
                    return w2 / h2;
                }
            }
        }
        catch
        {
            // Fallback
        }

        return 2f;
    }

    private void LoadBadges()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        _logger.LogInformation("Loading badges. Available resources: {Resources}", string.Join(", ", resourceNames));

        var assetsMarker = ".Assets.";
        var badgeBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in resourceNames)
        {
            var assetsIdx = resourceName.IndexOf(assetsMarker, StringComparison.OrdinalIgnoreCase);
            if (assetsIdx < 0) continue;

            var fileName = resourceName[(assetsIdx + assetsMarker.Length)..];
            if (fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                var baseName = Path.GetFileNameWithoutExtension(fileName);
                badgeBaseNames.Add(baseName);
            }
        }

        var customDir = GetCustomBadgeDir();

        foreach (var baseName in badgeBaseNames)
        {
            var svgFileName = baseName + ".svg";
            var pngFileName = baseName + ".png";

            if (customDir != null)
            {
                var customSvg = Path.Combine(customDir, svgFileName);
                if (File.Exists(customSvg))
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(customSvg);
                        _svgCache[svgFileName] = bytes;
                        _logger.LogInformation("Loaded custom SVG badge: {FileName}", svgFileName);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load custom SVG badge: {Path}", customSvg);
                    }
                }

                var customPng = Path.Combine(customDir, pngFileName);
                if (File.Exists(customPng))
                {
                    try
                    {
                        var customBadge = SKBitmap.Decode(customPng);
                        if (customBadge != null)
                        {
                            customBadge = TrimTransparent(customBadge);
                            _rasterCache[svgFileName] = customBadge;
                            _logger.LogInformation("Loaded custom PNG badge: {FileName}", pngFileName);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load custom PNG badge: {Path}", customPng);
                    }
                }

                var foundJpeg = false;
                foreach (var ext in new[] { ".jpg", ".jpeg" })
                {
                    var customJpg = Path.Combine(customDir, baseName + ext);
                    if (File.Exists(customJpg))
                    {
                        try
                        {
                            var customBadge = SKBitmap.Decode(customJpg);
                            if (customBadge != null)
                            {
                                customBadge = TrimTransparent(customBadge);
                                _rasterCache[svgFileName] = customBadge;
                                _logger.LogInformation("Loaded custom JPEG badge: {FileName}", baseName + ext);
                                foundJpeg = true;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to load custom JPEG badge: {Path}", customJpg);
                        }
                    }
                }

                if (foundJpeg) continue;
            }

            var svgResourceName = resourceNames.FirstOrDefault(r =>
                r.IndexOf(assetsMarker, StringComparison.OrdinalIgnoreCase) >= 0 &&
                r.EndsWith(svgFileName, StringComparison.OrdinalIgnoreCase));

            if (svgResourceName != null)
            {
                try
                {
                    using var stream = assembly.GetManifestResourceStream(svgResourceName);
                    if (stream != null)
                    {
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        _svgCache[svgFileName] = ms.ToArray();
                        _logger.LogInformation("Loaded embedded SVG badge: {FileName}", svgFileName);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load embedded SVG badge: {FileName}", svgFileName);
                }
            }

            var pngResourceName = resourceNames.FirstOrDefault(r =>
                r.IndexOf(assetsMarker, StringComparison.OrdinalIgnoreCase) >= 0 &&
                r.EndsWith(pngFileName, StringComparison.OrdinalIgnoreCase));

            if (pngResourceName != null)
            {
                try
                {
                    using var stream = assembly.GetManifestResourceStream(pngResourceName);
                    if (stream != null)
                    {
                        var badge = SKBitmap.Decode(stream);
                        if (badge != null)
                        {
                            badge = TrimTransparent(badge);
                            _rasterCache[svgFileName] = badge;
                            _logger.LogInformation("Loaded embedded PNG fallback: {FileName}", pngFileName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load embedded PNG badge: {FileName}", pngFileName);
                }
            }
        }

        _logger.LogInformation("Badge loading complete. SVG: {SvgCount}, Raster: {RasterCount}", _svgCache.Count, _rasterCache.Count);
    }

    private static SKBitmap TrimTransparent(SKBitmap bitmap)
    {
        int minX = bitmap.Width, minY = bitmap.Height, maxX = 0, maxY = 0;
        var pixelBytes = bitmap.GetPixelSpan();
        var bitmapWidth = bitmap.Width;
        var bytesPerPixel = bitmap.BytesPerPixel;
        var alphaOffset = 3;

        for (int y = 0; y < bitmap.Height; y++)
        {
            var rowOffset = y * bitmapWidth * bytesPerPixel;
            for (int x = 0; x < bitmapWidth; x++)
            {
                if (pixelBytes[rowOffset + (x * bytesPerPixel) + alphaOffset] > 25)
                {
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        if (maxX < minX || maxY < minY) return bitmap;

        var trimWidth = maxX - minX + 1;
        var trimHeight = maxY - minY + 1;

        var trimmed = new SKBitmap(trimWidth, trimHeight, bitmap.ColorType, bitmap.AlphaType);
        using var canvas = new SKCanvas(trimmed);
        canvas.DrawBitmap(bitmap, SKRect.Create(minX, minY, trimWidth, trimHeight), SKRect.Create(0, 0, trimWidth, trimHeight));
        canvas.Flush();

        bitmap.Dispose();
        return trimmed;
    }

    private static string? GetCustomBadgeDir()
    {
        var dataFolder = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrEmpty(dataFolder)) return null;
        return Path.Combine(dataFolder, "custom-badges");
    }

    private static bool ShouldReverseOrder(BadgeLayout layout, BadgePosition position) =>
        (layout == BadgeLayout.Vertical && (position == BadgePosition.BottomLeft || position == BadgePosition.BottomRight))
        || (layout == BadgeLayout.Horizontal && (position == BadgePosition.TopRight || position == BadgePosition.BottomRight));

    private static List<SKPointI> CalculateStackedPositions(
        int imageWidth, int imageHeight,
        List<SKSizeI> badges,
        BadgePosition position, int margin, int gap,
        BadgeLayout layout, int priorExtent = 0)
    {
        var positions = new List<SKPointI>();
        if (badges.Count == 0) return positions;

        if (layout == BadgeLayout.Horizontal)
        {
            var totalWidth = badges.Sum(b => b.Width) + (badges.Count - 1) * gap;
            var maxHeight = badges.Max(b => b.Height);

            int startX, startY;
            switch (position)
            {
                case BadgePosition.TopLeft:
                    startX = margin; startY = margin + priorExtent; break;
                case BadgePosition.TopRight:
                    startX = Math.Max(0, imageWidth - totalWidth - margin); startY = margin + priorExtent; break;
                case BadgePosition.BottomLeft:
                    startX = margin; startY = Math.Max(0, imageHeight - maxHeight - margin - priorExtent); break;
                case BadgePosition.BottomRight:
                    startX = Math.Max(0, imageWidth - totalWidth - margin); startY = Math.Max(0, imageHeight - maxHeight - margin - priorExtent); break;
                default:
                    startX = margin; startY = margin + priorExtent; break;
            }

            var currentX = startX;
            for (int i = 0; i < badges.Count; i++)
            {
                var yOffset = (maxHeight - badges[i].Height) / 2;
                positions.Add(new SKPointI(currentX, startY + yOffset));
                currentX += badges[i].Width + gap;
            }
        }
        else
        {
            var totalHeight = badges.Sum(b => b.Height) + (badges.Count - 1) * gap;
            var maxWidth = badges.Max(b => b.Width);

            int startX, startY;
            switch (position)
            {
                case BadgePosition.TopLeft:
                    startX = margin; startY = margin + priorExtent; break;
                case BadgePosition.TopRight:
                    startX = Math.Max(0, imageWidth - maxWidth - margin); startY = margin + priorExtent; break;
                case BadgePosition.BottomLeft:
                    startX = margin; startY = Math.Max(0, imageHeight - totalHeight - margin - priorExtent); break;
                case BadgePosition.BottomRight:
                    startX = Math.Max(0, imageWidth - maxWidth - margin); startY = Math.Max(0, imageHeight - totalHeight - margin - priorExtent); break;
                default:
                    startX = margin; startY = margin + priorExtent; break;
            }

            var currentY = startY;
            for (int i = 0; i < badges.Count; i++)
            {
                int x;
                if (position == BadgePosition.TopRight || position == BadgePosition.BottomRight)
                {
                    x = Math.Max(0, imageWidth - badges[i].Width - margin);
                }
                else
                {
                    x = startX;
                }

                positions.Add(new SKPointI(x, currentY));
                currentY += badges[i].Height + gap;
            }
        }

        return positions;
    }

    private static string GetBadgeDisplayText(string badgeKey)
    {
        var config = Plugin.Instance?.Configuration;
        var customText = config?.CustomBadgeTexts?.FirstOrDefault(x => string.Equals(x.Key, badgeKey, StringComparison.OrdinalIgnoreCase))?.Text;
        if (!string.IsNullOrEmpty(customText)) return customText;
        return BadgeDisplayText.TryGetValue(badgeKey, out var text) ? text : badgeKey.ToUpperInvariant();
    }

    private static string DetectImageContentType(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        var read = stream.Read(header);
        stream.Position = 0;
        if (read >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            return "image/png";
        if (read >= 4 && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
            && read >= 12 && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            return "image/webp";
        if (read >= 2 && header[0] == 0xFF && header[1] == 0xD8)
            return "image/jpeg";
        return "image/jpeg";
    }

    private static SKColor ParseHexColor(string hex, byte alpha)
    {
        if (SKColor.TryParse(hex, out var color))
        {
            return color.WithAlpha(alpha);
        }

        return new SKColor(0, 0, 0, alpha);
    }

    /// <summary>
    /// Resolves colors for a badge, checking per-badge-type overrides in the panel first.
    /// </summary>
    private static (string bg, string text, byte opacity, int cornerRadius) ResolveBadgeStyle(BadgeInfo badge, BadgePanelSettings panel, ImageTypeConfig imageConfig)
    {
        var bgColor = panel.TextBgColor ?? "#000000";
        var textColor = panel.TextColor ?? "#FFFFFF";
        var opacity = (byte)Math.Clamp(panel.TextBgOpacity, 0, 255);
        var cornerRadius = Math.Clamp(panel.TextCornerRadius, 0, 50);

        // VOST override for subtitle badges
        if (badge.Category == BadgeCategory.Subtitle)
        {
            if (!string.IsNullOrEmpty(imageConfig.VostBgColor)) bgColor = imageConfig.VostBgColor;
            if (!string.IsNullOrEmpty(imageConfig.VostTextColor)) textColor = imageConfig.VostTextColor;
            if (imageConfig.VostBgOpacity > 0) opacity = (byte)Math.Clamp(imageConfig.VostBgOpacity, 0, 255);
            if (imageConfig.VostCornerRadius >= 0) cornerRadius = Math.Clamp(imageConfig.VostCornerRadius, 0, 50);
        }

        // Per-badge-type overrides
        var typeOverride = panel.BadgeTypeOverrides?.FirstOrDefault(o => string.Equals(o.BadgeKey, badge.BadgeKey, StringComparison.OrdinalIgnoreCase));
        if (typeOverride != null)
        {
            if (!string.IsNullOrEmpty(typeOverride.BgColor)) bgColor = typeOverride.BgColor;
            if (!string.IsNullOrEmpty(typeOverride.TextColor)) textColor = typeOverride.TextColor;
            if (typeOverride.BgOpacity.HasValue) opacity = (byte)Math.Clamp(typeOverride.BgOpacity.Value, 0, 255);
            if (typeOverride.CornerRadius.HasValue) cornerRadius = Math.Clamp(typeOverride.CornerRadius.Value, 0, 50);
        }

        return (bgColor, textColor, opacity, cornerRadius);
    }

    private static void RenderTextBadges(SKCanvas canvas, List<BadgeInfo> badges, List<SKPointI> positions, List<SKSizeI> sizes, BadgePanelSettings panel, ImageTypeConfig imageConfig)
    {
        for (int i = 0; i < badges.Count; i++)
        {
            var (bgHex, textHex, bgAlpha, cornerRadiusPct) = ResolveBadgeStyle(badges[i], panel, imageConfig);
            var bgColor = ParseHexColor(bgHex, bgAlpha);
            var textColor = SKColor.TryParse(textHex, out var tc) ? tc : SKColors.White;

            using var bgPaint = new SKPaint { IsAntialias = true, Color = bgColor, Style = SKPaintStyle.Fill };
            using var textPaint = new SKPaint { IsAntialias = true, Color = textColor, Style = SKPaintStyle.Fill };

            var text = GetBadgeDisplayText(badges[i].BadgeKey);
            var width = sizes[i].Width;
            var height = sizes[i].Height;
            var rect = SKRect.Create(positions[i].X, positions[i].Y, width, height);
            var cornerRadius = height * (cornerRadiusPct / 100f);

            canvas.DrawRoundRect(rect, cornerRadius, cornerRadius, bgPaint);

            var padding = width * 0.1f;
            var availableWidth = width - (2 * padding);

            var fontSize = height * 0.7f;
            var font = new SKFont(SKTypeface.Default, fontSize);
            try
            {
                font.Edging = SKFontEdging.SubpixelAntialias;
                var textWidth = font.MeasureText(text);
                if (textWidth > availableWidth && fontSize > 1f)
                {
                    fontSize *= (availableWidth / textWidth) * 0.95f;
                    fontSize = Math.Max(fontSize, 1f);
                    font.Dispose();
                    font = new SKFont(SKTypeface.Default, fontSize);
                    font.Edging = SKFontEdging.SubpixelAntialias;
                    textWidth = font.MeasureText(text);
                }

                var textX = rect.MidX - (textWidth / 2f);
                var textY = rect.MidY + (fontSize / 3f);

                canvas.DrawText(text, textX, textY, font, textPaint);
            }
            finally
            {
                font.Dispose();
            }
        }
    }

    private static void RenderBadgeGroup(
        SKCanvas canvas, List<BadgeInfo> filtered, List<SKBitmap> sourceBitmaps,
        List<SKPointI> positions, List<SKSizeI> sizes, bool useText,
        BadgePanelSettings panel, ImageTypeConfig imageConfig, SKPaint paint, SKSamplingOptions sampling)
    {
        if (filtered.Count == 0) return;

        if (useText)
        {
            RenderTextBadges(canvas, filtered, positions, sizes, panel, imageConfig);
        }
        else
        {
            int bitmapIdx = 0;
            var textBadges = new List<BadgeInfo>();
            var textPositions = new List<SKPointI>();
            var textSizes = new List<SKSizeI>();

            for (int i = 0; i < filtered.Count; i++)
            {
                if (bitmapIdx < sourceBitmaps.Count && !string.IsNullOrEmpty(filtered[i].ResourceFileName))
                {
                    var destRect = SKRect.Create(positions[i].X, positions[i].Y, sizes[i].Width, sizes[i].Height);
                    using var badgeImage = SKImage.FromBitmap(sourceBitmaps[bitmapIdx]);
                    canvas.DrawImage(badgeImage, destRect, sampling, paint);
                    bitmapIdx++;
                }
                else
                {
                    textBadges.Add(filtered[i]);
                    textPositions.Add(positions[i]);
                    textSizes.Add(sizes[i]);
                }
            }

            if (textBadges.Count > 0)
            {
                RenderTextBadges(canvas, textBadges, textPositions, textSizes, panel, imageConfig);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            foreach (var badge in _rasterCache.Values)
            {
                badge?.Dispose();
            }

            _rasterCache.Clear();
            _svgCache.Clear();
            _badgeLock.Dispose();
        }

        _disposed = true;
    }
}
