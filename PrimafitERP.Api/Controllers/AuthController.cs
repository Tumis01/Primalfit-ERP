using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Primafit_ERP.Components.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace PrimafitERP.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;

        public AuthController(UserManager<ApplicationUser> userManager, IConfiguration configuration)
        {
            _userManager = userManager;
            _configuration = configuration;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto model)
        {
            if (string.IsNullOrEmpty(model.Email) || string.IsNullOrEmpty(model.Password))
                return BadRequest("Email and Password are required");

            // 1. Find the User
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null || !await _userManager.CheckPasswordAsync(user, model.Password))
                return Unauthorized("Invalid login credentials");

            // 2. Get User Roles from Database
            var userRoles = await _userManager.GetRolesAsync(user);

            // 3. Populate Base Identity Claims
            var authClaims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };

            // 4. Inject Multi-Tenant Company Context Claim
            if (user.CompanyDetailsId.HasValue && user.CompanyDetailsId != Guid.Empty)
            {
                authClaims.Add(new Claim("CompanyId", user.CompanyDetailsId.Value.ToString()));
            }

            // 5. Process Multi-Tenant Formatted Roles and Flag SuperAdmins
            foreach (var userRole in userRoles)
            {
                // Add the raw DB name (e.g., "00000000-0000..._CFO")
                authClaims.Add(new Claim(ClaimTypes.Role, userRole));

                // Extract clean role value by stripping out the Company ID prefix
                var separatorIndex = userRole.IndexOf('_');
                if (separatorIndex >= 0 && separatorIndex < userRole.Length - 1)
                {
                    var cleanRoleName = userRole.Substring(separatorIndex + 1);
                    authClaims.Add(new Claim("CleanRole", cleanRoleName));

                    // Triggers the safety validation bypass built into your PermissionGuard
                    if (cleanRoleName.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase))
                    {
                        authClaims.Add(new Claim("IsSuperAdmin", "true"));
                    }
                }
            }

            // 6. Generate Signed JWT Security Token
            var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                expires: DateTime.Now.AddHours(3),
                claims: authClaims,
                signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
            );

            return Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token),
                expiration = token.ValidTo
            });
        }
    }
}