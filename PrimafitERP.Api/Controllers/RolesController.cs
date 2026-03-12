using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;

namespace PrimafitERP.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RolesController : ControllerBase
    {
        private readonly RoleManager<ApplicationRole> _roleManager;

        public RolesController(RoleManager<ApplicationRole> roleManager)
        {
            _roleManager = roleManager;
        }

        [HttpGet]
        public async Task<ActionResult<List<ApplicationRole>>> GetRoles()
        {
            return await _roleManager.Roles.ToListAsync();
        }

        [HttpPost]
        public async Task<IActionResult> CreateRole([FromBody] ApplicationRole model)
        {
            if (string.IsNullOrWhiteSpace(model.Name)) return BadRequest("Role Name Required");

            model.Id = Guid.NewGuid().ToString(); // Ensure ID is generated
            model.CreatedDate = DateTime.UtcNow;

            var result = await _roleManager.CreateAsync(model);
            if (result.Succeeded) return Ok(model);

            return BadRequest(result.Errors);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRole(string id)
        {
            var role = await _roleManager.FindByIdAsync(id);
            if (role == null) return NotFound();
            await _roleManager.DeleteAsync(role);
            return NoContent();
        }
    }
}