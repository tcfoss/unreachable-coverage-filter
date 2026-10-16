using Microsoft.Extensions.Logging;

namespace TcfOss.UnreachableCoverageFilter;

internal static partial class LoggingMessages
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "SKIP module: assembly not found or ambiguous: {AssemblyPath}")]
    internal static partial void ModuleAssemblyUnavailable(ILogger logger, string assemblyPath);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "SKIP module while loading symbols")]
    internal static partial void ModuleSymbolsUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "KEEP unmapped or unmatched branch: {Method}, IL {Offset}")]
    internal static partial void KeepUnmappedBranch(ILogger logger, string method, string? offset);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "KEEP ambiguous or visited guard: {SourcePath}:{Line}")]
    internal static partial void KeepAmbiguousGuard(ILogger logger, string sourcePath, int line);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "EXCLUDE branch: {SourcePath}:{Line}, {Method}, IL {Offset}")]
    internal static partial void ExcludeBranch(ILogger logger, string sourcePath, int line, string method, string? offset);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "EXCLUDE sequence: {SourcePath}:{Line}, {Method}")]
    internal static partial void ExcludeSequence(ILogger logger, string sourcePath, int line, string method);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "SKIP source: not found or ambiguous: {SourcePath}")]
    internal static partial void SourceUnavailable(ILogger logger, string sourcePath);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "SKIP source: PDB checksum mismatch or unsupported checksum: {SourcePath}")]
    internal static partial void SourceChecksumMismatch(ILogger logger, string sourcePath);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Processing coverage report {ReportPath}")]
    internal static partial void ProcessingReport(ILogger logger, string reportPath);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Renamed existing output {OutputPath} to {BackupPath}")]
    internal static partial void RenamedOutput(ILogger logger, string outputPath, string backupPath);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning, Message = "Overwriting existing output {OutputPath}")]
    internal static partial void OverwritingOutput(ILogger logger, string outputPath);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "Output already exists; skipping {OutputPath}")]
    internal static partial void SkippingOutput(ILogger logger, string outputPath);

    [LoggerMessage(EventId = 13, Level = LogLevel.Error, Message = "Output file already exists: {OutputPath}")]
    internal static partial void OutputCollision(ILogger logger, string outputPath);

    [LoggerMessage(EventId = 14, Level = LogLevel.Information, Message = "Wrote filtered report {OutputPath}")]
    internal static partial void WroteReport(ILogger logger, string outputPath);
}
