using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using OfficeOpenXml;
using Primafit_ERP.Components;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Services;
using PrimafitERP.Data;

var builder = WebApplication.CreateBuilder(args);

ExcelPackage.License.SetNonCommercialPersonal("Primafit");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Factory for Blazor Components (Scoped to prevent concurrency issues)
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")),
    ServiceLifetime.Scoped);

// --- 2. IDENTITY CONFIGURATION ---
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options => {
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false; // Easier for MVP
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options => {
    options.LoginPath = "/login";
    options.ExpireTimeSpan = TimeSpan.FromDays(1);
});

// --- 3. UI & FRAMEWORK SERVICES ---
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = true); // Add this
builder.Services.AddMudServices();
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR(e => { e.MaximumReceiveMessageSize = 10 * 1024 * 1024; });

// --- 4. APPLICATION SERVICES (Scoped) ---
// Core Auth & Identity
builder.Services.AddScoped<AuthService>(); // <--- NEW: Handles the Tenant Registration Logic
builder.Services.AddScoped<UserApiService>();
builder.Services.AddScoped<RoleApiService>();
builder.Services.AddScoped<CompanyApiService>();

// Operations
builder.Services.AddScoped<GLSetupService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<CurrencyService>();
builder.Services.AddScoped<TaxService>();
builder.Services.AddScoped<AccountingPeriodService>();
builder.Services.AddScoped<GLOperationsService>();
builder.Services.AddScoped<ReconciliationService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<SalesService>();
builder.Services.AddScoped<WarehouseService>();
builder.Services.AddScoped<MasterDataService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<PurchasingService>();
builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<BudgetService>();
builder.Services.AddScoped<ICashbookService, CashbookService>();
builder.Services.AddScoped<IStatementParser, CsvStatementParser>();
builder.Services.AddScoped<IStatementParser, ExcelStatementParser>();
builder.Services.AddScoped<StatementImportService>();
builder.Services.AddScoped<SegAccountTypeService>();
builder.Services.AddScoped<IPermissionGuard, PermissionGuard>();
builder.Services.AddScoped<SegCoaService>();
builder.Services.AddScoped<SegmentsSetupService>();
builder.Services.AddScoped<PurchaseReturnService>();
builder.Services.AddScoped<CreditNoteService>();
builder.Services.AddScoped<InventoryValuationService>();
builder.Services.AddHostedService<Primafit_ERP.Services.DepreciationWorker>();
builder.Services.AddScoped<ReportExportService>();
builder.Services.AddScoped<OperationalReportingService>();
builder.Services.AddScoped<FinancialReportingService>();
builder.Services.AddScoped<AssetCategoryService>();
builder.Services.AddScoped<ShipmentService>();
builder.Services.AddScoped<PayrollJournalService>();
builder.Services.AddScoped<PayrollService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<ComplianceService>();
builder.Services.AddScoped<PayslipService>();
builder.Services.AddScoped<PayrollReportingService>();
builder.Services.AddScoped<PayrollSettingsService>();
builder.Services.AddScoped<HrSetupService>();
builder.Services.AddScoped<PermissionCacheService>();
builder.Services.AddScoped<IPermissionGuard, PermissionGuard>();
var app = builder.Build();

// --- 5. HTTP REQUEST PIPELINE ---
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        // 1. Get the database context and role manager
        var context = services.GetRequiredService<PrimafitERP.Data.AppDbContext>(); // Adjust namespace if needed
        var roleManager = services.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Primafit_ERP.Components.Models.ApplicationRole>>();

        // 2. Automatically apply any pending EF Core migrations
        if (context.Database.GetPendingMigrations().Any())
        {
            context.Database.Migrate();
        }

        // 3. Execute your Seeder
        await Primafit_ERP.Data.Seed.RbacSeeder.SeedAsync(context, roleManager);
    }
    catch (Exception ex)
    {
        // Log any errors that happen during seeding
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();