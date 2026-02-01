using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTag.Services;

/// <summary>
/// Service for caching modified images.
/// </summary>
public class ImageCacheService : IImageCacheService
{
    private readonly ILogger<ImageCacheService> _logger;
    private readonly string _cachePath;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageCacheService"/> class.
    /// </summary>
    public ImageCacheService(ILogger<ImageCacheService> logger)
    {
        _logger = logger;
        _cachePath = Plugin.Instance?.CacheFolderPath ?? Path.Combine(Path.GetTempPath(), "JellyTag", "cache");
        EnsureCacheDirectoryExists();
    }

    /// <inheritdoc />
    public Task<Stream?> GetCachedImageAsync(Guid itemId, string badgeKey, string imageTag)
    {
        var cacheKey = GenerateCacheKey(itemId, badgeKey, imageTag);
        var cacheFilePath = GetCachePath(cacheKey);

        if (!File.Exists(cacheFilePath))
        {
            return Task.FromResult<Stream?>(null);
        }

        var config = Plugin.Instance?.Configuration;
        var cacheHours = config?.CacheDurationHours ?? 24;
        var fileInfo = new FileInfo(cacheFilePath);

        if (fileInfo.LastWriteTimeUtc.AddHours(cacheHours) < DateTime.UtcNow)
        {
            _logger.LogDebug("Cache expired for item {ItemId}", itemId);
            try
            {
                File.Delete(cacheFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete expired cache file: {Path}", cacheFilePath);
            }

            return Task.FromResult<Stream?>(null);
        }

        _logger.LogDebug("Cache hit for item {ItemId}", itemId);
        Stream stream = new FileStream(cacheFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        return Task.FromResult<Stream?>(stream);
    }

    /// <inheritdoc />
    public async Task CacheImageAsync(Guid itemId, string badgeKey, string imageTag, Stream imageStream)
    {
        var cacheKey = GenerateCacheKey(itemId, badgeKey, imageTag);
        var cachePath = GetCachePath(cacheKey);
        var tempPath = cachePath + ".tmp";

        try
        {
            EnsureCacheDirectoryExists();

            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await imageStream.CopyToAsync(fileStream).ConfigureAwait(false);
            }

            File.Move(tempPath, cachePath, overwrite: true);

            _logger.LogDebug("Cached image for item {ItemId} at {Path}", itemId, cachePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache image for item {ItemId}", itemId);

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <inheritdoc />
    public string GetCacheDirectory() => _cachePath;

    /// <inheritdoc />
    public void ClearCache()
    {
        lock (_lock)
        {
            try
            {
                if (Directory.Exists(_cachePath))
                {
                    var jpgFiles = Directory.GetFiles(_cachePath, "*.jpg");
                    var webpFiles = Directory.GetFiles(_cachePath, "*.webp");
                    var files = jpgFiles.Concat(webpFiles).ToArray();
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete cache file: {Path}", file);
                        }
                    }

                    _logger.LogInformation("Cleared {Count} cached images", files.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear cache");
            }
        }
    }

    /// <inheritdoc />
    public void InvalidateCache(Guid itemId)
    {
        try
        {
            var jpgPattern = $"{itemId}_*.jpg";
            var webpPattern = $"{itemId}_*.webp";
            var files = Directory.GetFiles(_cachePath, jpgPattern).Concat(Directory.GetFiles(_cachePath, webpPattern)).ToArray();
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                    _logger.LogDebug("Invalidated cache for item {ItemId}: {File}", itemId, file);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete cache file: {Path}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate cache for item {ItemId}", itemId);
        }
    }

    /// <inheritdoc />
    public (int FileCount, long TotalSizeBytes, DateTime? OldestEntry, DateTime? NewestEntry) GetCacheStats()
    {
        try
        {
            if (!Directory.Exists(_cachePath))
            {
                return (0, 0, null, null);
            }

            var jpgFiles = Directory.GetFiles(_cachePath, "*.jpg");
            var webpFiles = Directory.GetFiles(_cachePath, "*.webp");
            var allFiles = jpgFiles.Concat(webpFiles).Select(f => new FileInfo(f)).ToArray();

            if (allFiles.Length == 0)
            {
                return (0, 0, null, null);
            }

            var totalSize = allFiles.Sum(f => f.Length);
            var oldest = allFiles.Min(f => f.LastWriteTimeUtc);
            var newest = allFiles.Max(f => f.LastWriteTimeUtc);

            return (allFiles.Length, totalSize, oldest, newest);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get cache stats");
            return (0, 0, null, null);
        }
    }

    private string GenerateCacheKey(Guid itemId, string badgeKey, string imageTag)
    {
        var config = Plugin.Instance?.Configuration;
        var configFingerprint = config != null ? ComputeConfigFingerprint(config) : string.Empty;
        var input = $"{itemId}_{badgeKey}_{imageTag}_{configFingerprint}";

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hash = Convert.ToHexString(hashBytes)[..16];

        return $"{itemId}_{hash}";
    }

    private static string ComputeConfigFingerprint(Configuration.PluginConfiguration config)
    {
        var sb = new StringBuilder(256);
        sb.Append(config.Enabled).Append('|');
        sb.Append(config.Show4K).Append(config.Show1080p).Append(config.Show720p).Append(config.ShowSD).Append('|');
        sb.Append(config.ShowHdr10).Append(config.ShowHdr10Plus).Append(config.ShowDolbyVision).Append(config.ShowHlg).Append(config.ShowGenericHdr).Append('|');
        sb.Append(config.ShowH264).Append(config.ShowHevc).Append(config.ShowAv1).Append(config.ShowVp9);
        sb.Append(config.Show3D).Append('|');
        sb.Append(config.ShowDolbyAtmos).Append(config.ShowDtsX).Append(config.ShowTrueHD).Append(config.ShowDtsHdMa).Append(config.ShowChannelBadge).Append('|');
        sb.Append((int)config.LanguageBadgeMode).Append(config.ShowSubtitleIndicator).Append('|');
        sb.Append((int)config.OutputFormat).Append(config.JpegQuality).Append(config.WebPQuality).Append('|');
        AppendSettingsFingerprint(sb, config.PosterSettings);
        AppendSettingsFingerprint(sb, config.ThumbnailSettings);
        AppendSettingsFingerprint(sb, config.BackdropSettings);
        // Include custom badge texts
        if (config.CustomBadgeTexts != null)
        {
            foreach (var cbt in config.CustomBadgeTexts)
            {
                sb.Append(cbt.Key).Append('=').Append(cbt.Text).Append(',');
            }
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hashBytes)[..16];
    }

    private static void AppendSettingsFingerprint(StringBuilder sb, Configuration.ImageTypeSettings s)
    {
        sb.Append(s.Enabled).Append(s.BadgeSizePercent).Append(s.AudioBadgeSizePercent).Append(s.LanguageBadgeSizePercent);
        sb.Append(s.BadgeMarginPercent).Append(s.BadgeGapPercent);
        sb.Append((int)s.BadgePosition).Append((int)s.BadgeLayout).Append((int)s.BadgeStyle);
        sb.Append(s.AudioBadgePosition?.ToString() ?? "n").Append(s.AudioBadgeLayout?.ToString() ?? "n").Append(s.AudioBadgeStyle?.ToString() ?? "n");
        sb.Append(s.LanguageBadgePosition?.ToString() ?? "n").Append(s.LanguageBadgeLayout?.ToString() ?? "n").Append(s.LanguageBadgeStyle?.ToString() ?? "n");
        sb.Append(s.TextBadgeBgColor).Append(s.TextBadgeBgOpacity).Append(s.TextBadgeTextColor).Append(s.TextBadgeCornerRadius);
        sb.Append(s.VideoBadgeBgColor ?? "n").Append(s.VideoBadgeTextColor ?? "n");
        sb.Append(s.AudioBadgeBgColor ?? "n").Append(s.AudioBadgeTextColor ?? "n");
        sb.Append(s.LanguageBadgeBgColor ?? "n").Append(s.LanguageBadgeTextColor ?? "n");
        sb.Append(s.SubtitleBadgeBgColor ?? "n").Append(s.SubtitleBadgeTextColor ?? "n");
        sb.Append(s.AudioTextBadgeBgOpacity).Append(s.AudioTextBadgeCornerRadius);
        sb.Append(s.LanguageTextBadgeBgOpacity).Append(s.LanguageTextBadgeCornerRadius);
        sb.Append(s.CodecBadgeBgColor ?? "n").Append(s.CodecBadgeTextColor ?? "n");
        sb.Append(s.CodecTextBadgeBgOpacity).Append(s.CodecTextBadgeCornerRadius);
        sb.Append(s.SubtitleTextBadgeBgOpacity).Append(s.SubtitleTextBadgeCornerRadius);
        sb.Append('|');
    }

    private string GetCachePath(string cacheKey)
    {
        var config = Plugin.Instance?.Configuration;
        var ext = config?.OutputFormat == Configuration.OutputImageFormat.WebP ? ".webp" : ".jpg";
        return Path.Combine(_cachePath, $"{cacheKey}{ext}");
    }

    private void EnsureCacheDirectoryExists()
    {
        if (!Directory.Exists(_cachePath))
        {
            Directory.CreateDirectory(_cachePath);
        }
    }
}
