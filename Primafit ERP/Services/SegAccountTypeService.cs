using Microsoft.EntityFrameworkCore;
using PrimafitERP.Data;
using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services
{
    public sealed class SegAccountTypeService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public SegAccountTypeService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<List<SegAccountType>> GetAllAsync()
        {
            using var ctx = _dbFactory.CreateDbContext();

            return await ctx.Set<SegAccountType>()
                .AsNoTracking()
                .OrderBy(x => x.Id)
                .ToListAsync();
        }

        public async Task<SegAccountType?> GetByIdAsync(int id)
        {
            using var ctx = _dbFactory.CreateDbContext();

            return await ctx.Set<SegAccountType>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);
        }
    }
}
