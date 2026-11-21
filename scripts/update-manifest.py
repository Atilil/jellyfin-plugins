#!/usr/bin/env python3
"""Add a published plugin release to manifest.json.

Usage: scripts/update-manifest.py OWNER/REPO TAG

Downloads the release zip, computes its MD5 (the checksum Jellyfin verifies),
reads meta.json inside the zip and adds the version at the top of the matching
plugin (matched by GUID). Older versions are kept, so servers on older Jellyfin
releases keep seeing the versions built for them. Running it twice is a no-op.
"""

import hashlib
import io
import json
import os
import re
import sys
import urllib.request
import zipfile
from pathlib import Path

MANIFEST = Path(__file__).resolve().parent.parent / "manifest.json"


def fetch(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"Accept": "application/vnd.github+json"})
    token = os.environ.get("GITHUB_TOKEN")
    if token and url.startswith("https://api.github.com/"):
        request.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def unwrap(text: str) -> str:
    """Join hard-wrapped lines, keeping blank-line paragraph breaks."""
    return re.sub(r"(?<!\n)\n(?!\n)", " ", text.strip())


def main(repo: str, tag: str) -> None:
    release = json.loads(fetch(f"https://api.github.com/repos/{repo}/releases/tags/{tag}"))
    if release.get("draft"):
        sys.exit(f"{repo} {tag} is a draft: publish it first")

    zips = [a for a in release["assets"] if a["name"].endswith(".zip")]
    if len(zips) != 1:
        sys.exit(f"expected exactly one .zip asset in {repo} {tag}, found {len(zips)}")
    asset = zips[0]

    data = fetch(asset["browser_download_url"])
    checksum = hashlib.md5(data).hexdigest()
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        meta = json.loads(archive.read("meta.json"))

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    plugin = next((p for p in manifest if p["guid"] == meta["guid"]), None)
    if plugin is None:
        sys.exit(f"no plugin with guid {meta['guid']} in {MANIFEST.name}")

    if any(v["version"] == meta["version"] for v in plugin["versions"]):
        print(f"{plugin['name']} {meta['version']} already in {MANIFEST.name}, nothing to do")
        return

    # The catalog shows the plugin-level texts: keep them in line with the latest release.
    plugin["overview"] = meta["overview"]
    plugin["description"] = unwrap(meta["description"])
    plugin["versions"].insert(0, {
        "version": meta["version"],
        "changelog": meta["changelog"].strip(),
        "targetAbi": meta["targetAbi"],
        "sourceUrl": asset["browser_download_url"],
        "checksum": checksum,
        "timestamp": meta["timestamp"],
    })

    MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"added {plugin['name']} {meta['version']} (targetAbi {meta['targetAbi']}, md5 {checksum})")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
