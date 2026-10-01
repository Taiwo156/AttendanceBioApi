using AttendanceBioApi.Models.Auth;

namespace AttendanceBioAPI.Data.Interfaces;

public interface IUserRepository
{
    Task<Users?> GetByCredentialsAsync(
        string companyId,
        string username);

    Task<Users?> GetByRefreshTokenAsync(
        string refreshToken);

    Task UpdateRefreshTokenAsync(
        string companyId,
        string divisionId,
        string departmentId,
        string username,
        string refreshToken,
        DateTime refreshTokenExpiry);

    Task ClearRefreshTokenAsync(
        string companyId,
        string divisionId,
        string departmentId,
        string username);
}

