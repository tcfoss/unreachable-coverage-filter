using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace TcfOss.UnreachableCoverageFilter;

public class ReportProcessor(FilterOptions options, ILogger logger)
{
    private readonly FilterOptions _options = options;
    private readonly ILogger _logger = logger;

    public XDocument Process(FileInfo reportFile)
    {
        LoggingMessages.ProcessingReport(_logger, reportFile.FullName);
        using var reader = XmlReader.Create(reportFile.FullName, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        XDocument report = XDocument.Load(reader);
        new CoverageFilter(_options, _logger).Apply(report, reportFile.DirectoryName!);
        return report;
    }
}
