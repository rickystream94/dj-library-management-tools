// <copyright file="PathUtils.cs" company="LibTools4DJs">
// Copyright (c) LibTools4DJs. All rights reserved.
// </copyright>

namespace LibTools4DJs.Utils
{
    /// <summary>
    /// Path-related helper utilities.
    /// </summary>
    internal static class PathUtils
    {
        /// <summary>
        /// Normalizes a file path by trimming, converting forward slashes to backslashes, and attempting to canonicalize via GetFullPath.
        /// </summary>
        /// <param name="p">Input path (absolute or relative).</param>
        /// <returns>A normalized absolute or best-effort path; empty string when input is null/whitespace.</returns>
        public static string NormalizePath(string p)
        {
            if (string.IsNullOrWhiteSpace(p))
            {
                return string.Empty;
            }

            // Mixed In Key stores Windows paths with backslashes; Rekordbox decode may yield forward slashes.
            p = p.Trim();
            p = p.Replace('/', '\\');
            try
            {
                // Path.GetFullPath will also canonicalize casing where possible
                p = Path.GetFullPath(p);
            }
            catch
            {
                // If invalid path characters, keep as-is after slash normalization.
            }

            return p;
        }
    }
}
