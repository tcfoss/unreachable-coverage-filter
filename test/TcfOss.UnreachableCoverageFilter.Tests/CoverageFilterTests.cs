using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;


namespace TcfOss.UnreachableCoverageFilter.Tests;

public sealed class CoverageFilterTests : IDisposable
{
    private const string MethodName = "System.Int32 Fixture::Choose(System.Int32)";
    private const string GuardPattern = @"throw new (?:System\.)?Diagnostics\.UnreachableException\(";
    private const string Source = """
        public static class Fixture
        {
            public static int Choose(int value)
            {
                return value switch
                {
                    0 => 10,
                    1 => 20,
                    _ => throw new System.Diagnostics.UnreachableException()
                };
            }

            public static T Echo<T>(T value) => value;
            public static int Other(int value)
            {
                return value switch
                {
                    0 => 10,
                    _ => throw new System.ArgumentException()
                };
            }

            public static int Statement(int value)
            {
                switch (value)
                {
                    case 0:
                        return 10;
                    default:
                        throw new System.Diagnostics.UnreachableException();
                }
            }
        }
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "UnreachableCoverageFilter-" + Guid.NewGuid().ToString("N"));
    private readonly string _sourcePath;
    private readonly string _assemblyPath;
    private readonly int _guardLine;
    private readonly int _guardOffset;
    private readonly int _coveredOffset;
    private readonly int _switchLine;

    public CoverageFilterTests()
    {
        Directory.CreateDirectory(_directory);
        _sourcePath = Path.Combine(_directory, "Fixture.cs");
        _assemblyPath = Path.Combine(_directory, "Fixture.dll");

        File.WriteAllText(_sourcePath, Source, new UTF8Encoding(false));

        _guardLine = LineOf("_ => throw new System.Diagnostics.UnreachableException()");
        _switchLine = LineOf("return value switch");

        SyntaxTree syntax = CSharpSyntaxTree.ParseText(SourceText.From(Source, new UTF8Encoding(false)), path: _sourcePath);

        IEnumerable<MetadataReference> references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        CSharpCompilation compilation = CSharpCompilation.Create("Fixture", [syntax], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));

        using (FileStream assembly = File.Create(_assemblyPath))
        using (FileStream pdb = File.Create(Path.ChangeExtension(_assemblyPath, ".pdb")))
        {
            EmitResult result = compilation.Emit(assembly, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        }

        using var symbols = new SymbolMap(_assemblyPath);
        using FileStream stream = File.OpenRead(_assemblyPath);
        using var pe = new PEReader(stream);

        MetadataReader metadata = pe.GetMetadataReader();
        MethodDefinition method = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition).First(definition => metadata.GetString(definition.Name) == "Choose");
        int length = pe.GetMethodBody(method.RelativeVirtualAddress).GetILContent().Length;
        _guardOffset = Enumerable.Range(0, length).First(offset => symbols.Resolve(MethodName, offset)?.StartLine == _guardLine);
        _coveredOffset = Enumerable.Range(0, length).First(offset => symbols.Resolve(MethodName, offset)?.StartLine == LineOf("0 => 10,"));
    }

    [Fact]
    public void RemovesOnlyUnvisitedMatchingGuardAndRecalculatesSummaries()
    {
        XDocument report = Report();
        var log = new TestLogger();
        Filter(report, log);
        Assert.Single(report.Descendants("BranchPoint"));
        XElement sequence = Assert.Single(report.Descendants("SequencePoint"));

        foreach (XElement summary in report.Descendants("Summary"))
        {
            Assert.Equal("1", (string?)summary.Attribute("numBranchPoints"));
            Assert.Equal("1", (string?)summary.Attribute("numSequencePoints"));
            Assert.Equal("100", (string?)summary.Attribute("branchCoverage"));
            Assert.Equal("100", (string?)summary.Attribute("sequenceCoverage"));
        }

        Assert.Equal("1", (string?)sequence.Attribute("bec"));
        Assert.Contains("EXCLUDE branch", log.ToString(), StringComparison.Ordinal);
        Assert.Contains("EXCLUDE sequence", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsVisitedGuardAndBranches()
    {
        XDocument report = Report(guardHits: 1);
        Filter(report);
        Assert.Equal(2, report.Descendants("BranchPoint").Count());
        Assert.Equal(2, report.Descendants("SequencePoint").Count());
    }

    [Fact]
    public void KeepsNonmatchingGuard()
    {
        XDocument report = Report();
        new CoverageFilter(new FilterOptions(["NotImplementedException"], [], []), NullLogger.Instance).Apply(report, _directory);
        Assert.Equal(2, report.Descendants("BranchPoint").Count());
        Assert.Equal(2, report.Descendants("SequencePoint").Count());
    }

    [Fact]
    public void KeepsUnknownBranchDestinationButCanExcludeGuardLine()
    {
        XDocument report = Report();
        report.Descendants("BranchPoint").Last().SetAttributeValue("offsetend", int.MaxValue);
        Filter(report);
        Assert.Equal(2, report.Descendants("BranchPoint").Count());
        Assert.Single(report.Descendants("SequencePoint"));
        Assert.Equal("50", (string?)report.Root!.Element("Summary")!.Attribute("branchCoverage"));
    }

    [Fact]
    public void KeepsPointsWhenSourceDoesNotMatchPdb()
    {
        File.AppendAllText(_sourcePath, Environment.NewLine);
        XDocument report = Report();
        var log = new TestLogger();
        Filter(report, log);
        Assert.Equal(2, report.Descendants("BranchPoint").Count());
        Assert.Equal(2, report.Descendants("SequencePoint").Count());
        Assert.Contains("checksum mismatch", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsPointsAndContinuesWhenPdbHashAlgorithmIsUnknown()
    {
        string pdbPath = Path.ChangeExtension(_assemblyPath, ".pdb");
        byte[] pdb = File.ReadAllBytes(pdbPath);
        byte[]? knownAlgorithm = null;
        int algorithmOffset = -1;
        foreach (Guid algorithm in new[]
        {
            new Guid("8829d00f-11b8-4213-878b-770e8597ac16"),
            new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460")
        })
        {
            byte[] candidate = algorithm.ToByteArray();
            algorithmOffset = pdb.AsSpan().IndexOf(candidate);
            if (algorithmOffset >= 0)
            {
                knownAlgorithm = candidate;
                break;
            }
        }
        Assert.NotNull(knownAlgorithm);
        Assert.Equal(-1, pdb.AsSpan(algorithmOffset + knownAlgorithm.Length).IndexOf(knownAlgorithm));
        new Guid("11111111-2222-3333-4444-555555555555").ToByteArray().CopyTo(pdb, algorithmOffset);
        File.WriteAllBytes(pdbPath, pdb);

        XDocument report = Report();
        var log = new TestLogger();
        Filter(report, log);

        Assert.Equal(2, report.Descendants("BranchPoint").Count());
        Assert.Equal(2, report.Descendants("SequencePoint").Count());
        Assert.Contains("unsupported checksum", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsModuleWhenPdbIsMissing()
    {
        File.Delete(Path.ChangeExtension(_assemblyPath, ".pdb"));
        XDocument report = Report();
        var log = new TestLogger();
        Filter(report, log);
        Assert.Equal(2, report.Descendants("BranchPoint").Count());
        Assert.Contains("SKIP module", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LocatesRelocatedAssemblyAndSource()
    {
        XDocument report = Report();
        report.Descendants("ModulePath").Single().Value = "/missing-build/Fixture.dll";
        string relocated = Path.Combine(_directory, "relocated");
        Directory.CreateDirectory(relocated);
        File.Move(_sourcePath, Path.Combine(relocated, "Fixture.cs"));
        new CoverageFilter(new FilterOptions([GuardPattern], [_directory], [relocated]), NullLogger.Instance).Apply(report, _directory);
        Assert.Single(report.Descendants("BranchPoint"));
        Assert.Single(report.Descendants("SequencePoint"));
    }

    [Fact]
    public void ResolvesGenericMethodSignature()
    {
        using var symbols = new SymbolMap(_assemblyPath);
        Assert.NotNull(symbols.ResolveLine("T Fixture::Echo(T)", LineOf("public static T Echo")));
    }

    [Fact]
    public void ExcludesStatementSwitchGuard()
    {
        const string statementName = "System.Int32 Fixture::Statement(System.Int32)";
        int guardLine = LineOf("throw new System.Diagnostics.UnreachableException();");
        using var symbols = new SymbolMap(_assemblyPath);
        using FileStream stream = File.OpenRead(_assemblyPath);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        MethodDefinition method = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition).First(definition => metadata.GetString(definition.Name) == "Statement");
        int length = pe.GetMethodBody(method.RelativeVirtualAddress).GetILContent().Length;
        int offset = Enumerable.Range(0, length).First(value => symbols.Resolve(statementName, value)?.StartLine == guardLine);
        XDocument report = Report();
        report.Descendants("Name").Single().Value = statementName;
        XElement guard = report.Descendants("SequencePoint").Last();
        guard.SetAttributeValue("sl", guardLine);
        guard.SetAttributeValue("el", guardLine);
        report.Descendants("BranchPoint").Last().SetAttributeValue("offsetend", offset);
        Filter(report);
        Assert.Single(report.Descendants("BranchPoint"));
        Assert.Single(report.Descendants("SequencePoint"));
    }

    [Fact]
    public void KeepsAmbiguousRelocatedAssemblies()
    {
        string other = Path.Combine(_directory, "other");
        Directory.CreateDirectory(other);
        File.Copy(_assemblyPath, Path.Combine(other, "Fixture.dll"));
        XDocument report = Report();
        report.Descendants("ModulePath").Single().Value = "/missing-build/Fixture.dll";
        var log = new TestLogger();
        new CoverageFilter(new FilterOptions([GuardPattern], [_directory, other], []), log).Apply(report, _directory);
        Assert.Equal(2, report.Descendants("BranchPoint").Count());
        Assert.Contains("SKIP module", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsPointsWhenReportFileDiffersFromPdbDocument()
    {
        XDocument report = Report();
        report.Descendants("File").Single().SetAttributeValue("fullPath", "/other/Fixture.cs");
        Filter(report);
        Assert.Equal(2, report.Descendants("BranchPoint").Count());
        Assert.Equal(2, report.Descendants("SequencePoint").Count());
    }

    [Fact]
    public void ResolvesNeitherUnknownMethodNorInvalidOffset()
    {
        using var symbols = new SymbolMap(_assemblyPath);
        Assert.Null(symbols.Resolve("System.Int32 Fixture::Missing()", 0));
        Assert.Null(symbols.Resolve(MethodName, -1));
        Assert.Null(symbols.Resolve(MethodName, int.MaxValue));
    }

    [Fact]
    public void RejectsNonOpenCoverInput()
    {
        Assert.Throws<InvalidDataException>(() => Filter(new XDocument(new XElement("coverage"))));
    }

    [Fact]
    public void RejectsInvalidRegex()
    {
        Assert.ThrowsAny<ArgumentException>(() => new CoverageFilter(new FilterOptions(["["], [], []), NullLogger.Instance));
    }

    private void Filter(XDocument report, ILogger? log = null)
        => new CoverageFilter(new FilterOptions([GuardPattern], [], []), log ?? NullLogger.Instance).Apply(report, _directory);

    private XDocument Report(int guardHits = 0)
    {
        var method = new XElement("Method", new XAttribute("sequenceCoverage", "50"), new XAttribute("branchCoverage", "50"),
            new XElement("Summary"), new XElement("Name", MethodName), new XElement("FileRef", new XAttribute("uid", "1")),
            new XElement("SequencePoints", Sequence(_switchLine, 1), Sequence(_guardLine, guardHits)),
            new XElement("BranchPoints", Branch(_coveredOffset, 1), Branch(_guardOffset, guardHits)));
        return new XDocument(new XElement("CoverageSession", new XElement("Summary"),
            new XElement("Modules", new XElement("Module", new XElement("Summary"), new XElement("ModulePath", _assemblyPath),
                new XElement("Files", new XElement("File", new XAttribute("uid", "1"), new XAttribute("fullPath", _sourcePath))),
                new XElement("Classes", new XElement("Class", new XElement("Summary"), new XElement("Methods", method)))))));
    }

    private static XElement Sequence(int line, int hits) => new("SequencePoint", new XAttribute("sl", line), new XAttribute("el", line),
        new XAttribute("vc", hits), new XAttribute("fileid", "1"), new XAttribute("bec", "2"), new XAttribute("bev", "1"));

    private XElement Branch(int offset, int hits) => new("BranchPoint", new XAttribute("sl", _switchLine), new XAttribute("offsetend", offset),
        new XAttribute("vc", hits), new XAttribute("fileid", "1"));

    private static int LineOf(string text) => Array.FindIndex(Source.Split('\n'), line => line.Contains(text, StringComparison.Ordinal)) + 1;

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
