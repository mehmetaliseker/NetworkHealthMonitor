using NetworkHealthMonitor.Data;

namespace NetworkHealthMonitor.Tests;

internal sealed class TestStore : IAsyncDisposable
{
    private TestStore(string root, string dataDirectory, string legacyDirectory, SqliteConnectionFactory connectionFactory)
    {
        Root = root;
        DataDirectory = dataDirectory;
        LegacyDirectory = legacyDirectory;
        ConnectionFactory = connectionFactory;
    }

    public string Root { get; }

    public string DataDirectory { get; }

    public string LegacyDirectory { get; }

    public SqliteConnectionFactory ConnectionFactory { get; }

    public static async Task<TestStore> CreateAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "nhm-tests-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "programdata");
        var legacy = Path.Combine(root, "legacy-localappdata");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(legacy);
        DatabasePaths.Configure(new FixedApplicationPathProvider(data), legacy);
        var factory = new SqliteConnectionFactory();
        await factory.InitializeAsync();
        return new TestStore(root, data, legacy, factory);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup for temp test data.
        }

        return ValueTask.CompletedTask;
    }
}
