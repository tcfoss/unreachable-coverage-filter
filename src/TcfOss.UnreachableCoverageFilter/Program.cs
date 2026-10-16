using System.CommandLine;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using TcfOss.UnreachableCoverageFilter;

var inputs = new Argument<string[]>("input") { Description = "Input OpenCover file paths or glob patterns; repeatable.", Arity = ArgumentArity.OneOrMore };
var outputDirectory = new Option<DirectoryInfo?>("--output-directory") { Description = "Directory for filtered reports; required unless --dry-run is used." };
var collisionAction = new Option<string>("--on-collision") { Description = "Action when an output exists: rename (default), overwrite, skip, or error." };
var patterns = new Option<string[]>("--exclude-regex") { Description = "Regex matching a defensive guard source line; repeatable.", Required = true, Arity = ArgumentArity.OneOrMore };
var assemblies = new Option<string[]>("--assembly-directory") { Description = "Directory containing matching assemblies and portable PDBs; repeatable." };
var sources = new Option<string[]>("--source-root") { Description = "Root for relocated source files; repeatable." };
var dryRun = new Option<bool>("--dry-run") { Description = "Print exclusions without writing a report." };
var logLevel = new Option<string?>("--log-level")
{
    Description = "Minimum log level: Trace, Debug, Information, Warning, Error, Critical, or None. Defaults to Information."
};

var command = new RootCommand("Exclude explicitly matched uncovered guards from OpenCover line and branch coverage.")
{
    inputs, outputDirectory, collisionAction, patterns, assemblies, sources, dryRun, logLevel
};

command.SetAction(result =>
{
    try
    {
        FileInfo[] inputFiles = InputFileResolver.Resolve(result.GetValue(inputs)!, Environment.CurrentDirectory);
        DirectoryInfo? outputDirectoryValue = result.GetValue(outputDirectory);
        bool preview = result.GetValue(dryRun);
        if (!preview && outputDirectoryValue == null)
        {
            throw new ArgumentException("--output-directory is required unless --dry-run is specified.");
        }

        OutputCollisionAction action = OutputCollisionAction.Rename;
        string? actionValue = result.GetValue(collisionAction);
        if (actionValue != null && (!Enum.TryParse(actionValue, ignoreCase: true, out action) || !Enum.IsDefined(action)))
        {
            throw new ArgumentException($"Unknown output collision action '{actionValue}'. Use rename, overwrite, skip, or error.");
        }

        string? fullOutputDirectory = outputDirectoryValue == null ? null : Path.GetFullPath(outputDirectoryValue.FullName);
        StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!preview)
        {
            foreach (FileInfo inputFile in inputFiles)
            {
                string outputPath = Path.Combine(fullOutputDirectory!, inputFile.Name);
                if (inputFiles.Any(candidate => string.Equals(candidate.FullName, Path.GetFullPath(outputPath), pathComparison)))
                {
                    throw new ArgumentException($"Output path would overwrite an input report: {outputPath}");
                }
            }
        }

        var options = new FilterOptions(result.GetValue(patterns)!, result.GetValue(assemblies) ?? [], result.GetValue(sources) ?? []);
        LogLevel minimumLogLevel = ParseLogLevel(result.GetValue(logLevel));
        ILoggerFactory loggerFactory = LoggingRegistration.CreateLoggerFactory(minimumLogLevel, out string logFilePath);
        using (loggerFactory)
        {
            Console.WriteLine($"Writing log to {logFilePath}");
            var processor = new ReportProcessor(options, loggerFactory.CreateLogger("ReportProcessor"));
            var writer = new ReportFileWriter(loggerFactory.CreateLogger("ReportFileWriter"));
            foreach (FileInfo inputFile in inputFiles)
            {
                XDocument report = processor.Process(inputFile);
                if (!preview)
                {
                    string outputPath = Path.Combine(fullOutputDirectory!, inputFile.Name);
                    writer.Write(report, outputPath, action);
                }
            }
        }
        return 0;
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
        or InvalidDataException or XmlException or RegexMatchTimeoutException or FormatException or OverflowException)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
});

static LogLevel ParseLogLevel(string? value)
{
    if (value == null)
    {
        return LogLevel.Information;
    }
    if (Enum.TryParse(value, ignoreCase: true, out LogLevel logLevel) && Enum.IsDefined(logLevel))
    {
        return logLevel;
    }
    throw new ArgumentException($"Unknown log level '{value}'. Use Trace, Debug, Information, Warning, Error, Critical, or None.");
}

return command.Parse(args).Invoke();
