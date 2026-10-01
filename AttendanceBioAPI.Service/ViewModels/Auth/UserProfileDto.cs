namespace AttendanceBioAPI.Service.ViewModels.Auth;

public class UserProfileDto
{
    public Guid Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? MiddleName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string CompanyId { get; set; } = string.Empty;

    public string DivisionId { get; set; } = string.Empty;

    public string DepartmentId { get; set; } = string.Empty;

    public string? BranchCode { get; set; }

    public string? UserGroupId { get; set; }

    public int? RoleId { get; set; }
}


