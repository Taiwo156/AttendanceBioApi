using AttendanceBioApi.Models.Auth;
using AttendanceBioAPI.Service.Interfaces;
using AttendanceBioAPI.Service.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AttendanceBioAPI.Service.Auth;

public class JwtService : IJwtService
{
    private readonly JwtOptions _options;

    public JwtService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string GenerateAccessToken(AuthenticatedUser user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),

            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),

            new("userId", user.Id.ToString()),
            new("username", user.Username),

            new("companyId", user.CompanyId),
            new("divisionId", user.DivisionId),
            new("departmentId", user.DepartmentId)
        };

        if (!string.IsNullOrWhiteSpace(user.BranchCode))
        {
            claims.Add(new Claim("branchCode", user.BranchCode));
        }

        if (!string.IsNullOrWhiteSpace(user.UserGroupId))
        {
            claims.Add(new Claim("userGroupId", user.UserGroupId));
        }

        if (user.RoleId.HasValue)
        {
            claims.Add(new Claim(
                ClaimTypes.Role,
                user.RoleId.Value.ToString()));

            claims.Add(new Claim(
                "roleId",
                user.RoleId.Value.ToString()));
        }

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_options.Key));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var expiresAt = GetAccessTokenExpiry();

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public DateTime GetAccessTokenExpiry()
    {
        return DateTime.UtcNow.AddMinutes(
            _options.AccessTokenMinutes);
    }
}


