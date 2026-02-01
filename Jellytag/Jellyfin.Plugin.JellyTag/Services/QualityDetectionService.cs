using System.Collections.Concurrent;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;
using VideoRange = Jellyfin.Data.Enums.VideoRange;
using VideoRangeType = Jellyfin.Data.Enums.VideoRangeType;

namespace Jellyfin.Plugin.JellyTag.Services;

/// <summary>
/// Service for detecting video quality from media items.
/// </summary>
public class QualityDetectionService : IQualityDetectionService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<QualityDetectionService> _logger;
    private readonly ConcurrentDictionary<Guid, (List<BadgeInfo> Badges, DateTime CachedAt)> _badgeCache = new();
    private static readonly TimeSpan BadgeCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="QualityDetectionService"/> class.
    /// </summary>
    public QualityDetectionService(
        ILibraryManager libraryManager,
        ILogger<QualityDetectionService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public VideoQuality GetQuality(Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item == null)
        {
            _logger.LogDebug("Item not found: {ItemId}", itemId);
            return VideoQuality.Unknown;
        }

        return GetQualityFromItem(item);
    }

    /// <summary>
    /// Determines the video quality based on resolution.
    /// </summary>
    public static VideoQuality DetermineQuality(int width, int height)
    {
        var maxDimension = Math.Max(width, height);

        if (maxDimension >= 3840)
        {
            return VideoQuality.UHD4K;
        }

        if (maxDimension >= 1920)
        {
            return VideoQuality.FHD1080p;
        }

        if (maxDimension >= 1280)
        {
            return VideoQuality.HD720p;
        }

        if (maxDimension > 0)
        {
            return VideoQuality.SD;
        }

        return VideoQuality.Unknown;
    }

    /// <inheritdoc />
    public VideoQuality GetQualityFromItem(BaseItem item)
    {
        if (item is Video video)
        {
            return GetQualityFromVideo(video);
        }

        var query = new InternalItemsQuery
        {
            ParentId = item.Id,
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
            Limit = 10
        };
        var children = _libraryManager.GetItemList(query);
        var bestQuality = VideoQuality.Unknown;
        foreach (var child in children)
        {
            if (child is Video childVideo)
            {
                var q = GetQualityFromVideo(childVideo);
                if (q != VideoQuality.Unknown && (bestQuality == VideoQuality.Unknown || q > bestQuality))
                {
                    bestQuality = q;
                    if (bestQuality == VideoQuality.UHD4K)
                    {
                        break;
                    }
                }
            }
        }

        if (bestQuality != VideoQuality.Unknown)
        {
            _logger.LogDebug("Resolved quality {Quality} for parent item: {ItemName}", bestQuality, item.Name);
        }

        return bestQuality;
    }

    /// <inheritdoc />
    public List<BadgeInfo> DetectAllBadges(BaseItem item)
    {
        if (_badgeCache.TryGetValue(item.Id, out var cached) && DateTime.UtcNow - cached.CachedAt < BadgeCacheTtl)
        {
            return new List<BadgeInfo>(cached.Badges);
        }

        var badges = DetectAllBadgesInternal(item);
        _badgeCache[item.Id] = (badges, DateTime.UtcNow);
        return badges;
    }

    /// <inheritdoc />
    public void ClearBadgeCache()
    {
        _badgeCache.Clear();
    }

    private List<BadgeInfo> DetectAllBadgesInternal(BaseItem item)
    {
        var badges = new List<BadgeInfo>();

        if (item is Video video)
        {
            DetectBadgesFromVideo(video, badges);
        }
        else
        {
            var query = new InternalItemsQuery
            {
                ParentId = item.Id,
                Recursive = true,
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
                Limit = 10
            };
            var children = _libraryManager.GetItemList(query);

            var bestResolution = VideoQuality.Unknown;
            Video? bestVideo = null;

            foreach (var child in children)
            {
                if (child is Video childVideo)
                {
                    var q = GetQualityFromVideo(childVideo);
                    if (q != VideoQuality.Unknown && (bestResolution == VideoQuality.Unknown || q > bestResolution))
                    {
                        bestResolution = q;
                        bestVideo = childVideo;
                    }

                    bestVideo ??= childVideo;
                }
            }

            if (bestResolution != VideoQuality.Unknown)
            {
                badges.Add(CreateResolutionBadge(bestResolution));
            }

            if (bestVideo != null)
            {
                DetectHdrAndAudioBadges(bestVideo, badges);
            }
        }

        return badges;
    }

    private void DetectBadgesFromVideo(Video video, List<BadgeInfo> badges)
    {
        DetectBadgesFromVideo(video, badges, includeResolution: true);
    }

    private void DetectHdrAndAudioBadges(Video video, List<BadgeInfo> badges)
    {
        DetectBadgesFromVideo(video, badges, includeResolution: false);
    }

    private void DetectBadgesFromVideo(Video video, List<BadgeInfo> badges, bool includeResolution)
    {
        try
        {
            var mediaSources = video.GetMediaSources(false);
            var mediaSource = mediaSources?.FirstOrDefault();
            var videoStream = mediaSource?.MediaStreams?.FirstOrDefault(s => s.Type == MediaStreamType.Video);

            if (videoStream != null)
            {
                if (includeResolution)
                {
                    var width = videoStream.Width ?? 0;
                    var height = videoStream.Height ?? 0;
                    var quality = DetermineQuality(width, height);
                    if (quality != VideoQuality.Unknown)
                    {
                        badges.Add(CreateResolutionBadge(quality));
                    }
                }

                var hdrBadge = DetectHdr(videoStream);
                if (hdrBadge != null)
                {
                    badges.Add(hdrBadge);
                }

                // Video codec detection
                var codec = videoStream.Codec?.ToLowerInvariant() ?? string.Empty;
                if (codec is "h264" or "avc")
                {
                    badges.Add(new BadgeInfo { Category = BadgeCategory.VideoCodec, BadgeKey = "h264", ResourceFileName = string.Empty });
                }
                else if (codec is "hevc" or "h265")
                {
                    badges.Add(new BadgeInfo { Category = BadgeCategory.VideoCodec, BadgeKey = "hevc", ResourceFileName = string.Empty });
                }
                else if (codec == "av1")
                {
                    badges.Add(new BadgeInfo { Category = BadgeCategory.VideoCodec, BadgeKey = "av1", ResourceFileName = string.Empty });
                }
                else if (codec == "vp9")
                {
                    badges.Add(new BadgeInfo { Category = BadgeCategory.VideoCodec, BadgeKey = "vp9", ResourceFileName = string.Empty });
                }
            }

            // 3D detection
            if (video.Video3DFormat.HasValue)
            {
                badges.Add(new BadgeInfo
                {
                    Category = BadgeCategory.ThreeD,
                    BadgeKey = "3d",
                    ResourceFileName = "badge-3d.svg"
                });
            }

            // Audio detection - prefer the default audio track
            var allAudioStreams = mediaSource?.MediaStreams?.Where(s => s.Type == MediaStreamType.Audio).ToList();
            if (allAudioStreams != null && allAudioStreams.Count > 0)
            {
                var defaultStream = allAudioStreams.FirstOrDefault(s => s.IsDefault);
                var streamsToAnalyze = defaultStream != null
                    ? new List<MediaStream> { defaultStream }
                    : new List<MediaStream> { allAudioStreams[0] };
                var audioBadges = DetectAudio(streamsToAnalyze);
                badges.AddRange(audioBadges);
            }

            // Language detection
            var allStreams = mediaSource?.MediaStreams;
            if (allStreams != null)
            {
                var langBadges = DetectLanguages(allStreams.ToList());
                badges.AddRange(langBadges);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect badges for video: {ItemName}", video.Name);
        }
    }

    private static readonly Dictionary<string, string> LangCodeToFlag = new(StringComparer.OrdinalIgnoreCase)
    {
        { "fre", "fra" }, { "ger", "deu" }, { "dut", "nld" }, { "cze", "ces" }, { "rum", "ron" }, { "chi", "zho" },
        { "gre", "ell" }, { "may", "msa" }, { "tgl", "fil" }, { "slo", "slk" }, { "baq", "eus" }, { "wel", "cym" }
    };

    private static readonly HashSet<string> KnownFlagCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "fra", "eng", "jpn", "deu", "spa", "ita", "por", "kor", "zho", "rus",
        "nld", "ara", "hin", "tha", "pol", "tur", "swe", "dan", "nor", "fin",
        "ces", "hun", "ron", "ukr", "vie", "heb",
        "ell", "ind", "msa", "fil", "hrv", "srp", "bul", "slk", "lit", "lav",
        "est", "cat", "eus", "glg", "cym"
    };

    private static string GetFlagResourceFileName(string langCode)
    {
        var normalized = LangCodeToFlag.TryGetValue(langCode, out var mapped) ? mapped : langCode;
        return KnownFlagCodes.Contains(normalized) ? $"flag-{normalized.ToLowerInvariant()}.svg" : string.Empty;
    }

    private static List<BadgeInfo> DetectLanguages(List<MediaStream> allStreams)
    {
        var badges = new List<BadgeInfo>();
        var config = Plugin.Instance?.Configuration;
        if (config == null || config.LanguageBadgeMode == Configuration.LanguageBadgeMode.None)
        {
            return badges;
        }

        var audioStreams = allStreams.Where(s => s.Type == MediaStreamType.Audio).ToList();
        if (audioStreams.Count == 0)
        {
            return badges;
        }

        var addedLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (config.LanguageBadgeMode == Configuration.LanguageBadgeMode.DefaultOnly)
        {
            var defaultStream = audioStreams.FirstOrDefault(s => s.IsDefault) ?? audioStreams[0];
            var lang = defaultStream.Language;
            if (!string.IsNullOrEmpty(lang) && addedLanguages.Add(lang))
            {
                var langLower = lang.ToLowerInvariant();
                badges.Add(new BadgeInfo
                {
                    Category = BadgeCategory.Language,
                    BadgeKey = langLower,
                    ResourceFileName = GetFlagResourceFileName(langLower)
                });
            }
        }
        else // All
        {
            foreach (var stream in audioStreams)
            {
                var lang = stream.Language;
                if (!string.IsNullOrEmpty(lang) && addedLanguages.Add(lang))
                {
                    var langLower = lang.ToLowerInvariant();
                    badges.Add(new BadgeInfo
                    {
                        Category = BadgeCategory.Language,
                        BadgeKey = langLower,
                        ResourceFileName = GetFlagResourceFileName(langLower)
                    });
                }
            }
        }

        // VOSTFR indicator
        if (config.ShowSubtitleIndicator)
        {
            var audioLanguages = new HashSet<string>(
                audioStreams.Where(s => !string.IsNullOrEmpty(s.Language)).Select(s => s.Language!.ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);

            var subtitleStreams = allStreams.Where(s => s.Type == MediaStreamType.Subtitle).ToList();
            foreach (var sub in subtitleStreams)
            {
                var subLang = sub.Language?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(subLang) && !audioLanguages.Contains(subLang))
                {
                    var key = "vost" + subLang;
                    if (addedLanguages.Add(key))
                    {
                        badges.Add(new BadgeInfo
                        {
                            Category = BadgeCategory.Subtitle,
                            BadgeKey = key,
                            ResourceFileName = string.Empty
                        });
                    }
                }
            }
        }

        return badges;
    }

    private static BadgeInfo? DetectHdr(MediaStream videoStream)
    {
        var config = Plugin.Instance?.Configuration;

        // If generic HDR mode is enabled, return a single "hdr" badge for any HDR content
        if (config?.ShowGenericHdr == true)
        {
            var range = videoStream.VideoRange;
            var rt = videoStream.VideoRangeType;
            if (range == VideoRange.HDR || rt is VideoRangeType.HDR10 or VideoRangeType.HDR10Plus
                or VideoRangeType.HLG or VideoRangeType.DOVI or VideoRangeType.DOVIWithHDR10
                or VideoRangeType.DOVIWithHLG or VideoRangeType.DOVIWithSDR or VideoRangeType.DOVIWithEL
                or VideoRangeType.DOVIWithHDR10Plus or VideoRangeType.DOVIWithELHDR10Plus)
            {
                return new BadgeInfo
                {
                    Category = BadgeCategory.Hdr,
                    BadgeKey = "hdr",
                    ResourceFileName = "badge-hdr.svg"
                };
            }

            return null;
        }

        var rangeType = videoStream.VideoRangeType;

        // Dolby Vision variants
        if (rangeType is VideoRangeType.DOVI
            or VideoRangeType.DOVIWithHDR10
            or VideoRangeType.DOVIWithHLG
            or VideoRangeType.DOVIWithSDR
            or VideoRangeType.DOVIWithEL
            or VideoRangeType.DOVIWithHDR10Plus
            or VideoRangeType.DOVIWithELHDR10Plus)
        {
            return new BadgeInfo
            {
                Category = BadgeCategory.Hdr,
                BadgeKey = "dv",
                ResourceFileName = "badge-dv.svg"
            };
        }

        // HDR10+
        if (rangeType == VideoRangeType.HDR10Plus)
        {
            return new BadgeInfo
            {
                Category = BadgeCategory.Hdr,
                BadgeKey = "hdr10plus",
                ResourceFileName = "badge-hdr10plus.svg"
            };
        }

        // HLG
        if (rangeType == VideoRangeType.HLG)
        {
            return new BadgeInfo
            {
                Category = BadgeCategory.Hdr,
                BadgeKey = "hlg",
                ResourceFileName = "badge-hlg.svg"
            };
        }

        // HDR10
        if (rangeType == VideoRangeType.HDR10)
        {
            return new BadgeInfo
            {
                Category = BadgeCategory.Hdr,
                BadgeKey = "hdr10",
                ResourceFileName = "badge-hdr10.svg"
            };
        }

        // Fallback to VideoRange
        if (videoStream.VideoRange == VideoRange.HDR)
        {
            return new BadgeInfo
            {
                Category = BadgeCategory.Hdr,
                BadgeKey = "hdr10",
                ResourceFileName = "badge-hdr10.svg"
            };
        }

        return null;
    }

    private static List<BadgeInfo> DetectAudio(IEnumerable<MediaStream> audioStreams)
    {
        var badges = new List<BadgeInfo>();
        BadgeInfo? codecBadge = null;
        int codecPriority = -1;
        int bestChannels = 0;

        foreach (var stream in audioStreams)
        {
            var codec = stream.Codec?.ToUpperInvariant() ?? string.Empty;
            var profile = stream.Profile?.ToUpperInvariant() ?? string.Empty;
            var channels = stream.Channels ?? 0;

            if (channels > bestChannels)
            {
                bestChannels = channels;
            }

            // Priority: Atmos > TrueHD > DTS-X > DTS-HD MA > EAC3 > AC3 > DTS
            int priority = -1;
            BadgeInfo? candidate = null;

            if (profile.Contains("ATMOS"))
            {
                priority = 7;
                candidate = new BadgeInfo { Category = BadgeCategory.Audio, BadgeKey = "atmos", ResourceFileName = "badge-atmos.svg" };
            }
            else if (codec == "TRUEHD")
            {
                priority = 6;
                candidate = new BadgeInfo { Category = BadgeCategory.Audio, BadgeKey = "truehd", ResourceFileName = "badge-truehd.svg" };
            }
            else if (profile.Contains("DTS:X") || profile.Contains("DTS-X") || profile.Contains("DTSX"))
            {
                priority = 5;
                candidate = new BadgeInfo { Category = BadgeCategory.Audio, BadgeKey = "dtsx", ResourceFileName = "badge-dtsx.svg" };
            }
            else if (profile.Contains("DTS-HD MA") || profile.Contains("DTS-HD MASTER") || (codec == "DTS" && profile.Contains("MA")))
            {
                priority = 4;
                candidate = new BadgeInfo { Category = BadgeCategory.Audio, BadgeKey = "dtshdma", ResourceFileName = "badge-dtshdma.svg" };
            }
            else if (codec is "EAC3" or "AC3")
            {
                priority = 1;
                // No specific badge for AC3, channel badge will cover it
            }
            else if (codec == "DTS")
            {
                priority = 0;
                // No specific badge for plain DTS
            }

            if (candidate != null && priority > codecPriority)
            {
                codecPriority = priority;
                codecBadge = candidate;
            }
        }

        if (codecBadge != null)
        {
            badges.Add(codecBadge);
        }

        // Channel layout badge
        if (bestChannels >= 8)
        {
            badges.Add(new BadgeInfo { Category = BadgeCategory.Audio, BadgeKey = "7.1", ResourceFileName = "badge-7_1.svg" });
        }
        else if (bestChannels >= 6)
        {
            badges.Add(new BadgeInfo { Category = BadgeCategory.Audio, BadgeKey = "5.1", ResourceFileName = "badge-5_1.svg" });
        }
        else if (bestChannels >= 2)
        {
            badges.Add(new BadgeInfo { Category = BadgeCategory.Audio, BadgeKey = "stereo", ResourceFileName = "badge-stereo.svg" });
        }

        return badges;
    }

    private static BadgeInfo CreateResolutionBadge(VideoQuality quality)
    {
        return quality switch
        {
            VideoQuality.UHD4K => new BadgeInfo { Category = BadgeCategory.Resolution, BadgeKey = "4k", ResourceFileName = "badge-4k.svg" },
            VideoQuality.FHD1080p => new BadgeInfo { Category = BadgeCategory.Resolution, BadgeKey = "1080p", ResourceFileName = "badge-1080p.svg" },
            VideoQuality.HD720p => new BadgeInfo { Category = BadgeCategory.Resolution, BadgeKey = "720p", ResourceFileName = "badge-720p.svg" },
            VideoQuality.SD => new BadgeInfo { Category = BadgeCategory.Resolution, BadgeKey = "sd", ResourceFileName = "badge-sd.svg" },
            _ => new BadgeInfo { Category = BadgeCategory.Resolution, BadgeKey = "unknown", ResourceFileName = string.Empty }
        };
    }

    private VideoQuality GetQualityFromVideo(Video video)
    {
        try
        {
            var mediaSources = video.GetMediaSources(false);
            var mediaSource = mediaSources?.FirstOrDefault();
            var videoStream = mediaSource?.MediaStreams?.FirstOrDefault(s => s.Type == MediaStreamType.Video);
            if (videoStream == null)
            {
                return VideoQuality.Unknown;
            }

            var width = videoStream.Width ?? 0;
            var height = videoStream.Height ?? 0;
            return DetermineQuality(width, height);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get media sources for video item: {ItemName}", video.Name);
            return VideoQuality.Unknown;
        }
    }
}
