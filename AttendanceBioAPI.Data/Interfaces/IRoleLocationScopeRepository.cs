namespace AttendanceBioAPI.Data.Interfaces;

public interface IRoleLocationScopeRepository
{
    Task<bool> HasAccessAsync(
        string baseCompanyId,
        string baseDivisionId,
        string baseDepartmentId,
        int roleId,
        string targetCompanyId,
        string targetDivisionId,
        string targetDepartmentId,
        string? targetBranchCode);
}
