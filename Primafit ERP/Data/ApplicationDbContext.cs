using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;

namespace PrimafitERP.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<CompanyDetails> CompanyDetails { get; set; }
    public DbSet<ChartOfAccount> ChartOfAccounts { get; set; }
    public DbSet<ApplicationUser> ApplicationUser { get; set; }
    public DbSet<ApplicationRole> ApplicationRoles { get; set; }

    
}
