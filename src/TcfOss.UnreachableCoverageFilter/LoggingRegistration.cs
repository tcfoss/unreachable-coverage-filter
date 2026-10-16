using System.Globalization;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace TcfOss.UnreachableCoverageFilter;

public static class LoggingRegistration
{
    public static ILoggerFactory CreateLoggerFactory(LogLevel minimumLevel, out string logFilePath)
    {
        logFilePath = Path.Combine(Environment.CurrentDirectory,
            $"unreachable-coverage-filter.{DateTime.Now.ToString("yyyyMMdd_HHmmss_ffffff", CultureInfo.InvariantCulture)}.log");

        var configuration = new LoggerConfiguration();
        if (minimumLevel == LogLevel.None)
        {
            configuration.Filter.ByExcluding(_ => true);
        }
        else
        {
            configuration.MinimumLevel.Is(ToSerilogLevel(minimumLevel));
        }

        Serilog.ILogger serilogLogger = configuration
            .WriteTo.File(logFilePath, formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();
        return new SerilogLoggerFactory(serilogLogger, dispose: true);
    }

    private static LogEventLevel ToSerilogLevel(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            LogLevel.Critical => LogEventLevel.Fatal,
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null)
        };
    }
}
