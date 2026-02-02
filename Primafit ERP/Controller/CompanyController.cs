using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Services; // Add this namespace

namespace Primafit_ERP.Controllers
{
    [ApiController]
    [Route("api/company-details")]
    [Authorize]
    public class CompanyDetailsController : ControllerBase
    {
        // Inject the Service instead of the DbContext
        private readonly CompanyApiService _companyService;

        public CompanyDetailsController(CompanyApiService companyService)
        {
            _companyService = companyService;
        }

        // LIST
        [HttpGet]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer, Accountant, Viewer")]
        public async Task<ActionResult<List<CompanyDetails>>> List()
        {
            // Delegate logic to the Service
            var items = await _companyService.GetCompaniesAsync();
            return Ok(items);
        }

        // GET ONE
        [HttpGet("{id:Guid}")]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer, Accountant, Viewer")]
        public async Task<ActionResult<CompanyDetails>> Get(Guid id)
        {
            var item = await _companyService.GetCompanyByIdAsync(id); 
            if (item == null) return NotFound();
            return Ok(item);
        }

        // CREATE
        [HttpPost]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer, Accountant")]
        public async Task<ActionResult<CompanyDetails>> Create(CompanyDetails model)
        {
            // The Service now handles ID generation, Date setting, and Logo Saving
            var success = await _companyService.CreateCompanyAsync(model);

            if (!success) return BadRequest("Could not create company.");

            return CreatedAtAction(nameof(Get), new { id = model.CompanyDetailsId }, model);
        }

        // UPDATE
        [HttpPut("{id:Guid}")]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer, Accountant")]
        public async Task<IActionResult> Update(Guid id, CompanyDetails model)
        {
            if (id != model.CompanyDetailsId) return BadRequest("ID Mismatch");

            // The Service handles Logo 
            var success = await _companyService.UpdateCompanyAsync(model);

            if (!success) return NotFound();

            return NoContent();
        }

        // DELETE
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = "SuperAdmin, Chief Of FInancial Officer")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var success = await _companyService.DeleteCompanyAsync(id);
            if (!success) return NotFound();

            return NoContent();
        }
    }
}