using Jellyfin.Plugin.JellyTag.Configuration;

namespace Jellyfin.Plugin.JellyTag.Services;

/// <summary>
/// Interface for image overlay service.
/// </summary>
public interface IImageOverlayService
{
    /// <summary>
    /// Adds multiple badge overlays to an image using per-panel configuration.
    /// Returns the result stream and the content type.
    /// </summary>
    Task<(Stream Stream, string ContentType)> AddBadgeOverlaysAsync(Stream originalImage, List<BadgeInfo> badges, ImageTypeConfig imageConfig);

    /// <summary>
    /// Determines if a badge should be shown based on the image type config panels.
    /// </summary>
    bool ShouldShowBadge(BadgeInfo badge, ImageTypeConfig imageConfig);

    /// <summary>
    /// Reloads all badge images from resources and custom badges directory.
    /// </summary>
    void ReloadBadges();
}
