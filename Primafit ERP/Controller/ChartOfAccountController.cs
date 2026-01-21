using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Locks the entire controller
    public class ChartOfAccountsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ChartOfAccountsController(AppDbContext db)
        {
            _db = db;
        }

        // GET: Visible to Everyone
        [HttpGet]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer, Accountant, Viewer")]
        public async Task<ActionResult<List<ChartOfAccount>>> GetAccounts()
        {
            return await _db.ChartOfAccounts.OrderBy(a => a.AccountCode).ToListAsync();
        }

        // CREATE: Restricted to Admins & Finance
        [HttpPost]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer, Accountant")]
        public async Task<ActionResult<ChartOfAccount>> CreateAccount(ChartOfAccount model)
        {
            // Note: Logic logic is duplicated here for external API safety
            if (model.AccountId == Guid.Empty) model.AccountId = Guid.NewGuid();

            if (model.ParentAccountId != null)
            {
                var parent = await _db.ChartOfAccounts.FindAsync(model.ParentAccountId);
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

            _db.ChartOfAccounts.Add(model);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetAccounts), new { id = model.AccountId }, model);
        }

        // UPDATE: Restricted to Admins & Finance
        [HttpPut("{id}")]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer, Accountant")]
        public async Task<IActionResult> UpdateAccount(Guid id, ChartOfAccount model)
        {
            if (id != model.AccountId) return BadRequest();
            var entity = await _db.ChartOfAccounts.FindAsync(id);
            if (entity == null) return NotFound();

            entity.AccountCode = model.AccountCode;
            entity.AccountName = model.AccountName;
            entity.ParentAccountId = model.ParentAccountId;
            entity.IsParent = model.IsParent;
            entity.IsActive = model.IsActive;
            entity.Description = model.Description;
            entity.AllowManualEntry = model.AllowManualEntry;

            if (entity.ParentAccountId != null)
            {
                var parent = await _db.ChartOfAccounts.FindAsync(entity.ParentAccountId);
                if (parent != null) entity.Level = parent.Level + 1;
            }
            else
            {
                entity.Level = 1;
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: Restricted to SuperAdmin & CFO Only (No Accountants)
        [HttpDelete("{id}")]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer")]
        public async Task<IActionResult> DeleteAccount(Guid id)
        {
            var account = await _db.ChartOfAccounts.FindAsync(id);
            if (account == null) return NotFound();

            bool hasChildren = await _db.ChartOfAccounts.AnyAsync(x => x.ParentAccountId == id);
            if (hasChildren) return BadRequest("Cannot delete account with sub-accounts.");

            _db.ChartOfAccounts.Remove(account);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}