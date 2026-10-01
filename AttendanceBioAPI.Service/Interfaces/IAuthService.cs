using AttendanceBioApi.Models.Auth;
using AttendanceBioAPI.Service.ViewModels.Auth;

namespace AttendanceBioAPI.Service.Interfaces;

public interface IAuthService
{
    Task<(bool Success, string Message, LoginResponse? Response)> LoginAsync(
        LoginRequest request);

    Task<(bool Success, string Message, LoginResponse? Response)> RefreshTokenAsync(
        string refreshToken);

    Task LogoutAsync(AuthenticatedUser user);
}

