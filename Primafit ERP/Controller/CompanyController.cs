using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Controllers;

[ApiController]
[Route("api/company-details")]
public class CompanyDetailsController : ControllerBase
{
    private readonly AppDbContext _db;

    public CompanyDetailsController(AppDbContext db)
    {
        _db = db;
    }

    // LIST
    [HttpGet]
    public async Task<ActionResult<List<CompanyDetails>>> List()
    {
        var items = await _db.CompanyDetails
            .AsNoTracking()
            .OrderByDescending(x => x.CompanyDetailsId)
            .ToListAsync();

        return Ok(items);
    }

    // GET: api/company-details
    [HttpGet("{id:Guid}")]
    public async Task<ActionResult<CompanyDetails>> Get(Guid id)
    {
        var item = await _db.CompanyDetails
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyDetailsId == id);

        if (item == null)
            return NotFound();

        return Ok(item);
    }


    //[HttpPost]
    //public async Task<ActionResult<CompanyDetails>> Create(CompanyDetails model)
    //{
    //    model.CreatedDate = DateTime.UtcNow;
    //    model.ModifiedDate = DateTime.UtcNow;

    //    _db.CompanyDetails.Add(model);
    //    await _db.SaveChangesAsync();

    //    // Points to GET BY ID, NOT LIST
    //    return CreatedAtAction(nameof(Get),
    //        new { id = model.CompanyDetailsId },
    //        model);
    //}
    [HttpPost]
    public async Task<ActionResult<CompanyDetails>> Create(CompanyDetails model)
    {
        if (model.CompanyDetailsId == Guid.Empty) model.CompanyDetailsId = Guid.NewGuid();

        // Handle Logo Upload
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


    // UPDATE
    [HttpPut("{id:Guid}")]
    public async Task<IActionResult> Update(Guid id, CompanyDetails model)
    {
        var entity = await _db.CompanyDetails.FirstOrDefaultAsync(x => x.CompanyDetailsId == id);
        if (entity is null) return NotFound();

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
        entity.CreatedDate = model.CreatedDate;
        entity.ModifiedDate = model.ModifiedDate;
        entity.Type = model.Type;
        entity.Status = model.Status;

        await _db.SaveChangesAsync();
        return NoContent();
    }
    [HttpDelete("{id:guid}")]
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

        // 1. Prepare the folder
        // Ensure "wwwroot/uploads/logos" exists
        var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "logos");
        if (!Directory.Exists(uploadPath))
            Directory.CreateDirectory(uploadPath);

        // 2. Create a unique filename
        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadPath, fileName);

        // 3. Convert Base64 back to Bytes and write to disk
        var imageBytes = Convert.FromBase64String(base64Data);
        await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);

        // 4. Return the relative URL to be stored in DB
        return $"/uploads/logos/{fileName}";
    }
}
