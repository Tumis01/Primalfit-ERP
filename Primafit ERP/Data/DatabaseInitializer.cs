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
            var defaultSchema = await GetDefaultSchemaAsync(context, cancellationToken);
            await RemoveOrphanedHistoryTableAsync(context, defaultSchema, cancellationToken);
            await context.Database.EnsureCreatedAsync(cancellationToken);
            await MarkCurrentModelAsBaselineAsync(context, defaultSchema, cancellationToken);
        }
        else
        {
            var defaultSchema = await GetDefaultSchemaAsync(context, cancellationToken);

            // Synchronize legacy migrations if their columns/tables already exist
            await SyncExistingSchemaMigrationsAsync(context, existingSchema, defaultSchema, cancellationToken);

            // Now run any genuinely new migrations safely
            await context.Database.MigrateAsync(cancellationToken);
        }

        await Primafit_ERP.Data.Seed.RbacSeeder.SeedAsync(context, roleManager);
    }
    private static async Task SyncExistingSchemaMigrationsAsync(
    AppDbContext context,
    string existingSchema,
    string defaultSchema,
    CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var wasClosed = connection.State == System.Data.ConnectionState.Closed;
        if (wasClosed) await connection.OpenAsync(cancellationToken);

        try
        {
            // 1. Check if the CurrencyId column already exists on PurchaseOrders
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
            SELECT COUNT(*) 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_SCHEMA = @schema 
              AND TABLE_NAME = 'PurchaseOrders' 
              AND COLUMN_NAME = 'CurrencyId';
            """;
            var param = cmd.CreateParameter();
            param.ParameterName = "@schema";
            param.Value = existingSchema;
            cmd.Parameters.Add(param);

            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

            // 2. If CurrencyId already exists, mark initial migrations so EF Core skips them
            if (count > 0)
            {
                var initialMigrations = new[]
                {
                "20260207180643_InitialSetu",
                "20260207185754_Initial",
                "20260207201304_exchange"
            };

                foreach (var mig in initialMigrations)
                {
                    await EnsureMigrationMarkedAsync(context, defaultSchema, mig, cancellationToken);
                }
            }
        }
        finally
        {
            if (wasClosed) await connection.CloseAsync();
        }
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
    private static async Task EnsureMigrationMarkedAsync(
    AppDbContext context,
    string schema,
    string migrationId,
    CancellationToken cancellationToken)
    {
        const string sql = """
        DECLARE @schema sysname = {1};
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

        SET @sql = N'IF NOT EXISTS (SELECT 1 FROM ' + @history + N' WHERE [MigrationId] = @migId)
                    INSERT INTO ' + @history + N' ([MigrationId], [ProductVersion]) VALUES (@migId, @prodVer)';
        EXEC sp_executesql @sql,
            N'@migId nvarchar(150), @prodVer nvarchar(32)',
            @migId = {0}, @prodVer = N'8.0.0';
        """;

        await context.Database.ExecuteSqlRawAsync(sql, [migrationId, schema], cancellationToken);
    }
    private static async Task MarkAllMigrationsAsAppliedAsync(
    AppDbContext context,
    string schema,
    CancellationToken cancellationToken)
    {
        var migrations = context.Database.GetMigrations().ToList();
        if (migrations.Count == 0) return;

        var productVersion = typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "8.0.0";

        const string tableCheckSql = @"
        DECLARE @schema sysname = @schemaName;
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
        END;";

        await context.Database.ExecuteSqlRawAsync(
            tableCheckSql,
            [new Microsoft.Data.SqlClient.SqlParameter("@schemaName", schema)],
            cancellationToken);

        const string insertSql = @"
        DECLARE @schema sysname = @schemaName;
        DECLARE @history nvarchar(517) = QUOTENAME(@schema) + N'.[__EFMigrationsHistory]';
        DECLARE @sql nvarchar(max) = N'IF NOT EXISTS (SELECT 1 FROM ' + @history + N' WHERE [MigrationId] = @migId)
                                     INSERT INTO ' + @history + N' ([MigrationId], [ProductVersion]) VALUES (@migId, @prodVer)';
        EXEC sp_executesql @sql, 
            N'@migId nvarchar(150), @prodVer nvarchar(32)', 
            @migId = @migrationId, 
            @prodVer = @version;";

        foreach (var migration in migrations)
        {
            await context.Database.ExecuteSqlRawAsync(
                insertSql,
                [
                    new Microsoft.Data.SqlClient.SqlParameter("@migrationId", migration),
                new Microsoft.Data.SqlClient.SqlParameter("@schemaName", schema),
                new Microsoft.Data.SqlClient.SqlParameter("@version", productVersion)
                ],
                cancellationToken);
        }
    }
}
