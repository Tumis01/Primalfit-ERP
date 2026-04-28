using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Primafit_ERP.Components.Models;
using Primafit_ERP.Services;

namespace PrimafitERP.Api.Controllers;

[ApiController]
[Route("api/[controller]")] 
[Authorize]
public class CompanyDetailsController : ControllerBase
{
    private readonly CompanyApiService _companyService;

    public CompanyDetailsController(CompanyApiService companyService)
    {
        _companyService = companyService;
    }

    
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CompanyDetails>> Get(Guid id)
    {
        var item = await _companyService.GetCompanyByIdAsync(id);
        if (item == null) return NotFound(new { message = $"Company with ID {id} not found." });

        return Ok(item);
    }

    //[HttpPost]
    //public async Task<ActionResult<CompanyDetails>> Create([FromBody] CompanyDetails model)
    //{
    //    if (!ModelState.IsValid) return BadRequest(ModelState);

    //    var success = await _companyService.CreateCompanyAsync(model);
    //    if (!success) return BadRequest(new { message = "Could not create company. Ensure all required fields are provided." });

    //    return CreatedAtAction(nameof(Get), new { id = model.CompanyDetailsId }, model);
    //}

    //[HttpPut("{id:guid}")]
    //public async Task<IActionResult> Update(Guid id, [FromBody] CompanyDetails model)
    //{
    //    if (id != model.CompanyDetailsId) return BadRequest(new { message = "ID Mismatch between URL and Body." });
    //    if (!ModelState.IsValid) return BadRequest(ModelState);

    //    var success = await _companyService.UpdateCompanyAsync(model);
    //    if (!success) return NotFound(new { message = "Company not found or update failed." });

    //    return NoContent();
    //}

    //[HttpDelete("{id:guid}")]
    //public async Task<IActionResult> Delete(Guid id)
    //{
    //    var success = await _companyService.DeleteCompanyAsync(id);
    //    if (!success) return NotFound(new { message = "Company not found." });
    //    return NoContent();
    //}
}