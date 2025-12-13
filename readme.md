# LibTools4DJs CLI

.NET CLI tools for DJs to streamline library management across [**Rekordbox**](https://rekordbox.com/) and [**Mixed In Key**](https://mixedinkey.com/), reduce friction between apps, and automate reliable sync workflows.

## Table of Contents

1. [Features & Commands Summary](#features--commands-summary)
1. [Notes](#notes)
1. [Build status](#build-status)
1. [Prerequisites](#prerequisites)
1. [Installation](#installation)
1. [Quick Start](#quick-start)
	- [`delete-tracks`](#delete-tracks)
	- [`sync-mik-tags-to-rekordbox`](#sync-mik-tags-to-rekordbox)
	- [`sync-rekordbox-library-to-mik`](#sync-rekordbox-library-to-mik)
	- [`sync-mik-folder-to-rekordbox`](#sync-mik-folder-to-rekordbox)
1. [Logging flags](#logging-flags)
1. [Motivation & Problems Addressed](#motivation--problems-addressed)
1. [Default Energy → Colour Mapping](#default-energy--colour-mapping)
1. [Troubleshooting](#troubleshooting)
1. [Verify Downloads](#verify-downloads)

## Features & Commands Summary

| Command | Purpose | Key Options | Dry-Run? | Output Artifacts |
|---------|---------|------------|---------|------------------|
| `delete-tracks` | Safely remove files & XML entries defined by the `LIBRARY MANAGEMENT/Delete` playlist. | `--xml <path>`, `--what-if` | Yes (`--what-if`) | Console summary; backup XML. |
| `sync-mik-tags-to-rekordbox` | Update Key (M4A only) & Colour from MIK Energy; create analysis playlists. | `--xml <path>`, `--mik-db <path>`, `--mik-version <ver>`, `--what-if` | Yes | `MIK Key Analysis`, `MIK Energy Level Analysis`; backup XML. |
| `sync-rekordbox-library-to-mik` | Mirror Rekordbox folder & playlist tree into MIK DB (additive). | `--xml <path>`, `--mik-db <path>`, `--mik-version <ver>`, `--what-if` | Yes | New collections in MIK DB; console hierarchy summary. |
| `sync-rekordbox-library-to-mik` (with `--reset-mik-library`) | Same as above, but first wipes MIK collections/memberships, preserving system rows. Use when you want to fully recreate the structure from Rekordbox. | `--reset-mik-library` | N/A | Clean slate in MIK DB before sync. |
| `sync-mik-folder-to-rekordbox` | Mirror one MIK folder subtree into Rekordbox under `LibTools4DJs_SyncFromMIK`. | `--xml <path>`, `--mik-folder <name>`, `--mik-db <path>`, `--mik-version <ver>`, `--what-if` | Yes | New folders/playlists; ASCII tree summary; backup XML. |
| Global logging flags | Control verbosity and persistence of logs. | `--debug`, `--save-logs` | N/A | Verbose console output (debug) and log file in `LibTools4DJs_Logs/`. |

## Notes
- Use the optional `--what-if` parameter first to simulate changes (no file system deletes, no DB writes, no XML modifications).
- For modifying commands, the tool now creates a timestamped backup copy of the original Rekordbox XML under a `LibTools4DJs_Backups` folder and then saves changes **in place** to the provided XML path (instead of producing a separate new file).
- Recommendation: periodically archive the `LibTools4DJs_Backups` folder if it grows large.
- **Use at your own risk**: deletion permanently removes audio files from disk; verify with `--what-if` before running a destructive command.

---

## Build status

[![.NET](https://github.com/rickystream94/dj-library-management-tools/actions/workflows/dotnet.yml/badge.svg)](https://github.com/rickystream94/dj-library-management-tools/actions/workflows/dotnet.yml)

---

## Prerequisites
- .NET Runtime (if you install the tool with `dotnet tool`)
- Rekordbox v7.x with your music collection.
- Mixed In Key, setup to write key & energy level _before_ the Comment tag (e.g. `1A - Energy 6 ...`), and with your music library already analyzed.

---

## Installation

You can install and use the tool in two ways:

### .NET Global Tool (recommended if you have the .NET runtime)

- Install globally:

	```pwsh
	dotnet tool install -g LibTools4DJs
	```

- Update:

	```pwsh
	dotnet tool update -g LibTools4DJs
	```

- Uninstall:

	```pwsh
	dotnet tool uninstall -g LibTools4DJs
	```

- Run:

	```pwsh
	djtools --help
	```

This requires the .NET runtime (not necessarily the full SDK). If the runtime is missing, install the latest .NET runtime from Microsoft.

### Self-contained binaries (no .NET required)

- Download the zip for your OS/arch from the GitHub Release (e.g., `LibTools4DJs-win-x64.zip`, `LibTools4DJs-linux-x64.zip`, `LibTools4DJs-osx-arm64.zip`).
- Extract and run the executable (`LibTools4DJs.exe` on Windows, `LibTools4DJs` on macOS/Linux). Optionally add the folder to your `PATH`.

These are single-file executables that bundle the runtime.

### Build from source

1. Clone repo: `git clone https://github.com/rickystream94/dj-library-management-tools.git`
1. `cd dj-library-management-tools`
1. Build: `dotnet build .\LibTools4DJs.sln -c Release`.
1. Find the generated `LibTools4DJs.exe` under the `bin` folder.
1. For all the commands listed in this readme, replace `djtools` with the path to the `LibTools4DJs.exe` file.

### Important note on code signing

Commits and tags in this repository are GPG‑signed to prove authorship and integrity of the source. My public key is available at: https://github.com/rickystream94.gpg

Binaries distributed via GitHub Releases are not platform‑signed (no Windows Authenticode certificate, no Apple notarization). As a result:

- Windows SmartScreen may warn when you run `djtools.exe` the first time.
- macOS Gatekeeper may require you to explicitly allow running the app.

This is expected and acceptable for this open‑source project. If you prefer, build from source or verify downloads via checksums/signatures when provided.

---

## Quick Start

### Common Prep
1. The tool expects a folder at the root of the Rekordbox playlists tree named `LIBRARY MANAGEMENT`.
1. If you want to run the `delete-tracks` command, create a playlist named `Delete` under the `LIBRARY MANAGEMENT` folder. Add manually the tracks to be deleted to such playlist, or you can create it as an intelligent playlist and use rules based on tags (e.g.  `My Tag contains Delete`).
1. Export the collection in XML format (`File > Export Collection in XML Format`) e.g. `D:\RekordboxExports\rekordbox.xml`.

### After execution of sync commands
1. Rekordbox XML is updated in place; a backup copy is stored under `LibTools4DJs_Backups` (same directory as original XML).
2. Open Rekordbox and re-import (or refresh) the modified XML if needed.
3. You will see any of the following playlists under `LIBRARY MANAGEMENT`:
	- `MIK Key Analysis` (tracks whose key was updated)
	- `MIK Energy Level Analysis` (tracks whose colour was updated)
	- A top-level folder `LibTools4DJs_SyncFromMIK` containing mirrored MIK folder hierarchy and playlists (from `sync-mik-folder-to-rekordbox`).
4. Select tracks in the analysis playlists and import into the collection to reflect updated Key/Colour metadata inside Rekordbox.

### `delete-tracks`

Bulk removes audio files from disk and their corresponding track entries from the Rekordbox collection. The set of tracks to delete is defined by placing them in (or matching rules for) the `LIBRARY MANAGEMENT/Delete` playlist. Use `--what-if` first to review what would be deleted without performing irreversible operations.

```powershell
djtools delete-tracks --xml "D:\RekordboxExports\rekordbox.xml" [--what-if]
```

### `sync-mik-tags-to-rekordbox`

Reads each track's tags (via Mixed In Key pre-written comments) to update the musical key for M4A tracks and assign a colour based on MIK energy level. Produces analysis playlists (`MIK Key Analysis`, `MIK Energy Level Analysis`) listing only the modified tracks so you can easily re-import them into Rekordbox.

```powershell
djtools sync-mik-tags-to-rekordbox --xml "D:\RekordboxExports\rekordbox.xml" [--what-if]
```

### `sync-rekordbox-library-to-mik`
Replicates the Rekordbox playlist & folder hierarchy into the Mixed In Key database (`MIKStore.db`). Existing playlists/folders in MIK are never deleted (safe additive sync). Track memberships are inserted only if not already present (idempotent behavior).

The tool searches for the MIK database in the default location location:
`$env:USERPROFILE\AppData\Local\Mixed In Key\Mixed In Key\<version>\MIKStore.db`.
You can optionally provide a different path with the `--mik-db` parameter, but I suggest to do so only if you know what you're doing. Otherwise, you can skip this parameter.

The default version is **11.0**, but it can be overridden with the `--mik-version` parameter.

Usage:
```powershell
djtools sync-rekordbox-library-to-mik --xml "D:\RekordboxExports\rekordbox.xml" [--what-if] [--mik-version "<MIK-version>"] [--mik-db "/path/to/MIKStore.db"] [--reset-mik-library] [--debug] [--save-logs]
```
When `--reset-mik-library` is used, the tool deletes all rows in `SongCollectionMembership` and all rows in `Collection` except the default system ones identified by `Sequence IS NULL AND IsLibrary = 1 AND ParentFolderId IS NULL`. Runs within a transaction; skipped in `--what-if`.

### `sync-mik-folder-to-rekordbox`
Mirrors a chosen Mixed In Key root folder (and all nested folders & playlists) into the Rekordbox XML under the `LIBRARY MANAGEMENT/LibTools4DJs_SyncFromMIK` hierarchy. Existing Rekordbox playlists are not cleared—tracks are only appended if not already present (idempotent). Duplicates are avoided.

The command creates a backup of the XML, then updates it in place unless `--what-if` is used.

MIK DB path resolution follows the same rules as the other MIK command (auto-resolve unless `--mik-db` provided).

Usage:
```powershell
djtools sync-mik-folder-to-rekordbox --xml "D:\RekordboxExports\rekordbox.xml" --mik-folder "My DJ Prep" [--what-if] [--mik-version "<MIK-version>"] [--mik-db "/path/to/MIKStore.db"]
```
Output summary includes an ASCII tree of folders/playlists with counts of tracks added.

---

## Logging Flags
All commands accept two optional logging flags:

- `--debug` enables verbose debug output (granular per-item actions). Without it, only summaries, warnings, and errors are printed.
- `--save-logs` persists all log messages (including debug ones even if not shown on console) to a timestamped file under `LibTools4DJs_Logs/` in the current working directory.

Example with both:
```powershell
djtools sync-rekordbox-library-to-mik --xml "D:\RekordboxExports\rekordbox.xml" --debug --save-logs
```

---

## Motivation & Problems Addressed
If you're a DJ and use Rekordbox to manage your library, you may have already heard of Mixed In Key and how useful it can be to complement Rekordbox when it comes to advanced key analysis and other features to enhance your creativity.
In particular, MIK can help with better key analysis and auto-detection of a track's energy level.
I have been using Rekordbox and MIK for quite some time and I'm overall happy with their functionality, however I did get frequently annoyed by some shortcomings that made my library management workflows more cumbersome and sometimes so much time consuming:
- At the time of writing, Rekordbox suffers from a known limitation with reloading tags for M4A tracks, to overwrite the Key detected by Rekordbox analysis with the one detected by MIK. Using the *Reload Tags* functionality, as described in the [official MIK documentation](https://mixedinkey.com/integration/rekordbox-integration/), will simply have no effect on M4A tracks, while it would work as expected for MP3, FLAC etc. This makes the MIK - Rekordbox integration practically useless if your library is made up of tons of M4A tracks that you don't want to re-download in a different format.
- There's no built-in way to map the MIK Energy Level of a track to a visual cue to immediately spot the track's energy level on the fly while mixing.
- Rekordbox doesn't provide a way to delete tracks from disk. It's possible to delete tracks from the collection, however the actual audio files would stay dangling in our PC, using precious space. It's possible to open Windows Explorer to navigate to a single audio file's location from the Rekordbox collection, but this becomes tedious and error prone if you want to permanently bulk-delete multiple tracks.

These utilities aim to:
1. Provide a repeatable way to purge tracks intentionally marked for deletion using a dedicated Rekordbox intelligent playlist ("Delete").
1. Update the Key of M4A tracks in the Rekordbox collection.
1. Set a Colour on the track based on MIK Energy Level.
1. Keep Rekordbox and MIK folder/playlist hierarchies aligned in either direction to avoid duplicating curation effort across both applications (neither tool natively supports syncing the other one's structure).

### Default Energy → Colour Mapping
Mixed In Key's energy level (1–10) is mapped to Rekordbox track colour using a simple JSON (`Configuration/EnergyLevelToColorCode.json`). You can edit this file to suit your visual preference. Current defaults:

| Energy | Hex Code | Rekordbox Colour |
|--------|----------|---------------|
| 1 | 0x0000FF | Blue ![#blue](https://placehold.co/15x15/blue/blue.png) |
| 2 | 0x0000FF | Blue ![#blue](https://placehold.co/15x15/blue/blue.png) |
| 3 | 0x0000FF | Blue ![#blue](https://placehold.co/15x15/blue/blue.png) |
| 4 | 0x0000FF | Blue ![#blue](https://placehold.co/15x15/blue/blue.png) |
| 5 | 0x00FF00 | Green ![#green](https://placehold.co/15x15/green/green.png) |
| 6 | 0xFFFF00 | Yellow ![#yellow](https://placehold.co/15x15/yellow/yellow.png) |
| 7 | 0xFFA500 | Orange ![#orange](https://placehold.co/15x15/orange/orange.png) |
| 8 | 0xFF0000 | Red ![#red](https://placehold.co/15x15/red/red.png) |
| 9 | 0xFF0000 | Red ![#red](https://placehold.co/15x15/red/red.png) |
| 10 | 0xFF0000 | Red ![#red](https://placehold.co/15x15/red/red.png)|

Only a limited palette renders distinctly in Rekordbox; adjust responsibly.

---

## Troubleshooting

| Problem | Cause | Resolution |
|---------|-------|------------|
| Command errors: `LIBRARY MANAGEMENT` folder not found | XML export missing required root folder | Create `LIBRARY MANAGEMENT` at the root of Rekordbox playlists before exporting. |
| No tracks updated for key | Tracks are not M4A or comment format differs | Ensure Mixed In Key writes key & energy at the beginning (e.g. `1A - Energy 6 ...`) and track `Kind` is `M4A File`. |
| Energy colour mapping not applied | Missing or malformed `Configuration/EnergyLevelToColorCode.json` | Verify file exists in output directory and JSON maps integers 1–10 to hex codes. |
| Tracks reported as missing during delete | Files moved/renamed outside Rekordbox | Re-analyze or relocate tracks in Rekordbox, then re-export XML; confirm `Location` paths are valid on disk. |
| MIK DB auto-resolve fails | Version folder path differs or USERPROFILE unset | Pass `--mik-db` explicitly or specify correct `--mik-version`. Check environment variable. |
| Playlist sync skips additions | Tracks already present in target playlist | Expected idempotent behavior. Remove unwanted tracks manually if a re-sync requires fresh ordering. |
| `sync-mik-folder-to-rekordbox` creates unexpected structure | Selected MIK folder name not a root-level folder | Choose a root-level folder (ParentFolderId NULL) or reorganize in MIK before syncing. |
| Large sync seems slow | High volume of inserts without transaction optimization | Currently uses simple inserts for clarity; performance usually acceptable. Consider adding batching logic if libraries exceed tens of thousands of memberships. |
| Backup folder growing large | Frequent runs accumulate backups | Periodically archive or prune `LibTools4DJs_Backups`. Keep recent backups before major changes. |

If an issue is not listed, inspect console warnings and errors; they are designed to be explicit. Feel free to open an issue with command output and environment details.

---

## Verify Downloads

You can verify the integrity and authenticity of release downloads:

1. Import the public key (one-time)

	- The public key is available at: https://github.com/rickystream94.gpg
	- Import into GPG:

		```bash
		gpg --import rickystream94_pubkey.asc
		```

1. Download files from the Release assets

	- The archive for your platform (e.g., `LibTools4DJs-win-x64.zip`)
	- The checksum file `SHA256SUMS`
	- The signature file `SHA256SUMS.sig`

1. Verify checksums and signature

	- Windows (PowerShell):

		```pwsh
		gpg --verify .\SHA256SUMS.sig .\SHA256SUMS
		```

	- macOS/Linux:

		```bash
		sha256sum -c SHA256SUMS   # Use 'shasum -a 256 -c SHA256SUMS' on macOS if sha256sum isn't available
		gpg --verify SHA256SUMS.sig SHA256SUMS
		```

If verification succeeds, the archives you downloaded match the published checksums and were signed by the expected key.