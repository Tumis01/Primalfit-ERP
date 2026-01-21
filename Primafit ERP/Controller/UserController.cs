using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;

namespace Primafit_ERP.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AppDbContext _db;

        public UsersController(UserManager<ApplicationUser> userManager, AppDbContext db)
        {
            _userManager = userManager;
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<List<UserDisplayDto>>> GetUsers()
        {
            var users = await _userManager.Users.ToListAsync();
            var displayList = new List<UserDisplayDto>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                var company = await _db.CompanyDetails.FindAsync(user.CompanyDetailsId);

                displayList.Add(new UserDisplayDto
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    Role = roles.FirstOrDefault() ?? "None",
                    CompanyName = company?.CompanyName ?? "Global / System",
                    IsActive = user.IsActive
                });
            }
            return displayList;
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] UserDto model)
        {
            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FirstName = model.FirstName,
                LastName = model.LastName,
                CompanyDetailsId = model.CompanyDetailsId,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (result.Succeeded)
            {
                if (!string.IsNullOrEmpty(model.RoleName))
                {
                    await _userManager.AddToRoleAsync(user, model.RoleName);
                }
                return Ok(user);
            }

            return BadRequest(result.Errors);
        }
    }
}