using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellyTag.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyTag;

/// <summary>
/// JellyTag plugin - Adds quality badges to media posters.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Gets the plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

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
    /// Gets the cache folder path for storing processed images.
    /// </summary>
    public string CacheFolderPath => Path.Combine(DataFolderPath, "cache");

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
}
