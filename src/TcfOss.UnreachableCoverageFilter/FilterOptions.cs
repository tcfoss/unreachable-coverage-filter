using System.Text.RegularExpressions;

namespace TcfOss.UnreachableCoverageFilter;

public sealed record FilterOptions(Regex[] Patterns, string[] AssemblyDirectories, string[] SourceRoots)
{
    public FilterOptions(string[] patterns, string[] assemblyDirectories, string[] sourceRoots)
        : this(Array.ConvertAll(patterns, pattern => new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))), assemblyDirectories, sourceRoots)
    { }
};
