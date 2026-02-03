using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Primafit_ERP.Components;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Services;
using PrimafitERP.Data;

var builder = WebApplication.CreateBuilder(args);

// DATABASE CONFIGURATION
// Standard Context for Controllers
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// FIX IS HERE: Added 'ServiceLifetime.Scoped' to prevent the Singleton crash
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")),
    ServiceLifetime.Scoped);

//  IDENTITY CONFIGURATION
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options => {
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options => {
    options.LoginPath = "/login";
});

//  UI & FRAMEWORK SERVICES
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddControllers();
builder.Services.AddSignalR(e => { e.MaximumReceiveMessageSize = 10 * 1024 * 1024; });

//  APPLICATION SERVICES (Direct DB Access)
builder.Services.AddScoped<CompanyApiService>();
builder.Services.AddScoped<UserApiService>();
builder.Services.AddScoped<RoleApiService>();
builder.Services.AddScoped<GLSetupService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<CurrencyService>();
builder.Services.AddScoped<TaxService>();
builder.Services.AddScoped<AccountingPeriodService>();
builder.Services.AddScoped<GLOperationsService>();
builder.Services.AddScoped<ReconciliationService>();
builder.Services.AddScoped<WarehouseService>();
builder.Services.AddScoped<IPermissionGuard, PermissionGuard>();

//  AUTH SERVICE (API Based)
builder.Services.AddHttpClient<AuthApiService>(client =>
    client.BaseAddress = new Uri("https://localhost:7069/"));

var app = builder.Build();

//  DATABASE SEEDING
using (var scope = app.Services.CreateScope())
{
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

    if (!await roleManager.RoleExistsAsync("SuperAdmin"))
    {
        await roleManager.CreateAsync(new ApplicationRole("SuperAdmin", "System Administrator"));
    }

    if (await userManager.FindByEmailAsync("admin@primafit.com") == null)
    {
        var admin = new ApplicationUser
        {
            UserName = "admin@primafit.com",
            Email = "admin@primafit.com",
            FirstName = "Super",
            LastName = "Admin",
            EmailConfirmed = true
        };
        await userManager.CreateAsync(admin, "Password123!");
        await userManager.AddToRoleAsync(admin, "SuperAdmin");
    }
}

//  HTTP REQUEST PIPELINE
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

app.Run();