using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace TcfOss.UnreachableCoverageFilter;

public sealed class ReportFileWriter(ILogger logger)
{
    public bool Write(XDocument report, string outputPath, OutputCollisionAction collisionAction)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new DirectoryNotFoundException($"Could not determine output directory for '{outputPath}'.");
        Directory.CreateDirectory(directory);

        if (File.Exists(fullPath))
        {
            switch (collisionAction)
            {
                case OutputCollisionAction.Rename:
                    string backupPath = GetBackupPath(fullPath);
                    File.Move(fullPath, backupPath);
                    LoggingMessages.RenamedOutput(logger, fullPath, backupPath);
                    break;
                case OutputCollisionAction.Overwrite:
                    LoggingMessages.OverwritingOutput(logger, fullPath);
                    break;
                case OutputCollisionAction.Skip:
                    LoggingMessages.SkippingOutput(logger, fullPath);
                    return false;
                case OutputCollisionAction.Error:
                    LoggingMessages.OutputCollision(logger, fullPath);
                    throw new IOException($"Output file already exists: {fullPath}");
                default:
                    throw new ArgumentOutOfRangeException(nameof(collisionAction), collisionAction, null);
            }
        }

        report.Save(fullPath);
        LoggingMessages.WroteReport(logger, fullPath);
        return true;
    }

    private static string GetBackupPath(string filePath)
    {
        string directory = Path.GetDirectoryName(filePath)!;
        string fileName = Path.GetFileName(filePath);
        Regex backupPattern = new($"^{Regex.Escape(fileName)}\\.bak\\.(\\d+)$", RegexOptions.CultureInvariant);
        int lastBackupNumber = Directory.EnumerateFiles(directory)
            .Select(path => backupPattern.Match(Path.GetFileName(path)))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max();
        return Path.Combine(directory, $"{fileName}.bak.{checked(lastBackupNumber + 1)}");
    }
}
