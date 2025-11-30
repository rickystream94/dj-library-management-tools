# LibTools4DJs CLI

Console utilities to help DJs with advanced library management tasks involving [**Rekordbox**](https://rekordbox.com/) and [**Mixed In Key**](https://mixedinkey.com/).

## Features

| Feature | Command |
|---------|---------|
| Bulk delete tracks (from Rekordbox collection and from disk) | `delete-tracks` |
| Sync MIK key for M4A tracks to Rekordbox XML | `sync-mik-tags-to-rekordbox` |
| Map MIK energy level to colour code in Rekordbox XML | `sync-mik-tags-to-rekordbox` |
| Sync Rekordbox library structure (folders and playlists) to MIK | `sync-rekordbox-playlists-to-mik` |
| Sync a MIK folder and all its child collections (folders and playlists) to Rekordbox XML | `sync-mik-folder-to-rekordbox` |

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
- .NET 10 SDK
- Rekordbox v7.x with your music collection.
- Mixed In Key, setup to write key & energy level _before_ the Comment tag (e.g. `1A - Energy 6 ...`), and with your music library already analyzed.

---

## Quick Start
> ❕Currently, only building the project from source is available. Support for running a packaged version of the tool will come soon.

### Common Prep
1. The tool expects a folder at the root of the Rekordbox playlists tree named `LIBRARY MANAGEMENT`.
1. If you want to run the `delete-tracks` command, create a playlist named `Delete` under the `LIBRARY MANAGEMENT` folder. Add manually the tracks to be deleted to such playlist, or you can create it as an intelligent playlist and use rules based on tags (e.g.  `My Tag contains Delete`).
1. Export the collection in XML format (`File > Export Collection in XML Format`) e.g. `D:\RekordboxExports\rekordbox.xml`.
1. Clone repo: `git clone https://github.com/rickystream94/dj-library-management-tools.git`
1. `cd dj-library-management-tools`
1. Build: `dotnet build .\LibTools4DJs.sln -c Release`.
1. Find the generated `LibToolsForDJs.exe` under the `bin` folder.

### After execution of sync commands
1. Rekordbox XML is updated in place; a backup copy is stored under `LibTools4DJs_Backups` (same directory as original XML).
2. Open Rekordbox and re-import (or refresh) the modified XML if needed.
3. You will see any of the following playlists under `LIBRARY MANAGEMENT`:
	- `MIK Key Analysis` (tracks whose key was updated)
	- `MIK Energy Level Analysis` (tracks whose colour was updated)
	- A top-level folder `LibTools4DJs_SyncFromMIK` containing mirrored MIK folder hierarchy and playlists (from `sync-mik-folder-to-rekordbox`).
4. Select tracks in the analysis playlists and import into the collection to reflect updated Key/Colour metadata inside Rekordbox.

### Delete Tracks

Bulk removes audio files from disk and their corresponding track entries from the Rekordbox collection. The set of tracks to delete is defined by placing them in (or matching rules for) the `LIBRARY MANAGEMENT/Delete` playlist. Use `--what-if` first to review what would be deleted without performing irreversible operations.

```powershell
LibTools4DJs.exe delete-tracks --xml "D:\RekordboxExports\rekordbox.xml" [--what-if]
```

### Sync MIK Key & Energy

Reads each track's tags (via Mixed In Key pre-written comments) to update the musical key for M4A tracks and assign a colour based on MIK energy level. Produces analysis playlists (`MIK Key Analysis`, `MIK Energy Level Analysis`) listing only the modified tracks so you can easily re-import them into Rekordbox.

```powershell
LibTools4DJs.exe sync-mik-tags-to-rekordbox --xml "D:\RekordboxExports\rekordbox.xml" [--what-if]
```

### Sync Rekordbox Playlists To Mixed In Key
Replicates the Rekordbox playlist & folder hierarchy into the Mixed In Key database (`MIKStore.db`). Existing playlists/folders in MIK are never deleted (safe additive sync). Track memberships are inserted only if not already present (idempotent behavior).

The tool searches for the MIK database in the default location location:
`$env:USERPROFILE\AppData\Local\Mixed In Key\Mixed In Key\<version>\MIKStore.db`.
You can optionally provide a different path with the `--mik-db` parameter, but I suggest to do so only if you know what you're doing. Otherwise, you can skip this parameter.

The default version is **11.0**, but it can be overridden with the `--mik-version` parameter.

Usage:
```powershell
LibTools4DJs.exe sync-rekordbox-playlists-to-mik --xml "D:\RekordboxExports\rekordbox.xml" [--what-if] [--mik-version "<MIK-version>"] [--mik-db "/path/to/MIKStore.db"]
```

### Sync MIK Folder To Rekordbox
Mirrors a chosen Mixed In Key root folder (and all nested folders & playlists) into the Rekordbox XML under the `LIBRARY MANAGEMENT/LibTools4DJs_SyncFromMIK` hierarchy. Existing Rekordbox playlists are not cleared—tracks are only appended if not already present (idempotent). Duplicates are avoided.

The command creates a backup of the XML, then updates it in place unless `--what-if` is used.

MIK DB path resolution follows the same rules as the other MIK command (auto-resolve unless `--mik-db` provided).

Usage:
```powershell
LibTools4DJs.exe sync-mik-folder-to-rekordbox --xml "D:\RekordboxExports\rekordbox.xml" --mik-folder "My DJ Prep" [--what-if] [--mik-version "<MIK-version>"] [--mik-db "/path/to/MIKStore.db"]
```
Output summary includes an ASCII tree of folders/playlists with counts of tracks added.

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

