using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Controllers
{
    [ApiController]
    [Route("api/company-details")]
    [Authorize] // Locks the controller
    public class CompanyDetailsController : ControllerBase
    {
        private readonly AppDbContext _db;

        public CompanyDetailsController(AppDbContext db)
        {
            _db = db;
        }

        // LIST - Visible to Everyone
        [HttpGet]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer, Accountant, Viewer")]
        public async Task<ActionResult<List<CompanyDetails>>> List()
        {
            var items = await _db.CompanyDetails
                .AsNoTracking()
                .OrderByDescending(x => x.CompanyDetailsId)
                .ToListAsync();

            return Ok(items);
        }

        // GET ONE - Visible to Everyone
        [HttpGet("{id:Guid}")]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer, Accountant, Viewer")]
        public async Task<ActionResult<CompanyDetails>> Get(Guid id)
        {
            var item = await _db.CompanyDetails
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyDetailsId == id);

            if (item == null) return NotFound();

            return Ok(item);
        }

        // CREATE - SuperAdmin, CFO & Accountant (Viewer blocked)
        [HttpPost]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer, Accountant")]
        public async Task<ActionResult<CompanyDetails>> Create(CompanyDetails model)
        {
            if (model.CompanyDetailsId == Guid.Empty) model.CompanyDetailsId = Guid.NewGuid();

            if (!string.IsNullOrEmpty(model.NewLogoBase64))
            {
                model.LogoPath = await SaveLogoAsync(model.NewLogoBase64, model.NewLogoExtension ?? ".png");
            }

            model.CreatedDate = DateTime.UtcNow;
            model.ModifiedDate = DateTime.UtcNow;

            _db.CompanyDetails.Add(model);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(Get), new { id = model.CompanyDetailsId }, model);
        }

        // UPDATE - SuperAdmin, CFO & Accountant (Viewer blocked)
        [HttpPut("{id:Guid}")]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer, Accountant")]
        public async Task<IActionResult> Update(Guid id, CompanyDetails model)
        {
            var entity = await _db.CompanyDetails.FirstOrDefaultAsync(x => x.CompanyDetailsId == id);
            if (entity is null) return NotFound();

            if (!string.IsNullOrEmpty(model.NewLogoBase64))
            {
                string newPath = await SaveLogoAsync(model.NewLogoBase64, model.NewLogoExtension ?? ".png");
                entity.LogoPath = newPath;
            }

            entity.CompanyName = model.CompanyName;
            entity.ComanyRegNumber = model.ComanyRegNumber;
            entity.TaxIdentidicationNum = model.TaxIdentidicationNum;
            entity.CompanyEmail = model.CompanyEmail;
            entity.PhysicalAddress = model.PhysicalAddress;
            entity.PostalAddress = model.PostalAddress;
            entity.FiscalStartYear = model.FiscalStartYear;
            entity.FiscalEndYear = model.FiscalEndYear;
            entity.country = model.country;
            entity.CompanyWebsite = model.CompanyWebsite;
            entity.FunctionalCurrency = model.FunctionalCurrency;
            entity.BaseCurrency = model.BaseCurrency;
            entity.Type = model.Type;
            entity.Status = model.Status;

            entity.ModifiedDate = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        // DELETE - SuperAdmin & CFO Only (Accountant & Viewer blocked)
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var item = await _db.CompanyDetails.FindAsync(id);
            if (item == null) return NotFound();

            _db.CompanyDetails.Remove(item);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        private async Task<string?> SaveLogoAsync(string base64Data, string extension)
        {
            if (string.IsNullOrEmpty(base64Data)) return null;

            var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "logos");
            if (!Directory.Exists(uploadPath))
                Directory.CreateDirectory(uploadPath);

            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadPath, fileName);

            var imageBytes = Convert.FromBase64String(base64Data);
            await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);

            return $"/uploads/logos/{fileName}";
        }
    }
}