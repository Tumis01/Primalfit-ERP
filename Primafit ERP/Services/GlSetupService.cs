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

        // -------------------------
        // ACCOUNT TYPES
        // -------------------------
        public async Task<List<GLAccountType>> GetAccountTypesAsync(Guid companyId)
        {
            using var context = _dbFactory.CreateDbContext();

            return await context.GLAccountTypes
                .AsNoTracking()
                .Where(t => t.CompanyId == companyId)
                .OrderBy(t => t.Code)
                .ToListAsync();
        }

        public async Task<string> CreateAccountTypeAsync(Guid companyId, GLAccountType type)
        {
            using var context = _dbFactory.CreateDbContext();

            if (string.IsNullOrWhiteSpace(type.Code) || string.IsNullOrWhiteSpace(type.Name))
                return "Type Code and Type Name are required.";

            type.Code = type.Code.Trim();
            type.Name = type.Name.Trim();

            bool codeExists = await context.GLAccountTypes.AnyAsync(x =>
                x.CompanyId == companyId && x.Code == type.Code);

            if (codeExists) return $"Type Code '{type.Code}' already exists for this company.";

            bool nameExists = await context.GLAccountTypes.AnyAsync(x =>
                x.CompanyId == companyId && x.Name == type.Name);

            if (nameExists) return $"Type Name '{type.Name}' already exists for this company.";

            type.CompanyId = companyId;
            type.Id = type.Id == Guid.Empty ? Guid.NewGuid() : type.Id;

            context.GLAccountTypes.Add(type);
            await context.SaveChangesAsync();
            return string.Empty;
        }

        // -------------------------
        // MAIN ACCOUNTS
        // -------------------------
        public async Task<List<GLMainAccount>> GetMainAccountsAsync(Guid companyId)
        {
            using var context = _dbFactory.CreateDbContext();

            return await context.GLMainAccounts
                .AsNoTracking()
                .Where(m => m.CompanyId == companyId)
                .Include(m => m.AccountType)
                .OrderBy(m => m.Code)
                .ToListAsync();
        }

        public async Task<string> CreateMainAccountAsync(Guid companyId, GLMainAccount main)
        {
            using var context = _dbFactory.CreateDbContext();

            if (string.IsNullOrWhiteSpace(main.Code) || string.IsNullOrWhiteSpace(main.Name))
                return "Main Code and Main Name are required.";

            if (main.AccountTypeId == Guid.Empty)
                return "Please select an Account Type.";

            main.Code = main.Code.Trim();
            main.Name = main.Name.Trim();

            // Parent must belong to same company
            var parentType = await context.GLAccountTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == main.AccountTypeId && t.CompanyId == companyId);

            if (parentType == null)
                return "Selected Account Type was not found for this company.";

            bool codeExists = await context.GLMainAccounts.AnyAsync(x =>
                x.CompanyId == companyId && x.Code == main.Code);

            if (codeExists) return $"Main Code '{main.Code}' already exists for this company.";

            bool nameExists = await context.GLMainAccounts.AnyAsync(x =>
                x.CompanyId == companyId && x.Name == main.Name);

            if (nameExists) return $"Main Name '{main.Name}' already exists for this company.";

            main.CompanyId = companyId;
            main.Id = main.Id == Guid.Empty ? Guid.NewGuid() : main.Id;

            context.GLMainAccounts.Add(main);
            await context.SaveChangesAsync();
            return string.Empty;
        }

        // -------------------------
        // CHART OF ACCOUNTS (LEAF)
        // -------------------------
        public async Task<List<GLChartOfAccount>> GetChartOfAccountsAsync(Guid companyId)
        {
            using var context = _dbFactory.CreateDbContext();

            return await context.GLChartOfAccounts
                .AsNoTracking()
                .Where(c => c.CompanyId == companyId)
                .Include(c => c.MainAccount)
                    .ThenInclude(m => m.AccountType)
                .OrderBy(c => c.AccountCode)
                .ToListAsync();
        }

        public async Task<string> CreateChartOfAccountAsync(Guid companyId, GLChartOfAccount account)
        {
            using var context = _dbFactory.CreateDbContext();

            if (string.IsNullOrWhiteSpace(account.AccountCode) || string.IsNullOrWhiteSpace(account.AccountName))
                return "Account Code and Account Name are required.";

            if (account.MainAccountId == Guid.Empty)
                return "Please select a Main Account.";

            account.AccountCode = account.AccountCode.Trim();
            account.AccountName = account.AccountName.Trim();

            // Parent must belong to same company
            var parentMain = await context.GLMainAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == account.MainAccountId && m.CompanyId == companyId);

            if (parentMain == null)
                return "Selected Main Account was not found for this company.";

            bool codeExists = await context.GLChartOfAccounts.AnyAsync(x =>
                x.CompanyId == companyId && x.AccountCode == account.AccountCode);

            if (codeExists) return $"Account Code '{account.AccountCode}' already exists for this company.";

            bool nameExists = await context.GLChartOfAccounts.AnyAsync(x =>
                x.CompanyId == companyId && x.AccountName == account.AccountName);

            if (nameExists) return $"Account Name '{account.AccountName}' already exists for this company.";

            account.CompanyId = companyId;
            account.Id = account.Id == Guid.Empty ? Guid.NewGuid() : account.Id;
            account.CreatedDate = DateTime.UtcNow;

            context.GLChartOfAccounts.Add(account);
            await context.SaveChangesAsync();
            return string.Empty;
        }
    }
}
