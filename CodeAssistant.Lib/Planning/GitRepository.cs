namespace CodeAssistant.Lib.Planning
{
    internal static class GitRepository
    {
        /// <summary>The closest directory at or above <paramref name="path"/> containing a <c>.git</c> entry, or null.</summary>
        public static string? FindRoot(string path)
        {
            for (DirectoryInfo? directory = new(Path.GetFullPath(path)); directory is not null; directory = directory.Parent)
            {
                if (Path.Exists(Path.Combine(directory.FullName, ".git")))
                {
                    return Path.TrimEndingDirectorySeparator(directory.FullName);
                }
            }

            return null;
        }

        public static bool IsRoot(string path)
        {
            return string.Equals(FindRoot(path), Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
    }
}
