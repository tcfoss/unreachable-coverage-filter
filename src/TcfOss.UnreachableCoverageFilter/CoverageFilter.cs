using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace TcfOss.UnreachableCoverageFilter;


public sealed class CoverageFilter(FilterOptions options, ILogger logger)
{
    private readonly Dictionary<string, string[]> _sources = new(StringComparer.Ordinal);

    public void Apply(XDocument report, string reportDirectory)
    {
        if (report.Root?.Name != "CoverageSession")
        {
            throw new InvalidDataException("Expected an OpenCover CoverageSession document.");
        }

        foreach (XElement module in report.Root.Elements("Modules").Elements("Module"))
        {
            string reportedAssembly = module.Element("ModulePath")?.Value ?? "";
            string? assemblyPath = FindAssembly(reportedAssembly, reportDirectory);
            if (assemblyPath == null)
            {
                LoggingMessages.ModuleAssemblyUnavailable(logger, reportedAssembly);
                continue;
            }

            SymbolMap symbols;
            try
            {
                symbols = new SymbolMap(assemblyPath);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or BadImageFormatException or InvalidOperationException)
            {
                LoggingMessages.ModuleSymbolsUnavailable(logger, exception);
                continue;
            }

            using (symbols)
            {
                Dictionary<string, string> files = module
                    .Elements("Files")
                    .Elements("File")
                    .ToDictionary(
                        file => Required(file, "uid"),
                        file => Required(file, "fullPath"), StringComparer.Ordinal);

                foreach (XElement method in module.Descendants("Method"))
                {
                    FilterMethod(method, files, symbols);
                }
            }
        }

        UpdateSummaries(report.Root);
    }

    private void FilterMethod(XElement method, Dictionary<string, string> files, SymbolMap symbols)
    {
        string name = method.Element("Name")?.Value ?? "";

        XElement[] sequences = [.. method.Elements("SequencePoints").Elements("SequencePoint")];
        foreach (XElement branch in method.Elements("BranchPoints").Elements("BranchPoint").ToArray())
        {
            if (branch.VisitCount() != 0)
            {
                continue;
            }

            SourceLocation? location = symbols.Resolve(name, Number(branch, "offsetend"));
            if (location == null || !Matches(location))
            {
                LoggingMessages.KeepUnmappedBranch(logger, name, branch.Attribute("offsetend")?.Value);
                continue;
            }

            XElement[] targets = [.. sequences.Where(sequence => SameFile(sequence, method, files, location.Path)
                && sequence.StartLine() <= location.EndLine && sequence.EndLine() >= location.StartLine)];
            if (targets.Length == 0 || targets.Any(sequence => sequence.VisitCount() != 0))
            {
                LoggingMessages.KeepAmbiguousGuard(logger, location.Path, location.StartLine);
                continue;
            }
            LoggingMessages.ExcludeBranch(logger, location.Path, location.StartLine, name, branch.Attribute("offsetend")?.Value);
            branch.Remove();
        }
        foreach (XElement sequence in sequences)
        {
            if (sequence.VisitCount() != 0)
            {
                continue;
            }
            SourceLocation? location = symbols.ResolveLine(name, sequence.StartLine());
            if (location != null && SameFile(sequence, method, files, location.Path) && Matches(location))
            {
                LoggingMessages.ExcludeSequence(logger, location.Path, location.StartLine, name);
                sequence.Remove();
            }
        }
        XElement[] remaining = [.. method.Elements("BranchPoints").Elements("BranchPoint")];
        foreach (XElement sequence in method.Elements("SequencePoints").Elements("SequencePoint"))
        {
            XElement[] branches = [.. remaining.Where(branch => branch.StartLine() == sequence.StartLine()
                && FileId(branch, method) == FileId(sequence, method))];
            sequence.SetAttributeValue("bec", branches.Length);
            sequence.SetAttributeValue("bev", branches.Count(branch => branch.VisitCount() > 0));
        }
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "SHA-1 is required to verify existing portable PDB source checksums, not for security decisions.")]
    private bool Matches(SourceLocation location)
    {
        string key = location.Path + ":" + Convert.ToHexString(location.Hash.AsSpan());

        if (!_sources.TryGetValue(key, out string[]? lines))
        {
            string? path = FindSource(location.Path);
            if (path == null)
            {
                LoggingMessages.SourceUnavailable(logger, location.Path);
                _sources[key] = [];
                return false;
            }

            byte[] bytes = File.ReadAllBytes(path);
            byte[]? hash = location.FileHasher?.HashData(bytes);

            if (hash == null || !hash.AsSpan().SequenceEqual(location.Hash.AsSpan()))
            {
                LoggingMessages.SourceChecksumMismatch(logger, path);
                _sources[key] = [];
                return false;
            }

            using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);

            var sourceLines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                sourceLines.Add(line);
            }

            lines = [.. sourceLines];
            _sources[key] = lines;

        }

        if (location.StartLine < 1 || location.EndLine > lines.Length)
        {
            return false;
        }

        return options.Patterns.Any(pattern => Enumerable.Range(location.StartLine - 1, location.EndLine - location.StartLine + 1)
            .Any(index => pattern.IsMatch(lines[index])));
    }

    private string? FindAssembly(string reportedPath, string reportDirectory)
    {
        string normalized = reportedPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (File.Exists(normalized))
        {
            return Path.GetFullPath(normalized);
        }
        return UniqueFile(options.AssemblyDirectories.Select(directory => Path.Combine(directory, Path.GetFileName(normalized)))
            .Append(Path.Combine(reportDirectory, normalized)));
    }

    private string? FindSource(string reportedPath)
    {
        string normalized = reportedPath.Replace('\\', '/');
        if (File.Exists(normalized))
        {
            return Path.GetFullPath(normalized);
        }
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return UniqueFile(options.SourceRoots.SelectMany(root => Enumerable.Range(0, parts.Length)
            .Select(index => Path.Combine(root, Path.Combine(parts[index..])))));
    }

    private static string? UniqueFile(IEnumerable<string> paths)
    {
        string[] candidates = [.. paths.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.Ordinal)];
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static bool SameFile(XElement point, XElement method, Dictionary<string, string> files, string path)
    {
        return files.TryGetValue(FileId(point, method), out string? reported)
               && string.Equals(reported.Replace('\\', '/'), path.Replace('\\', '/'), StringComparison.Ordinal);
    }

    private static string FileId(XElement point, XElement method)
    {
        return (string?)point.Attribute("fileid")
            ?? (string?)method.Element("FileRef")?.Attribute("uid")
            ?? "";
    }

    private static string Required(XElement element, string attribute)
    {
        return (string?)element.Attribute(attribute)
            ?? throw new InvalidDataException($"Missing {attribute} on {element.Name}.");
    }

    private static int Number(XElement element, string attribute)
    {
        return int.Parse(Required(element, attribute), CultureInfo.InvariantCulture);
    }

    private static void UpdateSummaries(XElement element)
    {
        foreach (XElement child in element.Elements().Where(child => child.Name != "Summary").ToArray())
        {
            UpdateSummaries(child);
        }

        XElement? summary = element.Element("Summary");
        if (summary == null)
        {
            return;
        }

        XElement[] sequences = [.. element.Descendants("SequencePoint")];
        XElement[] branches = [.. element.Descendants("BranchPoint")];

        int visitedSequences = sequences.Count(point => point.VisitCount() > 0);
        int visitedBranches = branches.Count(point => point.VisitCount() > 0);

        summary.SetAttributeValue("numSequencePoints", sequences.Length);
        summary.SetAttributeValue("visitedSequencePoints", visitedSequences);
        summary.SetAttributeValue("numBranchPoints", branches.Length);
        summary.SetAttributeValue("visitedBranchPoints", visitedBranches);

        string lineRate = Percentage(visitedSequences, sequences.Length);
        summary.SetAttributeValue("sequenceCoverage", lineRate);
        string branchRate = Percentage(visitedBranches, branches.Length);
        summary.SetAttributeValue("branchCoverage", branchRate);

        if (element.Name == "Method")
        {
            element.SetAttributeValue("sequenceCoverage", lineRate);
            element.SetAttributeValue("branchCoverage", branchRate);
        }
    }

    private static string Percentage(int covered, int total)
    {
        return (total == 0 ? 100m : 100m * covered / total).ToString("0.####", CultureInfo.InvariantCulture);
    }
}
