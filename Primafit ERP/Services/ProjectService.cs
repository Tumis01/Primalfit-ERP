using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Services
{
    public class ProjectService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public ProjectService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<List<Project>> GetProjectsAsync()
        {
            using var context = _dbFactory.CreateDbContext();
            return await context.Projects.OrderBy(p => p.Code).ToListAsync();
        }

        public async Task<bool> CreateProjectAsync(Project project)
        {
            using var context = _dbFactory.CreateDbContext();
            context.Projects.Add(project);
            return await context.SaveChangesAsync() > 0;
        }
    }
}