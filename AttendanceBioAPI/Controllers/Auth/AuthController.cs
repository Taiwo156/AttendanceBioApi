using AttendanceBioApi.Common.Responses;
using AttendanceBioAPI.Service.Interfaces;
using AttendanceBioAPI.Service.ViewModels.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceBioApi.Controllers.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const string RefreshTokenCookie = "workforce_refresh_token";

    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUserService;

    public AuthController(
        IAuthService authService,
        ICurrentUserService currentUserService)
    {
        _authService = authService;
        _currentUserService = currentUserService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);

        if (!result.Success)
        {
            return Unauthorized(new StatusMessage<object>
            {
                Status = "Failed",
                Code = "401",
                Message = result.Message,
                Data = null
            });
        }

        var user = result.Response!.User;


        return Ok(new StatusMessage<LoginResponse>
        {
            Status = "Success",
            Code = "200",
            Message = result.Message,
            Data = result.Response
        });
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.Cookies.TryGetValue(
                RefreshTokenCookie,
                out var refreshToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(new StatusMessage<object>
            {
                Status = "Failed",
                Code = "401",
                Message = "Refresh token is missing.",
                Data = null
            });
        }

        var result = await _authService.RefreshTokenAsync(
            refreshToken);

        if (!result.Success)
        {
            Response.Cookies.Delete(RefreshTokenCookie);

            return Unauthorized(new StatusMessage<object>
            {
                Status = "Failed",
                Code = "401",
                Message = result.Message,
                Data = null
            });
        }

        return Ok(new StatusMessage<LoginResponse>
        {
            Status = "Success",
            Code = "200",
            Message = result.Message,
            Data = result.Response
        });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var user = _currentUserService.GetCurrentUser();

        await _authService.LogoutAsync(user);

        Response.Cookies.Delete(RefreshTokenCookie);

        return Ok(new StatusMessage<object>
        {
            Status = "Success",
            Code = "200",
            Message = "Logout successful.",
            Data = null
        });
    }

    [Authorize]
    [HttpGet("current-user")]
    public IActionResult Me()
    {
        var user = _currentUserService.GetCurrentUser();

        return Ok(new StatusMessage<object>
        {
            Status = "Success",
            Code = "200",
            Message = "Current user retrieved successfully.",
            Data = user
        });
    }
}