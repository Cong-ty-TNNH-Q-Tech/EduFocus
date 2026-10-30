using EduFocus.SqliteDataAccess.Persistence;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.WPF.Services;

/// <summary>
/// Initializes the local database once, lazily, so the shell still starts when
/// the data folder is unavailable. A failed attempt can be retried.
/// </summary>
public sealed class DatabaseBootstrapper
{
    private readonly SqliteDatabaseInitializer _initializer;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private Task? _initialization;

    public DatabaseBootstrapper(SqliteDatabaseInitializer initializer, IAppLogger logger)
    {
        _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        Task initialization;

        lock (_gate)
        {
            _initialization ??= InitializeAsync(cancellationToken);
            initialization = _initialization;
        }

        return AwaitAndAllowRetryAsync(initialization);
    }

    private async Task AwaitAndAllowRetryAsync(Task initialization)
    {
        try
        {
            await initialization.ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                if (ReferenceEquals(_initialization, initialization))
                    _initialization = null;
            }

            throw;
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _logger.Info(LogCategories.Database, "Preparing the local database for Parent Mode.");
        await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }
}
