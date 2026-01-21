using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class ChartOfAccountService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public ChartOfAccountService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<List<ChartOfAccount>> GetAccountsAsync()
        {
            using var context = _dbFactory.CreateDbContext();
            return await context.ChartOfAccounts
                                .AsNoTracking()
                                .OrderBy(a => a.AccountCode)
                                .ToListAsync();
        }

        public async Task<bool> CreateAccountAsync(ChartOfAccount model)
        {
            using var context = _dbFactory.CreateDbContext();

            if (model.AccountId == Guid.Empty) model.AccountId = Guid.NewGuid();

            // 1. Logic: Auto-Calculate Level & Inherit Type
            if (model.ParentAccountId != null)
            {
                var parent = await context.ChartOfAccounts.FindAsync(model.ParentAccountId);
                if (parent != null)
                {
                    model.Level = parent.Level + 1;
                    model.Type = parent.Type;
                    model.NormalBalance = parent.NormalBalance;
                }
            }
            else
            {
                model.Level = 1;
            }

            context.ChartOfAccounts.Add(model);
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateAccountAsync(Guid id, ChartOfAccount model)
        {
            using var context = _dbFactory.CreateDbContext();

            var entity = await context.ChartOfAccounts.FindAsync(id);
            if (entity == null) return false;

            // 2. Update Fields
            entity.AccountCode = model.AccountCode;
            entity.AccountName = model.AccountName;
            entity.ParentAccountId = model.ParentAccountId;
            entity.IsParent = model.IsParent;
            entity.IsActive = model.IsActive;
            entity.Description = model.Description;
            entity.AllowManualEntry = model.AllowManualEntry;

            // 3. Logic: Recalculate Level if moved
            if (entity.ParentAccountId != null)
            {
                var parent = await context.ChartOfAccounts.FindAsync(entity.ParentAccountId);
                if (parent != null) entity.Level = parent.Level + 1;
            }
            else
            {
                entity.Level = 1;
            }

            await context.SaveChangesAsync();
            return true;
        }

        public async Task<string?> DeleteAccountAsync(Guid id)
        {
            using var context = _dbFactory.CreateDbContext();

            var account = await context.ChartOfAccounts.FindAsync(id);
            if (account == null) return "Account not found";

            // 4. Logic: Prevent Orphan Records
            bool hasChildren = await context.ChartOfAccounts.AnyAsync(x => x.ParentAccountId == id);
            if (hasChildren)
            {
                return "Cannot delete this account because it has sub-accounts attached to it.";
            }

            context.ChartOfAccounts.Remove(account);
            await context.SaveChangesAsync();
            return null; // Null means Success
        }
    }
}