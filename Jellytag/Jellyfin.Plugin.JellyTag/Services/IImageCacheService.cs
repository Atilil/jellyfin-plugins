namespace Jellyfin.Plugin.JellyTag.Services;

/// <summary>
/// Interface for image cache service.
/// </summary>
public interface IImageCacheService
{
    /// <summary>
    /// Gets a cached image if available and not expired.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <param name="badgeKey">The composite badge key (e.g. "4k_hdr10_atmos").</param>
    /// <param name="imageTag">The image tag/etag for cache invalidation.</param>
    /// <returns>The cached image stream, or null if not cached.</returns>
    Task<Stream?> GetCachedImageAsync(Guid itemId, string badgeKey, string imageTag);

    /// <summary>
    /// Caches an image.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    /// <param name="badgeKey">The composite badge key.</param>
    /// <param name="imageTag">The image tag/etag.</param>
    /// <param name="imageStream">The image stream to cache.</param>
    Task CacheImageAsync(Guid itemId, string badgeKey, string imageTag, Stream imageStream);

    /// <summary>
    /// Clears all cached images.
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Invalidates cache for a specific item.
    /// </summary>
    /// <param name="itemId">The item ID.</param>
    void InvalidateCache(Guid itemId);

    /// <summary>
    /// Gets the cache directory path.
    /// </summary>
    /// <returns>The absolute path to the cache directory.</returns>
    string GetCacheDirectory();

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    /// <returns>A tuple of (FileCount, TotalSizeBytes, OldestEntry, NewestEntry).</returns>
    (int FileCount, long TotalSizeBytes, DateTime? OldestEntry, DateTime? NewestEntry) GetCacheStats();
}
