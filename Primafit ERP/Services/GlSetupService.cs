using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class GLSetupService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public GLSetupService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        
        public async Task<List<GLAccountType>> GetAccountTypesAsync()
        {
            using var context = _dbFactory.CreateDbContext();
            return await context.GLAccountTypes.OrderBy(t => t.Code).ToListAsync();
        }

        public async Task<bool> CreateAccountTypeAsync(GLAccountType type)
        {
            using var context = _dbFactory.CreateDbContext();
            context.GLAccountTypes.Add(type);
            return await context.SaveChangesAsync() > 0;
        }

        // --- MAIN ACCOUNTS ---
        public async Task<List<GLMainAccount>> GetMainAccountsAsync()
        {
            using var context = _dbFactory.CreateDbContext();
            return await context.GLMainAccounts
                .Include(m => m.AccountType) // Fetch Parent
                .OrderBy(m => m.Code)
                .ToListAsync();
        }

        public async Task<bool> CreateMainAccountAsync(GLMainAccount main)
        {
            using var context = _dbFactory.CreateDbContext();
            context.GLMainAccounts.Add(main);
            return await context.SaveChangesAsync() > 0;
        }

        // --- CHART OF ACCOUNTS (The Leaf Nodes) ---
        public async Task<List<GLChartOfAccount>> GetChartOfAccountsAsync()
        {
            using var context = _dbFactory.CreateDbContext();
            return await context.GLChartOfAccounts
                .Include(c => c.MainAccount)
                    .ThenInclude(m => m.AccountType) // Fetch Grandparent
                .OrderBy(c => c.AccountCode)
                .ToListAsync();
        }
        

        public async Task<bool> CreateChartOfAccountAsync(GLChartOfAccount account)
        {
            using var context = _dbFactory.CreateDbContext();
            context.GLChartOfAccounts.Add(account);
            return await context.SaveChangesAsync() > 0;
        }
    }
}