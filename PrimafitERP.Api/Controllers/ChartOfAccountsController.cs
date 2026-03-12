using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrimafitERP.Data;
using Primafit_ERP.Components.Models; // Your models namespace
using PrimafitERP.Api.DTOs;

namespace PrimafitERP.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChartOfAccountsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ChartOfAccountsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/ChartOfAccounts/{companyId}
        [HttpGet("{companyId:guid}")]
        public async Task<IActionResult> GetAccounts(Guid companyId)
        {
            // 1. Fetch the actual segmented accounts from the correct table
            var accounts = await _context.SegChartOfAccounts
                .Where(a => a.CompanyId == companyId)
                .AsNoTracking()
                .ToListAsync();

            // 2. Fetch the seeded account types so we can map the 'int' ID to the Description
            var accountTypes = await _context.Set<SegAccountType>().AsNoTracking().ToListAsync();

            // 3. Map the heavy DB models to our clean DTOs
            var response = accounts.Select(a => new SegAccountResponseDto
            {
                Id = a.Id,
                AccountCode = a.AccountCode,
                Description = a.Description,
                AccountType = accountTypes.FirstOrDefault(t => t.Id == a.SegAccountTypeId)?.Description ?? "Unknown Type",
                IsActive = a.IsActive,
                AllowJournal = a.AllowJournal
            })
            .OrderBy(a => a.AccountCode)
            .ToList();

            return Ok(response);
        }
    }
}