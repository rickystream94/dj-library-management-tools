## AI Coding Agent Instructions (`dj-library-management-tools`)

Purpose: C#/.NET console utilities to bridge gaps between Rekordbox and Mixed In Key (MIK): safe bulk deletion, tag/key/energy sync, and bidirectional folder/playlist structure replication.

### Core Architecture
Located in `src/LibTools4DJs/`. Entry point `Program.cs` uses `System.CommandLine` to wire commands:
1. `delete-tracks` → `DeleteTracksHandler`
2. `sync-mik-tags-to-rekordbox` → `SyncMikTagsToRekordboxHandler`
3. `sync-rekordbox-library-to-mik` → `SyncRekordboxPlaylistsToMikHandler`
4. `sync-mik-folder-to-rekordbox` → `SyncMikFolderToRekordboxHandler`

Library XML abstraction: `RekordboxXmlLibrary` wraps `XmlDocument` providing helpers:
- Load (`Load(path)`), decode file URIs (`DecodeFileUri`)
- Get library management folder (`GetLibraryManagementFolder`)
- Track enumeration (`GetCollectionTracks`)
- Playlist/folder creation: `InitializeLibraryManagementChildPlaylist` (resetting), `GetOrCreateFolder(...)` & `GetOrCreatePlaylist(...)` (idempotent, no clearing)
- Track addition (`AddTrackToPlaylist`), entries update (`UpdatePlaylistTracksCount`), backups (`CreateBackupCopy`), save (`SaveAs`)
Use `Constants.cs` for all XML attribute & playlist names; never inline strings (prevents typos, keeps handlers uniform).

### Rekordbox XML Conventions
Required root playlist folder: `LIBRARY MANAGEMENT`. Track reference inside playlists: `<TRACK Key="<TrackID>" />`. Playlists = `NODE Type="1"`; folders = `NODE Type="0"`. Colour mapping uses `Colour` attribute (e.g. `0xFFFF00`).

### Mixed In Key DB Access
`MikDao` (SQLite via `Microsoft.Data.Sqlite`): lazy connection; methods for playlist hierarchy & membership:
- `GetRootFolderIdByNameAsync`
- `GetChildFoldersAsync(parentId)` / `GetChildPlaylistsAsync(parentId)`
- `GetPlaylistSongFilesAsync(collectionId)`
- Prepared/batched membership inserts for performance (`AddSongsToPlaylistBatchAsync`, transaction-aware reuse).
Uniqueness for folders/playlists: tuple `(ParentId, Name, IsFolder)` cached in `ExistingCollections`.
Path matching normalizes absolute file paths via `PathUtils.NormalizePath` before lookup in `SongIdsByPath`.

### Handlers Overview
`DeleteTracksHandler`: builds deletion set from `LIBRARY MANAGEMENT/Delete` playlist; validates file existence; `--what-if` prevents actual deletion.
`SyncMikTagsToRekordboxHandler`: reads tags with TagLib; regex-based key & energy extraction; updates M4A tonality & colour (energy→hex JSON in `Configuration/EnergyLevelToColorCode.json`); collects modified tracks into analysis playlists.
`SyncRekordboxPlaylistsToMikHandler`: mirrors Rekordbox folder/playlist tree into MIK DB (additive; never deletes existing collections; skips existing memberships).
`SyncMikFolderToRekordboxHandler`: mirrors a specified MIK folder hierarchy into Rekordbox under `LibTools4DJs_SyncFromMIK` using true nested folders (`GetOrCreateFolder`); playlists idempotently populated (no duplicate tracks); prints ASCII tree summary of additions.

### Backup & Persistence
Before mutating XML (non `--what-if`), handlers call `RekordboxXmlLibrary.CreateBackupCopy()` → timestamped `.bak.xml` in `LibTools4DJs_Backups` sibling folder, then save in place (`SaveAs(library.Path)`). Always log the backup path.

### Build & Execution Workflow
Prereq: .NET 8/10 SDK (solution: `LibTools4DJs.sln`). Build: `dotnet build LibTools4DJs.sln`. Run examples:
```powershell
dotnet run --project src/LibTools4DJs -- delete-tracks --xml D:\RekordboxExports\rekordbox.xml --what-if
dotnet run --project src/LibTools4DJs -- sync-mik-tags-to-rekordbox --xml D:\RekordboxExports\rekordbox.xml
dotnet run --project src/LibTools4DJs -- sync-rekordbox-library-to-mik --xml D:\RekordboxExports\rekordbox.xml --what-if
dotnet run --project src/LibTools4DJs -- sync-mik-folder-to-rekordbox --xml D:\RekordboxExports\rekordbox.xml --mik-folder "My DJ Prep" --what-if
```
Auto-resolve MIK DB path: `$env:USERPROFILE\AppData\Local\Mixed In Key\Mixed In Key\<version>\MIKStore.db` unless `--mik-db` supplied.

### Logging & What-If Pattern
All handlers accept `--what-if` for dry-run. Use `ConsoleLogger.Info/Warn/Error`; warn on skips (missing paths, no energy tag), error on IO/TagLib exceptions.

### Extension Guidelines
Add new commands by updating `Program.cs`: declare constant, create `Command` with validated `Option`s, and delegate to a new handler. Reuse `HandleMikCommandAsync` for MIK DB aware commands. Prefer `GetOrCreateFolder/Playlist` for idempotent XML changes.

### Pitfalls & Checks
Missing `LIBRARY MANAGEMENT` → throw early. Ensure energy mapping JSON present in output `Configuration/`. Avoid clearing existing playlists unless intentionally resetting (only `InitializeLibraryManagementChildPlaylist` performs a wipe). Always normalize file paths before key lookups.

---
Feedback welcome: request clarifications or additions (tests strategy, future commands) to evolve these agent notes.