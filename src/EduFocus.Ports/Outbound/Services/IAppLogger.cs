namespace EduFocus.Ports.Outbound.Services;

public interface IAppLogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? ex = null);

    void Log(string level, string category, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
    {
        var detail = $"[{category}] {message}";
        if (string.Equals(level, "Error", StringComparison.OrdinalIgnoreCase)) Error(detail, exception);
        else if (string.Equals(level, "Warning", StringComparison.OrdinalIgnoreCase)) Warning(detail);
        else Info(detail);
    }

    IDisposable BeginCorrelationScope(Guid? correlationId = null) => EmptyCorrelationScope.Instance;

    private sealed class EmptyCorrelationScope : IDisposable
    {
        public static readonly EmptyCorrelationScope Instance = new();
        public void Dispose() { }
    }
}
