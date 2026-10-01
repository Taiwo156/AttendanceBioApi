
using AttendanceBioApi.Models.Auth;

namespace AttendanceBioAPI.Service.Interfaces;

public interface ICurrentUserService
{
    bool IsAuthenticated { get; }

    Guid UserId { get; }

    string Username { get; }

    string CompanyId { get; }

    string DivisionId { get; }

    string DepartmentId { get; }

    string? BranchCode { get; }

    int? RoleId { get; }

    AuthenticatedUser GetCurrentUser();
}

