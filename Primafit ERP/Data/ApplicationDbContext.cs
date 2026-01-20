using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;

namespace PrimafitERP.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<CompanyDetails> CompanyDetails { get; set; }

    
}
