namespace AttendanceBioAPI.Service.ViewModels.Auth;

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public UserProfileDto User { get; set; } = new();
}



