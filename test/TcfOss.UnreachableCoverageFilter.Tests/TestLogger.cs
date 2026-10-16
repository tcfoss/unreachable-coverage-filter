using Microsoft.Extensions.Logging;

namespace TcfOss.UnreachableCoverageFilter.Tests;

internal sealed class TestLogger : ILogger
{
    private readonly List<string> _messages = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _messages.Add(formatter(state, exception));
    }

    public override string ToString() => string.Join(Environment.NewLine, _messages);
}
