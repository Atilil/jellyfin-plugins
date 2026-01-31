using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.JellyTag.Services;

/// <summary>
/// Interface for quality detection service.
/// </summary>
public interface IQualityDetectionService
{
    /// <summary>
    /// Gets the video quality for an item by its ID.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <returns>The detected video quality.</returns>
    VideoQuality GetQuality(Guid itemId);

    /// <summary>
    /// Gets the video quality from a base item.
    /// </summary>
    /// <param name="item">The base item.</param>
    /// <returns>The detected video quality.</returns>
    VideoQuality GetQualityFromItem(BaseItem item);

    /// <summary>
    /// Detects all applicable badges for an item (resolution, HDR, audio, etc.).
    /// </summary>
    /// <param name="item">The base item.</param>
    /// <returns>A list of detected badges.</returns>
    List<BadgeInfo> DetectAllBadges(BaseItem item);

    /// <summary>
    /// Clears the in-memory badge detection cache.
    /// </summary>
    void ClearBadgeCache();
}
