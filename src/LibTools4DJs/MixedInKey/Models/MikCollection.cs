// <copyright file="MikCollection.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.MixedInKey.Models
{
    /// <summary>
    /// Represents a Mixed In Key collection node (folder or playlist), identified by parent/name/isFolder tuple.
    /// </summary>
    /// <param name="parentId">Optional parent folder ID; null for root-level nodes.</param>
    /// <param name="name">The collection name.</param>
    /// <param name="isFolder">True for a folder; false for a playlist.</param>
    public sealed class MikCollection(string? parentId, string name, bool isFolder) : IEquatable<MikCollection>
    {
        /// <summary>
        /// Gets the parent folder ID or null when at the root level.
        /// </summary>
        public string? ParentId { get; } = parentId;

        /// <summary>
        /// Gets the collection name.
        /// </summary>
        public string Name { get; } = name;

        /// <summary>
        /// Gets a value indicating whether this collection is a folder (true) or a playlist (false).
        /// </summary>
        public bool IsFolder { get; } = isFolder;

        /// <summary>
        /// Returns a hash code based on <see cref="ParentId"/>, <see cref="Name"/>, and <see cref="IsFolder"/>.
        /// </summary>
        /// <returns>A stable hash code for use in dictionaries/sets.</returns>
        public override int GetHashCode() => HashCode.Combine(this.ParentId, this.Name, this.IsFolder);

        /// <summary>
        /// Determines equality with another object.
        /// </summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns>True when equal; otherwise false.</returns>
        public override bool Equals(object? obj) => this.Equals(obj as MikCollection);

        /// <summary>
        /// Determines equality with another <see cref="MikCollection"/> using case-insensitive name/parent and folder flag.
        /// </summary>
        /// <param name="other">The other instance.</param>
        /// <returns>True when equal; otherwise false.</returns>
        public bool Equals(MikCollection? other) =>
            other is not null &&
            string.Equals(this.ParentId, other.ParentId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(this.Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
            this.IsFolder == other.IsFolder;
    }
}
