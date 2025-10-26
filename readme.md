# Rekordbox & Mixed In Key Library Management Utilities for Windows

PowerShell utilities to patch common shortcomings in the workflow between [Rekordbox](https://rekordbox.com/) and [Mixed In Key (MIK)](https://mixedinkey.com/). These scripts operate directly on a Rekordbox exported collection XML and on the audio files referenced therein to:
1. safely bulk delete tracks marked for removal, both from the Rekordbox library *and* from disk.
1. synchronize MIK key (for M4A tracks, more details below) & energy level analysis results back into Rekordbox metadata with supporting playlists for easy re–import.
1. **Coming soon**: import Rekordbox playlists in Mixed In Key to avoid duplicate library management and improve seamless integration between the two software.

> **IMPORTANT**: It's recommended to work on a backup copy of your Rekordbox collection XML and audio files, especially when familiarizing with the scripts. These scripts can modify or delete files permanently.

---

## Table of Contents
* [Quick Start](#quick-start)
* [1. Motivation & Problems Addressed](#1-motivation--problems-addressed)
* [2. Script Inventory](#2-script-inventory)
* [3. Prerequisites & Installation](#3-prerequisites--installation)
* [4. Exporting Your Rekordbox Collection XML](#4-exporting-your-rekordbox-collection-xml)
* [5. Mixed In Key Configuration Assumptions](#5-mixed-in-key-configuration-assumptions)
* [6. Energy Level to Color Mapping](#6-energy-level-to-color-mapping)
* [7. DeleteTracks.ps1 (Bulk Safe Delete)](#7-deletetrackssps1-bulk-safe-delete)
* [8. SyncMixedInKeyTagsWithRekordboxXml.ps1 (Key & Energy Sync)](#8-syncmixedinkeytagswithrekordboxxmlps1-key--energy-sync)
* [9. Recommended End‑to‑End Workflow](#9-recommended-endtoend-workflow)
* [10. Logging & Output Artifacts](#10-logging--output-artifacts)
* [11. Edge Cases, Limitations & Safety](#11-edge-cases-limitations--safety)
* [12. Customization](#12-customization)
* [13. Troubleshooting](#13-troubleshooting)
* [14. FAQ](#14-faq)
* [Quick Reference Commands](#quick-reference-commands)
* [.NET CLI (Experimental)](#net-cli-experimental-replacement-for-powershell-scripts)
* [Disclaimer](#disclaimer)

---

## Quick Start
For a more extensive description, please refer to the next sections.

### Permanently delete tracks from Rekordbox library and disk

```powershell
# 1. In Rekordbox, create a custom tag named 'Delete', that you can use to tag tracks you wish to permanently delete from Rekordbox and from disk.
# 2. Create a 'LIBRARY MANAGEMENT' folder at the root of the playlists tree, and place an intelligent playlist named 'Delete' inside it. Rule should be 'My Tag contains Delete'.
# 3. Export your Rekordbox collection XML to a convenient location.

cd "c:\path\to\dj_tools\library_management"

# (Optional) Preview deletions
pwsh ./DeleteTracks.ps1 -RekordboxCollectionXmlFilePath "D:\RekordboxExports\rekordbox.xml" -WhatIf

# Permanently delete tracks
pwsh ./DeleteTracks.ps1 -RekordboxCollectionXmlFilePath "D:\RekordboxExports\rekordbox.xml"
```

### Generate new Rekordbox collection XML file
The goal is to fix the key for M4A tracks & use color coding to label tracks based on the Energy Level from MIK analysis.

```powershell
# 1. Place TagLibSharp.dll in the ./lib folder next to the scripts.
# 2. Export your Rekordbox collection XML to a convenient location.

pwsh ./SyncMixedInKeyTagsWithRekordboxXml.ps1 -RekordboxCollectionXmlFilePath "D:\RekordboxExports\rekordbox.xml"
```

### Import MIK results into Rekordbox
1. Import the newly generated `rekordbox_collection_YYYY-MM-DD_HH-mm.xml` back into Rekordbox.
1. Inspect the playlists `MIK Key Analysis` and `MIK Energy Level Analysis`.
1. Select all the tracks in the two playlists, right click and "Import to collection". This will overwrite the key for the M4A tracks and set a Colour on the track based on the Energy Level detected by MIK.

Jump to: [Motivation](#1-motivation--problems-addressed) · [Energy Mapping](#6-energy-level-to-color-mapping) · [Delete Script](#7-deletetrackssps1-bulk-safe-delete) · [Sync Script](#8-syncmixedinkeytagswithrekordboxxmlps1-key--energy-sync) · [Troubleshooting](#13-troubleshooting)

---

## 1. Motivation & Problems Addressed
If you're a a DJ and use Rekordbox to manage your library, you may have already heard of Mixed In Key and how useful it can be to complement Rekordbox when it comes to advanced key analysis and other features to enhance your creativity.
In particular, MIK can help with better track key detection and auto-detection of a track's energy level.
I have been using Rekordbox and MIK for quite some time and I'm overall happy with their functionality, however I did get frequently annoyed by some shortcomings that made my library management workflows more cumbersome and sometimes so much time consuming:
- At the time of writing, Rekordbox suffers from a known limitation with reloading tags for M4A tracks, to overwrite the Key detected by Rekordbox analysis with the one detected by MIK. Using the *Reload Tags* functionality, as described in the [official MIK documentation](https://mixedinkey.com/integration/rekordbox-integration/), will simply have no effect on M4A tracks, while it would work as expected for MP3 etc. This makes the MIK - Rekordbox integration practically useless if your library is made up of tons of M4A tracks that you don't want to re-download in a different format.
- There's no built-in way to map the MIK Energy Level of a track to a visual cue to immediately spot the track's energy level on the fly while mixing.
- Rekordbox doesn't provide a way to delete tracks from disk. It's possible to delete tracks from the collection, however the actual audio files would stay dangling in our PC, using precious space. It's possible to open Windows Explorer to navigate to a single audio file's location from the Rekordbox collection, but this becomes tedious and error prone if you want to permanently bulk-delete multiple tracks.

These utilities aim to:
1. Provide a repeatable way to purge tracks intentionally marked for deletion using a dedicated Rekordbox intelligent playlist ("Delete").
1. Update the Key of M4A tracks in the Rekordbox collection
1. Set a Color on the track based on MIK Energy Level.

See the [FAQ](#14-faq) for a concise explanation of the M4A key reload issue driving this workaround.

---

## 2. Script Inventory
Scripts live in `dj_tools/library_management/`.

| Script | Purpose | High‑Level Actions |
|--------|---------|--------------------|
| [`DeleteTracks.ps1`](./DeleteTracks.ps1) | Permanently remove tracks from disk that are listed in playlist `LIBRARY MANAGEMENT > Delete`. | Reads playlist, resolves file paths, optionally (WhatIf) simulates deletion, logs successes & failures. |
| [`SyncMixedInKeyTagsWithRekordboxXml.ps1`](./SyncMixedInKeyTagsWithRekordboxXml.ps1) | Synchronize Key & Energy metadata from MIK comments into Rekordbox XML. | Parses each track comment, updates `Tonality` (for M4A only), maps Energy Level to Hex Color, creates result playlists, saves new XML. |
| [`EnergyLevelToColorCode.json`](./EnergyLevelToColorCode.json) | Mapping file from MIK Energy Level (1–10) to Rekordbox color hex codes. | Used by sync script to set the `Colour` attribute. |

---

## 3. Prerequisites & Installation
1. Windows with PowerShell 7+ (recommended) or Windows PowerShell 5.1.
1. Rekordbox v7.x
1. Mixed In Key already configured with comment/tag writing.
1. [TagLibSharp](https://www.nuget.org/packages/TagLibSharp) DLL:
	- Download the NuGet package or DLL release.
	- Extract/copy `TagLibSharp.dll` into `dj_tools/library_management/lib/`.
1. \[Optional, but recommended\] Adequate backups of:
	- Your entire music library folders.
	- The Rekordbox collection XML (`File > Library > Export Collection in XML Format`).

No additional PowerShell modules are required; scripts rely on the built‑in runtime and TagLibSharp only.

---

## 4. Exporting Your Rekordbox Collection XML
Inside Rekordbox:
1. Open Preferences > Advanced > Database and enable XML export if not already enabled.
2. Go to `File > Library > Export Collection in XML Format` and choose a working folder (e.g., `D:\RekordboxExports\`).
3. Confirm the file path you will pass to scripts (e.g., `D:\RekordboxExports\rekordbox.xml`).
4. Ensure your playlist tree contains a parent folder named `LIBRARY MANAGEMENT`. Scripts rely on this exact name.
5. Create sub‑playlists:
	- `Delete` (tracks placed here will be removed by `DeleteTracks.ps1`).
	- The sync script will add / refresh: `MIK Key Analysis`, `MIK Energy Level Analysis` automatically.

---

## 5. Mixed In Key Configuration Assumptions
The sync script assumes MIK writes a structured prefix into the Comment tag, e.g.:

```
1A - Energy 6 Some prior existing comment text...
```

Parsing rules:
- First whitespace‑delimited token is treated as the Key (e.g., `1A`). Must match regex `^\d{1,2}[A-G]$` to be considered valid.
- Energy Level is extracted using regex `Energy (\d{1,2})` anywhere in the comment.
- Additional comment text after those tokens is ignored for sync purposes.

If this format differs in your setup you must adjust MIK output preferences or modify the script logic.

---

## 6. Energy Level to Color Mapping
Mapping lives in `EnergyLevelToColorCode.json` and can be customized. Default mapping:

| Energy Level | Hex Code | Color Name (Typical) |
|--------------|----------|----------------------|
| 1 | 0x0000FF | Blue |
| 2 | 0x0000FF | Blue |
| 3 | 0x0000FF | Blue |
| 4 | 0x0000FF | Blue |
| 5 | 0x00FF00 | Green |
| 6 | 0xFFFF00 | Yellow |
| 7 | 0xFFA500 | Orange |
| 8 | 0xFF0000 | Red |
| 9 | 0xFF0000 | Red |
| 10 | 0xFF0000 | Red |

Rekordbox internally stores the `Colour` attribute as the hex code. Adjust values to fit your personal color scheme; maintain the `"<energy>": "0xRRGGBB"` string format.

---

## 7. `DeleteTracks.ps1` (Bulk Safe Delete)
### Purpose
Automates deletion of tracks you have curated in the `Delete` playlist under `LIBRARY MANAGEMENT`.

### Logic Overview
1. Load Rekordbox collection XML.
2. Locate `LIBRARY MANAGEMENT > Delete` playlist and gather Track IDs.
3. Filter collection `<TRACK>` nodes whose `TrackID` matches.
4. Convert each track `Location` (URI form) to a real filesystem path.
5. For each path:
	- If missing, warn & log.
	- If present: delete (unless `-WhatIf` provided).
6. Maintain counts of deleted vs not found vs failed.
7. Write a timestamped log file (`delete_tracks_YYYY-MM-DD_HH-mm.log`).

### Usage
```powershell
cd "c:\path\to\dj_tools\library_management"
pwsh ./DeleteTracks.ps1 -RekordboxCollectionXmlFilePath "D:\RekordboxExports\rekordbox.xml"

# Dry run preview
pwsh ./DeleteTracks.ps1 -RekordboxCollectionXmlFilePath "D:\RekordboxExports\rekordbox.xml" -WhatIf
```

### Safety
- Use `-WhatIf` first to confirm the list of files targeted.
- Recommend backing up or using file versioning before irreversible deletion.

---

## 8. `SyncMixedInKeyTagsWithRekordboxXml.ps1` (Key & Energy Sync)
### Purpose
Bring MIK Key and Energy Level results into Rekordbox by editing the exported collection XML and preparing playlists of modified tracks. Key updates are limited to M4A files (see [FAQ](#14-faq)).

### Logic Overview
1. Ensure `TagLibSharp.dll` is loaded (for reading ID3/metadata from audio files).
2. Read the energy level mapping JSON.
3. Load Rekordbox collection XML.
4. Find the `LIBRARY MANAGEMENT` playlist folder.
5. Create or reset playlists: `MIK Key Analysis` & `MIK Energy Level Analysis`.
6. Iterate over every `<TRACK>`:
	- Decode file path; verify existence.
	- Open file with TagLib to read `Comment`.
	- Extract Key (first token) & Energy Level (regex).
	- If Energy Level present and mapped color differs, update `Colour` attribute and add track to energy playlist.
	- If file kind is `M4A File` and Key is valid & differs from `Tonality`, set new tonality and add track to key playlist.
7. Update playlist `Entries` counts.
8. Write a new XML file `rekordbox_collection_YYYY-MM-DD_HH-mm.xml` alongside an info log (`info_YYYY-MM-DD_HH-mm.log`).
9. Display summary counts.

### Key Updates Scope
M4A only (until Rekordbox fixes its reload behavior). To extend to other formats later remove the `Kind` check (see [Customization](#12-customization)).

### Usage
```powershell
cd "c:\path\to\dj_tools\library_management"
pwsh ./SyncMixedInKeyTagsWithRekordboxXml.ps1 -RekordboxCollectionXmlFilePath "D:\RekordboxExports\rekordbox.xml"
```

### Post‑Run: Re‑Importing into Rekordbox
1. In Rekordbox, re‑import or replace your collection using the newly created XML.
2. Locate playlists under `LIBRARY MANAGEMENT`:
	- `MIK Key Analysis`: All tracks whose Tonality was updated.
	- `MIK Energy Level Analysis`: All tracks whose Color was updated.
3. Optionally, filter & verify before committing changes to the database or performing further exports.

---

## 9. Recommended End‑to‑End Workflow
1. Export current collection XML (and backup original).
2. In Rekordbox, curate the `Delete` playlist with unwanted tracks.
3. Run `DeleteTracks.ps1` in `-WhatIf` mode; then run real delete once satisfied.
4. Run `SyncMixedInKeyTagsWithRekordboxXml.ps1` to refresh Key & Color metadata.
5. Inspect generated playlists in the new XML after importing back into Rekordbox.
6. (Optional) Update energy mapping JSON for personal color scheme and re‑run sync.
7. Commit changes to version control for historical tracking.

---

## 10. Logging & Output Artifacts
| Artifact | Created By | Purpose |
|----------|------------|---------|
| `delete_tracks_YYYY-MM-DD_HH-mm.log` | Delete script | Records each deletion attempt, missing files, failures. |
| `rekordbox_collection_YYYY-MM-DD_HH-mm.xml` | Sync script | Modified Rekordbox collection XML with updated metadata & playlists. |
| `info_YYYY-MM-DD_HH-mm.log` | Sync script | Warnings & informational notes about skipped or missing tracks. |

Logs are appended line by line; review them for audits or rollback decisions.

---

## 11. Edge Cases, Limitations & Safety
- Missing `LIBRARY MANAGEMENT` folder or required playlists causes scripts to throw explicit errors.
- Tracks with inaccessible or moved file paths are skipped & logged.
- Key updates restricted to `Kind = "M4A File"` (see [FAQ](#14-faq)).
- Comment parsing depends on MIK format; irregular comments yield warnings and are skipped for key/energy updates.
- Deleting cannot be undone. Always use backups + `-WhatIf`.
- Color codes must be valid hex in the JSON (`0xRRGGBB`).

---

## 12. Customization
| Area | How |
|------|-----|
| Energy color mapping | Edit `EnergyLevelToColorCode.json` and rerun sync script. |
| Additional playlists | Duplicate logic inside sync script (search for playlist creation block). |
| Other audio formats | Remove or adapt the `if ($track.Kind -ne "M4A File")` check. Validate TagLib field names first. |
| Comment parsing rules | Adjust regex or token extraction in sync script around `$comment` handling. |

---

## 13. Troubleshooting
| Symptom | Possible Cause | Fix |
|---------|----------------|-----|
| `TagLibSharp.dll not found` error | DLL missing or wrong path. | Place DLL in `lib/` folder next to scripts. |
| `No 'LIBRARY MANAGEMENT' playlist folder found` | Folder not created in Rekordbox before export. | Create folder & playlists, re‑export XML. |
| Tracks always skipped for key update | File format not M4A or comment malformed. | Ensure MIK writes proper comment; consider enabling format support. |
| Color not updating | Energy Level absent or already matches mapping. | Verify comment contains `Energy <n>` and mapping JSON value. |
| New XML not appearing after import | Wrong import procedure or caching. | Restart Rekordbox; ensure you imported the new timestamped XML. |

---

## 14. FAQ
**Why only M4A for key syncing?**
Rekordbox's "Reload Tags" feature does not currently refresh the `Initial Key` tag from M4A files after Mixed In Key analysis, even though the key is written into the file metadata. Other common formats (MP3, WAV, AIFF) do reload correctly, so updating them here would be redundant. This script acts purely as a workaround for that M4A limitation.

**Will key syncing for M4A be removed later?**
Yes—once Rekordbox reliably reloads the Initial Key from M4A files, the M4A-specific tonality override logic becomes unnecessary and can be removed.

**Can I force key updates for other formats now?**
Yes. Delete (or adjust) the `if ($track.Kind -ne "M4A File")` conditional in the script. Only do this if you have validated that it won't overwrite correct existing data.

**Does Mixed In Key write the key correctly into M4A?**
Yes, Mixed In Key writes valid key info; Rekordbox just fails to pull it back automatically for M4A during a tag reload.

**Is this a Rekordbox bug or limitation?**
Functionally it's a limitation impacting workflow parity across formats. Treat it as a bug until official release notes indicate a fix.

**How do I check if the issue is fixed?**
Analyze an M4A in Mixed In Key, confirm its key in a tag editor, then use Rekordbox's "Reload Tags". If the `Initial Key` updates correctly without the script, the workaround is no longer required.

---

## Quick Reference Commands

```powershell
# Delete (dry run)
pwsh ./DeleteTracks.ps1 -RekordboxCollectionXmlFilePath "D:\RekordboxExports\rekordbox.xml" -WhatIf

# Delete (execute)
pwsh ./DeleteTracks.ps1 -RekordboxCollectionXmlFilePath "D:\RekordboxExports\rekordbox.xml"

# Sync key & energy
pwsh ./SyncMixedInKeyTagsWithRekordboxXml.ps1 -RekordboxCollectionXmlFilePath "D:\RekordboxExports\rekordbox.xml"
```

---

## .NET CLI (Experimental Replacement for PowerShell Scripts)
An early .NET 8 console application alternative is scaffolded under `src/DJTools`. After building you can invoke the executable instead of the PowerShell scripts.

### Build
```powershell
dotnet build .\DJTools.sln -c Release
```

Executable path (default): `src\DJTools\bin\Release\net8.0\DJTools.exe`

### Commands
```powershell
# Delete tracks (simulate then execute)
DJTools.exe delete-tracks --xml "D:\RekordboxExports\rekordbox.xml" --what-if
DJTools.exe delete-tracks --xml "D:\RekordboxExports\rekordbox.xml"

# Sync MIK key (M4A only) & energy colors
DJTools.exe sync-mik --xml "D:\RekordboxExports\rekordbox.xml"
DJTools.exe sync-mik --xml "D:\RekordboxExports\rekordbox.xml" --mapping "c:\path\to\EnergyLevelToColorCode.json"
```

### Differences vs PowerShell scripts
| Area | PowerShell | .NET CLI |
|------|------------|----------|
| Dependency management | Manual DLL (TagLibSharp) | NuGet package reference |
| Invocation | `pwsh ./Script.ps1` | `DJTools.exe <command>` |
| Extensibility | Edit script | Add C# handlers / services |
| Mapping file path | Fixed relative | Configurable via `--mapping` |
| Error handling | Basic try/catch | Structured exceptions (expandable) |

### Roadmap Ideas
- Add structured logging & persistent log files.
- Add dry-run flag for `sync-mik`.
- Global tool publishing (`dotnet tool install`).
- Playlist import into Mixed In Key.
- Unit tests for XML parsing & transformations.

---

## Disclaimer
Use at your own risk. The author(s) assume no responsibility for data loss. Always keep external backups of your music library and master Rekordbox database.