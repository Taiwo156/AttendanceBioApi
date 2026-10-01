namespace AttendanceBioAPI.Service.ViewModels.Auth;

public class LoginRequest
{
    public string CompanyId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}


