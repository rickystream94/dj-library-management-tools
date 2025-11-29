namespace LibTools4DJs.Utils
{
    internal static class PathUtils
    {
        public static string NormalizePath(string p)
        {
            if (string.IsNullOrWhiteSpace(p))
                return string.Empty;

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
