using AttendanceBioApi.Models.Auth;
using AttendanceBioAPI.Service.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace AttendanceBioAPI.Service.Auth;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal User =>
        _httpContextAccessor.HttpContext?.User
        ?? new ClaimsPrincipal();

    private string? GetClaim(string claimType)
    {
        return User.FindFirst(claimType)?.Value;
    }

    public bool IsAuthenticated =>
        User.Identity?.IsAuthenticated == true;

    public Guid UserId
    {
        get
        {
            var value = GetClaim("userId");

            return Guid.TryParse(value, out var id)
                ? id
                : Guid.Empty;
        }
    }

    public string Username =>
        GetClaim("username")
        ?? GetClaim(ClaimTypes.Name)
        ?? string.Empty;

    public string CompanyId =>
        GetClaim("companyId")
        ?? string.Empty;

    public string DivisionId =>
        GetClaim("divisionId")
        ?? string.Empty;

    public string DepartmentId =>
        GetClaim("departmentId")
        ?? string.Empty;

    public string? BranchCode =>
        GetClaim("branchCode");

    public int? RoleId
    {
        get
        {
            var value = GetClaim("roleId");

            return int.TryParse(value, out var roleId)
                ? roleId
                : null;
        }
    }

    public AuthenticatedUser GetCurrentUser()
    {
        return new AuthenticatedUser
        {
            Id = UserId,
            Username = Username,
            CompanyId = CompanyId,
            DivisionId = DivisionId,
            DepartmentId = DepartmentId,
            BranchCode = BranchCode,
            RoleId = RoleId,
            UserGroupId = GetClaim("userGroupId")
        };
    }
}
