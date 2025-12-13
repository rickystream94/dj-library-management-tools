// <copyright file="IMikDao.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.MixedInKey
{
    using LibTools4DJs.MixedInKey.Models;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// Data access object (DAO) for Mixed In Key's SQLite database, exposing helpers for collections and memberships.
    /// </summary>
    public interface IMikDao : IAsyncDisposable
    {
        /// <summary>
        /// Gets a cached map of existing collections keyed by <see cref="MikCollection"/> (ParentId, Name, IsFolder).
        /// </summary>
        Task<Dictionary<MikCollection, string>> ExistingCollections { get; }

        /// <summary>
        /// Gets a cached map of normalized absolute file paths to Song IDs.
        /// </summary>
        Task<Dictionary<string, string>> SongIdsByPath { get; }

        /// <summary>
        /// Creates a new collection (folder or playlist).
        /// </summary>
        /// <param name="newCollection">The collection model (name, parent, folder flag).</param>
        /// <param name="id">The ID to assign.</param>
        /// <param name="tx">Optional transaction to join.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task CreateNewCollectionAsync(MikCollection newCollection, string id, SqliteTransaction? tx = null);

        /// <summary>
        /// Gets the current max sequence number in a playlist, or -1 when empty.
        /// </summary>
        /// <param name="collectionId">Playlist collection ID.</param>
        /// <returns>The max sequence or -1.</returns>
        Task<int> GetMaxSongSequenceInPlaylistAsync(string collectionId);

        /// <summary>
        /// Retrieves the set of Song IDs currently present in a playlist.
        /// </summary>
        /// <param name="collectionId">Playlist collection ID.</param>
        /// <returns>Set of Song IDs.</returns>
        Task<HashSet<string>> GetSongsInPlaylistAsync(string collectionId);

        /// <summary>
        /// Inserts memberships for songs into a playlist.
        /// </summary>
        /// <param name="memberships">Membership rows to insert.</param>
        /// <param name="tx">Optional transaction to join.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task AddSongsToPlaylistAsync(IEnumerable<MikSongCollectionMembership> memberships, SqliteTransaction? tx = null);

        /// <summary>
        /// Resets the MIK library structure by deleting all memberships and non-system collections.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ResetLibraryAsync();

        /// <summary>
        /// Begins a new database transaction.
        /// </summary>
        /// <returns>The started transaction.</returns>
        Task<SqliteTransaction> BeginTransactionAsync();

        /// <summary>
        /// Gets the root folder ID by name.
        /// </summary>
        /// <param name="name">Folder name.</param>
        /// <returns>The folder ID or null.</returns>
        Task<string?> GetRootFolderIdByNameAsync(string name);

        /// <summary>
        /// Lists child folders under the specified parent folder.
        /// </summary>
        /// <param name="parentId">Parent folder ID.</param>
        /// <returns>List of (Id, Name) tuples.</returns>
        Task<List<(string Id, string Name)>> GetChildFoldersAsync(string parentId);

        /// <summary>
        /// Lists child playlists under the specified parent folder.
        /// </summary>
        /// <param name="parentId">Parent folder ID.</param>
        /// <returns>List of (Id, Name) tuples.</returns>
        Task<List<(string Id, string Name)>> GetChildPlaylistsAsync(string parentId);

        /// <summary>
        /// Gets ordered song file paths for a playlist.
        /// </summary>
        /// <param name="collectionId">Playlist collection ID.</param>
        /// <returns>List of absolute file paths, order by sequence.</returns>
        Task<List<string>> GetPlaylistSongFilesAsync(string collectionId);
    }
}