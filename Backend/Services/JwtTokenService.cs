using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

interface IJwtTokenService
{
    string GenerateToken(int userId);
    Task<bool> ValidateToken(string token);
    int GetUserId(string token);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly string _secretKey = Environment.GetEnvironmentVariable("JWTSecretKey") ?? throw new ArgumentNullException(nameof(_secretKey));
    private readonly string _issuer = "https://happy-cliff-0f5439d03.5.azurestaticapps.net";

    public string GenerateToken(int userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _issuer,
            claims: claims,
            expires: DateTime.Now.AddHours(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<bool> ValidateToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_secretKey);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _issuer,
            ValidAudience = _issuer,
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };


        var tokenValidationResult = await tokenHandler.ValidateTokenAsync(token, validationParameters);
        return tokenValidationResult.IsValid;
    }

    public int GetUserId(string token)
    {
        var jwtToken = new JwtSecurityToken(token);

        var userIdClaim = jwtToken.Claims.First(claim => claim.Type == JwtRegisteredClaimNames.Sub);
        return int.Parse(userIdClaim.Value);
    }
}

