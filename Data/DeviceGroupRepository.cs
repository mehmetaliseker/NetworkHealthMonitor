using System.Globalization;
using Microsoft.Data.Sqlite;
using NetworkHealthMonitor.Models;

namespace NetworkHealthMonitor.Data;

public sealed class DeviceGroupRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public DeviceGroupRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DeviceGroup>> GetAllAsync()
    {
        var groups = new List<DeviceGroup>();
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.Id, g.Name, g.Description, g.DefaultSchedulePlanId, g.CreatedAt, g.UpdatedAt,
                   COUNT(d.Id) AS DeviceCount
            FROM DeviceGroups g
            LEFT JOIN Devices d ON d.GroupId = g.Id
            GROUP BY g.Id, g.Name, g.Description, g.DefaultSchedulePlanId, g.CreatedAt, g.UpdatedAt
            ORDER BY g.Name COLLATE NOCASE;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            groups.Add(ReadGroup(reader));
        }

        return groups;
    }

    public async Task<int> AddAsync(DeviceGroup group)
    {
        var now = DateTime.Now;
        group.CreatedAt = now;
        group.UpdatedAt = now;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DeviceGroups (Name, Description, DefaultSchedulePlanId, CreatedAt, UpdatedAt)
            VALUES (@Name, @Description, @DefaultSchedulePlanId, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();
            """;

        AddGroupParameters(command, group);
        var result = await command.ExecuteScalarAsync();
        group.Id = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        return group.Id;
    }

    public async Task UpdateAsync(DeviceGroup group)
    {
        group.UpdatedAt = DateTime.Now;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DeviceGroups
            SET Name = @Name,
                Description = @Description,
                DefaultSchedulePlanId = @DefaultSchedulePlanId,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id;
            """;

        AddGroupParameters(command, group);
        AddParameter(command, "@Id", group.Id);
        await command.ExecuteNonQueryAsync();

        await using var deviceUpdate = connection.CreateCommand();
        deviceUpdate.CommandText = """
            UPDATE Devices
            SET GroupName = @Name,
                UpdatedAt = @UpdatedAt
            WHERE GroupId = @Id;
            """;
        AddParameter(deviceUpdate, "@Name", group.Name.Trim());
        AddParameter(deviceUpdate, "@UpdatedAt", ToStorageDate(DateTime.Now));
        AddParameter(deviceUpdate, "@Id", group.Id);
        await deviceUpdate.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        using var transaction = connection.BeginTransaction();

        await using var deviceCommand = connection.CreateCommand();
        deviceCommand.Transaction = transaction;
        deviceCommand.CommandText = """
            UPDATE Devices
            SET GroupId = NULL,
                GroupName = '',
                UpdatedAt = @UpdatedAt
            WHERE GroupId = @Id;
            """;
        AddParameter(deviceCommand, "@UpdatedAt", ToStorageDate(DateTime.Now));
        AddParameter(deviceCommand, "@Id", id);
        await deviceCommand.ExecuteNonQueryAsync();

        await using var groupCommand = connection.CreateCommand();
        groupCommand.Transaction = transaction;
        groupCommand.CommandText = "DELETE FROM DeviceGroups WHERE Id = @Id;";
        AddParameter(groupCommand, "@Id", id);
        await groupCommand.ExecuteNonQueryAsync();

        transaction.Commit();
    }

    public async Task<bool> ExistsByNameAsync(string name, int? excludeId = null)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM DeviceGroups
            WHERE Name = @Name
              AND (@ExcludeId IS NULL OR Id <> @ExcludeId);
            """;
        AddParameter(command, "@Name", name.Trim());
        AddParameter(command, "@ExcludeId", excludeId);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
    }

    private static DeviceGroup ReadGroup(SqliteDataReader reader)
    {
        return new DeviceGroup
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            DefaultSchedulePlanId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            CreatedAt = FromStorageDate(reader.GetString(4)),
            UpdatedAt = FromStorageDate(reader.GetString(5)),
            DeviceCount = reader.GetInt32(6)
        };
    }

    private static void AddGroupParameters(SqliteCommand command, DeviceGroup group)
    {
        AddParameter(command, "@Name", group.Name.Trim());
        AddParameter(command, "@Description", group.Description);
        AddParameter(command, "@DefaultSchedulePlanId", group.DefaultSchedulePlanId);
        AddParameter(command, "@CreatedAt", ToStorageDate(group.CreatedAt));
        AddParameter(command, "@UpdatedAt", ToStorageDate(group.UpdatedAt));
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string ToStorageDate(DateTime value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTime FromStorageDate(string value)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : DateTime.Now;
    }
}
