using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyTag.Configuration;

/// <summary>
/// Badge position options.
/// </summary>
public enum BadgePosition
{
    /// <summary>
    /// Top left corner.
    /// </summary>
    TopLeft,

    /// <summary>
    /// Top right corner.
    /// </summary>
    TopRight,

    /// <summary>
    /// Bottom left corner.
    /// </summary>
    BottomLeft,

    /// <summary>
    /// Bottom right corner.
    /// </summary>
    BottomRight
}

/// <summary>
/// Badge rendering style.
/// </summary>
public enum BadgeStyle
{
    /// <summary>
    /// Render badges as PNG images.
    /// </summary>
    Image,

    /// <summary>
    /// Render badges as text with a semi-transparent background.
    /// </summary>
    Text
}

/// <summary>
/// Language badge display mode.
/// </summary>
public enum LanguageBadgeMode
{
    /// <summary>
    /// Do not show language badges.
    /// </summary>
    None,

    /// <summary>
    /// Show badge only for the default audio track language.
    /// </summary>
    DefaultOnly,

    /// <summary>
    /// Show badges for all distinct audio track languages.
    /// </summary>
    All
}

/// <summary>
/// Output image format.
/// </summary>
public enum OutputImageFormat
{
    /// <summary>
    /// JPEG format.
    /// </summary>
    Jpeg,

    /// <summary>
    /// WebP format.
    /// </summary>
    WebP
}

/// <summary>
/// Badge layout direction for stacking multiple badges.
/// </summary>
public enum BadgeLayout
{
    /// <summary>
    /// Badges are arranged horizontally.
    /// </summary>
    Horizontal,

    /// <summary>
    /// Badges are arranged vertically.
    /// </summary>
    Vertical
}

/// <summary>
/// Settings for a specific image type (poster, thumbnail, backdrop).
/// </summary>
public class ImageTypeSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether badges are enabled for this image type.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the video badge size (resolution/HDR) as a percentage of the image width.
    /// </summary>
    public int BadgeSizePercent { get; set; }

    /// <summary>
    /// Gets or sets the audio badge size as a percentage of the image width.
    /// </summary>
    public int AudioBadgeSizePercent { get; set; }

    /// <summary>
    /// Gets or sets the badge margin from image edge as a percentage of image width.
    /// </summary>
    public float BadgeMarginPercent { get; set; }

    /// <summary>
    /// Gets or sets the gap between stacked badges as a percentage of badge height.
    /// </summary>
    public float BadgeGapPercent { get; set; }

    /// <summary>
    /// Gets or sets the badge position.
    /// </summary>
    public BadgePosition BadgePosition { get; set; }

    /// <summary>
    /// Gets or sets the badge layout direction (horizontal or vertical stacking).
    /// </summary>
    public BadgeLayout BadgeLayout { get; set; }

    /// <summary>
    /// Gets or sets the badge rendering style (image or text).
    /// </summary>
    public BadgeStyle BadgeStyle { get; set; }

    /// <summary>
    /// Gets or sets the audio badge position. When null, uses the same position as video badges.
    /// </summary>
    public BadgePosition? AudioBadgePosition { get; set; }

    /// <summary>
    /// Gets or sets the audio badge layout direction. When null, uses the same layout as video badges.
    /// </summary>
    public BadgeLayout? AudioBadgeLayout { get; set; }

    /// <summary>
    /// Gets or sets the text badge background color as a hex string (e.g. "#000000").
    /// </summary>
    public string TextBadgeBgColor { get; set; } = "#000000";

    /// <summary>
    /// Gets or sets the text badge background opacity (0-255).
    /// </summary>
    public int TextBadgeBgOpacity { get; set; }

    /// <summary>
    /// Gets or sets the text badge text color as a hex string (e.g. "#FFFFFF").
    /// </summary>
    public string TextBadgeTextColor { get; set; } = "#FFFFFF";

    /// <summary>
    /// Gets or sets the text badge corner radius as a percentage of badge height (0-50).
    /// </summary>
    public int TextBadgeCornerRadius { get; set; }

    /// <summary>
    /// Gets or sets the video badge background color override. When null, uses global TextBadgeBgColor.
    /// </summary>
    public string? VideoBadgeBgColor { get; set; }

    /// <summary>
    /// Gets or sets the video badge text color override. When null, uses global TextBadgeTextColor.
    /// </summary>
    public string? VideoBadgeTextColor { get; set; }

    /// <summary>
    /// Gets or sets the audio badge background color override. When null, uses global TextBadgeBgColor.
    /// </summary>
    public string? AudioBadgeBgColor { get; set; }

    /// <summary>
    /// Gets or sets the audio badge text color override. When null, uses global TextBadgeTextColor.
    /// </summary>
    public string? AudioBadgeTextColor { get; set; }

    /// <summary>
    /// Gets or sets the language badge background color override. When null, uses global TextBadgeBgColor.
    /// </summary>
    public string? LanguageBadgeBgColor { get; set; }

    /// <summary>
    /// Gets or sets the language badge text color override. When null, uses global TextBadgeTextColor.
    /// </summary>
    public string? LanguageBadgeTextColor { get; set; }

    /// <summary>
    /// Gets or sets the audio badge rendering style override. When null, inherits from BadgeStyle (video).
    /// </summary>
    public BadgeStyle? AudioBadgeStyle { get; set; }

    /// <summary>
    /// Gets or sets the language badge rendering style override. When null, inherits from resolved audio style.
    /// </summary>
    public BadgeStyle? LanguageBadgeStyle { get; set; }

    /// <summary>
    /// Gets or sets the language badge position. When null, uses the same position as audio badges.
    /// </summary>
    public BadgePosition? LanguageBadgePosition { get; set; }

    /// <summary>
    /// Gets or sets the language badge layout direction. When null, uses the same layout as audio badges.
    /// </summary>
    public BadgeLayout? LanguageBadgeLayout { get; set; }

    /// <summary>
    /// Gets or sets the language badge size as a percentage of the image width.
    /// </summary>
    public int LanguageBadgeSizePercent { get; set; }

    /// <summary>
    /// Gets or sets the audio text badge background opacity override (0 = inherit from global).
    /// </summary>
    public int AudioTextBadgeBgOpacity { get; set; }

    /// <summary>
    /// Gets or sets the audio text badge corner radius override (-1 = inherit from global).
    /// </summary>
    public int AudioTextBadgeCornerRadius { get; set; } = -1;

    /// <summary>
    /// Gets or sets the language text badge background opacity override (0 = inherit from global).
    /// </summary>
    public int LanguageTextBadgeBgOpacity { get; set; }

    /// <summary>
    /// Gets or sets the language text badge corner radius override (-1 = inherit from global).
    /// </summary>
    public int LanguageTextBadgeCornerRadius { get; set; } = -1;

    /// <summary>
    /// Gets or sets the video codec badge background color override. When null, uses video badge colors.
    /// </summary>
    public string? CodecBadgeBgColor { get; set; }

    /// <summary>
    /// Gets or sets the video codec badge text color override. When null, uses video badge colors.
    /// </summary>
    public string? CodecBadgeTextColor { get; set; }

    /// <summary>
    /// Gets or sets the video codec text badge background opacity override (0 = inherit from global).
    /// </summary>
    public int CodecTextBadgeBgOpacity { get; set; }

    /// <summary>
    /// Gets or sets the video codec text badge corner radius override (-1 = inherit from global).
    /// </summary>
    public int CodecTextBadgeCornerRadius { get; set; } = -1;

    /// <summary>
    /// Gets or sets the subtitle badge background color override. When null, uses global TextBadgeBgColor.
    /// </summary>
    public string? SubtitleBadgeBgColor { get; set; }

    /// <summary>
    /// Gets or sets the subtitle badge text color override. When null, uses global TextBadgeTextColor.
    /// </summary>
    public string? SubtitleBadgeTextColor { get; set; }

    /// <summary>
    /// Gets or sets the subtitle text badge background opacity override (0 = inherit from global).
    /// </summary>
    public int SubtitleTextBadgeBgOpacity { get; set; }

    /// <summary>
    /// Gets or sets the subtitle text badge corner radius override (-1 = inherit from global).
    /// </summary>
    public int SubtitleTextBadgeCornerRadius { get; set; } = -1;
}

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        Enabled = true;

        // Resolution badges
        Show4K = true;
        Show1080p = true;
        Show720p = true;
        ShowSD = true;

        // HDR badges
        ShowHdr10 = true;
        ShowHdr10Plus = true;
        ShowDolbyVision = true;
        ShowHlg = true;

        // HDR generic
        ShowGenericHdr = false;

        // Video codec badges
        ShowH264 = false;
        ShowHevc = false;
        ShowAv1 = false;
        ShowVp9 = false;

        // 3D
        Show3D = true;

        // Audio badges
        ShowDolbyAtmos = true;
        ShowDtsX = true;
        ShowTrueHD = true;
        ShowDtsHdMa = true;
        ShowChannelBadge = true;

        // Language badges
        LanguageBadgeMode = LanguageBadgeMode.All;
        ShowSubtitleIndicator = true;

        PosterSettings = new ImageTypeSettings
        {
            Enabled = true,
            BadgeSizePercent = 15,
            AudioBadgeSizePercent = 15,
            LanguageBadgeSizePercent = 15,
            BadgeMarginPercent = 2.5f,
            BadgeGapPercent = 10.0f,
            BadgePosition = BadgePosition.TopLeft,
            BadgeLayout = BadgeLayout.Vertical,
            BadgeStyle = BadgeStyle.Image,
            LanguageBadgeStyle = BadgeStyle.Image,
            LanguageBadgePosition = BadgePosition.TopLeft,
            LanguageBadgeLayout = BadgeLayout.Vertical,
            TextBadgeBgColor = "#000000",
            TextBadgeBgOpacity = 180,
            TextBadgeTextColor = "#FFFFFF",
            TextBadgeCornerRadius = 25,
            AudioTextBadgeBgOpacity = 0,
            AudioTextBadgeCornerRadius = -1,
            LanguageTextBadgeBgOpacity = 0,
            LanguageTextBadgeCornerRadius = -1,
            SubtitleTextBadgeBgOpacity = 255,
            SubtitleTextBadgeCornerRadius = 1
        };

        ThumbnailSettings = new ImageTypeSettings
        {
            Enabled = true,
            BadgeSizePercent = 10,
            AudioBadgeSizePercent = 10,
            LanguageBadgeSizePercent = 10,
            BadgeMarginPercent = 2.5f,
            BadgeGapPercent = 10.0f,
            BadgePosition = BadgePosition.TopLeft,
            BadgeLayout = BadgeLayout.Vertical,
            BadgeStyle = BadgeStyle.Image,
            TextBadgeBgColor = "#000000",
            TextBadgeBgOpacity = 180,
            TextBadgeTextColor = "#FFFFFF",
            TextBadgeCornerRadius = 25,
            AudioTextBadgeBgOpacity = 0,
            AudioTextBadgeCornerRadius = -1,
            LanguageTextBadgeBgOpacity = 0,
            LanguageTextBadgeCornerRadius = -1,
            SubtitleTextBadgeBgOpacity = 255,
            SubtitleTextBadgeCornerRadius = 1
        };

        BackdropSettings = new ImageTypeSettings
        {
            Enabled = false,
            BadgeSizePercent = 10,
            AudioBadgeSizePercent = 10,
            LanguageBadgeSizePercent = 10,
            BadgeMarginPercent = 2.0f,
            BadgeGapPercent = 3.0f,
            BadgePosition = BadgePosition.TopLeft,
            BadgeLayout = BadgeLayout.Vertical,
            BadgeStyle = BadgeStyle.Image,
            TextBadgeBgColor = "#000000",
            TextBadgeBgOpacity = 180,
            TextBadgeTextColor = "#FFFFFF",
            TextBadgeCornerRadius = 25,
            AudioTextBadgeBgOpacity = 0,
            AudioTextBadgeCornerRadius = -1,
            LanguageTextBadgeBgOpacity = 0,
            LanguageTextBadgeCornerRadius = -1,
            SubtitleTextBadgeBgOpacity = 0,
            SubtitleTextBadgeCornerRadius = -1
        };

        CustomBadgeTexts = new List<BadgeTextOverride>();

        CacheDurationHours = 24;
        JpegQuality = 90;
        OutputFormat = OutputImageFormat.WebP;
        WebPQuality = 100;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    // Resolution badges

    /// <summary>
    /// Gets or sets a value indicating whether to show 4K badges.
    /// </summary>
    public bool Show4K { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show 1080p badges.
    /// </summary>
    public bool Show1080p { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show 720p badges.
    /// </summary>
    public bool Show720p { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show SD badges.
    /// </summary>
    public bool ShowSD { get; set; }

    // HDR badges

    /// <summary>
    /// Gets or sets a value indicating whether to show HDR10 badges.
    /// </summary>
    public bool ShowHdr10 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show HDR10+ badges.
    /// </summary>
    public bool ShowHdr10Plus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show Dolby Vision badges.
    /// </summary>
    public bool ShowDolbyVision { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show HLG badges.
    /// </summary>
    public bool ShowHlg { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to use a generic HDR badge instead of specific HDR format badges.
    /// </summary>
    public bool ShowGenericHdr { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show H.264/AVC codec badges.
    /// </summary>
    public bool ShowH264 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show HEVC/H.265 codec badges.
    /// </summary>
    public bool ShowHevc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show AV1 codec badges.
    /// </summary>
    public bool ShowAv1 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show VP9 codec badges.
    /// </summary>
    public bool ShowVp9 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show 3D badges.
    /// </summary>
    public bool Show3D { get; set; }

    // Audio badges

    /// <summary>
    /// Gets or sets a value indicating whether to show Dolby Atmos badges.
    /// </summary>
    public bool ShowDolbyAtmos { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show DTS:X badges.
    /// </summary>
    public bool ShowDtsX { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show TrueHD badges.
    /// </summary>
    public bool ShowTrueHD { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show DTS-HD MA badges.
    /// </summary>
    public bool ShowDtsHdMa { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show channel layout badges (7.1/5.1/Stereo).
    /// </summary>
    public bool ShowChannelBadge { get; set; }

    /// <summary>
    /// Gets or sets the language badge display mode.
    /// </summary>
    public LanguageBadgeMode LanguageBadgeMode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to show VOSTFR indicator when default language has no audio track but subtitles exist.
    /// </summary>
    public bool ShowSubtitleIndicator { get; set; }

    /// <summary>
    /// Gets or sets the poster (Primary) image settings.
    /// </summary>
    public ImageTypeSettings PosterSettings { get; set; }

    /// <summary>
    /// Gets or sets the thumbnail (Thumb) image settings.
    /// </summary>
    public ImageTypeSettings ThumbnailSettings { get; set; }

    /// <summary>
    /// Gets or sets the backdrop image settings.
    /// </summary>
    public ImageTypeSettings BackdropSettings { get; set; }

    /// <summary>
    /// Gets or sets custom display text overrides per badge key.
    /// </summary>
    public List<BadgeTextOverride> CustomBadgeTexts { get; set; }

    /// <summary>
    /// Gets or sets the cache duration in hours.
    /// </summary>
    public int CacheDurationHours { get; set; }

    /// <summary>
    /// Gets or sets the JPEG quality for output images (1-100).
    /// </summary>
    public int JpegQuality { get; set; }

    /// <summary>
    /// Gets or sets the output image format.
    /// </summary>
    public OutputImageFormat OutputFormat { get; set; }

    /// <summary>
    /// Gets or sets the WebP quality for output images (1-100).
    /// </summary>
    public int WebPQuality { get; set; }
}

/// <summary>
/// Represents a custom display text override for a badge.
/// </summary>
public class BadgeTextOverride
{
    /// <summary>
    /// Gets or sets the badge key (e.g. "4k", "atmos").
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the custom display text.
    /// </summary>
    public string Text { get; set; } = string.Empty;
}
