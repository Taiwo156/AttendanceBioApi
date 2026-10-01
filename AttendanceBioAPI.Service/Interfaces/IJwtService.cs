using AttendanceBioApi.Models.Auth;
using AttendanceBioApi.Models.Auth;

namespace AttendanceBioAPI.Service.Interfaces;

public interface IJwtService
{
    string GenerateAccessToken(AuthenticatedUser user);

    DateTime GetAccessTokenExpiry();
}


