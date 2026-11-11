using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Configuration;
using Jellyfin.Plugin.WatchSync.Services;
using Jellyfin.Plugin.WatchSync.Utils;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchSync.Tasks;

/// <summary>
/// Scheduled task to perform a full synchronization of watch history.
/// Optimized version with O(n) indexing instead of O(n²).
/// </summary>
public class FullSyncTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IConflictResolver _conflictResolver;
    private readonly ILogger<FullSyncTask> _logger;

    /// <summary>
    /// Initializes the full synchronization task.
    /// </summary>
    public FullSyncTask(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        IConflictResolver conflictResolver,
        ILogger<FullSyncTask> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _conflictResolver = conflictResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "WatchSync Full Synchronization";

    /// <inheritdoc />
    public string Description => "Synchronizes watch history between all libraries for all users.";

    /// <inheritdoc />
    public string Category => "WatchSync";

    /// <inheritdoc />
    public string Key => "WatchSyncFullSync";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.WeeklyTrigger,
                DayOfWeek = DayOfWeek.Sunday,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
            }
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null || !config.IsEnabled)
        {
            _logger.LogInformation("Plugin disabled, full sync skipped");
            return;
        }

        _logger.LogInformation("Starting full cross-library sync (optimized)");

        var users = _userManager.GetUsers()
            .Where(u => !config.ExcludedUserIds.Contains(u.Id.ToString()))
            .ToList();

        _logger.LogInformation("Syncing for {UserCount} user(s)", users.Count);

        // Phase 1: Get all items
        progress.Report(0);
        var allItems = GetAllSyncableItems(config);
        _logger.LogInformation("Loaded {ItemCount} media item(s)", allItems.Count);

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 2: Build indexes (single pass over all items)
        progress.Report(5);
        _logger.LogInformation("Building provider indexes...");
        var matchGroups = BuildMatchIndex(allItems, config, cancellationToken);
        _logger.LogInformation("Index built: {GroupCount} match groups found", matchGroups.Count);

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 3: Sync pairs
        progress.Report(10);
        var syncedPairs = 0;
        var processedGroups = 0;
        var totalGroups = matchGroups.Count;

        foreach (var group in matchGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var items = group.Value;
            if (items.Count < 2)
            {
                continue;
            }

            // Sync all pairs in this group (bidirectional)
            for (int i = 0; i < items.Count; i++)
            {
                for (int j = i + 1; j < items.Count; j++)
                {
                    foreach (var user in users)
                    {
                        // Method syncs in both directions
                        SyncItemPairForUser(items[i], items[j], user);
                    }

                    syncedPairs++;
                }
            }

            processedGroups++;
            progress.Report(10 + ((double)processedGroups / totalGroups * 90));
        }

        _logger.LogInformation(
            "Full sync completed: {GroupCount} groups analyzed, {SyncedPairs} pairs synced",
            totalGroups,
            syncedPairs);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Builds an index of items grouped by their match identifiers.
    /// Each group contains items representing the same content.
    /// </summary>
    private Dictionary<string, List<BaseItem>> BuildMatchIndex(
        List<BaseItem> items,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        // Temporary indexes by provider
        var imdbIndex = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);
        var tmdbIndex = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);
        var tvdbIndex = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);
        var episodeIndex = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);
        var titleYearIndex = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);

        // First pass: index all items
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Index by IMDB
            if (config.UseImdbId)
            {
                var imdbId = item.GetProviderId(MetadataProvider.Imdb);
                if (!string.IsNullOrEmpty(imdbId))
                {
                    AddToIndex(imdbIndex, $"imdb:{imdbId}", item);
                }
            }

            // Index by TMDB
            if (config.UseTmdbId)
            {
                var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
                if (!string.IsNullOrEmpty(tmdbId))
                {
                    AddToIndex(tmdbIndex, $"tmdb:{tmdbId}", item);
                }
            }

            // Index by TVDB
            if (config.UseTvdbId)
            {
                var tvdbId = item.GetProviderId(MetadataProvider.Tvdb);
                if (!string.IsNullOrEmpty(tvdbId))
                {
                    AddToIndex(tvdbIndex, $"tvdb:{tvdbId}", item);
                }
            }

            // Index episodes by series + S##E##
            if (item is Episode episode && episode.Series != null)
            {
                var seriesKey = GetSeriesKey(episode.Series, config);
                if (!string.IsNullOrEmpty(seriesKey) &&
                    episode.ParentIndexNumber.HasValue &&
                    episode.IndexNumber.HasValue)
                {
                    var episodeKey = $"{seriesKey}:S{episode.ParentIndexNumber:D2}E{episode.IndexNumber:D2}";
                    AddToIndex(episodeIndex, episodeKey, item);
                }
            }

            // Index by Title + Year (fallback)
            if (config.UseTitleYearFallback && item is not Episode)
            {
                var titleKey = GetTitleYearKey(item);
                if (!string.IsNullOrEmpty(titleKey))
                {
                    AddToIndex(titleYearIndex, titleKey, item);
                }
            }
        }

        // Merge indexes according to configured priority
        var finalGroups = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);
        var processedItemIds = new HashSet<Guid>();

        // Provider priority order
        var providerOrder = GetProviderOrder(config.ProviderPriority);

        foreach (var provider in providerOrder)
        {
            var index = provider.ToUpperInvariant() switch
            {
                "IMDB" => imdbIndex,
                "TMDB" => tmdbIndex,
                "TVDB" => tvdbIndex,
                _ => null
            };

            if (index == null)
            {
                continue;
            }

            MergeIndex(index, finalGroups, processedItemIds);
        }

        // Add episode index
        MergeIndex(episodeIndex, finalGroups, processedItemIds);

        // Add title+year fallback last
        if (config.UseTitleYearFallback)
        {
            MergeIndex(titleYearIndex, finalGroups, processedItemIds);
        }

        // Keep only groups with at least 2 items (matches)
        return finalGroups
            .Where(g => g.Value.Count >= 2)
            .ToDictionary(g => g.Key, g => g.Value);
    }

    private static void AddToIndex(Dictionary<string, List<BaseItem>> index, string key, BaseItem item)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = new List<BaseItem>();
            index[key] = list;
        }

        list.Add(item);
    }

    private static void MergeIndex(
        Dictionary<string, List<BaseItem>> sourceIndex,
        Dictionary<string, List<BaseItem>> targetGroups,
        HashSet<Guid> processedItemIds)
    {
        foreach (var kvp in sourceIndex)
        {
            // Keep only unprocessed items
            var newItems = kvp.Value.Where(item => !processedItemIds.Contains(item.Id)).ToList();

            if (newItems.Count < 2)
            {
                continue;
            }

            // Mark these items as processed
            foreach (var item in newItems)
            {
                processedItemIds.Add(item.Id);
            }

            targetGroups[kvp.Key] = newItems;
        }
    }

    private static string? GetSeriesKey(Series series, PluginConfiguration config)
    {
        // Try each provider in priority order
        var providerOrder = GetProviderOrder(config.ProviderPriority);

        foreach (var provider in providerOrder)
        {
            var providerId = provider.ToUpperInvariant() switch
            {
                "IMDB" when config.UseImdbId => series.GetProviderId(MetadataProvider.Imdb),
                "TMDB" when config.UseTmdbId => series.GetProviderId(MetadataProvider.Tmdb),
                "TVDB" when config.UseTvdbId => series.GetProviderId(MetadataProvider.Tvdb),
                _ => null
            };

            if (!string.IsNullOrEmpty(providerId))
            {
                return $"series:{provider.ToLowerInvariant()}:{providerId}";
            }
        }

        return null;
    }

    private static string? GetTitleYearKey(BaseItem item)
    {
        if (string.IsNullOrEmpty(item.Name) || !item.ProductionYear.HasValue)
        {
            return null;
        }

        var normalizedTitle = TitleUtils.NormalizeTitle(item.Name);
        return $"title:{normalizedTitle}:{item.ProductionYear}";
    }

    private static string[] GetProviderOrder(string? priorityString)
    {
        if (string.IsNullOrWhiteSpace(priorityString))
        {
            return new[] { "IMDB", "TMDB", "TVDB" };
        }

        return priorityString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private void SyncItemPairForUser(BaseItem item1, BaseItem item2, User user)
    {
        try
        {
            var userData1 = _userDataManager.GetUserData(user, item1);
            var userData2 = _userDataManager.GetUserData(user, item2);

            var lastPlayed1 = userData1?.LastPlayedDate ?? DateTime.MinValue;
            var lastPlayed2 = userData2?.LastPlayedDate ?? DateTime.MinValue;

            BaseItem sourceItem, targetItem;
            if (lastPlayed1 >= lastPlayed2)
            {
                sourceItem = item1;
                targetItem = item2;
            }
            else
            {
                sourceItem = item2;
                targetItem = item1;
            }

            // Sync source -> target
            var syncData = _conflictResolver.ResolveSyncData(sourceItem, targetItem, user);
            ApplySyncData(syncData, targetItem, user, sourceItem.Name);

            // Reverse sync to unify PlayCount (target -> source)
            var reverseSyncData = _conflictResolver.ResolveSyncData(targetItem, sourceItem, user);
            ApplySyncData(reverseSyncData, sourceItem, user, targetItem.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error syncing {Item1} <-> {Item2} for user {UserName}",
                item1.Name,
                item2.Name,
                user.Username);
        }
    }

    private void ApplySyncData(SyncData syncData, BaseItem targetItem, User user, string sourceName)
    {
        try
        {
            if (!syncData.ShouldSync)
            {
                return;
            }

            var targetUserData = _userDataManager.GetUserData(user, targetItem);
            if (targetUserData == null)
            {
                return;
            }

            if (syncData.Played.HasValue)
            {
                targetUserData.Played = syncData.Played.Value;
            }

            if (syncData.PlaybackPositionTicks.HasValue)
            {
                targetUserData.PlaybackPositionTicks = syncData.PlaybackPositionTicks.Value;
            }

            if (syncData.PlayCount.HasValue)
            {
                targetUserData.PlayCount = syncData.PlayCount.Value;
            }

            if (syncData.LastPlayedDate.HasValue)
            {
                targetUserData.LastPlayedDate = syncData.LastPlayedDate;
            }

            _userDataManager.SaveUserData(
                user,
                targetItem,
                targetUserData,
                UserDataSaveReason.UpdateUserData,
                CancellationToken.None);

            _logger.LogDebug(
                "Synced {SourceName} -> {TargetName} for user {UserName}",
                sourceName,
                targetItem.Name,
                user.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying sync to {TargetName}", targetItem.Name);
        }
    }

    private List<BaseItem> GetAllSyncableItems(PluginConfiguration config)
    {
        var excludedLibraries = config.ExcludedLibraryIds ?? new List<string>();

        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[]
            {
                BaseItemKind.Movie,
                BaseItemKind.Episode
            },
            IsVirtualItem = false,
            Recursive = true
        };

        var items = _libraryManager.GetItemList(query);

        return items
            .Where(item =>
            {
                var libraryId = GetLibraryId(item);
                return libraryId == null || !excludedLibraries.Contains(libraryId.Value.ToString());
            })
            .ToList();
    }

    private static Guid? GetLibraryId(BaseItem item)
    {
        var parent = item;
        while (parent != null)
        {
            if (parent is Folder folder && folder.IsTopParent)
            {
                return folder.Id;
            }

            parent = parent.GetParent();
        }

        return item.ParentId;
    }
}
