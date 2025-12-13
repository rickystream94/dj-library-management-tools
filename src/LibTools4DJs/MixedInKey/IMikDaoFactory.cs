// <copyright file="IMikDaoFactory.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.MixedInKey
{
    /// <summary>
    /// Factory interface to create instances of <see cref="IMikDao"/>.
    /// </summary>
    public interface IMikDaoFactory
    {
        /// <summary>
        /// Creates a new instance of <see cref="IMikDao"/> for the given database path.
        /// </summary>
        /// <param name="mikDbPath">Absolute path to the Mixed In Key SQLite database file.</param>
        /// <returns>An initialized <see cref="IMikDao"/>.</returns>
        IMikDao CreateMikDao(string mikDbPath);
    }
}
