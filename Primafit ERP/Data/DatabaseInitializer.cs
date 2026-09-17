using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Data;

/// <summary>
/// Initializes an ERP database without mixing EnsureCreated and Migrate on
/// the same database. A genuinely empty database is bootstrapped directly
/// from the current model; an existing database is upgraded through EF
/// migrations. The application intentionally leaves schema resolution to
/// SQL Server, so it works with the database user's default schema.
/// </summary>
public static class DatabaseInitializer
{
    private const string ApplicationTablesProbe = """
        SELECT TOP (1) TABLE_SCHEMA
        FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_TYPE = N'BASE TABLE'
          AND TABLE_NAME IN
          (
              N'AspNetRoles', N'AspNetUsers', N'CompanyDetails', N'Items',
              N'GLBatches', N'AccountingPeriods', N'Warehouses',
              N'AccountTypes1'
          )
        ORDER BY CASE WHEN TABLE_SCHEMA = N'dbo' THEN 0 ELSE 1 END;
        """;

    public static async Task InitializeAsync(
        AppDbContext context,
        RoleManager<ApplicationRole> roleManager,
        CancellationToken cancellationToken = default)
    {
        var existingSchema = await FindApplicationSchemaAsync(context, cancellationToken);

        if (existingSchema is null)
        {
            // The database was created by the hosting provider but contains
            // no ERP tables. EnsureCreated builds the current model directly,
            // so old, historically inconsistent migrations cannot break a
            // brand-new deployment.
            var defaultSchema = await GetDefaultSchemaAsync(context, cancellationToken);
            await RemoveOrphanedHistoryTableAsync(context, defaultSchema, cancellationToken);
            await context.Database.EnsureCreatedAsync(cancellationToken);
            await MarkCurrentModelAsBaselineAsync(context, defaultSchema, cancellationToken);
        }
        else
        {
            // Existing databases retain their data and are upgraded normally.
            // A previous failed bootstrap can leave the tables in place while
            // the migration history is empty. Never replay migration zero in
            // that situation because it attempts to recreate identity tables.
            if (!await HasMigrationHistoryAsync(context, existingSchema, cancellationToken))
            {
                if (!await HasCompleteCurrentSchemaAsync(context, existingSchema, cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"ERP tables were found in schema '{existingSchema}', but the schema is incomplete and has no usable migration history. " +
                        "Restore the database backup or repair the migration history before starting the application.");
                }

                var defaultSchema = await GetDefaultSchemaAsync(context, cancellationToken);
                await MarkCurrentModelAsBaselineAsync(context, defaultSchema, cancellationToken);
            }
            else
            {
                await context.Database.MigrateAsync(cancellationToken);
            }
        }

        await Primafit_ERP.Data.Seed.RbacSeeder.SeedAsync(context, roleManager);
    }

    private static async Task<string?> FindApplicationSchemaAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State == System.Data.ConnectionState.Closed;

        if (wasClosed)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ApplicationTablesProbe;
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }
        finally
        {
            if (wasClosed)
                await connection.CloseAsync();
        }
    }

    private static async Task<string> GetDefaultSchemaAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State == System.Data.ConnectionState.Closed;

        if (wasClosed)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT SCHEMA_NAME();";
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(Convert.ToString(value))
                ? "dbo"
                : Convert.ToString(value)!;
        }
        finally
        {
            if (wasClosed)
                await connection.CloseAsync();
        }
    }

    private static async Task<bool> HasMigrationHistoryAsync(
        AppDbContext context,
        string schema,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State == System.Data.ConnectionState.Closed;

        if (wasClosed)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @schema sysname = @schemaName;
                DECLARE @history nvarchar(517) = QUOTENAME(@schema) + N'.[__EFMigrationsHistory]';
                IF OBJECT_ID(@history, N'U') IS NULL
                    SELECT CAST(0 AS int);
                ELSE
                    EXEC(N'SELECT COUNT(*) FROM ' + @history);
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@schemaName";
            parameter.Value = schema;
            command.Parameters.Add(parameter);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(value) > 0;
        }
        finally
        {
            if (wasClosed)
                await connection.CloseAsync();
        }
    }

    private static async Task<bool> HasCompleteCurrentSchemaAsync(
        AppDbContext context,
        string schema,
        CancellationToken cancellationToken)
    {
        var expectedTables = context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State == System.Data.ConnectionState.Closed;

        if (wasClosed)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = @schemaName
                  AND TABLE_TYPE = N'BASE TABLE';
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@schemaName";
            parameter.Value = schema;
            command.Parameters.Add(parameter);

            var actualTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                actualTables.Add(reader.GetString(0));

            return expectedTables.IsSubsetOf(actualTables);
        }
        finally
        {
            if (wasClosed)
                await connection.CloseAsync();
        }
    }

    private static async Task MarkCurrentModelAsBaselineAsync(
        AppDbContext context,
        string schema,
        CancellationToken cancellationToken)
    {
        var latestMigration = context.Database.GetMigrations().LastOrDefault();
        if (latestMigration is null)
            return;

        const string sql = """
            DECLARE @schema sysname = {2};
            DECLARE @history nvarchar(517) = QUOTENAME(@schema) + N'.[__EFMigrationsHistory]';
            DECLARE @sql nvarchar(max);
            IF OBJECT_ID(@history, N'U') IS NULL
            BEGIN
                SET @sql = N'CREATE TABLE ' + @history + N'
                (
                    [MigrationId] nvarchar(150) NOT NULL,
                    [ProductVersion] nvarchar(32) NOT NULL,
                    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                )';
                EXEC sp_executesql @sql;
            END;
            SET @sql = N'DELETE FROM ' + @history;
            EXEC sp_executesql @sql;
            SET @sql = N'INSERT INTO ' + @history + N' ([MigrationId], [ProductVersion]) VALUES (@migrationId, @productVersion)';
            EXEC sp_executesql @sql,
                N'@migrationId nvarchar(150), @productVersion nvarchar(32)',
                @migrationId = {0}, @productVersion = {1};
            """;

        var productVersion = typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "10.0.0";
        await context.Database.ExecuteSqlRawAsync(
            sql,
            [latestMigration, productVersion, schema],
            cancellationToken);
    }

    private static Task<int> RemoveOrphanedHistoryTableAsync(
        AppDbContext context,
        string schema,
        CancellationToken cancellationToken)
    {
        // This is safe only because the application-table probe has already
        // confirmed that the database contains no ERP tables. It handles a
        // previous failed bootstrap that left only the EF history table.
        return context.Database.ExecuteSqlRawAsync(
            "DECLARE @schema sysname = {0}; DECLARE @history nvarchar(517) = QUOTENAME(@schema) + N'.[__EFMigrationsHistory]'; DECLARE @sql nvarchar(max); IF OBJECT_ID(@history, N'U') IS NOT NULL BEGIN SET @sql = N'DROP TABLE ' + @history; EXEC sp_executesql @sql; END;",
            [schema],
            cancellationToken);
    }
}
