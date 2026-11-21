# Jellyfin plugins by Atilili

Plugin repository (catalog) for Jellyfin. It only holds the `manifest.json` that
Jellyfin reads; each plugin lives in its own repository.

| Plugin    | Description                                                  | Repository                                              |
| --------- | ------------------------------------------------------------ | ------------------------------------------------------- |
| WatchSync | keeps watch history in sync between copies of the same media | [Atilil/watchsync](https://github.com/Atilil/watchsync) |
| JellyTag  | quality badges on posters and thumbnails                     | [Atilil/jellytag](https://github.com/Atilil/jellytag)   |

## Installation

In the Jellyfin dashboard, add this plugin repository, then install the plugins
from the catalog:

```text
https://raw.githubusercontent.com/Atilil/jellyfin-plugins/main/manifest.json
```

Issues and feature requests go to the repository of each plugin.

## Maintenance

`scripts/update-manifest.py OWNER/REPO TAG` adds a published release to the
manifest. The checksum and metadata come from the release zip itself.
