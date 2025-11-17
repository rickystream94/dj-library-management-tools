# LibTools4DJs CLI

Console utilities to help DJs with advanced library management tasks involving [**Rekordbox**](https://rekordbox.com/) and [**Mixed In Key**](https://mixedinkey.com/).

## Features

| Feature | Command |
|---------|---------|
| Bulk delete tracks (from Rekordbox collection and from disk) | `delete-tracks` |
| Sync MIK key for M4A tracks to Rekordbox XML | `sync-mik-tags-to-rekordbox` |
| Map MIK energy level to colour code in Rekordbox XML | `sync-mik-tags-to-rekordbox` |
| Import Rekordbox library structure (folders and playlists) in MIK | `sync-rekordbox-playlists-to-mik` |
| Import MIK library structure (folders and playlists) in Rekordbox | **Coming soon** |

## Notes
- Always test the outcome of the command execution with the optional `--what-if` parameter. This will only run a simulation and log the results to the console output.
- No changes are applied to the original `rekordbox.xml` file provided as input. Changes are only written to a _copy_ of such file, which will be generated at the end of the command execution. This allows you to preserve a history of the files and prevent destructive changes to the XML file.
- Recommendation: keep a backup of the Rekordbox XML file(s) over time.
- **Use at your own risk**: when deleting audio tracks, keep in mind that the operation is irreversible. The author is not responsible for unintended data loss.

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

### After execution of commands that generate a new rekordbox XML file
1. Import the generated `rekordbox_collection_YYYY-MM-DD_HH-mm.xml` into Rekordbox.
1. In the rekordbox.xml pane, you will notice the `MIK Key Analysis` & `MIK Energy Level Analysis` playlists.
1. Select all tracks in each playlist, right‑click → "Import to collection". Now those tracks will show the correct key from the MIK analysis (only relevant to M4A tracks) and will have a colour based on the energy level as detected by MIK.

### Delete Tracks

```powershell
LibTools4DJs.exe delete-tracks --xml "D:\RekordboxExports\rekordbox.xml" [--what-if]
```

### Sync MIK Key & Energy

```powershell
LibTools4DJs.exe sync-mik-tags-to-rekordbox --xml "D:\RekordboxExports\rekordbox.xml" [--what-if]
```

### Sync Rekordbox Playlists To Mixed In Key
Replicates the Rekordbox playlist & folder hierarchy into the Mixed In Key database (`MIKStore.db`). Existing playlists/folders in MIK are never deleted (safe additive sync). Track memberships are inserted only if not already present (idempotent behavior).

Requires the path to your MIK SQLite database file via `--mik-db`.

Typical MIK database location (adjust version / user profile as needed):
`C:\Users\<YOU>\AppData\Local\Mixed In Key\Mixed In Key\11.0\MIKStore.db`

Usage:
```powershell
LibTools4DJs.exe sync-rekordbox-playlists-to-mik --xml "D:\RekordboxExports\rekordbox.xml" --mik-db "C:\Users\<YOU>\AppData\Local\Mixed In Key\Mixed In Key\11.0\MIKStore.db" [--what-if]
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
1. Update the Key of M4A tracks in the Rekordbox collection
1. Set a Color on the track based on MIK Energy Level.

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

