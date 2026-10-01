using AttendanceBioApi.Models.Auth;
using AttendanceBioAPI.Data.Interfaces;
using AttendanceBioAPI.Service.Helpers;
using AttendanceBioAPI.Service.Interfaces;
using AttendanceBioAPI.Service.Security;
using AttendanceBioAPI.Service.ViewModels.Auth;
using Microsoft.Extensions.Options;
using PowerAPI.Service.Helper;
using System.Security.Cryptography;

namespace AttendanceBioAPI.Service.Auth;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IJwtService _jwtService;
    private readonly JwtOptions _jwtOptions;
    private readonly SecurityHelper _securityHelper;
    private readonly IUserLocationScopeRepository _userLocationScopeRepository;
    private readonly IRoleLocationScopeRepository _roleLocationScopeRepository;

    public AuthService(
    IUserRepository userRepository,
    IUserLocationScopeRepository userLocationScopeRepository,
    IRoleLocationScopeRepository roleLocationScopeRepository,
    IJwtService jwtService,
    IOptions<JwtOptions> jwtOptions,
    SecurityHelper securityHelper)
    {
        _userRepository = userRepository;
        _userLocationScopeRepository = userLocationScopeRepository;
        _roleLocationScopeRepository = roleLocationScopeRepository;
        _jwtService = jwtService;
        _jwtOptions = jwtOptions.Value;
        _securityHelper = securityHelper;
    }

    public async Task<(bool Success, string Message, LoginResponse? Response)> LoginAsync(
    LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyId) ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return (
                false,
                "Company, username and password are required.",
                null);
        }

        var user = await _userRepository.GetByCredentialsAsync(
            request.CompanyId.Trim(),
            request.Username.Trim());

        if (user is null)
        {
            return (
                false,
                "Invalid username, password or company.",
                null);
        }

        if (!user.Active)
        {
            return (
                false,
                "This user account is inactive.",
                null);
        }

        var providedPasswordHash = EnterpriseExtras.doConvertPwd(request.Password);

        if (!string.Equals(
                providedPasswordHash,
                user.Password,
                StringComparison.Ordinal))
        {
            return (
                false,
                "Invalid username, password or company.",
                null);
        }

        if (user.PasswordExpires == true &&
            user.PasswordExpiresDate.HasValue &&
            user.PasswordExpiresDate.Value < DateTime.UtcNow)
        {
            return (
                false,
                "Your password has expired.",
                null);
        }

        var authenticatedUser = ToAuthenticatedUser(user);

        var accessToken = _jwtService.GenerateAccessToken(
            authenticatedUser);

        var expiresAt = _jwtService.GetAccessTokenExpiry();

        var refreshToken = GenerateRefreshToken();

        var refreshTokenExpiry = DateTime.UtcNow.AddDays(
            _jwtOptions.RefreshTokenDays);

        await _userRepository.UpdateRefreshTokenAsync(
            user.CompanyId,
            user.DivisionId,
            user.DepartmentId,
            user.Username,
            refreshToken,
            refreshTokenExpiry);

        var response = new LoginResponse
        {
            AccessToken = accessToken,
            ExpiresAt = expiresAt,
            User = ToUserProfile(user)
        };

        return (
            true,
            "Login successful.",
            response);
    }

    public async Task<(bool Success, string Message, LoginResponse? Response)> RefreshTokenAsync(
        string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return (
                false,
                "Refresh token is required.",
                null);
        }

        var user = await _userRepository.GetByRefreshTokenAsync(
            refreshToken);

        if (user is null)
        {
            return (
                false,
                "Invalid refresh token.",
                null);
        }

        if (!user.Active)
        {
            return (
                false,
                "This user account is inactive.",
                null);
        }

        if (!user.RefreshTokenExpiry.HasValue ||
            user.RefreshTokenExpiry.Value <= DateTime.UtcNow)
        {
            return (
                false,
                "Refresh token has expired.",
                null);
        }

        var authenticatedUser = ToAuthenticatedUser(user);

        var accessToken = _jwtService.GenerateAccessToken(
            authenticatedUser);

        var expiresAt = _jwtService.GetAccessTokenExpiry();


        var newRefreshToken = GenerateRefreshToken();

        var newRefreshTokenExpiry = DateTime.UtcNow.AddDays(
            _jwtOptions.RefreshTokenDays);

        await _userRepository.UpdateRefreshTokenAsync(
            user.CompanyId,
            user.DivisionId,
            user.DepartmentId,
            user.Username,
            newRefreshToken,
            newRefreshTokenExpiry);

        var response = new LoginResponse
        {
            AccessToken = accessToken,
            ExpiresAt = expiresAt,
            User = ToUserProfile(user)
        };

        return (
            true,
            "Token refreshed successfully.",
            response);
    }

    public async Task LogoutAsync(AuthenticatedUser user)
    {
        await _userRepository.ClearRefreshTokenAsync(
            user.CompanyId,
            user.DivisionId,
            user.DepartmentId,
            user.Username);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);

        return Convert.ToBase64String(bytes);
    }

    private static AuthenticatedUser ToAuthenticatedUser(Users user)
    {
        return new AuthenticatedUser
        {
            Id = user.Id,
            Username = user.Username,
            CompanyId = user.CompanyId,
            DivisionId = user.DivisionId,
            DepartmentId = user.DepartmentId,
            FirstName = user.FirstName,
            MiddleName = user.MiddleName,
            LastName = user.LastName,
            Email = user.Email,
            Phone = user.Phone,
            BranchCode = user.BranchCode,
            UserGroupId = user.UserGroupId,
            RoleId = user.RoleId
        };
    }

    private static UserProfileDto ToUserProfile(Users user)
    {
        return new UserProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            FirstName = user.FirstName,
            MiddleName = user.MiddleName,
            LastName = user.LastName,
            Email = user.Email,
            Phone = user.Phone,
            CompanyId = user.CompanyId,
            DivisionId = user.DivisionId,
            DepartmentId = user.DepartmentId,
            BranchCode = user.BranchCode,
            UserGroupId = user.UserGroupId,
            RoleId = user.RoleId
        };
    }
}



