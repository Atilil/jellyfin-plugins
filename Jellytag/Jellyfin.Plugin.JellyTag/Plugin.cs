using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyTag.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyTag;

/// <summary>
/// JellyTag plugin - Adds quality badges to media posters.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ILogger<Plugin> _logger;

    /// <summary>
    /// Gets the plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{Plugin}"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        _logger = logger;

        CacheFolderPath = Path.Combine(applicationPaths.CachePath, "jellytag");
        BadgeFolderPath = Path.Combine(applicationPaths.DataPath, "jellytag");

        MoveOutOfPluginsFolder(applicationPaths.PluginsPath);

        // Run legacy migration once at startup
        Configuration.MigrateFromLegacy();
    }

    /// <inheritdoc />
    public override string Name => "JellyTag";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("f4a2e8c1-9b3d-4f7a-b6c5-2d8e1a3f9b04");

    /// <inheritdoc />
    public override string Description => "Adds quality badges (4K, 1080p, etc.) to media posters and thumbnails.";

    /// <summary>
    /// Gets the cache folder path for storing processed images, under Jellyfin's cache directory.
    /// </summary>
    public string CacheFolderPath { get; }

    /// <summary>
    /// Gets the folder holding persistent plugin data (custom badges), under Jellyfin's data directory.
    /// </summary>
    public string BadgeFolderPath { get; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
                EnableInMainMenu = true,
                MenuSection = "Extensions",
                MenuIcon = "style"
            }
        };
    }

    /// <summary>
    /// Moves plugin data out of <c>&lt;config&gt;/plugins</c> and removes the folder left there by
    /// earlier versions.
    /// </summary>
    /// <remarks>
    /// Jellyfin points <see cref="BasePlugin.DataFolderPath"/> at
    /// <c>&lt;config&gt;/plugins/Jellyfin.Plugin.JellyTag</c>, i.e. a sibling of
    /// <c>&lt;config&gt;/plugins/configurations</c>. PluginManager.DiscoverPlugins() treats every
    /// folder directly under <c>&lt;config&gt;/plugins</c> as a plugin candidate, and derives the
    /// name of a folder that has no meta.json by cutting the <em>full path</em> at its last
    /// underscore. On a server whose data path contains an underscore - QNAP's
    /// <c>/share/CACHEDEV1_DATA/...</c>, or any bind mount such as <c>/volume1/media_server/...</c> -
    /// every underscore-free folder there collapses to the same generated name, and the duplicate is
    /// deleted with <c>Directory.Delete(path, recursive: true)</c>. That duplicate is sometimes
    /// <c>configurations</c>, which wipes every installed plugin's settings. Keeping nothing of ours
    /// under <c>&lt;config&gt;/plugins</c> removes the collision.
    /// </remarks>
    /// <param name="pluginsPath">Jellyfin's plugins directory.</param>
    private void MoveOutOfPluginsFolder(string pluginsPath)
    {
        try
        {
            var legacyPath = DataFolderPath;
            if (string.IsNullOrEmpty(legacyPath) || !Directory.Exists(legacyPath))
            {
                return;
            }

            // Only ever touch the folder Jellyfin generated for us inside the plugins directory,
            // never the directory the plugin itself was installed into.
            var installDir = Path.GetDirectoryName(AssemblyFilePath);
            if (!IsInside(legacyPath, pluginsPath)
                || (!string.IsNullOrEmpty(installDir) && SamePath(legacyPath, installDir)))
            {
                return;
            }

            var legacyBadges = Path.Combine(legacyPath, "custom-badges");
            if (Directory.Exists(legacyBadges))
            {
                MoveCustomBadges(legacyBadges, Path.Combine(BadgeFolderPath, "custom-badges"));
            }

            Directory.Delete(legacyPath, true);
            _logger.LogInformation(
                "Removed legacy JellyTag folder {Path} from the plugins directory; cache is now {Cache} and data {Data}",
                legacyPath,
                CacheFolderPath,
                BadgeFolderPath);
        }
        catch (Exception ex)
        {
            // Never let migration stop the plugin from loading.
            _logger.LogWarning(ex, "Failed to move JellyTag data out of the plugins folder");
        }
    }

    private static void MoveCustomBadges(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(file));
            if (File.Exists(destination))
            {
                continue;
            }

            // Directory.Move/File.Move fail across volumes, which is the normal case here
            // (cache and data can live on a different mount than the config directory).
            File.Copy(file, destination);
        }
    }

    private static bool IsInside(string path, string parent)
    {
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedParent, StringComparison.Ordinal);
    }

    private static bool SamePath(string a, string b)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.Ordinal);
}
