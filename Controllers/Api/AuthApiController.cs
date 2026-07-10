using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace TaskFlow.Controllers.Api
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthApiController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly IConfiguration _config;

        public AuthApiController(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            IConfiguration config)
            {
            _userManager = userManager;
            _signInManager = signInManager;
            _config = config;
            }

            // POST /api/auth/login
            // Le client envoie : { "email" : "...", "password" : "..."}
            // Le serveur retourne : { "token" : "ejytdfliuuyu..." }
            // Le client stocke ce token et l'envoie dans chaque requête :
            // Authorization: Bearer ejytdfliuuyu...
            [HttpPost("login")]
            public async Task<IActionResult> Login(LoginRequest request)
            {
                // Chercher l'utilisateur par email dans Identity
                var user = await _userManager.FindByEmailAsync(request.Email);
                if (user == null)
                    return Unauthorized(new { message = "Identifiants incorrect" });

                // Vérifier le mot de passe - CheckPasswordSignInAsync compare
                // le mdp fourni avec le hash stocké en base.
                // lockoutOnFailure = true : verrouille après trop d'échecs
                var result = await _signInManager
                    .CheckPasswordSignInAsync(user, request.Password,
                                              lockoutOnFailure: true);
                
                if (!result.Succeeded)
                    return Unauthorized(new { message = "Mot de passe incorrect."});

                // Générer le token JWT
                // Claims = infos contenues dans le token.
                // Le token est signé mais pas chiffré. Pas mettre de données sensible.
                var claims = new[]
                {
                    // NameIdentifier = l'Id de l'utilisateur (GUID)
                    // C'est ce que GetUserId(User) lit dans les contrôleurs.
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Email, user.Email ?? ""),
                    new Claim(ClaimTypes.Name, user.UserName ?? "")
                };

                var key = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(
                       _config["Jwt:SecretKey"] 
                       ?? "TaskFlow-Super-Cle-Secrete-Only-Dev"
                    )
                );

                var token = new JwtSecurityToken(
                    claims : claims,
                    // Le token expire dans 24 heures
                    // En production : 15 min + refresh token.
                    expires: DateTime.UtcNow.AddHours(24),
                    signingCredentials: new SigningCredentials(
                        key, SecurityAlgorithms.HmacSha256)
                );

                var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

                return Ok(new
                {
                    token = tokenString,
                    email = user.Email,
                    userId = user.Id,
                    expires = DateTime.UtcNow.AddHours(24)
                });
            }
    }
    // Classe simple pour recevoir le login
    public record LoginRequest(string Email, string Password);
}