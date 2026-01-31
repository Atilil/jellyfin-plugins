using System.Text.RegularExpressions;
using Jellyfin.Plugin.JellyTag.Configuration;
using Jellyfin.Plugin.JellyTag.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTag.Middleware;

/// <summary>
/// Middleware that intercepts Jellyfin image requests and adds quality badge overlays.
/// This works for ALL clients (web, mobile, TV, Kodi) since it operates at the HTTP level.
/// </summary>
public partial class ImageOverlayMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ImageOverlayMiddleware> _logger;

    [GeneratedRegex(@"^/Items/([0-9a-f]{32}|[0-9a-f-]{36})/Images/(Primary|Thumb|Backdrop)(/\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ImagePathRegex();

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageOverlayMiddleware"/> class.
    /// </summary>
    public ImageOverlayMiddleware(RequestDelegate next, ILogger<ImageOverlayMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Processes the HTTP request, intercepting image requests to add badge overlays.
    /// </summary>
    public async Task InvokeAsync(
        HttpContext context,
        IQualityDetectionService qualityService,
        IImageOverlayService overlayService,
        IImageCacheService cacheService,
        MediaBrowser.Controller.Library.ILibraryManager libraryManager)
    {
        var path = context.Request.Path.Value;
        if (path == null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var match = ImagePathRegex().Match(path);
        if (!match.Success)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var config = Plugin.Instance?.Configuration;
        if (config == null || !config.Enabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var itemIdStr = match.Groups[1].Value;
        var imageType = match.Groups[2].Value;

        if (!Guid.TryParse(itemIdStr, out var itemId))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var item = libraryManager.GetItemById(itemId);
        if (item == null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (item is not (Movie or Series or Season or Episode or Video))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Episodes use Thumbnail settings for Primary images (they are landscape thumbnails, not posters)
        var imageSettings = GetImageTypeSettings(config, imageType, item);
        if (imageSettings == null || !imageSettings.Enabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Detect all badges and filter by config
        var allBadges = qualityService.DetectAllBadges(item);
        _logger.LogDebug("[JellyTag] DetectAllBadges for {Item}: {Count} badges found: {Badges}",
            item.Name, allBadges.Count, string.Join(", ", allBadges.Select(b => $"{b.Category}:{b.BadgeKey}")));

        var visibleBadges = allBadges.Where(b => overlayService.ShouldShowBadge(b)).ToList();
        _logger.LogDebug("[JellyTag] Visible badges after filter: {Count}: {Badges}",
            visibleBadges.Count, string.Join(", ", visibleBadges.Select(b => b.BadgeKey)));

        if (visibleBadges.Count == 0)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Build composite badge key for caching
        var badgeKey = string.Join("_", visibleBadges.Select(b => b.BadgeKey));
        _logger.LogInformation("[JellyTag] Applying {Count} badges to {Item}: {BadgeKey}", visibleBadges.Count, item.Name, badgeKey);

        var query = context.Request.QueryString.Value ?? string.Empty;
        var tag = context.Request.Query["tag"].FirstOrDefault() ?? item.DateModified.Ticks.ToString();
        var imageTag = $"{tag}_{imageType}_{query}";

        // Check cache
        var cachedImage = await cacheService.GetCachedImageAsync(itemId, badgeKey, imageTag).ConfigureAwait(false);
        if (cachedImage != null)
        {
            await using (cachedImage.ConfigureAwait(false))
            {
                context.Response.ContentType = "image/jpeg";
                context.Response.ContentLength = cachedImage.Length;
                await cachedImage.CopyToAsync(context.Response.Body).ConfigureAwait(false);
            }

            return;
        }

        // Capture the original response
        var originalBody = context.Response.Body;
        using var capturedBody = new MemoryStream();
        context.Response.Body = capturedBody;

        try
        {
            await _next(context).ConfigureAwait(false);

            if (context.Response.StatusCode != 200 || capturedBody.Length == 0)
            {
                capturedBody.Position = 0;
                await capturedBody.CopyToAsync(originalBody).ConfigureAwait(false);
                return;
            }

            capturedBody.Position = 0;

            Stream resultStream;
            try
            {
                resultStream = await overlayService.AddBadgeOverlaysAsync(capturedBody, visibleBadges, imageSettings).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyTag] Failed to add badge overlay, serving original image");
                capturedBody.Position = 0;
                await capturedBody.CopyToAsync(originalBody).ConfigureAwait(false);
                return;
            }

            await using (resultStream.ConfigureAwait(false))
            {
                // Cache the result
                resultStream.Position = 0;
                await cacheService.CacheImageAsync(itemId, badgeKey, imageTag, resultStream).ConfigureAwait(false);

                // Write to response
                resultStream.Position = 0;
                context.Response.ContentType = "image/jpeg";
                context.Response.ContentLength = resultStream.Length;
                await resultStream.CopyToAsync(originalBody).ConfigureAwait(false);
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static ImageTypeSettings? GetImageTypeSettings(PluginConfiguration config, string imageType, BaseItem item)
    {
        return imageType.ToUpperInvariant() switch
        {
            // Episode Primary images are landscape thumbnails, not portrait posters
            "PRIMARY" when item is Episode => config.ThumbnailSettings,
            "PRIMARY" => config.PosterSettings,
            "THUMB" => config.ThumbnailSettings,
            "BACKDROP" => config.BackdropSettings,
            _ => null
        };
    }
}
