using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class GLService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public GLService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // --- MASTER ACCOUNTS (CRUD) ---

        public async Task<List<GLMasterAccount>> GetMastersAsync()
        {
            using var context = _dbFactory.CreateDbContext();
            // Include Subs to display the count/hierarchy
            return await context.GLMasterAccounts
                .Include(m => m.SubAccounts)
                .OrderBy(m => m.AccountCode)
                .ToListAsync();
        }
        public async Task<List<GLSubAccount>> GetAllSubsAsync()
        {
            using var context = _dbFactory.CreateDbContext();
            return await context.GLSubAccounts
                .Include(s => s.MasterAccount) // REQUIRED for Master Name/Code/Type
                .Include(s => s.LinkedProject) // REQUIRED for Project Name
                .OrderBy(s => s.MasterAccount.AccountCode)
                .ThenBy(s => s.SubCode)
                .ToListAsync();
        }

        public async Task<bool> CreateMasterAsync(GLMasterAccount master)
        {
            using var context = _dbFactory.CreateDbContext();
            context.GLMasterAccounts.Add(master);
            return await context.SaveChangesAsync() > 0;
        }

        public async Task<bool> UpdateMasterAsync(GLMasterAccount master)
        {
            using var context = _dbFactory.CreateDbContext();
            var existing = await context.GLMasterAccounts.FindAsync(master.Id);
            if (existing == null) return false;

            // Update properties
            existing.AccountCode = master.AccountCode;
            existing.AccountName = master.AccountName;
            existing.AccountType = master.AccountType;

            return await context.SaveChangesAsync() > 0;
        }

        public async Task<bool> DeleteMasterAsync(Guid id)
        {
            using var context = _dbFactory.CreateDbContext();
            var master = await context.GLMasterAccounts.FindAsync(id);
            if (master == null) return false;

            context.GLMasterAccounts.Remove(master);
            return await context.SaveChangesAsync() > 0;
        }

        // --- SUB ACCOUNTS (CRUD) ---

        public async Task<bool> CreateSubAsync(GLSubAccount sub)
        {
            using var context = _dbFactory.CreateDbContext();
            context.GLSubAccounts.Add(sub);
            return await context.SaveChangesAsync() > 0;
        }
        public async Task<bool> UpdateSubAsync(GLSubAccount sub)
        {
            using var context = _dbFactory.CreateDbContext();
            var existing = await context.GLSubAccounts.FindAsync(sub.Id);
            if (existing == null) return false;

            // Update fields
            existing.SubCode = sub.SubCode;
            existing.AccountName = sub.AccountName;
            existing.MasterAccountId = sub.MasterAccountId; // Allow changing parent
            existing.ProjectId = sub.ProjectId;             // Allow changing project link

            return await context.SaveChangesAsync() > 0;
        }

        public async Task<bool> DeleteSubAsync(Guid id)
        {
            using var context = _dbFactory.CreateDbContext();
            var sub = await context.GLSubAccounts.FindAsync(id);
            if (sub == null) return false;

            context.GLSubAccounts.Remove(sub);
            return await context.SaveChangesAsync() > 0;
        }

    }
}