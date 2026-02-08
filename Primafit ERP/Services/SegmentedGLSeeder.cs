using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class SegmentedGLSeeder
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public SegmentedGLSeeder(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task EnsureSeededAsync()
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // 1. Seed Account Types (If empty)
            if (!await ctx.AccountTypes1.AnyAsync())
            {
                var types = new List<AccountType1>
                {
                    // Assets
                    new() { Id = 1, Name = "Cash and Cash Equivalents", IsBalanceSheet = true, IsDebit = true },
                    new() { Id = 3, Name = "Other Current Asset", IsBalanceSheet = true, IsDebit = true },
                    new() { Id = 8, Name = "Property, Plant and Equipment", IsBalanceSheet = true, IsDebit = true },
                    new() { Id = 24, Name = "Inventories", IsBalanceSheet = true, IsDebit = true },
                    new() { Id = 25, Name = "Trade Receivables", IsBalanceSheet = true, IsDebit = true },
                    
                    // Liabilities
                    new() { Id = 5, Name = "Other Current Liability", IsBalanceSheet = true, IsDebit = false },
                    new() { Id = 26, Name = "Trade Payables", IsBalanceSheet = true, IsDebit = false },
                    new() { Id = 35, Name = "Bank Overdraft", IsBalanceSheet = true, IsDebit = false },

                    // Equity
                    new() { Id = 7, Name = "Share Capital", IsBalanceSheet = true, IsDebit = false },
                    new() { Id = 11, Name = "Retained Earnings", IsBalanceSheet = true, IsDebit = false },

                    // Income
                    new() { Id = 9, Name = "Revenue", IsBalanceSheet = false, IsDebit = false },
                    new() { Id = 4, Name = "Other Income", IsBalanceSheet = false, IsDebit = false },

                    // Expenses
                    new() { Id = 10, Name = "Cost of Sales", IsBalanceSheet = false, IsDebit = true },
                    new() { Id = 2, Name = "Other Expense", IsBalanceSheet = false, IsDebit = true },
                    new() { Id = 17, Name = "Finance Cost", IsBalanceSheet = false, IsDebit = true },
                    new() { Id = 12, Name = "Tax Expense", IsBalanceSheet = false, IsDebit = true }
                    
                    // (Add the rest of your 36 types here following this pattern)
                };
                ctx.AccountTypes1.AddRange(types);
            }

            // 2. Seed Segment Definitions
            if (!await ctx.SegmentDefinitions.AnyAsync())
            {
                var segments = new List<SegmentDefinition>
                {
                    new() { SegmentNumber = 1, SegmentName = "Main Account", Length = 4, IsActive = true }, // Header
                    new() { SegmentNumber = 2, SegmentName = "Department", Length = 3, IsActive = true },
                    new() { SegmentNumber = 3, SegmentName = "Project", Length = 3, IsActive = true },
                    new() { SegmentNumber = 4, SegmentName = "Region", Length = 2, IsActive = false },
                    new() { SegmentNumber = 5, SegmentName = "Cost Center", Length = 3, IsActive = false },
                    new() { SegmentNumber = 6, SegmentName = "Intercompany", Length = 3, IsActive = false }
                };
                ctx.SegmentDefinitions.AddRange(segments);
            }

            // Save only if changes were made
            if (ctx.ChangeTracker.HasChanges())
            {
                await ctx.SaveChangesAsync();
            }
        }
    }
}