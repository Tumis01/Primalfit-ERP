using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class SegmentedAccountService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public SegmentedAccountService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // --- FETCHING DATA ---
        public async Task<List<SegmentDefinition>> GetDefinitionsAsync()
            => await (await _dbFactory.CreateDbContextAsync()).SegmentDefinitions.OrderBy(s => s.SegmentNumber).ToListAsync();

        public async Task<List<AccountType1>> GetAccountTypesAsync()
            => await (await _dbFactory.CreateDbContextAsync()).AccountTypes1.OrderBy(t => t.Id).ToListAsync();

        public async Task<List<MainAccount>> GetMainAccountsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.MainAccounts
                .Include(m => m.AccountType)
                .Where(m => m.CompanyId == companyId)
                .OrderBy(m => m.AccountCode)
                .ToListAsync();
        }
        // Add this new method to SegmentedAccountService class
        public async Task<int> InitializeDefaultsAsync()
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var allTypes = new List<AccountType1>
    {
        // ASSETS
        new() { Id = 1, Name = "Cash and Cash Equivalents", IsBalanceSheet = true, IsDebit = true },
        new() { Id = 3, Name = "Other Current Asset", IsBalanceSheet = true, IsDebit = true },
        new() { Id = 8, Name = "Property, Plant and Equipment", IsBalanceSheet = true, IsDebit = true },
        new() { Id = 22, Name = "Investments", IsBalanceSheet = true, IsDebit = true },
        new() { Id = 23, Name = "Other Fixed Assets", IsBalanceSheet = true, IsDebit = true },
        new() { Id = 24, Name = "Inventories", IsBalanceSheet = true, IsDebit = true },
        new() { Id = 25, Name = "Trade Receivables", IsBalanceSheet = true, IsDebit = true },
        new() { Id = 30, Name = "Other Non-Current Asset", IsBalanceSheet = true, IsDebit = true },
        new() { Id = 31, Name = "Intangible Asset", IsBalanceSheet = true, IsDebit = true },
        new() { Id = 33, Name = "Investment Property", IsBalanceSheet = true, IsDebit = true },
        new() { Id = 34, Name = "Biological Asset", IsBalanceSheet = true, IsDebit = true },

        // LIABILITIES
        new() { Id = 5, Name = "Other Current Liability", IsBalanceSheet = true, IsDebit = false },
        new() { Id = 6, Name = "Non-Current Liability", IsBalanceSheet = true, IsDebit = false },
        new() { Id = 21, Name = "Other Non-Current Liability", IsBalanceSheet = true, IsDebit = false },
        new() { Id = 26, Name = "Trade Payables", IsBalanceSheet = true, IsDebit = false },
        new() { Id = 27, Name = "Taxation Liability", IsBalanceSheet = true, IsDebit = false },
        new() { Id = 28, Name = "Deferred Tax", IsBalanceSheet = true, IsDebit = false },
        new() { Id = 35, Name = "Bank Overdraft", IsBalanceSheet = true, IsDebit = false },
        new() { Id = 36, Name = "Finance Lease Liability", IsBalanceSheet = true, IsDebit = false },

        // EQUITY
        new() { Id = 7, Name = "Share Capital", IsBalanceSheet = true, IsDebit = false },
        new() { Id = 11, Name = "Retained Earnings", IsBalanceSheet = true, IsDebit = false },
        new() { Id = 19, Name = "Unallocated BS", IsBalanceSheet = true, IsDebit = false },
        new() { Id = 20, Name = "Shareholders Loan", IsBalanceSheet = true, IsDebit = false },
        new() { Id = 29, Name = "Non-Distributable Reserves", IsBalanceSheet = true, IsDebit = false },

        // INCOME
        new() { Id = 4, Name = "Other Income", IsBalanceSheet = false, IsDebit = false },
        new() { Id = 9, Name = "Revenue", IsBalanceSheet = false, IsDebit = false },
        new() { Id = 15, Name = "Dividends Received", IsBalanceSheet = false, IsDebit = false },
        new() { Id = 16, Name = "Profit/Loss on Sale of Asset", IsBalanceSheet = false, IsDebit = false },
        new() { Id = 18, Name = "Profit/Loss on Exchange", IsBalanceSheet = false, IsDebit = false },
        new() { Id = 32, Name = "Other Comprehensive Income", IsBalanceSheet = false, IsDebit = false },

        // EXPENSES
        new() { Id = 2, Name = "Other Expense", IsBalanceSheet = false, IsDebit = true },
        new() { Id = 10, Name = "Cost of Sales", IsBalanceSheet = false, IsDebit = true },
        new() { Id = 12, Name = "Tax Expense", IsBalanceSheet = false, IsDebit = true },
        new() { Id = 13, Name = "Unallocated IS", IsBalanceSheet = false, IsDebit = true },
        new() { Id = 14, Name = "Dividends Paid", IsBalanceSheet = false, IsDebit = true },
        new() { Id = 17, Name = "Finance Cost", IsBalanceSheet = false, IsDebit = true },
    };

            var existingIds = await ctx.AccountTypes1
                .Select(x => x.Id)
                .ToListAsync();

            var missing = allTypes
                .Where(x => !existingIds.Contains(x.Id))
                .ToList();

            if (missing.Count == 0) return 0;

            ctx.AccountTypes1.AddRange(missing);

            try
            {
                await ctx.SaveChangesAsync();
                return missing.Count;
            }
            catch (DbUpdateException ex)
            {
                // This is where the truth shows up (IDENTITY, constraints, etc.)
                throw new Exception("Failed to seed AccountType1. Check if the Id column is IDENTITY in SQL Server.", ex);
            }
        }

        public async Task<List<SegmentValue>> GetSegmentValuesAsync(Guid companyId, int segNum)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.SegmentValues
                .Where(v => v.CompanyId == companyId && v.SegmentNumber == segNum)
                .OrderBy(v => v.Value)
                .ToListAsync();
        }

        // --- CREATION LOGIC ---
        public async Task<string> CreateMainAccountAsync(MainAccount main)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (await ctx.MainAccounts.AnyAsync(m => m.CompanyId == main.CompanyId && m.AccountCode == main.AccountCode))
                return "Account Code already exists.";

            ctx.MainAccounts.Add(main);
            await ctx.SaveChangesAsync();
            return "";
        }

        public async Task<string> CreateSegmentValueAsync(SegmentValue val)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            if (await ctx.SegmentValues.AnyAsync(v => v.CompanyId == val.CompanyId && v.SegmentNumber == val.SegmentNumber && v.Value == val.Value))
                return "Value code already exists for this segment.";

            ctx.SegmentValues.Add(val);
            await ctx.SaveChangesAsync();
            return "";
        }

        // --- THE BUILDER (The Brain) ---
        public async Task<string> BuildSegmentedAccountAsync(Guid companyId, Guid mainId, Guid? s2, Guid? s3, Guid? s4, Guid? s5, Guid? s6)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            var main = await ctx.MainAccounts.FindAsync(mainId);
            if (main == null) return "Main Account required.";

            var defs = await ctx.SegmentDefinitions.ToListAsync();
            var parts = new List<string> { main.AccountCode };

            // Function to resolve segment code safely
            async Task AddPart(int num, Guid? id)
            {
                var def = defs.FirstOrDefault(d => d.SegmentNumber == num);
                if (def == null || !def.IsActive) return; // Skip inactive

                if (id == null) parts.Add(new string('0', def.Length)); // Default 000
                else
                {
                    var val = await ctx.SegmentValues.FindAsync(id);
                    parts.Add(val?.Value ?? new string('0', def.Length));
                }
            }

            await AddPart(2, s2);
            await AddPart(3, s3);
            await AddPart(4, s4);
            await AddPart(5, s5);
            await AddPart(6, s6);

            string finalCode = string.Join("-", parts);

            if (await ctx.SegmentedAccounts.AnyAsync(x => x.CompanyId == companyId && x.AccountCodeString == finalCode))
                return $"Account {finalCode} already exists.";

            var newAcc = new SegmentedAccount
            {
                CompanyId = companyId,
                MainAccountId = mainId,
                Segment2ValueId = s2,
                Segment3ValueId = s3,
                Segment4ValueId = s4,
                Segment5ValueId = s5,
                Segment6ValueId = s6,
                AccountCodeString = finalCode
            };

            ctx.SegmentedAccounts.Add(newAcc);
            await ctx.SaveChangesAsync();
            return "";
        }

        public async Task<List<SegmentedAccount>> GetSegmentedAccountsAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.SegmentedAccounts
                .Include(s => s.MainAccount).ThenInclude(m => m.AccountType)
                .Include(s => s.Segment2)
                .Include(s => s.Segment3)
                .Include(s => s.Segment4)
                .Include(s => s.Segment5)
                .Include(s => s.Segment6)
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.AccountCodeString)
                .ToListAsync();
        }
    }
}