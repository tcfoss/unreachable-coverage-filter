using System.Collections.Immutable;

namespace TcfOss.UnreachableCoverageFilter;

public sealed record SourceLocation(string Path, int StartLine, int EndLine, FileHasher? FileHasher, ImmutableArray<byte> Hash);
