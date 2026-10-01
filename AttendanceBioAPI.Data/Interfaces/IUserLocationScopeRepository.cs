using AttendanceBioApi.Models.Auth;

namespace AttendanceBioAPI.Data.Interfaces;

public interface IUserLocationScopeRepository
{
    Task<bool> HasAccessAsync(
        string username,
        string baseCompanyId,
        string baseDivisionId,
        string baseDepartmentId,
        string targetCompanyId,
        string targetDivisionId,
        string targetDepartmentId,
        string? targetBranchCode);

    Task<IReadOnlyCollection<UserLocationScope>> GetScopesAsync(
        string username,
        string baseCompanyId);
}

