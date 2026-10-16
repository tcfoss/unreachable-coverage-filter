using Microsoft.Extensions.FileSystemGlobbing;

namespace TcfOss.UnreachableCoverageFilter;

public static class InputFileResolver
{
    public static FileInfo[] Resolve(IEnumerable<string> inputs, string workingDirectory)
    {
        string baseDirectory = Path.GetFullPath(workingDirectory);
        StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var files = new HashSet<string>(pathComparer);

        foreach (string input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException("Input paths and glob patterns must not be empty.", nameof(inputs));
            }

            if (!HasGlob(input))
            {
                string fullPath = Path.GetFullPath(input, baseDirectory);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException($"Input file not found: {input}", fullPath);
                }
                files.Add(fullPath);
                continue;
            }

            (string root, string pattern) = GetGlobRoot(input, baseDirectory);
            if (!Directory.Exists(root))
            {
                throw new FileNotFoundException($"Input glob matched no files: {input}");
            }

            string[] matches = [.. new Matcher().AddInclude(pattern).GetResultsInFullPath(root)];
            if (matches.Length == 0)
            {
                throw new FileNotFoundException($"Input glob matched no files: {input}");
            }
            files.UnionWith(matches.Select(Path.GetFullPath));
        }

        if (files.Count == 0)
        {
            throw new ArgumentException("At least one input path or glob pattern is required.", nameof(inputs));
        }

        return [.. files.OrderBy(path => path, pathComparer).Select(path => new FileInfo(path))];
    }

    private static bool HasGlob(string input) => input.Contains('*') || input.Contains('?');

    private static (string Root, string Pattern) GetGlobRoot(string input, string baseDirectory)
    {
        string normalized = input.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string root;
        string remainder;
        if (Path.IsPathRooted(normalized))
        {
            root = Path.GetPathRoot(normalized)!;
            remainder = normalized[root.Length..];
        }
        else
        {
            root = baseDirectory;
            remainder = normalized;
        }

        string[] segments = remainder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        int globSegment = Array.FindIndex(segments, HasGlob);
        if (globSegment < 0)
        {
            throw new ArgumentException($"Could not find a glob pattern in '{input}'.", nameof(input));
        }

        string searchRoot = Path.GetFullPath(Path.Combine([root, .. segments[..globSegment]]));
        string pattern = Path.Combine(segments[globSegment..]);
        return (searchRoot, pattern);
    }
}
