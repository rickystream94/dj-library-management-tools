// <copyright file="MikDaoFactory.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.MixedInKey
{
    /// <summary>
    /// Default factory that creates <see cref="MikDao"/> instances.
    /// </summary>
    public sealed class MikDaoFactory : IMikDaoFactory
    {
        /// <inheritdoc/>
        public IMikDao CreateMikDao(string mikDbPath)
        {
            return new MikDao(mikDbPath);
        }
    }
}
