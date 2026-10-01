namespace AttendanceBioApi.Models.Auth;

public partial class Users
{
    public string CompanyId { get; set; } = string.Empty;

    public string DivisionId { get; set; } = string.Empty;

    public string DepartmentId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? MiddleName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? BranchCode { get; set; }

    public string? UserGroupId { get; set; }

    public int? RoleId { get; set; }

    public bool Active { get; set; }

    public bool? PasswordExpires { get; set; }

    public DateTime? PasswordExpiresDate { get; set; }

    public string? RefreshToken { get; set; }

    public DateTime? RefreshTokenExpiry { get; set; }

    public Guid Id { get; set; }
}