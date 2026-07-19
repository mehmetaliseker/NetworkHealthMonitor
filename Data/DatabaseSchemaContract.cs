using Microsoft.Data.Sqlite;

namespace NetworkHealthMonitor.Data;

public static class DatabaseSchemaContract
{
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> ExpectedColumns { get; } =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["SchemaMigrations"] = Set("Version", "AppliedAtUtc"),
            ["SchedulePlans"] = Set("Id", "Name", "TargetType", "TargetValue", "IntervalMinutes", "ScheduleMode", "IntervalValue", "IntervalUnit", "TimesPerDay", "DailyTimes", "SelectedWeekDays", "TimeZoneId", "FailureRetryEnabled", "ConfirmationRetryCount", "ConfirmationRetryIntervalSeconds", "OfflineRecheckIntervalSeconds", "MissedRunPolicy", "TimeoutMs", "MaxParallelism", "FailureThreshold", "IsActive", "Description", "LastRunAt", "NextRunAt", "LastStatus", "CreatedAt", "UpdatedAt"),
            ["DeviceGroups"] = Set("Id", "Name", "Description", "DefaultSchedulePlanId", "DefaultAutoCheckEnabled", "DefaultCheckIntervalSeconds", "DefaultPingTimeoutMs", "DefaultFailureRetryIntervalSeconds", "DefaultFailureRetryLimit", "DefaultFailureThreshold", "TargetAvailabilityPercent", "CreatedAt", "UpdatedAt"),
            ["Devices"] = Set("Id", "Name", "IpAddress", "DeviceType", "Location", "GroupId", "GroupName", "IsCritical", "IsActive", "IsEnabled", "IsDeleted", "DeletedAtUtc", "AutoCheckEnabled", "DefaultSchedulePlanId", "PingTimeoutMs", "CheckIntervalSeconds", "FailureRetryIntervalSeconds", "FailureRetryLimit", "FailureThreshold", "Description", "LastStatus", "LastLatencyMs", "LastCheckedAt", "LastSuccessfulCheckAt", "LastFailedCheckAt", "ConsecutiveFailures", "ConsecutiveSuccesses", "LastStableStatus", "SuppressionMode", "SuppressedFromUtc", "SuppressedUntilUtc", "SuppressionReason", "SuppressedBy", "TargetAvailabilityPercent", "CreatedAt", "UpdatedAt"),
            ["DeviceGroupMembers"] = Set("DeviceId", "GroupId", "CreatedAt"),
            ["PingLogs"] = Set("Id", "DeviceId", "DeviceName", "IpAddress", "DeviceType", "GroupName", "Status", "IsReachable", "LatencyMs", "ResponseMessage", "ErrorCode", "ErrorMessage", "CheckedAt", "Source", "TriggerType", "PlanId", "SchedulePlanId", "SchedulePlanName", "WorkerInstanceId"),
            ["Outages"] = Set("Id", "DeviceId", "StartedAt", "EndedAt", "FailureCount", "RecoveryPingLogId", "IsResolved", "CreatedAt"),
            ["DeviceIncidents"] = Set("Id", "DeviceId", "StartedAtUtc", "EndedAtUtc", "Status", "InitialFailureCount", "CurrentFailureCount", "RecoverySuccessCount", "FirstFailureAtUtc", "ConfirmedDownAtUtc", "DetectionDelaySeconds", "LastFailureAtUtc", "LastObservedAtUtc", "LastSuccessAtUtc", "LastSuccessfulCheckAtUtc", "DownNotificationCreatedAtUtc", "InitialNotificationSentAtUtc", "EscalationNotificationSentAtUtc", "RecoveryNotificationCreatedAtUtc", "ResolvedAtUtc", "CurrentState", "SuppressedDurationSeconds", "FlapCount", "CreatedAtUtc", "UpdatedAtUtc"),
            ["NotificationOutbox"] = Set("Id", "EventType", "DeviceId", "IncidentId", "Channel", "Recipient", "Subject", "Body", "PayloadJson", "DeduplicationKey", "IdempotencyKey", "Status", "AttemptCount", "NextAttemptAtUtc", "LockedAtUtc", "LockedBy", "LastError", "LastAttemptAtUtc", "CreatedAtUtc", "SentAtUtc", "CancelledAtUtc"),
            ["CsvImportAudits"] = Set("Id", "ImportedAtUtc", "FileName", "ImportMode", "ImportScope", "AddedCount", "UpdatedCount", "DeletedCount", "RestoredCount", "UnchangedCount", "SkippedCount", "InvalidRowCount", "DuplicateRowCount", "InitiatedBy", "Result", "ErrorMessage"),
            ["WorkerHeartbeat"] = Set("WorkerInstanceId", "MachineName", "ProcessId", "Version", "StartedAtUtc", "LastSeenAtUtc", "LastSchedulerCycleAtUtc", "LastSchedulerPollAtUtc", "LastSuccessfulPingAtUtc", "LastNotificationDispatchAtUtc", "Status", "LastError", "LastCriticalError", "LastDatabaseLockedError", "LastSchedulerException", "LastNtfyException", "AverageSchedulerCycleMs"),
            ["DeviceAvailabilityPeriods"] = Set("Id", "DeviceId", "Status", "StartedAtUtc", "EndedAtUtc", "DurationSeconds", "IncidentId", "ReasonCode", "ReasonText", "DetectionSource", "FirstFailureAtUtc", "ConfirmedAtUtc", "CreatedAtUtc", "UpdatedAtUtc"),
            ["MonitoringCalendars"] = Set("Id", "Name", "TimezoneId", "IsDefault", "CreatedAtUtc", "UpdatedAtUtc"),
            ["MonitoringCalendarRules"] = Set("Id", "CalendarId", "DayOfWeek", "StartTime", "EndTime", "IsEnabled"),
            ["DeviceMonitoringCalendarAssignments"] = Set("Id", "TargetType", "TargetId", "CalendarId", "CreatedAtUtc"),
            ["MaintenanceWindows"] = Set("Id", "Name", "StartedAtUtc", "EndedAtUtc", "Reason", "SuppressNotifications", "ContinuePings", "Status", "CreatedBy", "CreatedAtUtc", "UpdatedAtUtc"),
            ["MaintenanceWindowTargets"] = Set("Id", "MaintenanceWindowId", "TargetType", "TargetId"),
            ["DeviceAvailabilityDaily"] = Set("Id", "DeviceId", "Date", "TimezoneId", "ExpectedMonitoringSeconds", "UpSeconds", "DownSeconds", "UnknownSeconds", "MaintenanceSeconds", "PausedSeconds", "IncidentCount", "RecoveredIncidentCount", "LongestOutageSeconds", "TotalDetectionDelaySeconds", "AvailabilityPercent", "StrictAvailabilityPercent", "CoveragePercent", "CalculatedAtUtc", "CalculationVersion"),
            ["AvailabilityRecalculationAudits"] = Set("Id", "RequestedAtUtc", "OperationType", "DeviceId", "GroupId", "StartDate", "EndDate", "RequestedBy", "Result", "Message"),
            ["AppSettings"] = Set("Key", "Value")
        };

    public static IReadOnlySet<string> ExpectedIndexes { get; } = Set(
        "UX_Devices_IpAddress",
        "IX_Devices_IsDeleted",
        "IX_Devices_IsEnabled",
        "IX_Devices_AutoCheck",
        "IX_Devices_Suppression",
        "IX_PingLogs_IsReachable",
        "IX_PingLogs_ErrorCode",
        "IX_PingLogs_Source",
        "IX_PingLogs_PlanId",
        "IX_PingLogs_DeviceId_CheckedAt",
        "IX_SchedulePlans_IsActive_NextRunAt",
        "IX_SchedulePlans_Mode_Target",
        "IX_DeviceIncidents_DeviceId",
        "IX_DeviceIncidents_Status",
        "IX_DeviceIncidents_StartedAtUtc",
        "IX_DeviceIncidents_EndedAtUtc",
        "UX_DeviceIncidents_Open_Device",
        "UX_NotificationOutbox_DeduplicationKey",
        "UX_NotificationOutbox_IdempotencyKey",
        "IX_NotificationOutbox_Status_NextAttempt",
        "IX_NotificationOutbox_Channel_Status",
        "IX_WorkerHeartbeat_LastSeenAtUtc",
        "IX_DeviceAvailabilityPeriods_EndedAtUtc",
        "UX_DeviceAvailabilityPeriods_Open_Device",
        "IX_MaintenanceWindows_Time");

    public static async Task VerifyAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        foreach (var expected in ExpectedColumns)
        {
            var actual = await GetColumnsAsync(connection, expected.Key, cancellationToken);
            if (actual.Count == 0)
            {
                errors.Add($"Missing table: {expected.Key}");
                continue;
            }

            foreach (var column in expected.Value.Where(column => !actual.Contains(column)))
            {
                errors.Add($"Missing column: {expected.Key}.{column}");
            }
        }

        var indexes = await GetIndexesAsync(connection, cancellationToken);
        foreach (var index in ExpectedIndexes.Where(index => !indexes.Contains(index)))
        {
            errors.Add($"Missing index: {index}");
        }

        var foreignKeyError = await GetFirstForeignKeyErrorAsync(connection, cancellationToken);
        if (!string.IsNullOrWhiteSpace(foreignKeyError))
        {
            errors.Add($"Foreign key check failed: {foreignKeyError}");
        }

        var integrity = await GetIntegrityCheckAsync(connection, cancellationToken);
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Integrity check failed: {integrity}");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Database schema contract validation failed: " + string.Join("; ", errors));
        }
    }

    private static IReadOnlySet<string> Set(params string[] values)
    {
        return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<HashSet<string>> GetColumnsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task<HashSet<string>> GetIndexesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            indexes.Add(reader.GetString(0));
        }

        return indexes;
    }

    private static async Task<string?> GetFirstForeignKeyErrorAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return $"{reader.GetString(0)} rowid {reader.GetInt64(1)}";
    }

    private static async Task<string> GetIntegrityCheckAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToString(result) ?? "no result";
    }
}
