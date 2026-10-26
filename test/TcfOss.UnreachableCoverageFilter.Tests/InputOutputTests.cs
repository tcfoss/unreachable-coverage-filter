using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TcfOss.UnreachableCoverageFilter.Tests;

public sealed class InputOutputTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "UnreachableCoverageFilter-IO-" + Guid.NewGuid().ToString("N"));

    public InputOutputTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void ResolvesLiteralRelativeAndAbsoluteGlobInputsOnce()
    {
        string first = Path.Combine(_directory, "first.xml");
        string nestedDirectory = Path.Combine(_directory, "nested");
        Directory.CreateDirectory(nestedDirectory);
        string second = Path.Combine(nestedDirectory, "second.xml");
        File.WriteAllText(first, "<coverage />");
        File.WriteAllText(second, "<coverage />");
        File.WriteAllText(Path.Combine(nestedDirectory, "ignored.txt"), "ignored");

        FileInfo[] files = InputFileResolver.Resolve(
            ["first.xml", Path.Combine(_directory, "**", "*.xml")],
            _directory);

        Assert.Equal([first, second], files.Select(file => file.FullName));
    }

    [Fact]
    public void ThrowsWhenInputFileIsMissing()
    {
        Assert.Throws<FileNotFoundException>(() => InputFileResolver.Resolve(["missing.xml"], _directory));
    }

    [Fact]
    public void ThrowsWhenGlobMatchesNoFiles()
    {
        Assert.Throws<FileNotFoundException>(() => InputFileResolver.Resolve(["**/*.xml"], _directory));
    }

    [Fact]
    public void RenameCollisionCreatesIncrementingBackup()
    {
        string output = Path.Combine(_directory, "report.xml");
        File.WriteAllText(output, "<old />");
        File.WriteAllText(output + ".bak.1", "<older />");

        bool written = Writer().Write(new XDocument(new XElement("new")), output, OutputCollisionAction.Rename);

        Assert.True(written);
        Assert.Equal("new", XDocument.Load(output).Root!.Name.LocalName);
        Assert.Equal("old", XDocument.Load(output + ".bak.2").Root!.Name.LocalName);
    }

    [Fact]
    public void OverwriteCollisionReplacesExistingFile()
    {
        string output = ExistingOutput();

        bool written = Writer().Write(new XDocument(new XElement("new")), output, OutputCollisionAction.Overwrite);

        Assert.True(written);
        Assert.Equal("new", XDocument.Load(output).Root!.Name.LocalName);
    }

    [Fact]
    public void SkipCollisionPreservesExistingFile()
    {
        string output = ExistingOutput();

        bool written = Writer().Write(new XDocument(new XElement("new")), output, OutputCollisionAction.Skip);

        Assert.False(written);
        Assert.Equal("<old />", File.ReadAllText(output));
    }

    [Fact]
    public void ErrorCollisionPreservesExistingFile()
    {
        string output = ExistingOutput();

        Assert.Throws<IOException>(() => Writer().Write(new XDocument(new XElement("new")), output, OutputCollisionAction.Error));
        Assert.Equal("<old />", File.ReadAllText(output));
    }

    [Fact]
    public void WritesLogsToSpecifiedDirectory()
    {
        string logDirectory = Path.Combine(_directory, "logs", "nested");
        string logFilePath;
        using (ILoggerFactory factory = LoggingRegistration.CreateLoggerFactory(LogLevel.Information, out logFilePath, logDirectory))
        {
            factory.CreateLogger("Test").LogInformation("Log directory test");
        }

        Assert.Equal(logDirectory, Path.GetDirectoryName(logFilePath));
        Assert.Contains("Log directory test", File.ReadAllText(logFilePath));
    }

    [Fact]
    public void DefaultsLogsToCurrentWorkingDirectory()
    {
        string logFilePath;
        using (ILoggerFactory factory = LoggingRegistration.CreateLoggerFactory(LogLevel.Information, out logFilePath))
        {
            factory.CreateLogger("Test").LogInformation("Default log directory test");
        }

        try
        {
            Assert.Equal(Environment.CurrentDirectory, Path.GetDirectoryName(logFilePath));
            Assert.Contains("Default log directory test", File.ReadAllText(logFilePath));
        }
        finally
        {
            File.Delete(logFilePath);
        }
    }

    private string ExistingOutput()
    {
        string output = Path.Combine(_directory, "report.xml");
        File.WriteAllText(output, "<old />");
        return output;
    }

    private static ReportFileWriter Writer() => new(NullLogger.Instance);

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
