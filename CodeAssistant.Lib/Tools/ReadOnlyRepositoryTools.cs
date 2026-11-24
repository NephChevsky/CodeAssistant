using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using CodeAssistant.Lib.Planning;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileSystemGlobbing;

namespace CodeAssistant.Lib.Tools
{
    /// <summary>Read-only file system tools. Every path goes through a <see cref="RepositorySandbox"/>.</summary>
    public sealed class ReadOnlyRepositoryTools
    {
        public const string ListDirName = "list_dir";
        public const string ReadFileName = "read_file";
        public const string GrepSearchName = "grep_search";
        public const string FileSearchName = "file_search";

        private const int MaxListEntries = 1000;
        private const int MaxLineLength = 300;
        private const int BinaryProbeBytes = 8000;
        private const string SearchDirectoryDescription = "Directory to search recursively, absolute or relative to the repository root. Defaults to the repository root; required when there is no repository root.";

        private readonly RepositorySandbox _sandbox;
        private readonly PlannerOptions _options;
        private readonly Action<string, string>? _onInvoked;

        public ReadOnlyRepositoryTools(RepositorySandbox sandbox, PlannerOptions options, Action<string, string>? onInvoked = null)
        {
            _sandbox = sandbox;
            _options = options;
            _onInvoked = onInvoked;
        }

        public static IReadOnlyList<AIFunction> Create(RepositorySandbox sandbox, PlannerOptions options, Action<string, string>? onInvoked = null)
        {
            ReadOnlyRepositoryTools tools = new(sandbox, options, onInvoked);
            return
            [
                AIFunctionFactory.Create(tools.ListDir, ListDirName, "List the files and directories of a directory. Directories end with '/'."),
                AIFunctionFactory.Create(tools.ReadFile, ReadFileName, "Read a range of lines of a text file. Lines are prefixed with their 1-based number."),
                AIFunctionFactory.Create(tools.GrepSearch, GrepSearchName, "Search the text files of a directory tree for a case-insensitive text or regular expression. Returns 'path:line: text' matches."),
                AIFunctionFactory.Create(tools.FileSearch, FileSearchName, "Find the files of a directory tree whose path relative to that directory matches a glob pattern, e.g. '**/*.cs' or 'src/**/Program.cs'."),
            ];
        }

        public string ListDir([Description("Directory path, absolute or relative to the repository root. Defaults to the repository root, or to the list of drives when there is no repository root.")] string path = ".")
        {
            _onInvoked?.Invoke(ListDirName, path);
            return Run(() =>
            {
                if (_sandbox.Root is null && (string.IsNullOrWhiteSpace(path) || path.Trim() == "."))
                {
                    return string.Join(Environment.NewLine, DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName));
                }

                string fullPath = _sandbox.Resolve(path);
                if (!Directory.Exists(fullPath))
                {
                    return $"Error: '{path}' is not a directory.";
                }

                List<FileSystemInfo> entries = [.. _sandbox.ListDirectory(fullPath).Take(MaxListEntries + 1)];
                if (entries.Count == 0)
                {
                    return "(empty directory)";
                }

                StringBuilder result = new();
                foreach (FileSystemInfo entry in entries.Take(MaxListEntries))
                {
                    result.AppendLine(entry is DirectoryInfo ? entry.Name + "/" : entry.Name);
                }

                if (entries.Count > MaxListEntries)
                {
                    result.AppendLine($"... (truncated at {MaxListEntries} entries)");
                }

                return result.ToString();
            });
        }

        public string ReadFile(
            [Description("File path, absolute or relative to the repository root.")] string path,
            [Description("First line to read, 1-based.")] int startLine = 1,
            [Description("Last line to read, inclusive. Defaults to the end of the file, within the line limit.")] int? endLine = null)
        {
            _onInvoked?.Invoke(ReadFileName, endLine is null ? $"{path} from line {startLine}" : $"{path} lines {startLine}-{endLine}");
            return Run(() =>
            {
                string fullPath = _sandbox.Resolve(path);
                FileInfo file = new(fullPath);
                if (!file.Exists)
                {
                    return $"Error: file '{path}' does not exist.";
                }

                if (file.Length > _options.MaxFileBytes)
                {
                    return $"Error: file '{path}' is too large ({file.Length} bytes, limit {_options.MaxFileBytes}).";
                }

                if (IsBinary(fullPath))
                {
                    return $"Error: file '{path}' is binary.";
                }

                string[] lines = File.ReadAllLines(fullPath);
                int start = Math.Max(1, startLine);
                if (start > lines.Length)
                {
                    return $"Error: start line {start} is past the end of the file ({lines.Length} lines).";
                }

                int requestedEnd = Math.Min(endLine ?? lines.Length, lines.Length);
                int end = Math.Min(requestedEnd, start + _options.MaxReadLines - 1);
                StringBuilder result = new();
                result.AppendLine($"{_sandbox.ToRelative(fullPath)} (lines {start}-{end} of {lines.Length})");
                for (int i = start; i <= end; i++)
                {
                    result.Append(i).Append(": ").AppendLine(lines[i - 1]);
                }

                if (end < requestedEnd)
                {
                    result.AppendLine($"... (truncated at {_options.MaxReadLines} lines, continue from line {end + 1})");
                }

                return result.ToString();
            });
        }

        public string GrepSearch(
            [Description("Text or regular expression to search for (case-insensitive).")] string query,
            [Description("True when query is a regular expression.")] bool isRegex = false,
            [Description("Optional glob restricting the searched files, relative to the searched directory, e.g. '**/*.cs'.")] string? includePattern = null,
            [Description(SearchDirectoryDescription)] string directory = ".")
        {
            _onInvoked?.Invoke(GrepSearchName, (includePattern is null ? query : $"{query} in {includePattern}") + $" ({directory})");
            return Run(() =>
            {
                if (string.IsNullOrEmpty(query))
                {
                    return "Error: query is empty.";
                }

                string searchRoot = ResolveSearchDirectory(directory);
                Regex regex = new(isRegex ? query : Regex.Escape(query), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, _options.RegexTimeout);
                Matcher? matcher = CreateMatcher(includePattern);
                StringBuilder result = new();
                int count = 0;
                foreach (string fullPath in _sandbox.EnumerateFiles(searchRoot))
                {
                    if ((matcher is not null && !matcher.Match(ToSearchRelative(searchRoot, fullPath)).HasMatches) || new FileInfo(fullPath).Length > _options.MaxFileBytes || IsBinary(fullPath))
                    {
                        continue;
                    }

                    string relative = _sandbox.ToRelative(fullPath);
                    int lineNumber = 0;
                    foreach (string line in File.ReadLines(fullPath))
                    {
                        lineNumber++;
                        if (!regex.IsMatch(line))
                        {
                            continue;
                        }

                        if (count == _options.MaxSearchResults)
                        {
                            result.AppendLine($"... (truncated at {_options.MaxSearchResults} matches)");
                            return result.ToString();
                        }

                        string text = line.Trim();
                        result.Append(relative).Append(':').Append(lineNumber).Append(": ").AppendLine(text.Length > MaxLineLength ? text[..MaxLineLength] + "..." : text);
                        count++;
                    }
                }

                return count == 0 ? "No matches." : result.ToString();
            });
        }

        public string FileSearch(
            [Description("Glob pattern matched against paths relative to the searched directory, e.g. '**/*.csproj'.")] string pattern,
            [Description(SearchDirectoryDescription)] string directory = ".")
        {
            _onInvoked?.Invoke(FileSearchName, $"{pattern} ({directory})");
            return Run(() =>
            {
                Matcher matcher = CreateMatcher(pattern) ?? throw new ArgumentException("Pattern is empty.");
                string searchRoot = ResolveSearchDirectory(directory);
                List<string> matches = [.. _sandbox.EnumerateFiles(searchRoot)
                    .Where(p => matcher.Match(ToSearchRelative(searchRoot, p)).HasMatches)
                    .Select(_sandbox.ToRelative)
                    .Take(_options.MaxSearchResults + 1)];
                if (matches.Count == 0)
                {
                    return "No files found.";
                }

                StringBuilder result = new();
                foreach (string match in matches.Take(_options.MaxSearchResults))
                {
                    result.AppendLine(match);
                }

                if (matches.Count > _options.MaxSearchResults)
                {
                    result.AppendLine($"... (truncated at {_options.MaxSearchResults} files)");
                }

                return result.ToString();
            });
        }

        private string ResolveSearchDirectory(string directory)
        {
            string fullPath = _sandbox.Resolve(directory);
            return Directory.Exists(fullPath) ? fullPath : throw new ArgumentException($"'{directory}' is not a directory.");
        }

        private static string ToSearchRelative(string searchRoot, string fullPath)
        {
            return Path.GetRelativePath(searchRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        }

        private static Matcher? CreateMatcher(string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return null;
            }

            Matcher matcher = new(OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            matcher.AddInclude(pattern.Trim().TrimStart('/'));
            return matcher;
        }

        private static bool IsBinary(string fullPath)
        {
            using FileStream stream = File.OpenRead(fullPath);
            Span<byte> buffer = stackalloc byte[BinaryProbeBytes];
            int read = stream.Read(buffer);
            return buffer[..read].Contains((byte)0);
        }

        private static string Run(Func<string> action)
        {
            try
            {
                return action();
            }
            catch (RegexMatchTimeoutException)
            {
                return "Error: the regular expression timed out. Use a simpler pattern.";
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
            {
                return "Error: " + ex.Message;
            }
        }
    }
}
