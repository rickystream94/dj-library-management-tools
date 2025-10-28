## AI Coding Agent Instructions for `dj-library-management-tools`

Purpose: Provide CLI & PowerShell utilities to patch gaps between Rekordbox and Mixed In Key (MIK): safe bulk deletion and syncing MIK key & energy level metadata back into a Rekordbox exported collection XML.

### Architecture Snapshot
Code lives under `src/LibTools4DJs/` (C# .NET 8 console app). Two primary commands exposed via `System.CommandLine` in `Program.cs`:
1. `delete-tracks` → invokes `DeleteTracksHandler`.
2. `sync-mik-to-rekordbox` → invokes `SyncMixedInKeyTagsToRekordboxHandler`.

Core model for library: `RekordboxXmlLibrary` (wrapper over `XmlDocument`)
- Loads exported Rekordbox XML (`RekordboxXmlLibrary.Load(path)`).
- XPath helpers to access playlist folder `LIBRARY MANAGEMENT` & collection `<TRACK>` nodes.
- Utilities: `DecodeFileUri`, `InitializePlaylist`, `AddTrackToPlaylist`, `UpdatePlaylistTracksCount`, `SaveAs`.
Shared constants: `Constants.cs` (prefer using these instead of string literals when manipulating XML attributes or playlist names).

### Rekordbox XML Conventions
Playlist folder required: `LIBRARY MANAGEMENT` (constant `LibraryManagement`).
Managed playlists (created/reset by sync handler):
- `MIK Key Analysis`
- `MIK Energy Level Analysis`
Deletion playlist expected name: `Delete`.
Playlist `<NODE>` attributes set via `SetPlaylistAttributes` (Type="1", KeyType="0", Entries updated afterward). Do not hand-edit XML directly—use library methods.
Track identification inside playlists: `<TRACK Key="<TrackID>" />` (note: playlist track node uses attribute `Key` to reference collection `TrackID`).

### Handler Logic Essentials
DeleteTracksHandler:
- Builds HashSet of track IDs from Delete playlist (`GetTracksToDelete()` then `GetAttribute(Key)` of playlist track nodes).
- Matches against collection tracks by `TrackID`; converts `Location` (URI with prefix `file://localhost/`) using `DecodeFileUri`.
- Supports `--what-if` (no deletes). All file operations must validate existence; log via injected `ILogger`.
SyncMixedInKeyTagsToRekordboxHandler:
- Loads energy mapping JSON `EnergyLevelToColorCode.json` from `AppContext.BaseDirectory`.
- Iterates all collection tracks; reads file tags via TagLib (`TagLibSharp.dll` expected in runtime bin or `lib` copied into output).
- Comment parse heuristics: first token = key (regex `^\d{1,2}[A-G]$`); energy level regex `Energy (\d{1,2})` anywhere in comment.
- Key updates limited to tracks where `Kind == "M4A File"` (workaround for Rekordbox limitation). Non‑M4A counted/skipped.
- Energy level maps to `Colour` attribute (hex code like `0xFFFF00`). Only updates when different.
- Adds affected tracks to corresponding analysis playlists using `AddTrackToPlaylist` and increments summary counts; final XML saved as timestamped `rekordbox_collection_YYYY-MM-DD_HH-mm.xml`.

### Build & Run Workflow
Prereqs: .NET 8 SDK. (PowerShell scripts remain separate in `scripts/`.)
Build: `dotnet build LibTools4DJs.sln`.
Run examples (from repo root):
- Delete: `dotnet run --project src/LibTools4DJs -- delete-tracks --xml D:\RekordboxExports\rekordbox.xml --what-if`
- Sync: `dotnet run --project src/LibTools4DJs -- sync-mik-to-rekordbox --xml D:\RekordboxExports\rekordbox.xml`
Binary output: `src/LibTools4DJs/bin/{Debug|Release}/net8.0/` (mapping JSON resolves from `AppContext.BaseDirectory` under `Configuration` folder). Ensure `Configuration/EnergyLevelToColorCode.json` is copied to output (Content item with `CopyToOutputDirectory`).

### Extension & Modification Guidelines
- Add new commands: modify `Program.cs`; keep constant for command name; follow existing option parsing pattern with validation.
- When adding new playlists, always go through `InitializePlaylist` to avoid stale entries & attribute drift.
- Use `Constants` for XML attribute names; never hardcode (avoids typos & keeps handlers consistent).
- If supporting non‑M4A key updates, remove the `Kind` gate but update summary counters & README alignment.
- For new metadata mappings, create a dedicated JSON and load similar to energy mapping; keep integer→string dictionary with hex codes.
- Prefer streaming iteration (handlers currently load all tracks into memory; acceptable given typical library sizes—optimize only if profiling shows need).

### Error & Logging Patterns
Handlers use injected `ILogger` (see `ConsoleLogger.cs`) with methods `Info`, `Warn`, `Error`. Follow pattern: validate early, warn on skips (missing file, malformed comment), error on exceptional conditions (I/O, TagLib failures). Summaries printed at end with counts for each category.

### Safe Operations & Testing Notes
- Always test destructive operations using `--what-if` first (delete command).
- For sync changes, verify generated playlists counts via `Entries` attribute after run; playlist counts are set post‑processing with `UpdatePlaylistTracksCount`.
- Keep backups of original Rekordbox XML when adjusting logic around file path decoding or tag parsing.

### Common Pitfalls
- Missing `LIBRARY MANAGEMENT` folder → handlers abort (explicit exception in sync, silent skip in delete). Provide clear guidance or auto-create in future enhancement.
- Energy mapping file not found → sync throws; ensure file exists in `Configuration/` in the output directory.
- TagLib initialization failures for corrupt or unsupported files → error logged per track, processing continues.

### Quick Reference Constants (do not rename without updating README)
LibraryManagement, MIKKeyAnalysis, MIKEnergyAnalysis, DeletePlaylistName, TrackID, Location, Kind, Tonality, Colour.

### When Unsure
Inspect sample XML structure via XPath queries in `RekordboxXmlLibrary`; replicate existing patterns. Ask for clarification before introducing new XML schema elements.

---
Feedback welcome: clarify any ambiguous workflow or add missing conventions; keep file under 50 lines by pruning generic advice.