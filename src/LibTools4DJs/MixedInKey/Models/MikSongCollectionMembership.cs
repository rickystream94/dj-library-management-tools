// <copyright file="MikSongCollectionMembership.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.MixedInKey.Models
{
    /// <summary>
    /// Represents membership of a song within a MIK collection (playlist), including sequence order.
    /// </summary>
    /// <param name="songId">The Song row ID.</param>
    /// <param name="collectionId">The Collection row ID (playlist).</param>
    /// <param name="sequence">The zero-based sequence order within the playlist.</param>
    internal sealed class MikSongCollectionMembership(string songId, string collectionId, int sequence)
    {
        /// <summary>
        /// Gets the Song ID.
        /// </summary>
        public string SongId { get; } = songId;

        /// <summary>
        /// Gets the playlist Collection ID.
        /// </summary>
        public string CollectionId { get; } = collectionId;

        /// <summary>
        /// Gets the song sequence within the playlist.
        /// </summary>
        public int Sequence { get; } = sequence;
    }
}
