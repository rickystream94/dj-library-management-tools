# LibTools4DJs CLI

Console utilities to help DJs with advanced library management tasks involving [**Rekordbox**](https://rekordbox.com/) and [**Mixed In Key**](https://mixedinkey.com/).
Currently supported features:

| Feature | Command |
|---------|---------|
| Bulk delete tracks (from Rekordbox collection and from disk) | `delete-tracks` |
| Sync MIK key for M4A tracks to Rekordbox XML | `sync-mik-to-rekordbox` |
| Map MIK energy level to colour code in Rekordbox XML | `sync-mik-to-rekordbox` |

More commands can be added over time; follow the same pattern. Always back up your XML and audio files—operations are irreversible.

---

## Prerequisites
- .NET 8 SDK
- Rekordbox v7.x
- Mixed In Key writing key & energy into the Comment (e.g. `1A - Energy 6 ...`)

---

## Quick Start
### Common Prep
1. In Rekordbox create playlist folder `LIBRARY MANAGEMENT` (root level).
2. Create empty playlist `Delete` (manual curation or intelligent rule `My Tag contains Delete`).
3. Export collection XML (`File > Library > Export Collection in XML Format`) e.g. `D:\RekordboxExports\rekordbox.xml`.
4. Build: `dotnet build .\LibTools4DJs.sln -c Release`.

### Delete Tracks
Dry run (preview):
```powershell
LibTools4DJs.exe delete-tracks --xml "D:\RekordboxExports\rekordbox.xml" --what-if
```
Execute (permanent):
```powershell
LibTools4DJs.exe delete-tracks --xml "D:\RekordboxExports\rekordbox.xml"
```

### Sync MIK Key & Energy
Dry run:
```powershell
LibTools4DJs.exe sync-mik-to-rekordbox --xml "D:\RekordboxExports\rekordbox.xml" --what-if
```
Execute:
```powershell
LibTools4DJs.exe sync-mik-to-rekordbox --xml "D:\RekordboxExports\rekordbox.xml"
```
After execution:
1. Import the generated `rekordbox_collection_YYYY-MM-DD_HH-mm.xml` into Rekordbox.
2. Open playlists `MIK Key Analysis` & `MIK Energy Level Analysis`.
3. Select playlist tracks, right‑click → "Import to collection" to apply updated key (for M4A tracks) and colour based on energy level.

---

## Motivation & Problems Addressed
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

### Default Energy → Colour Mapping
Mixed In Key's energy level (1–10) is mapped to Rekordbox track colour using a simple JSON (`Configuration/EnergyLevelToColorCode.json`). You can edit this file to suit your visual preference. Current defaults:

| Energy | Hex Code | Rekordbox Colour |
|--------|----------|---------------|
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

Only a limited palette renders distinctly in Rekordbox; adjust responsibly.

---

## Disclaimer
Use at your own risk. Maintain offline backups of music files and XML; the author is not responsible for data loss.

---

