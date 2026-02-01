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
    private readonly Dictionary<string, byte[]> _svgCache = new();
    private readonly Dictionary<string, SKBitmap?> _rasterCache = new();
    private readonly SemaphoreSlim _badgeLock = new(1, 1);
    private bool _badgesLoaded;
    private bool _disposed;

    private const int MinBadgeSizePercent = 5;
    private const int MaxBadgeSizePercent = 50;
    private const float MinBadgeMarginPercent = 0f;
    private const float MaxBadgeMarginPercent = 20f;

    private static readonly string[] SupportedCustomExtensions = { ".svg", ".png", ".jpg", ".jpeg" };

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
        var jpegQuality = Math.Clamp(config.JpegQuality, 1, 100);

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
        var languageBadges = badges.Where(b => b.Category is BadgeCategory.Language or BadgeCategory.Subtitle).ToList();

        var videoPosition = settings.BadgePosition;
        var videoLayout = settings.BadgeLayout;
        var audioPosition = settings.AudioBadgePosition ?? settings.BadgePosition;
        var audioLayout = settings.AudioBadgeLayout ?? settings.BadgeLayout;
        var languagePosition = settings.LanguageBadgePosition ?? audioPosition;
        var languageLayout = settings.LanguageBadgeLayout ?? audioLayout;

        // Resolve badge style per group with inheritance chain: language -> audio -> video
        var videoStyle = settings.BadgeStyle;
        var audioStyle = settings.AudioBadgeStyle ?? videoStyle;
        var languageStyle = settings.LanguageBadgeStyle ?? audioStyle;

        var videoUseText = videoStyle == Configuration.BadgeStyle.Text;
        var audioUseText = audioStyle == Configuration.BadgeStyle.Text;
        var languageUseText = languageStyle == Configuration.BadgeStyle.Text;

        // When audio uses "Same as video", merge into a single group (same position + layout + style)
        var audioIsSameAsVideo = settings.AudioBadgePosition == null && settings.AudioBadgeLayout == null && settings.AudioBadgeStyle == null;

        // When language uses same position+layout+style as audio, merge into audio group
        var languageIsSameAsAudio = languagePosition == audioPosition && languageLayout == audioLayout && languageStyle == audioStyle;

        // Process each group independently
        var videoSizes = new List<SKSizeI>();
        var videoSourceBitmaps = new List<SKBitmap>();
        var videoFiltered = new List<BadgeInfo>();
        var videoOwnedBitmaps = new List<SKBitmap>();

        var audioSizes = new List<SKSizeI>();
        var audioSourceBitmaps = new List<SKBitmap>();
        var audioFiltered = new List<BadgeInfo>();
        var audioOwnedBitmaps = new List<SKBitmap>();

        var languageSizes = new List<SKSizeI>();
        var languageSourceBitmaps = new List<SKBitmap>();
        var languageFiltered = new List<BadgeInfo>();
        var languageOwnedBitmaps = new List<SKBitmap>();

        var languageBadgeSizePercent = Math.Clamp(settings.LanguageBadgeSizePercent > 0 ? settings.LanguageBadgeSizePercent : audioBadgeSizePercent, MinBadgeSizePercent, MaxBadgeSizePercent);

        try
        {
            // Prepare video badges
            await PrepareBadgeGroup(videoBadges, videoBadgeSizePercent, image.Width, videoUseText, false, videoSizes, videoSourceBitmaps, videoFiltered, videoOwnedBitmaps).ConfigureAwait(false);
            // Prepare audio badges
            await PrepareBadgeGroup(audioBadges, audioBadgeSizePercent, image.Width, audioUseText, true, audioSizes, audioSourceBitmaps, audioFiltered, audioOwnedBitmaps).ConfigureAwait(false);
            // Prepare language badges — in image mode, badges with ResourceFileName get flag images; those without fall back to text
            await PrepareBadgeGroup(languageBadges, languageBadgeSizePercent, image.Width, languageUseText, true, languageSizes, languageSourceBitmaps, languageFiltered, languageOwnedBitmaps).ConfigureAwait(false);

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
                audioOwnedBitmaps.AddRange(languageOwnedBitmaps);
                languageFiltered.Clear();
                languageSizes.Clear();
                languageSourceBitmaps.Clear();
                languageOwnedBitmaps.Clear();
            }

            // When "Same as video", merge audio into the video group
            if (audioIsSameAsVideo)
            {
                videoFiltered.AddRange(audioFiltered);
                videoSizes.AddRange(audioSizes);
                videoSourceBitmaps.AddRange(audioSourceBitmaps);
                videoOwnedBitmaps.AddRange(audioOwnedBitmaps);
                audioFiltered.Clear();
                audioSizes.Clear();
                audioSourceBitmaps.Clear();
                audioOwnedBitmaps.Clear();
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

            // Calculate vertical extent of a badge group (always height-based for stacking rows)
            int GroupVerticalExtent(List<SKSizeI> sizes, int groupGap, BadgeLayout lay) =>
                sizes.Count == 0 ? 0 : lay == BadgeLayout.Horizontal
                    ? sizes.Max(s => s.Height) + groupGap
                    : sizes.Sum(s => s.Height) + (sizes.Count - 1) * groupGap + groupGap;

            // Calculate stacking positions, offsetting groups that share the same corner
            var videoGap = ComputeGap(videoSizes);
            var videoPositions = videoSizes.Count > 0
                ? CalculateStackedPositions(image.Width, image.Height, videoSizes, videoPosition, badgeMargin, videoGap, videoLayout)
                : new List<SKPointI>();

            // Audio: offset vertically if same corner as video
            var audioGap = ComputeGap(audioSizes);
            var audioPriorExtent = (audioPosition == videoPosition)
                ? GroupVerticalExtent(videoSizes, videoGap, videoLayout) : 0;
            var audioPositions = audioSizes.Count > 0
                ? CalculateStackedPositions(image.Width, image.Height, audioSizes, audioPosition, badgeMargin, audioGap, audioLayout, audioPriorExtent)
                : new List<SKPointI>();

            // Language: offset vertically if same corner as video and/or audio
            var languageGap = ComputeGap(languageSizes);
            var languagePriorExtent = 0;
            if (languagePosition == videoPosition)
            {
                languagePriorExtent += GroupVerticalExtent(videoSizes, videoGap, videoLayout);
            }

            if (languagePosition == audioPosition)
            {
                languagePriorExtent += GroupVerticalExtent(audioSizes, audioGap, audioLayout);
            }

            var languagePositions = languageSizes.Count > 0
                ? CalculateStackedPositions(image.Width, image.Height, languageSizes, languagePosition, badgeMargin, languageGap, languageLayout, languagePriorExtent)
                : new List<SKPointI>();

            // Draw badges onto image
            using var surface = SKSurface.Create(new SKImageInfo(image.Width, image.Height));
            var canvas = surface.Canvas;
            canvas.DrawBitmap(image, 0, 0);

            using var paint = new SKPaint { IsAntialias = true };
            var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);

            // Render each group with its own style
            RenderBadgeGroup(canvas, videoFiltered, videoSourceBitmaps, videoPositions, videoSizes, videoUseText, settings, paint, sampling);
            RenderBadgeGroup(canvas, audioFiltered, audioSourceBitmaps, audioPositions, audioSizes, audioUseText, settings, paint, sampling);
            RenderBadgeGroup(canvas, languageFiltered, languageSourceBitmaps, languagePositions, languageSizes, languageUseText, settings, paint, sampling);

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
            // Dispose SVG-rasterized bitmaps (owned by this call, not the cache)
            foreach (var bmp in videoOwnedBitmaps) bmp.Dispose();
            foreach (var bmp in audioOwnedBitmaps) bmp.Dispose();
            foreach (var bmp in languageOwnedBitmaps) bmp.Dispose();
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
            BadgeCategory.Subtitle => true,
            _ => false
        };
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
        List<BadgeInfo> badges, int sizePercent, int imageWidth, bool useTextStyle, bool isAudio,
        List<SKSizeI> sizes, List<SKBitmap> sourceBitmaps, List<BadgeInfo> filtered, List<SKBitmap> ownedBitmaps)
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
            await EnsureBadgesLoaded().ConfigureAwait(false);

            foreach (var badgeInfo in badges)
            {
                var resourceFileName = badgeInfo.ResourceFileName;
                var badgeWidth = Math.Max(1, (int)(imageWidth * (sizePercent / 100.0)));

                if (string.IsNullOrEmpty(resourceFileName))
                {
                    // No image resource — fall back to text rendering for this badge
                    var text = GetBadgeDisplayText(badgeInfo.BadgeKey);
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    var badgeHeight = Math.Max(1, (int)(badgeWidth * 0.5));
                    filtered.Add(badgeInfo);
                    sizes.Add(new SKSizeI(badgeWidth, badgeHeight));
                    continue;
                }

                // Try SVG cache first
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
                }

                // Try raster cache
                if (_rasterCache.TryGetValue(resourceFileName, out var rasterBitmap) && rasterBitmap != null)
                {
                    var badgeHeight = Math.Max(1, (int)(rasterBitmap.Height * ((double)badgeWidth / rasterBitmap.Width)));
                    sourceBitmaps.Add(rasterBitmap);
                    filtered.Add(badgeInfo);
                    sizes.Add(new SKSizeI(badgeWidth, badgeHeight));
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
        if (picture == null)
        {
            return null;
        }

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

            // Try width/height attributes
            var widthAttr = root.Attribute("width")?.Value;
            var heightAttr = root.Attribute("height")?.Value;
            if (!string.IsNullOrEmpty(widthAttr) && !string.IsNullOrEmpty(heightAttr))
            {
                // Strip "px" suffix if present
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

        return 2f; // Default 2:1 ratio
    }

    private void LoadBadges()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        _logger.LogInformation("[JellyTag] Loading badges. Available resources: {Resources}", string.Join(", ", resourceNames));

        var assetsMarker = ".Assets.";
        var badgeBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Collect all badge base names from embedded resources (both .svg and .png)
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

            // 1. Check custom badges: SVG > PNG > JPG
            if (customDir != null)
            {
                var customSvg = Path.Combine(customDir, svgFileName);
                if (File.Exists(customSvg))
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(customSvg);
                        _svgCache[svgFileName] = bytes;
                        _logger.LogInformation("[JellyTag] Loaded custom SVG badge: {FileName}", svgFileName);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[JellyTag] Failed to load custom SVG badge: {Path}", customSvg);
                    }
                }

                // Custom PNG
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
                            _logger.LogInformation("[JellyTag] Loaded custom PNG badge: {FileName}", pngFileName);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[JellyTag] Failed to load custom PNG badge: {Path}", customPng);
                    }
                }

                // Custom JPG/JPEG
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
                                _logger.LogInformation("[JellyTag] Loaded custom JPEG badge: {FileName}", baseName + ext);
                                goto nextBadge;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[JellyTag] Failed to load custom JPEG badge: {Path}", customJpg);
                        }
                    }
                }
            }

            // 2. Embedded SVG
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
                        _logger.LogInformation("[JellyTag] Loaded embedded SVG badge: {FileName}", svgFileName);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyTag] Failed to load embedded SVG badge: {FileName}", svgFileName);
                }
            }

            // 3. Fallback: embedded PNG
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
                            _logger.LogInformation("[JellyTag] Loaded embedded PNG fallback: {FileName}", pngFileName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[JellyTag] Failed to load embedded PNG badge: {FileName}", pngFileName);
                }
            }

            nextBadge:;
        }

        _logger.LogInformation("[JellyTag] Badge loading complete. SVG: {SvgCount}, Raster: {RasterCount}", _svgCache.Count, _rasterCache.Count);
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

    private static string? GetCustomBadgeDir()
    {
        var dataFolder = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrEmpty(dataFolder))
        {
            return null;
        }

        return Path.Combine(dataFolder, "custom-badges");
    }

    private static string? GetCustomBadgePath(string fileName)
    {
        var dir = GetCustomBadgeDir();
        if (dir == null) return null;
        return Path.Combine(dir, fileName);
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
                    startY = margin + priorExtent;
                    break;
                case BadgePosition.TopRight:
                    startX = Math.Max(0, imageWidth - totalWidth - margin);
                    startY = margin + priorExtent;
                    break;
                case BadgePosition.BottomLeft:
                    startX = margin;
                    startY = Math.Max(0, imageHeight - maxHeight - margin - priorExtent);
                    break;
                case BadgePosition.BottomRight:
                    startX = Math.Max(0, imageWidth - totalWidth - margin);
                    startY = Math.Max(0, imageHeight - maxHeight - margin - priorExtent);
                    break;
                default:
                    startX = margin;
                    startY = margin + priorExtent;
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
                    startY = margin + priorExtent;
                    break;
                case BadgePosition.TopRight:
                    startX = Math.Max(0, imageWidth - maxWidth - margin);
                    startY = margin + priorExtent;
                    break;
                case BadgePosition.BottomLeft:
                    startX = margin;
                    startY = Math.Max(0, imageHeight - totalHeight - margin - priorExtent);
                    break;
                case BadgePosition.BottomRight:
                    startX = Math.Max(0, imageWidth - maxWidth - margin);
                    startY = Math.Max(0, imageHeight - totalHeight - margin - priorExtent);
                    break;
                default:
                    startX = margin;
                    startY = margin + priorExtent;
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
        var customText = config?.CustomBadgeTexts?.FirstOrDefault(x => string.Equals(x.Key, badgeKey, StringComparison.OrdinalIgnoreCase))?.Text;
        if (!string.IsNullOrEmpty(customText))
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
            BadgeCategory.Subtitle =>
                settings.SubtitleBadgeBgColor ?? settings.TextBadgeBgColor ?? "#000000",
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
            BadgeCategory.Subtitle =>
                settings.SubtitleBadgeTextColor ?? settings.TextBadgeTextColor ?? "#FFFFFF",
            _ => settings.TextBadgeTextColor ?? "#FFFFFF"
        };
        return (bgColor, textColor);
    }

    private static byte ResolveCategoryOpacity(BadgeCategory category, ImageTypeSettings settings)
    {
        var raw = category switch
        {
            BadgeCategory.Audio => settings.AudioTextBadgeBgOpacity > 0 ? settings.AudioTextBadgeBgOpacity : settings.TextBadgeBgOpacity,
            BadgeCategory.Language => settings.LanguageTextBadgeBgOpacity > 0 ? settings.LanguageTextBadgeBgOpacity : settings.TextBadgeBgOpacity,
            BadgeCategory.Subtitle => settings.SubtitleTextBadgeBgOpacity > 0 ? settings.SubtitleTextBadgeBgOpacity : settings.TextBadgeBgOpacity,
            _ => settings.TextBadgeBgOpacity
        };
        return (byte)Math.Clamp(raw, 0, 255);
    }

    private static int ResolveCategoryCornerRadius(BadgeCategory category, ImageTypeSettings settings)
    {
        var raw = category switch
        {
            BadgeCategory.Audio => settings.AudioTextBadgeCornerRadius >= 0 ? settings.AudioTextBadgeCornerRadius : settings.TextBadgeCornerRadius,
            BadgeCategory.Language => settings.LanguageTextBadgeCornerRadius >= 0 ? settings.LanguageTextBadgeCornerRadius : settings.TextBadgeCornerRadius,
            BadgeCategory.Subtitle => settings.SubtitleTextBadgeCornerRadius >= 0 ? settings.SubtitleTextBadgeCornerRadius : settings.TextBadgeCornerRadius,
            _ => settings.TextBadgeCornerRadius
        };
        return Math.Clamp(raw, 0, 50);
    }

    private static void RenderTextBadges(SKCanvas canvas, List<BadgeInfo> badges, List<SKPointI> positions, List<SKSizeI> sizes, ImageTypeSettings settings)
    {
        for (int i = 0; i < badges.Count; i++)
        {
            var (bgHex, textHex) = ResolveCategoryColors(badges[i].Category, settings);
            var bgAlpha = ResolveCategoryOpacity(badges[i].Category, settings);
            var cornerRadiusPct = ResolveCategoryCornerRadius(badges[i].Category, settings);
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

    private static void RenderBadgeGroup(
        SKCanvas canvas, List<BadgeInfo> filtered, List<SKBitmap> sourceBitmaps,
        List<SKPointI> positions, List<SKSizeI> sizes, bool useText,
        ImageTypeSettings settings, SKPaint paint, SKSamplingOptions sampling)
    {
        if (filtered.Count == 0) return;

        if (useText)
        {
            RenderTextBadges(canvas, filtered, positions, sizes, settings);
        }
        else
        {
            // In image mode, render bitmaps for badges that have them, text fallback for others
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
                RenderTextBadges(canvas, textBadges, textPositions, textSizes, settings);
            }
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
