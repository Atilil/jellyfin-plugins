using System.Reflection;
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
    private readonly Dictionary<string, SKBitmap?> _badgeCache = new();
    private readonly SemaphoreSlim _badgeLock = new(1, 1);
    private bool _badgesLoaded;
    private bool _disposed;

    private const int MinBadgeSizePercent = 5;
    private const int MaxBadgeSizePercent = 50;
    private const float MinBadgeMarginPercent = 0f;
    private const float MaxBadgeMarginPercent = 20f;

    private static readonly Dictionary<string, string> BadgeDisplayText = new(StringComparer.OrdinalIgnoreCase)
    {
        { "4k", "4K" },
        { "1080p", "1080p" },
        { "720p", "720p" },
        { "sd", "SD" },
        { "hdr10", "HDR10" },
        { "hdr10plus", "HDR10+" },
        { "dv", "DV" },
        { "hlg", "HLG" },
        { "atmos", "ATMOS" },
        { "dtsx", "DTS:X" },
        { "truehd", "TrueHD" },
        { "dtshdma", "DTS-HD MA" },
        { "7.1", "7.1" },
        { "5.1", "5.1" },
        { "stereo", "STEREO" },
        { "hdr", "HDR" },
        { "3d", "3D" },
        { "UHD4K", "4K" },
        { "FHD1080p", "1080p" },
        { "HD720p", "720p" },
        { "fra", "FR" },
        { "fre", "FR" },
        { "eng", "EN" },
        { "jpn", "JP" },
        { "deu", "DE" },
        { "ger", "DE" },
        { "spa", "ES" },
        { "ita", "IT" },
        { "por", "PT" },
        { "kor", "KR" },
        { "zho", "ZH" },
        { "chi", "ZH" },
        { "rus", "RU" },
        { "nld", "NL" },
        { "dut", "NL" },
        { "ara", "AR" },
        { "hin", "HI" },
        { "tha", "TH" },
        { "pol", "PL" },
        { "tur", "TR" },
        { "swe", "SV" },
        { "dan", "DA" },
        { "nor", "NO" },
        { "fin", "FI" },
        { "ces", "CS" },
        { "cze", "CS" },
        { "hun", "HU" },
        { "ron", "RO" },
        { "rum", "RO" },
        { "ukr", "UK" },
        { "vie", "VI" },
        { "heb", "HE" },
        { "vostfra", "VOSTFR" },
        { "vostfre", "VOSTFR" },
        { "vosteng", "VOSTEN" },
        { "vostjpn", "VOSTJP" },
        { "vostdeu", "VOSTDE" },
        { "vostger", "VOSTDE" },
        { "vostspa", "VOSTES" },
        { "vostita", "VOSTIT" },
        { "vostpor", "VOSTPT" },
        { "vostkor", "VOSTKR" },
        { "vostzho", "VOSTZH" },
        { "vostchi", "VOSTZH" },
        { "vostrus", "VOSTR" },
        { "vostnld", "VOSTNL" },
        { "vostdut", "VOSTNL" }
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageOverlayService"/> class.
    /// </summary>
    public ImageOverlayService(ILogger<ImageOverlayService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(Stream Stream, string ContentType)> AddBadgeOverlaysAsync(Stream originalImage, List<BadgeInfo> badges, ImageTypeSettings settings)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        var videoBadgeSizePercent = Math.Clamp(settings.BadgeSizePercent, MinBadgeSizePercent, MaxBadgeSizePercent);
        var audioBadgeSizePercent = Math.Clamp(settings.AudioBadgeSizePercent > 0 ? settings.AudioBadgeSizePercent : settings.BadgeSizePercent, MinBadgeSizePercent, MaxBadgeSizePercent);
        var marginPercent = Math.Clamp(settings.BadgeMarginPercent, MinBadgeMarginPercent, MaxBadgeMarginPercent);
        var gapPercent = Math.Max(0f, settings.BadgeGapPercent);
        var jpegQuality = Math.Clamp(config.JpegQuality, 50, 100);

        using var image = SKBitmap.Decode(originalImage);
        if (image == null)
        {
            originalImage.Position = 0;
            var output = new MemoryStream();
            await originalImage.CopyToAsync(output).ConfigureAwait(false);
            output.Position = 0;
            return (output, "image/jpeg");
        }

        // Split badges into video (Resolution+Hdr), audio, and language groups
        var videoBadges = badges.Where(b => b.Category is BadgeCategory.Resolution or BadgeCategory.Hdr or BadgeCategory.ThreeD).ToList();
        var audioBadges = badges.Where(b => b.Category == BadgeCategory.Audio).ToList();
        var languageBadges = badges.Where(b => b.Category == BadgeCategory.Language).ToList();

        var videoPosition = settings.BadgePosition;
        var videoLayout = settings.BadgeLayout;
        var audioPosition = settings.AudioBadgePosition ?? settings.BadgePosition;
        var audioLayout = settings.AudioBadgeLayout ?? settings.BadgeLayout;
        var languagePosition = settings.LanguageBadgePosition ?? audioPosition;
        var languageLayout = settings.LanguageBadgeLayout ?? audioLayout;

        // When audio uses "Same as video", merge into a single group
        var audioIsSameAsVideo = settings.AudioBadgePosition == null && settings.AudioBadgeLayout == null;

        var useTextStyle = settings.BadgeStyle == Configuration.BadgeStyle.Text;

        // When language uses same position+layout as audio, merge into audio group
        // But only in text mode — in image mode, language badges are always text-rendered separately
        var languageIsSameAsAudio = useTextStyle && languagePosition == audioPosition && languageLayout == audioLayout;

        // Process each group independently
        var videoSizes = new List<SKSizeI>();
        var videoSourceBitmaps = new List<SKBitmap>();
        var videoFiltered = new List<BadgeInfo>();

        var audioSizes = new List<SKSizeI>();
        var audioSourceBitmaps = new List<SKBitmap>();
        var audioFiltered = new List<BadgeInfo>();

        var languageSizes = new List<SKSizeI>();
        var languageSourceBitmaps = new List<SKBitmap>();
        var languageFiltered = new List<BadgeInfo>();

        var languageBadgeSizePercent = Math.Clamp(settings.LanguageBadgeSizePercent > 0 ? settings.LanguageBadgeSizePercent : audioBadgeSizePercent, MinBadgeSizePercent, MaxBadgeSizePercent);

        try
        {
            // Prepare video badges
            await PrepareBadgeGroup(videoBadges, videoBadgeSizePercent, image.Width, useTextStyle, false, videoSizes, videoSourceBitmaps, videoFiltered).ConfigureAwait(false);
            // Prepare audio badges
            await PrepareBadgeGroup(audioBadges, audioBadgeSizePercent, image.Width, useTextStyle, true, audioSizes, audioSourceBitmaps, audioFiltered).ConfigureAwait(false);
            // Prepare language badges (always text mode since ResourceFileName is empty)
            await PrepareBadgeGroup(languageBadges, languageBadgeSizePercent, image.Width, true, true, languageSizes, languageSourceBitmaps, languageFiltered).ConfigureAwait(false);

            if (videoSizes.Count == 0 && audioSizes.Count == 0 && languageSizes.Count == 0)
            {
                originalImage.Position = 0;
                var output = new MemoryStream();
                await originalImage.CopyToAsync(output).ConfigureAwait(false);
                output.Position = 0;
                return (output, "image/jpeg");
            }

            // Merge language into audio group if same position+layout
            if (languageIsSameAsAudio)
            {
                audioFiltered.AddRange(languageFiltered);
                audioSizes.AddRange(languageSizes);
                audioSourceBitmaps.AddRange(languageSourceBitmaps);
                languageFiltered.Clear();
                languageSizes.Clear();
                languageSourceBitmaps.Clear();
            }

            // When "Same as video", merge audio into the video group
            if (audioIsSameAsVideo)
            {
                videoFiltered.AddRange(audioFiltered);
                videoSizes.AddRange(audioSizes);
                videoSourceBitmaps.AddRange(audioSourceBitmaps);
                audioFiltered.Clear();
                audioSizes.Clear();
                audioSourceBitmaps.Clear();
            }

            // Reverse after merge so the full combined list is reversed together
            if (ShouldReverseOrder(videoLayout, videoPosition))
            {
                videoFiltered.Reverse();
                videoSizes.Reverse();
                videoSourceBitmaps.Reverse();
            }

            if (!audioIsSameAsVideo && ShouldReverseOrder(audioLayout, audioPosition))
            {
                audioFiltered.Reverse();
                audioSizes.Reverse();
                audioSourceBitmaps.Reverse();
            }

            if (!languageIsSameAsAudio && ShouldReverseOrder(languageLayout, languagePosition))
            {
                languageFiltered.Reverse();
                languageSizes.Reverse();
                languageSourceBitmaps.Reverse();
            }

            // Calculate margin in pixels from percentage of image width
            var badgeMargin = (int)(image.Width * marginPercent / 100f);

            // Calculate gap: percentage of average badge height per group
            int ComputeGap(List<SKSizeI> sizes) =>
                sizes.Count > 0 ? (int)(sizes.Average(s => s.Height) * gapPercent / 100f) : 0;

            // Calculate stacking positions
            var videoPositions = videoSizes.Count > 0
                ? CalculateStackedPositions(image.Width, image.Height, videoSizes, videoPosition, badgeMargin, ComputeGap(videoSizes), videoLayout)
                : new List<SKPointI>();
            var audioPositions = audioSizes.Count > 0
                ? CalculateStackedPositions(image.Width, image.Height, audioSizes, audioPosition, badgeMargin, ComputeGap(audioSizes), audioLayout)
                : new List<SKPointI>();
            var languagePositions = languageSizes.Count > 0
                ? CalculateStackedPositions(image.Width, image.Height, languageSizes, languagePosition, badgeMargin, ComputeGap(languageSizes), languageLayout)
                : new List<SKPointI>();

            // Draw badges onto image
            using var surface = SKSurface.Create(new SKImageInfo(image.Width, image.Height));
            var canvas = surface.Canvas;
            canvas.DrawBitmap(image, 0, 0);

            if (useTextStyle)
            {
                if (videoFiltered.Count > 0)
                {
                    RenderTextBadges(canvas, videoFiltered, videoPositions, videoSizes, settings);
                }

                if (audioFiltered.Count > 0)
                {
                    RenderTextBadges(canvas, audioFiltered, audioPositions, audioSizes, settings);
                }

                if (languageFiltered.Count > 0)
                {
                    RenderTextBadges(canvas, languageFiltered, languagePositions, languageSizes, settings);
                }
            }
            else
            {
                using var paint = new SKPaint { IsAntialias = true };
                var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);

                for (int i = 0; i < videoSourceBitmaps.Count; i++)
                {
                    var destRect = SKRect.Create(videoPositions[i].X, videoPositions[i].Y, videoSizes[i].Width, videoSizes[i].Height);
                    using var badgeImage = SKImage.FromBitmap(videoSourceBitmaps[i]);
                    canvas.DrawImage(badgeImage, destRect, sampling, paint);
                }

                for (int i = 0; i < audioSourceBitmaps.Count; i++)
                {
                    var destRect = SKRect.Create(audioPositions[i].X, audioPositions[i].Y, audioSizes[i].Width, audioSizes[i].Height);
                    using var badgeImage = SKImage.FromBitmap(audioSourceBitmaps[i]);
                    canvas.DrawImage(badgeImage, destRect, sampling, paint);
                }

                // Language badges are text-only, render them as text even in image mode
                if (languageFiltered.Count > 0)
                {
                    RenderTextBadges(canvas, languageFiltered, languagePositions, languageSizes, settings);
                }
            }

            canvas.Flush();

            // Encode to configured format
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
            // sourceBadges are references to the cache, don't dispose them
        }
    }

    /// <inheritdoc />
    public bool ShouldShowBadge(BadgeInfo badge)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null || !config.Enabled)
        {
            return false;
        }

        return badge.Category switch
        {
            BadgeCategory.Resolution => badge.BadgeKey switch
            {
                "4k" => config.Show4K,
                "1080p" => config.Show1080p,
                "720p" => config.Show720p,
                "sd" => config.ShowSD,
                _ => false
            },
            BadgeCategory.Hdr => badge.BadgeKey switch
            {
                "hdr" => config.ShowGenericHdr,
                "hdr10" => config.ShowHdr10,
                "hdr10plus" => config.ShowHdr10Plus,
                "dv" => config.ShowDolbyVision,
                "hlg" => config.ShowHlg,
                _ => false
            },
            BadgeCategory.ThreeD => config.Show3D,
            BadgeCategory.Audio => badge.BadgeKey switch
            {
                "atmos" => config.ShowDolbyAtmos,
                "dtsx" => config.ShowDtsX,
                "truehd" => config.ShowTrueHD,
                "dtshdma" => config.ShowDtsHdMa,
                "7.1" or "5.1" or "stereo" => config.ShowChannelBadge,
                _ => false
            },
            BadgeCategory.Language => true,
            _ => false
        };
    }

    /// <inheritdoc />
    public void ReloadBadges()
    {
        _badgeLock.Wait();
        try
        {
            foreach (var badge in _badgeCache.Values)
            {
                badge?.Dispose();
            }

            _badgeCache.Clear();
            _badgesLoaded = false;
        }
        finally
        {
            _badgeLock.Release();
        }
    }

    private async Task PrepareBadgeGroup(
        List<BadgeInfo> badges, int sizePercent, int imageWidth, bool useTextStyle, bool isAudio,
        List<SKSizeI> sizes, List<SKBitmap> sourceBitmaps, List<BadgeInfo> filtered)
    {
        if (useTextStyle)
        {
            var badgeWidth = Math.Max(1, (int)(imageWidth * (sizePercent / 100.0)));
            var badgeHeight = Math.Max(1, (int)(badgeWidth * 0.5));

            foreach (var badgeInfo in badges)
            {
                var text = GetBadgeDisplayText(badgeInfo.BadgeKey);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                filtered.Add(badgeInfo);
                sizes.Add(new SKSizeI(badgeWidth, badgeHeight));
            }
        }
        else
        {
            foreach (var badgeInfo in badges)
            {
                var badgeBitmap = await GetBadgeByFileNameAsync(badgeInfo.ResourceFileName).ConfigureAwait(false);
                if (badgeBitmap == null)
                {
                    continue;
                }

                var badgeWidth = Math.Max(1, (int)(imageWidth * (sizePercent / 100.0)));
                var badgeHeight = Math.Max(1, (int)(badgeBitmap.Height * ((double)badgeWidth / badgeBitmap.Width)));

                sourceBitmaps.Add(badgeBitmap);
                filtered.Add(badgeInfo);
                sizes.Add(new SKSizeI(badgeWidth, badgeHeight));
            }
        }
    }

    private async Task<SKBitmap?> GetBadgeByFileNameAsync(string resourceFileName)
    {
        if (string.IsNullOrEmpty(resourceFileName))
        {
            return null;
        }

        await _badgeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_badgesLoaded)
            {
                LoadBadges();
                _badgesLoaded = true;
            }

            return _badgeCache.GetValueOrDefault(resourceFileName);
        }
        finally
        {
            _badgeLock.Release();
        }
    }

    private const int SvgRasterWidth = 512;

    private void LoadBadges()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        _logger.LogInformation("[JellyTag] Loading badges. Available resources: {Resources}", string.Join(", ", resourceNames));

        // Build a set of badge base names from all embedded resources (both .svg and .png)
        var assetsMarker = ".Assets.";
        var badgeBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resourceName in resourceNames)
        {
            var assetsIdx = resourceName.IndexOf(assetsMarker, StringComparison.OrdinalIgnoreCase);
            if (assetsIdx < 0)
            {
                continue;
            }

            var fileName = resourceName[(assetsIdx + assetsMarker.Length)..];
            if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                var baseName = Path.GetFileNameWithoutExtension(fileName);
                badgeBaseNames.Add(baseName);
            }
        }

        foreach (var baseName in badgeBaseNames)
        {
            var pngFileName = baseName + ".png";

            // Check for custom badge override first (SVG then PNG)
            var customSvgPath = GetCustomBadgePath(baseName + ".svg");
            var customPngPath = GetCustomBadgePath(pngFileName);

            if (customSvgPath != null && File.Exists(customSvgPath))
            {
                try
                {
                    var customBadge = RasterizeSvgFile(customSvgPath);
                    if (customBadge != null)
                    {
                        customBadge = TrimTransparent(customBadge);
                        _badgeCache[pngFileName] = customBadge;
                        _logger.LogInformation("[JellyTag] Loaded custom SVG badge override: {FileName}", baseName + ".svg");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[JellyTag] Failed to load custom SVG badge: {Path}", customSvgPath);
                }
            }

            if (customPngPath != null && File.Exists(customPngPath))
            {
                try
                {
                    var customBadge = SKBitmap.Decode(customPngPath);
                    if (customBadge != null)
                    {
                        customBadge = TrimTransparent(customBadge);
                        _badgeCache[pngFileName] = customBadge;
                        _logger.LogInformation("[JellyTag] Loaded custom PNG badge override: {FileName}", pngFileName);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[JellyTag] Failed to load custom badge: {Path}, falling back to embedded", customPngPath);
                }
            }

            // Try embedded SVG first, then PNG
            var svgResourceName = resourceNames.FirstOrDefault(r =>
                r.IndexOf(assetsMarker, StringComparison.OrdinalIgnoreCase) >= 0 &&
                r.EndsWith(baseName + ".svg", StringComparison.OrdinalIgnoreCase));

            if (svgResourceName != null)
            {
                try
                {
                    using var stream = assembly.GetManifestResourceStream(svgResourceName);
                    if (stream != null)
                    {
                        var badge = RasterizeSvgStream(stream);
                        if (badge != null)
                        {
                            badge = TrimTransparent(badge);
                            _badgeCache[pngFileName] = badge;
                            _logger.LogInformation("[JellyTag] Loaded SVG badge {BaseName}: {Width}x{Height}", baseName, badge.Width, badge.Height);
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[JellyTag] Failed to load SVG badge: {BaseName}, trying PNG fallback", baseName);
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
                            _badgeCache[pngFileName] = badge;
                            _logger.LogInformation("[JellyTag] Loaded badge {FileName}: {Width}x{Height}", pngFileName, badge.Width, badge.Height);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyTag] Failed to load badge: {FileName}", pngFileName);
                }
            }
        }

        _logger.LogInformation("[JellyTag] Badge loading complete. Loaded {Count} badges", _badgeCache.Count);
    }

    private static SKBitmap? RasterizeSvgFile(string filePath)
    {
        using var svg = new SKSvg();
        svg.Load(filePath);
        return RasterizeSvgPicture(svg.Picture);
    }

    private static SKBitmap? RasterizeSvgStream(Stream stream)
    {
        using var svg = new SKSvg();
        svg.Load(stream);
        return RasterizeSvgPicture(svg.Picture);
    }

    private static SKBitmap? RasterizeSvgPicture(SKPicture? picture)
    {
        if (picture == null)
        {
            return null;
        }

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var scale = SvgRasterWidth / bounds.Width;
        var height = (int)(bounds.Height * scale);
        var bitmap = new SKBitmap(SvgRasterWidth, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Flush();
        return bitmap;
    }

    private static SKBitmap TrimTransparent(SKBitmap bitmap)
    {
        int minX = bitmap.Width, minY = bitmap.Height, maxX = 0, maxY = 0;
        var pixelBytes = bitmap.GetPixelSpan();
        var bitmapWidth = bitmap.Width;
        var bytesPerPixel = bitmap.BytesPerPixel;
        // Alpha is at offset 3 for BGRA/RGBA pixel formats
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

        if (maxX < minX || maxY < minY)
        {
            return bitmap;
        }

        var trimWidth = maxX - minX + 1;
        var trimHeight = maxY - minY + 1;

        var trimmed = new SKBitmap(trimWidth, trimHeight, bitmap.ColorType, bitmap.AlphaType);
        using var canvas = new SKCanvas(trimmed);
        canvas.DrawBitmap(bitmap, SKRect.Create(minX, minY, trimWidth, trimHeight), SKRect.Create(0, 0, trimWidth, trimHeight));
        canvas.Flush();

        bitmap.Dispose();
        return trimmed;
    }

    private static string? GetCustomBadgePath(string fileName)
    {
        var dataFolder = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrEmpty(dataFolder))
        {
            return null;
        }

        return Path.Combine(dataFolder, "custom-badges", fileName);
    }

    private static bool ShouldReverseOrder(BadgeLayout layout, BadgePosition position) =>
        (layout == BadgeLayout.Vertical && (position == BadgePosition.BottomLeft || position == BadgePosition.BottomRight))
        || (layout == BadgeLayout.Horizontal && (position == BadgePosition.TopRight || position == BadgePosition.BottomRight));

    private static List<SKPointI> CalculateStackedPositions(
        int imageWidth, int imageHeight,
        List<SKSizeI> badges,
        BadgePosition position, int margin, int gap,
        BadgeLayout layout)
    {
        var positions = new List<SKPointI>();
        if (badges.Count == 0)
        {
            return positions;
        }

        if (layout == BadgeLayout.Horizontal)
        {
            var totalWidth = badges.Sum(b => b.Width) + (badges.Count - 1) * gap;
            var maxHeight = badges.Max(b => b.Height);

            int startX, startY;
            switch (position)
            {
                case BadgePosition.TopLeft:
                    startX = margin;
                    startY = margin;
                    break;
                case BadgePosition.TopRight:
                    startX = Math.Max(0, imageWidth - totalWidth - margin);
                    startY = margin;
                    break;
                case BadgePosition.BottomLeft:
                    startX = margin;
                    startY = Math.Max(0, imageHeight - maxHeight - margin);
                    break;
                case BadgePosition.BottomRight:
                    startX = Math.Max(0, imageWidth - totalWidth - margin);
                    startY = Math.Max(0, imageHeight - maxHeight - margin);
                    break;
                default:
                    startX = margin;
                    startY = margin;
                    break;
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
                    startX = margin;
                    startY = margin;
                    break;
                case BadgePosition.TopRight:
                    startX = Math.Max(0, imageWidth - maxWidth - margin);
                    startY = margin;
                    break;
                case BadgePosition.BottomLeft:
                    startX = margin;
                    startY = Math.Max(0, imageHeight - totalHeight - margin);
                    break;
                case BadgePosition.BottomRight:
                    startX = Math.Max(0, imageWidth - maxWidth - margin);
                    startY = Math.Max(0, imageHeight - totalHeight - margin);
                    break;
                default:
                    startX = margin;
                    startY = margin;
                    break;
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
        if (config?.CustomBadgeTexts != null && config.CustomBadgeTexts.TryGetValue(badgeKey, out var customText) && !string.IsNullOrEmpty(customText))
        {
            return customText;
        }

        return BadgeDisplayText.TryGetValue(badgeKey, out var text) ? text : badgeKey.ToUpperInvariant();
    }

    private static SKColor ParseHexColor(string hex, byte alpha)
    {
        if (SKColor.TryParse(hex, out var color))
        {
            return color.WithAlpha(alpha);
        }

        return new SKColor(0, 0, 0, alpha);
    }

    private static (string bg, string text) ResolveCategoryColors(BadgeCategory category, ImageTypeSettings settings)
    {
        var bgColor = category switch
        {
            BadgeCategory.Resolution or BadgeCategory.Hdr or BadgeCategory.ThreeD =>
                settings.VideoBadgeBgColor ?? settings.TextBadgeBgColor ?? "#000000",
            BadgeCategory.Audio =>
                settings.AudioBadgeBgColor ?? settings.TextBadgeBgColor ?? "#000000",
            BadgeCategory.Language =>
                settings.LanguageBadgeBgColor ?? settings.TextBadgeBgColor ?? "#000000",
            _ => settings.TextBadgeBgColor ?? "#000000"
        };
        var textColor = category switch
        {
            BadgeCategory.Resolution or BadgeCategory.Hdr or BadgeCategory.ThreeD =>
                settings.VideoBadgeTextColor ?? settings.TextBadgeTextColor ?? "#FFFFFF",
            BadgeCategory.Audio =>
                settings.AudioBadgeTextColor ?? settings.TextBadgeTextColor ?? "#FFFFFF",
            BadgeCategory.Language =>
                settings.LanguageBadgeTextColor ?? settings.TextBadgeTextColor ?? "#FFFFFF",
            _ => settings.TextBadgeTextColor ?? "#FFFFFF"
        };
        return (bgColor, textColor);
    }

    private static void RenderTextBadges(SKCanvas canvas, List<BadgeInfo> badges, List<SKPointI> positions, List<SKSizeI> sizes, ImageTypeSettings settings)
    {
        var bgAlpha = (byte)Math.Clamp(settings.TextBadgeBgOpacity, 0, 255);
        var cornerRadiusPct = Math.Clamp(settings.TextBadgeCornerRadius, 0, 50);

        for (int i = 0; i < badges.Count; i++)
        {
            var (bgHex, textHex) = ResolveCategoryColors(badges[i].Category, settings);
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

            // Start at 70% of height, then adjust by ratio if too wide
            var fontSize = height * 0.7f;
            var font = new SKFont(SKTypeface.Default, fontSize);
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
            font.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and optionally managed resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            foreach (var badge in _badgeCache.Values)
            {
                badge?.Dispose();
            }

            _badgeCache.Clear();
            _badgeLock.Dispose();
        }

        _disposed = true;
    }
}
