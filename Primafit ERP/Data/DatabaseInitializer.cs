using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Data;

/// <summary>
/// Initializes an ERP database without mixing EnsureCreated and Migrate on
/// the same database. A genuinely empty database is bootstrapped directly
/// from the current model; an existing database is upgraded through EF
/// migrations.
/// </summary>
public static class DatabaseInitializer
{
    private const string ApplicationTablesProbe = """
        SELECT TOP (1) s.name
        FROM sys.tables t
        INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE t.name IN
        (
            N'AspNetUsers', N'CompanyDetails', N'Items', N'GLBatches',
            N'AccountingPeriods', N'Warehouses'
        )
        ORDER BY CASE WHEN s.name = N'dbo' THEN 0 ELSE 1 END;
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
            await RemoveOrphanedHistoryTableAsync(context, cancellationToken);
            await context.Database.EnsureCreatedAsync(cancellationToken);
            await MarkCurrentModelAsBaselineAsync(context, cancellationToken);
        }
        else
        {
            if (!string.Equals(existingSchema, "dbo", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The existing ERP tables are in schema '{existingSchema}'. " +
                    "This build uses the dbo schema. Move the ERP tables to dbo " +
                    "or migrate them before starting the application.");
            }

            // Existing databases retain their data and are upgraded normally.
            await context.Database.MigrateAsync(cancellationToken);
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

    private static async Task MarkCurrentModelAsBaselineAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        var latestMigration = context.Database.GetMigrations().LastOrDefault();
        if (latestMigration is null)
            return;

        const string sql = """
            IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[__EFMigrationsHistory]
                (
                    [MigrationId] nvarchar(150) NOT NULL,
                    [ProductVersion] nvarchar(32) NOT NULL,
                    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                );
            END;
            DELETE FROM [dbo].[__EFMigrationsHistory];
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
            VALUES ({0}, {1});
            """;

        var productVersion = typeof(DbContext).Assembly.GetName().Version?.ToString(3) ?? "10.0.0";
        await context.Database.ExecuteSqlRawAsync(
            sql,
            [latestMigration, productVersion],
            cancellationToken);
    }

    private static Task<int> RemoveOrphanedHistoryTableAsync(
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        // This is safe only because the application-table probe has already
        // confirmed that the database contains no ERP tables. It handles a
        // previous failed bootstrap that left only the EF history table.
        return context.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL DROP TABLE [dbo].[__EFMigrationsHistory];",
            cancellationToken);
    }
}
