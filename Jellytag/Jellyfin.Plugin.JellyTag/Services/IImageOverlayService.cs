using Jellyfin.Plugin.JellyTag.Configuration;

namespace Jellyfin.Plugin.JellyTag.Services;

/// <summary>
/// Interface for image overlay service.
/// </summary>
public interface IImageOverlayService
{
    /// <summary>
    /// Adds multiple badge overlays to an image, stacking them according to layout settings.
    /// Returns the result stream and the content type (e.g. "image/jpeg" or "image/webp").
    /// </summary>
    Task<(Stream Stream, string ContentType)> AddBadgeOverlaysAsync(Stream originalImage, List<BadgeInfo> badges, ImageTypeSettings settings);

    /// <summary>
    /// Determines if a badge should be shown based on its info and configuration.
    /// </summary>
    bool ShouldShowBadge(BadgeInfo badge);

    /// <summary>
    /// Reloads all badge images from resources and custom badges directory.
    /// </summary>
    void ReloadBadges();
}
