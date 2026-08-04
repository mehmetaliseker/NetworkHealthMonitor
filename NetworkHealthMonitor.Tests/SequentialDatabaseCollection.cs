using Xunit;

namespace NetworkHealthMonitor.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SequentialDatabaseCollection : ICollectionFixture<object>
{
    public const string Name = "SequentialDatabase";
}
