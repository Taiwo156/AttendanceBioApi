namespace AttendanceBioApi.Models.Auth;

public class UserLocationScope
{
    public int LocationScopeId { get; set; }

    public string BaseCompanyId { get; set; } = string.Empty;
    public string BaseDivisionId { get; set; } = string.Empty;
    public string BaseDepartmentId { get; set; } = string.Empty;

    public string TargetCompanyId { get; set; } = string.Empty;
    public string TargetDivisionId { get; set; } = string.Empty;
    public string TargetDepartmentId { get; set; } = string.Empty;

    public string? TargetBranchCode { get; set; }

    public string ScopeType { get; set; } = string.Empty;

    public bool IsHome { get; set; }
    public bool IsActive { get; set; }
}