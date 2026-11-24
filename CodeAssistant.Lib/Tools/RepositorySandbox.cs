namespace CodeAssistant.Lib.Tools
{
    /// <summary>Confines file system access to a repository root, hiding excluded directories and links that lead outside the root.</summary>
    public sealed class RepositorySandbox
    {
        private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private readonly HashSet<string> _excludedDirectories;
        private readonly string _rootWithSeparator;

        public RepositorySandbox(string root, IEnumerable<string> excludedDirectories)
        {
            string fullRoot = Path.GetFullPath(root);
            if (!Directory.Exists(fullRoot))
            {
                throw new DirectoryNotFoundException($"Repository root '{fullRoot}' does not exist.");
            }

            Root = Path.TrimEndingDirectorySeparator(fullRoot);
            _rootWithSeparator = Path.EndsInDirectorySeparator(Root) ? Root : Root + Path.DirectorySeparatorChar;
            _excludedDirectories = new HashSet<string>(excludedDirectories, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }

        public string Root { get; }

        /// <summary>Resolves a repository-relative path to a full path, or throws <see cref="UnauthorizedAccessException"/> when it is not accessible.</summary>
        public string Resolve(string? path)
        {
            string trimmed = path?.Trim() ?? string.Empty;
            string fullPath = trimmed.Length == 0 || trimmed == "." || trimmed == "/"
                ? Root
                : Path.GetFullPath(Path.Combine(Root, trimmed.TrimStart('/', '\\')));

            if (!IsInsideRoot(fullPath))
            {
                throw new UnauthorizedAccessException($"Path '{path}' is outside the repository.");
            }

            if (IsExcluded(fullPath))
            {
                throw new UnauthorizedAccessException($"Path '{path}' is in an excluded directory.");
            }

            string current = Root;
            foreach (string segment in Path.GetRelativePath(Root, fullPath).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".")
                {
                    continue;
                }

                current = Path.Combine(current, segment);
                FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (!info.Exists)
                {
                    break;
                }

                if (IsEscapingLink(info))
                {
                    throw new UnauthorizedAccessException($"Path '{path}' goes through a link that leads outside the repository.");
                }
            }

            return fullPath;
        }

        /// <summary>Returns the path relative to the root, with forward slashes.</summary>
        public string ToRelative(string fullPath)
        {
            string relative = Path.GetRelativePath(Root, fullPath);
            return relative == "." ? "." : relative.Replace(Path.DirectorySeparatorChar, '/');
        }

        /// <summary>Lists the accessible entries of a directory, sorted by name.</summary>
        public IEnumerable<FileSystemInfo> ListDirectory(string fullPath)
        {
            return new DirectoryInfo(fullPath)
                .EnumerateFileSystemInfos()
                .Where(IsVisible)
                .OrderBy(e => e is DirectoryInfo ? 0 : 1)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Recursively enumerates accessible files. Directory links are not followed.</summary>
        public IEnumerable<string> EnumerateFiles(string fullDirectoryPath)
        {
            Stack<DirectoryInfo> pending = new();
            pending.Push(new DirectoryInfo(fullDirectoryPath));
            while (pending.Count > 0)
            {
                DirectoryInfo directory = pending.Pop();
                List<FileSystemInfo> entries;
                try
                {
                    entries = [.. ListDirectory(directory.FullName)];
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    continue;
                }

                foreach (FileSystemInfo entry in entries.OfType<FileInfo>())
                {
                    yield return entry.FullName;
                }

                foreach (DirectoryInfo child in entries.OfType<DirectoryInfo>().Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint)).Reverse())
                {
                    pending.Push(child);
                }
            }
        }

        private bool IsVisible(FileSystemInfo entry)
        {
            if (entry is DirectoryInfo && _excludedDirectories.Contains(entry.Name))
            {
                return false;
            }

            return !IsEscapingLink(entry);
        }

        private bool IsInsideRoot(string fullPath)
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(fullPath), Root, PathComparison)
                || fullPath.StartsWith(_rootWithSeparator, PathComparison);
        }

        private bool IsExcluded(string fullPath)
        {
            string relative = Path.GetRelativePath(Root, fullPath);
            string[] segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            // The last segment is only checked when it is a directory: a file may share an excluded directory's name.
            for (int i = 0; i < segments.Length; i++)
            {
                bool isDirectory = i < segments.Length - 1 || Directory.Exists(fullPath);
                if (isDirectory && _excludedDirectories.Contains(segments[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsEscapingLink(FileSystemInfo info)
        {
            if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            FileSystemInfo? target;
            try
            {
                target = info.ResolveLinkTarget(returnFinalTarget: true);
            }
            catch (IOException)
            {
                return true;
            }

            // Null means a reparse point that is not a link (e.g. cloud placeholder): it stays where it is.
            return target is not null && !IsInsideRoot(Path.GetFullPath(target.FullName));
        }
    }
}
