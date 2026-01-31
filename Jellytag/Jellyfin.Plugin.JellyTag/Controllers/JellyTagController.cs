using System.Text.RegularExpressions;
using Jellyfin.Plugin.JellyTag.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyTag.Controllers;

/// <summary>
/// Controller for JellyTag plugin admin and debug endpoints.
/// </summary>
[ApiController]
[Route("JellyTag")]
public partial class JellyTagController : ControllerBase
{
    private readonly IImageCacheService _cacheService;
    private readonly IImageOverlayService _overlayService;
    private readonly IQualityDetectionService _qualityService;

    [GeneratedRegex(@"^[a-zA-Z0-9._-]+$")]
    private static partial Regex SafeBadgeKeyRegex();

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyTagController"/> class.
    /// </summary>
    public JellyTagController(IImageCacheService cacheService, IImageOverlayService overlayService, IQualityDetectionService qualityService)
    {
        _cacheService = cacheService;
        _overlayService = overlayService;
        _qualityService = qualityService;
    }

    /// <summary>
    /// Clears the image cache.
    /// </summary>
    [HttpPost("ClearCache")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult ClearCache()
    {
        _cacheService.ClearCache();
        _qualityService.ClearBadgeCache();
        return NoContent();
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    [HttpGet("CacheStats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetCacheStats()
    {
        var stats = _cacheService.GetCacheStats();
        return Ok(new
        {
            FileCount = stats.FileCount,
            TotalSizeMB = Math.Round(stats.TotalSizeBytes / (1024.0 * 1024.0), 2),
            OldestEntry = stats.OldestEntry,
            NewestEntry = stats.NewestEntry
        });
    }

    /// <summary>
    /// Gets the plugin status.
    /// </summary>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var config = Plugin.Instance?.Configuration;
        return Ok(new
        {
            Enabled = config?.Enabled ?? false,
            Show4K = config?.Show4K ?? false,
            Show1080p = config?.Show1080p ?? false,
            Show720p = config?.Show720p ?? false,
            ShowSD = config?.ShowSD ?? false,
            ShowHdr10 = config?.ShowHdr10 ?? false,
            ShowHdr10Plus = config?.ShowHdr10Plus ?? false,
            ShowDolbyVision = config?.ShowDolbyVision ?? false,
            ShowHlg = config?.ShowHlg ?? false,
            ShowDolbyAtmos = config?.ShowDolbyAtmos ?? false,
            ShowDtsX = config?.ShowDtsX ?? false,
            ShowTrueHD = config?.ShowTrueHD ?? false,
            ShowDtsHdMa = config?.ShowDtsHdMa ?? false,
            ShowChannelBadge = config?.ShowChannelBadge ?? false
        });
    }

    /// <summary>
    /// Debug endpoint to list embedded resources.
    /// </summary>
    [HttpGet("Debug/Resources")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetResources()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames();
        return Ok(new
        {
            AssemblyName = assembly.FullName,
            Resources = resources
        });
    }

    /// <summary>
    /// Debug endpoint to get a raw badge image.
    /// </summary>
    [HttpGet("Debug/Badge/{quality}")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetBadge(string quality)
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        var fileName = quality.ToLower() switch
        {
            "4k" => "badge-4k.png",
            "1080p" => "badge-1080p.png",
            "720p" => "badge-720p.png",
            "sd" => "badge-sd.png",
            "hdr10" => "badge-hdr10.png",
            "hdr10plus" => "badge-hdr10plus.png",
            "dv" => "badge-dv.png",
            "hlg" => "badge-hlg.png",
            "atmos" => "badge-atmos.png",
            "dtsx" => "badge-dtsx.png",
            "truehd" => "badge-truehd.png",
            "dtshdma" => "badge-dtshdma.png",
            "5.1" => "badge-5_1.png",
            "7.1" => "badge-7_1.png",
            "stereo" => "badge-stereo.png",
            _ => null
        };

        if (fileName == null)
            return NotFound("Invalid quality");

        var resourceName = resourceNames.FirstOrDefault(r => r.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
            return NotFound($"Resource not found: {fileName}");

        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return NotFound("Stream is null");

        return File(stream, "image/png");
    }

    /// <summary>
    /// Uploads a custom badge PNG to override the default badge for a given key.
    /// </summary>
    [HttpPost("CustomBadge/{badgeKey}")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadCustomBadge(string badgeKey, IFormFile file)
    {
        if (!SafeBadgeKeyRegex().IsMatch(badgeKey))
        {
            return BadRequest("Invalid badge key");
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded");
        }

        if (!file.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only PNG files are accepted");
        }

        var dataFolder = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrEmpty(dataFolder))
        {
            return BadRequest("Plugin data folder not available");
        }

        var customDir = Path.Combine(dataFolder, "custom-badges");
        Directory.CreateDirectory(customDir);

        var fileName = $"badge-{badgeKey}.png";
        var filePath = Path.Combine(customDir, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream).ConfigureAwait(false);
        }

        // Reload badges and clear cache
        _overlayService.ReloadBadges();
        _cacheService.ClearCache();

        return NoContent();
    }

    /// <summary>
    /// Deletes a custom badge override, reverting to the default embedded badge.
    /// </summary>
    [HttpDelete("CustomBadge/{badgeKey}")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteCustomBadge(string badgeKey)
    {
        if (!SafeBadgeKeyRegex().IsMatch(badgeKey))
        {
            return BadRequest("Invalid badge key");
        }

        var dataFolder = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrEmpty(dataFolder))
        {
            return NotFound();
        }

        var fileName = $"badge-{badgeKey}.png";
        var filePath = Path.Combine(dataFolder, "custom-badges", fileName);

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound("Custom badge not found");
        }

        System.IO.File.Delete(filePath);

        _overlayService.ReloadBadges();
        _cacheService.ClearCache();

        return NoContent();
    }

    /// <summary>
    /// Lists all custom badge overrides.
    /// </summary>
    [HttpGet("CustomBadges")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetCustomBadges()
    {
        var dataFolder = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrEmpty(dataFolder))
        {
            return Ok(Array.Empty<string>());
        }

        var customDir = Path.Combine(dataFolder, "custom-badges");
        if (!Directory.Exists(customDir))
        {
            return Ok(Array.Empty<string>());
        }

        var files = Directory.GetFiles(customDir, "badge-*.png")
            .Select(f => Path.GetFileNameWithoutExtension(f).Replace("badge-", string.Empty))
            .ToArray();

        return Ok(files);
    }
}
