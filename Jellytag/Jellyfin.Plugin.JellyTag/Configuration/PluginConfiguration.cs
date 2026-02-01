using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyTag.Configuration;

/// <summary>
/// Badge position options.
/// </summary>
public enum BadgePosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>
/// Badge rendering style.
/// </summary>
public enum BadgeStyle
{
    Image,
    Text
}

/// <summary>
/// Badge display mode (highest quality only, or all).
/// </summary>
public enum BadgeDisplayMode
{
    Highest,
    All
}

/// <summary>
/// Output image format.
/// </summary>
public enum OutputImageFormat
{
    Jpeg,
    WebP
}

/// <summary>
/// Badge layout direction for stacking multiple badges.
/// </summary>
public enum BadgeLayout
{
    Horizontal,
    Vertical
}

/// <summary>
/// Per-badge-type text style override.
/// </summary>
public class BadgeTypeStyleOverride
{
    public string BadgeKey { get; set; } = "";
    public string? BgColor { get; set; }
    public int? BgOpacity { get; set; }
    public string? TextColor { get; set; }
    public int? CornerRadius { get; set; }
}

/// <summary>
/// Settings for a badge panel (Resolution, HDR, Codec, Audio, Language).
/// </summary>
public class BadgePanelSettings
{
    public bool Enabled { get; set; } = true;
    public BadgePosition Position { get; set; } = BadgePosition.TopLeft;
    public BadgeDisplayMode ShowMode { get; set; } = BadgeDisplayMode.Highest;
    public BadgeLayout Layout { get; set; } = BadgeLayout.Vertical;
    public float GapPercent { get; set; } = 2f;
    public int SizePercent { get; set; } = 15;
    public float MarginPercent { get; set; } = 2.5f;
    public BadgeStyle Style { get; set; } = BadgeStyle.Image;
    public int Order { get; set; }

    // Text style settings (used when Style == Text)
    public string TextBgColor { get; set; } = "#000000";
    public int TextBgOpacity { get; set; } = 180;
    public string TextColor { get; set; } = "#FFFFFF";
    public int TextCornerRadius { get; set; } = 25;

    // Per-badge-type overrides (key = badge key, value = style override)
    public List<BadgeTypeStyleOverride> BadgeTypeOverrides { get; set; } = new();

    // Badges enabled in this panel (e.g. ["4k","1080p","720p","sd"])
    public List<string> EnabledBadges { get; set; } = new();
}

/// <summary>
/// Configuration for a specific image type (Poster, Thumbnail).
/// </summary>
public class ImageTypeConfig
{
    public bool Enabled { get; set; } = true;
    public BadgePanelSettings ResolutionPanel { get; set; } = new();
    public BadgePanelSettings HdrPanel { get; set; } = new();
    public BadgePanelSettings CodecPanel { get; set; } = new();
    public BadgePanelSettings AudioPanel { get; set; } = new();
    public BadgePanelSettings LanguagePanel { get; set; } = new();

    // VOST settings (attached to Language panel)
    public bool ShowVostIndicator { get; set; } = true;
    public string? VostBgColor { get; set; }
    public string? VostTextColor { get; set; }
    public int VostBgOpacity { get; set; } = 255;
    public int VostCornerRadius { get; set; } = 1;
}

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        Enabled = true;

        PosterConfig = CreateDefaultPosterConfig();
        ThumbnailConfig = CreateDefaultThumbnailConfig();
        ThumbnailSameAsPoster = true;
        ThumbnailSizeReduction = 5;
        ExcludedLibraryIds = new List<string>();

        CustomBadgeTexts = new List<BadgeTextOverride>();
        CacheDurationHours = 24;
        JpegQuality = 90;
        OutputFormat = OutputImageFormat.Jpeg;
        WebPQuality = 90;
    }

    public bool Enabled { get; set; }

    public ImageTypeConfig PosterConfig { get; set; }
    public ImageTypeConfig ThumbnailConfig { get; set; }
    public bool ThumbnailSameAsPoster { get; set; }

    /// <summary>
    /// Size reduction (in percentage points) applied to badge sizes when ThumbnailSameAsPoster is enabled.
    /// For example, 10 means a 15% poster badge becomes 5% on thumbnails.
    /// </summary>
    public int ThumbnailSizeReduction { get; set; }

    /// <summary>
    /// Library IDs excluded from badge generation. Empty means all libraries are included.
    /// </summary>
    public List<string> ExcludedLibraryIds { get; set; }

    public List<BadgeTextOverride> CustomBadgeTexts { get; set; }
    public int CacheDurationHours { get; set; }
    public int JpegQuality { get; set; }
    public OutputImageFormat OutputFormat { get; set; }
    public int WebPQuality { get; set; }

    // Legacy properties kept for deserialization migration
    // These will be read during migration and then ignored
    public ImageTypeSettings? PosterSettings { get; set; }
    public ImageTypeSettings? ThumbnailSettings { get; set; }
    public ImageTypeSettings? BackdropSettings { get; set; }

    // Legacy global booleans for migration
    public bool? Show4K { get; set; }
    public bool? Show1080p { get; set; }
    public bool? Show720p { get; set; }
    public bool? ShowSD { get; set; }
    public bool? ShowHdr10 { get; set; }
    public bool? ShowHdr10Plus { get; set; }
    public bool? ShowDolbyVision { get; set; }
    public bool? ShowHlg { get; set; }
    public bool? ShowGenericHdr { get; set; }
    public bool? ShowH264 { get; set; }
    public bool? ShowHevc { get; set; }
    public bool? ShowAv1 { get; set; }
    public bool? ShowVp9 { get; set; }
    public bool? Show3D { get; set; }
    public bool? ShowDolbyAtmos { get; set; }
    public bool? ShowDtsX { get; set; }
    public bool? ShowTrueHD { get; set; }
    public bool? ShowDtsHdMa { get; set; }
    public bool? ShowChannelBadge { get; set; }
    public LanguageBadgeMode? LanguageBadgeMode { get; set; }
    public bool? ShowSubtitleIndicator { get; set; }

    /// <summary>
    /// Migrates legacy config format to the new per-panel format.
    /// Call this after deserialization when legacy properties are present.
    /// </summary>
    public void MigrateFromLegacy()
    {
        // Only migrate if legacy properties are present
        if (Show4K == null && PosterSettings == null)
        {
            return;
        }

        var resolutionBadges = new List<string>();
        if (Show4K == true) resolutionBadges.Add("4k");
        if (Show1080p == true) resolutionBadges.Add("1080p");
        if (Show720p == true) resolutionBadges.Add("720p");
        if (ShowSD == true) resolutionBadges.Add("sd");

        var hdrBadges = new List<string>();
        if (ShowHdr10 == true) hdrBadges.Add("hdr10");
        if (ShowHdr10Plus == true) hdrBadges.Add("hdr10plus");
        if (ShowDolbyVision == true) hdrBadges.Add("dv");
        if (ShowHlg == true) hdrBadges.Add("hlg");
        if (ShowGenericHdr == true) hdrBadges.Add("hdr");
        if (Show3D == true) hdrBadges.Add("3d");

        var codecBadges = new List<string>();
        if (ShowH264 == true) codecBadges.Add("h264");
        if (ShowHevc == true) codecBadges.Add("hevc");
        if (ShowAv1 == true) codecBadges.Add("av1");
        if (ShowVp9 == true) codecBadges.Add("vp9");

        var audioBadges = new List<string>();
        if (ShowDolbyAtmos == true) audioBadges.Add("atmos");
        if (ShowDtsX == true) audioBadges.Add("dtsx");
        if (ShowTrueHD == true) audioBadges.Add("truehd");
        if (ShowDtsHdMa == true) audioBadges.Add("dtshdma");
        if (ShowChannelBadge == true) { audioBadges.Add("7.1"); audioBadges.Add("5.1"); audioBadges.Add("stereo"); }

        var langMode = LanguageBadgeMode ?? Configuration.LanguageBadgeMode.All;

        void MigrateImageType(ImageTypeSettings? old, ImageTypeConfig target)
        {
            if (old == null) return;

            target.Enabled = old.Enabled;

            // Resolution panel
            target.ResolutionPanel.Position = old.BadgePosition;
            target.ResolutionPanel.Layout = old.BadgeLayout;
            target.ResolutionPanel.SizePercent = old.BadgeSizePercent;
            target.ResolutionPanel.MarginPercent = old.BadgeMarginPercent;
            target.ResolutionPanel.GapPercent = old.BadgeGapPercent;
            target.ResolutionPanel.Style = old.BadgeStyle;
            target.ResolutionPanel.TextBgColor = old.VideoBadgeBgColor ?? old.TextBadgeBgColor;
            target.ResolutionPanel.TextBgOpacity = old.TextBadgeBgOpacity;
            target.ResolutionPanel.TextColor = old.VideoBadgeTextColor ?? old.TextBadgeTextColor;
            target.ResolutionPanel.TextCornerRadius = old.TextBadgeCornerRadius;
            target.ResolutionPanel.EnabledBadges = new List<string>(resolutionBadges);
            target.ResolutionPanel.Order = 0;

            // HDR panel (same position/layout as video)
            target.HdrPanel.Position = old.BadgePosition;
            target.HdrPanel.Layout = old.BadgeLayout;
            target.HdrPanel.SizePercent = old.BadgeSizePercent;
            target.HdrPanel.MarginPercent = old.BadgeMarginPercent;
            target.HdrPanel.GapPercent = old.BadgeGapPercent;
            target.HdrPanel.Style = old.BadgeStyle;
            target.HdrPanel.TextBgColor = old.VideoBadgeBgColor ?? old.TextBadgeBgColor;
            target.HdrPanel.TextBgOpacity = old.TextBadgeBgOpacity;
            target.HdrPanel.TextColor = old.VideoBadgeTextColor ?? old.TextBadgeTextColor;
            target.HdrPanel.TextCornerRadius = old.TextBadgeCornerRadius;
            target.HdrPanel.EnabledBadges = new List<string>(hdrBadges);
            target.HdrPanel.Order = 1;

            // Codec panel
            target.CodecPanel.Position = old.BadgePosition;
            target.CodecPanel.Layout = old.BadgeLayout;
            target.CodecPanel.SizePercent = old.BadgeSizePercent;
            target.CodecPanel.MarginPercent = old.BadgeMarginPercent;
            target.CodecPanel.GapPercent = old.BadgeGapPercent;
            target.CodecPanel.Style = old.BadgeStyle;
            target.CodecPanel.TextBgColor = old.CodecBadgeBgColor ?? old.VideoBadgeBgColor ?? old.TextBadgeBgColor;
            target.CodecPanel.TextBgOpacity = old.CodecTextBadgeBgOpacity > 0 ? old.CodecTextBadgeBgOpacity : old.TextBadgeBgOpacity;
            target.CodecPanel.TextColor = old.CodecBadgeTextColor ?? old.VideoBadgeTextColor ?? old.TextBadgeTextColor;
            target.CodecPanel.TextCornerRadius = old.CodecTextBadgeCornerRadius >= 0 ? old.CodecTextBadgeCornerRadius : old.TextBadgeCornerRadius;
            target.CodecPanel.EnabledBadges = new List<string>(codecBadges);
            target.CodecPanel.Order = 2;

            // Audio panel
            var audioPos = old.AudioBadgePosition ?? old.BadgePosition;
            var audioLayout = old.AudioBadgeLayout ?? old.BadgeLayout;
            var audioStyle = old.AudioBadgeStyle ?? old.BadgeStyle;
            target.AudioPanel.Position = audioPos;
            target.AudioPanel.Layout = audioLayout;
            target.AudioPanel.SizePercent = old.AudioBadgeSizePercent > 0 ? old.AudioBadgeSizePercent : old.BadgeSizePercent;
            target.AudioPanel.MarginPercent = old.BadgeMarginPercent;
            target.AudioPanel.GapPercent = old.BadgeGapPercent;
            target.AudioPanel.Style = audioStyle;
            target.AudioPanel.TextBgColor = old.AudioBadgeBgColor ?? old.TextBadgeBgColor;
            target.AudioPanel.TextBgOpacity = old.AudioTextBadgeBgOpacity > 0 ? old.AudioTextBadgeBgOpacity : old.TextBadgeBgOpacity;
            target.AudioPanel.TextColor = old.AudioBadgeTextColor ?? old.TextBadgeTextColor;
            target.AudioPanel.TextCornerRadius = old.AudioTextBadgeCornerRadius >= 0 ? old.AudioTextBadgeCornerRadius : old.TextBadgeCornerRadius;
            target.AudioPanel.EnabledBadges = new List<string>(audioBadges);
            target.AudioPanel.Order = 3;

            // Language panel
            var langPos = old.LanguageBadgePosition ?? audioPos;
            var langLayout = old.LanguageBadgeLayout ?? audioLayout;
            var langStyle = old.LanguageBadgeStyle ?? audioStyle;
            target.LanguagePanel.Position = langPos;
            target.LanguagePanel.Layout = langLayout;
            target.LanguagePanel.SizePercent = old.LanguageBadgeSizePercent > 0 ? old.LanguageBadgeSizePercent : old.AudioBadgeSizePercent;
            target.LanguagePanel.MarginPercent = old.BadgeMarginPercent;
            target.LanguagePanel.GapPercent = old.BadgeGapPercent;
            target.LanguagePanel.Style = langStyle;
            target.LanguagePanel.TextBgColor = old.LanguageBadgeBgColor ?? old.TextBadgeBgColor;
            target.LanguagePanel.TextBgOpacity = old.LanguageTextBadgeBgOpacity > 0 ? old.LanguageTextBadgeBgOpacity : old.TextBadgeBgOpacity;
            target.LanguagePanel.TextColor = old.LanguageBadgeTextColor ?? old.TextBadgeTextColor;
            target.LanguagePanel.TextCornerRadius = old.LanguageTextBadgeCornerRadius >= 0 ? old.LanguageTextBadgeCornerRadius : old.TextBadgeCornerRadius;
            target.LanguagePanel.Enabled = langMode != Configuration.LanguageBadgeMode.None;
            target.LanguagePanel.ShowMode = langMode == Configuration.LanguageBadgeMode.DefaultOnly ? BadgeDisplayMode.Highest : BadgeDisplayMode.All;
            target.LanguagePanel.Order = 4;

            // VOST
            target.ShowVostIndicator = ShowSubtitleIndicator ?? true;
            target.VostBgColor = old.SubtitleBadgeBgColor;
            target.VostTextColor = old.SubtitleBadgeTextColor;
            target.VostBgOpacity = old.SubtitleTextBadgeBgOpacity;
            target.VostCornerRadius = old.SubtitleTextBadgeCornerRadius;
        }

        MigrateImageType(PosterSettings, PosterConfig);
        MigrateImageType(ThumbnailSettings, ThumbnailConfig);

        // Clear legacy properties after migration
        PosterSettings = null;
        ThumbnailSettings = null;
        BackdropSettings = null;
        Show4K = null;
        Show1080p = null;
        Show720p = null;
        ShowSD = null;
        ShowHdr10 = null;
        ShowHdr10Plus = null;
        ShowDolbyVision = null;
        ShowHlg = null;
        ShowGenericHdr = null;
        ShowH264 = null;
        ShowHevc = null;
        ShowAv1 = null;
        ShowVp9 = null;
        Show3D = null;
        ShowDolbyAtmos = null;
        ShowDtsX = null;
        ShowTrueHD = null;
        ShowDtsHdMa = null;
        ShowChannelBadge = null;
        LanguageBadgeMode = null;
        ShowSubtitleIndicator = null;
    }

    private static ImageTypeConfig CreateDefaultPosterConfig()
    {
        var config = new ImageTypeConfig { Enabled = true };

        config.ResolutionPanel = new BadgePanelSettings
        {
            Enabled = true, Order = 0, Position = BadgePosition.TopLeft,
            Layout = BadgeLayout.Vertical, SizePercent = 15, MarginPercent = 2f, GapPercent = 5f,
            EnabledBadges = new List<string> { "4k", "1080p", "720p", "sd" }
        };
        config.HdrPanel = new BadgePanelSettings
        {
            Enabled = true, Order = 1, Position = BadgePosition.TopLeft,
            Layout = BadgeLayout.Vertical, SizePercent = 15, MarginPercent = 2f, GapPercent = 10f,
            EnabledBadges = new List<string> { "hdr10", "hdr10plus", "dv", "hlg", "3d" }
        };
        config.CodecPanel = new BadgePanelSettings
        {
            Enabled = true, Order = 2, Position = BadgePosition.TopLeft,
            Layout = BadgeLayout.Vertical, SizePercent = 15, MarginPercent = 2f, GapPercent = 10f,
            EnabledBadges = new List<string> { "h264", "hevc", "av1", "vp9" }
        };
        config.AudioPanel = new BadgePanelSettings
        {
            Enabled = true, Order = 3, Position = BadgePosition.TopLeft,
            Layout = BadgeLayout.Vertical, SizePercent = 15, MarginPercent = 2f, GapPercent = 10f,
            EnabledBadges = new List<string> { "atmos", "dtsx", "truehd", "dtshdma", "7.1", "5.1", "stereo" }
        };
        config.LanguagePanel = new BadgePanelSettings
        {
            Enabled = true, Order = 4, Position = BadgePosition.TopLeft,
            Layout = BadgeLayout.Vertical, SizePercent = 15, MarginPercent = 2f, GapPercent = 10f,
            ShowMode = BadgeDisplayMode.All,
            Style = BadgeStyle.Image,
            EnabledBadges = new List<string>()
        };

        config.ShowVostIndicator = true;
        config.VostBgColor = "#000000";
        config.VostTextColor = "#ffffff";
        config.VostBgOpacity = 255;
        config.VostCornerRadius = 10;

        return config;
    }

    private static ImageTypeConfig CreateDefaultThumbnailConfig()
    {
        var config = new ImageTypeConfig { Enabled = true };

        config.ResolutionPanel = new BadgePanelSettings
        {
            Enabled = true, Order = 0, Position = BadgePosition.TopLeft,
            Layout = BadgeLayout.Vertical, SizePercent = 10, MarginPercent = 2.5f, GapPercent = 10f,
            EnabledBadges = new List<string> { "4k", "1080p", "720p", "sd" }
        };
        config.HdrPanel = new BadgePanelSettings
        {
            Enabled = true, Order = 1, Position = BadgePosition.TopLeft,
            Layout = BadgeLayout.Vertical, SizePercent = 10, MarginPercent = 2.5f, GapPercent = 10f,
            EnabledBadges = new List<string> { "hdr10", "hdr10plus", "dv", "hlg", "3d" }
        };
        config.CodecPanel = new BadgePanelSettings
        {
            Enabled = false, Order = 2, Position = BadgePosition.TopLeft,
            Layout = BadgeLayout.Vertical, SizePercent = 10, MarginPercent = 2.5f, GapPercent = 10f,
            EnabledBadges = new List<string> { "h264", "hevc", "av1", "vp9" }
        };
        config.AudioPanel = new BadgePanelSettings
        {
            Enabled = true, Order = 3, Position = BadgePosition.TopLeft,
            Layout = BadgeLayout.Vertical, SizePercent = 10, MarginPercent = 2.5f, GapPercent = 10f,
            EnabledBadges = new List<string> { "atmos", "dtsx", "truehd", "dtshdma", "7.1", "5.1", "stereo" }
        };
        config.LanguagePanel = new BadgePanelSettings
        {
            Enabled = true, Order = 4, Position = BadgePosition.TopLeft,
            Layout = BadgeLayout.Vertical, SizePercent = 10, MarginPercent = 2.5f, GapPercent = 10f,
            ShowMode = BadgeDisplayMode.All,
            Style = BadgeStyle.Image,
            EnabledBadges = new List<string>()
        };

        config.ShowVostIndicator = true;
        config.VostBgOpacity = 255;
        config.VostCornerRadius = 1;

        return config;
    }
}

/// <summary>
/// Represents a custom display text override for a badge.
/// </summary>
public class BadgeTextOverride
{
    public string Key { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Language badge display mode (legacy, used for migration only).
/// </summary>
public enum LanguageBadgeMode
{
    None,
    DefaultOnly,
    All
}

/// <summary>
/// Legacy settings class kept for deserialization/migration only.
/// </summary>
public class ImageTypeSettings
{
    public bool Enabled { get; set; }
    public int BadgeSizePercent { get; set; }
    public int AudioBadgeSizePercent { get; set; }
    public float BadgeMarginPercent { get; set; }
    public float BadgeGapPercent { get; set; }
    public BadgePosition BadgePosition { get; set; }
    public BadgeLayout BadgeLayout { get; set; }
    public BadgeStyle BadgeStyle { get; set; }
    public BadgePosition? AudioBadgePosition { get; set; }
    public BadgeLayout? AudioBadgeLayout { get; set; }
    public string TextBadgeBgColor { get; set; } = "#000000";
    public int TextBadgeBgOpacity { get; set; }
    public string TextBadgeTextColor { get; set; } = "#FFFFFF";
    public int TextBadgeCornerRadius { get; set; }
    public string? VideoBadgeBgColor { get; set; }
    public string? VideoBadgeTextColor { get; set; }
    public string? AudioBadgeBgColor { get; set; }
    public string? AudioBadgeTextColor { get; set; }
    public string? LanguageBadgeBgColor { get; set; }
    public string? LanguageBadgeTextColor { get; set; }
    public BadgeStyle? AudioBadgeStyle { get; set; }
    public BadgeStyle? LanguageBadgeStyle { get; set; }
    public BadgePosition? LanguageBadgePosition { get; set; }
    public BadgeLayout? LanguageBadgeLayout { get; set; }
    public int LanguageBadgeSizePercent { get; set; }
    public int AudioTextBadgeBgOpacity { get; set; }
    public int AudioTextBadgeCornerRadius { get; set; } = -1;
    public int LanguageTextBadgeBgOpacity { get; set; }
    public int LanguageTextBadgeCornerRadius { get; set; } = -1;
    public string? CodecBadgeBgColor { get; set; }
    public string? CodecBadgeTextColor { get; set; }
    public int CodecTextBadgeBgOpacity { get; set; }
    public int CodecTextBadgeCornerRadius { get; set; } = -1;
    public string? SubtitleBadgeBgColor { get; set; }
    public string? SubtitleBadgeTextColor { get; set; }
    public int SubtitleTextBadgeBgOpacity { get; set; }
    public int SubtitleTextBadgeCornerRadius { get; set; } = -1;
}
