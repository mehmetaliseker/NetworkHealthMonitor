namespace NetworkHealthMonitor.Data;

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task VerifySchemaAsync(CancellationToken cancellationToken = default);
}
