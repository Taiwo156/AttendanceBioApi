using AttendanceBioApi.Common.Responses;
using AttendanceBioApi.Models;
using AttendanceBioAPI.Data.Interfaces;
using AttendanceBioAPI.Service.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace AttendanceBioApi.Controllers;

[ApiController]
[Route("api/attendance")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private static readonly Regex DateFormat =
        new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    private readonly IAttendanceQueryService _queryService;
    private readonly IBiometricDeviceRepository _biometricDeviceRepository;
    private readonly ILogger<AttendanceController> _logger;

    public AttendanceController(
        IAttendanceQueryService queryService,
        IBiometricDeviceRepository biometricDeviceRepository,
        ILogger<AttendanceController> logger)
    {
        _queryService = queryService;
        _biometricDeviceRepository = biometricDeviceRepository;
        _logger = logger;
    }

    [HttpGet("current-status")]
    public async Task<IActionResult> GetCurrentStatus()
    {
        Console.WriteLine("ATTENDANCE CONTROLLER WAS HIT ");
        try
        {
            var result = await _queryService.GetCurrentStatusAsync();

            return Ok(new StatusMessage<object>
            {
                Status = "Success",
                Code = "200",
                Message = "Current attendance status retrieved successfully.",
                Data = result
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "500",
                    Message = ex.Message,
                    Data = null
                });
        }
    }

    [HttpGet("report")]
    public async Task<IActionResult> GetReport([FromQuery] string from,[FromQuery] string to,[FromQuery] string? employeeId)
    {
        if (string.IsNullOrWhiteSpace(from) ||
            string.IsNullOrWhiteSpace(to) ||
            !DateFormat.IsMatch(from) ||
            !DateFormat.IsMatch(to))
        {
            return BadRequest(new StatusMessage<object>
            {
                Status = "Failed",
                Code = "400",
                Message = "from and to are required and must use the format YYYY-MM-DD.",
                Data = null
            });
        }

        if (!DateTime.TryParse(
                $"{from} 00:00:00",
                out var localFrom) ||
            !DateTime.TryParse(
                $"{to} 23:59:59",
                out var localTo))
        {
            return BadRequest(new StatusMessage<object>
            {
                Status = "Failed",
                Code = "400",
                Message = "Invalid date range.",
                Data = null
            });
        }

        if (localFrom > localTo)
        {
            return BadRequest(new StatusMessage<object>
            {
                Status = "Failed",
                Code = "400",
                Message = "The from date cannot be later than the to date.",
                Data = null
            });
        }

        try
        {
            var result = await _queryService.GetReportAsync(
                localFrom,
                localTo,
                employeeId);

            return Ok(new StatusMessage<object>
            {
                Status = "Success",
                Code = "200",
                Message = "Attendance report retrieved successfully.",
                Data = result
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "500",
                    Message = ex.Message,
                    Data = null
                });
        }
    }

    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees()
    {
        try
        {
            var result = await _queryService.GetEmployeesAsync();

            return Ok(new StatusMessage<object>
            {
                Status = "Success",
                Code = "200",
                Message = "Employees retrieved successfully.",
                Data = result
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "500",
                    Message = ex.Message,
                    Data = null
                });
        }
    }

    [HttpGet("leaves")]
    public async Task<IActionResult> GetLeaves(
        [FromQuery] string? startDate,
        [FromQuery] string? endDate)
    {
        try
        {
            var result = await _queryService.GetLeavesAsync(
                startDate,
                endDate);

            return Ok(new StatusMessage<object>
            {
                Status = "Success",
                Code = "200",
                Message = "Leave records retrieved successfully.",
                Data = result
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "500",
                    Message = ex.Message,
                    Data = null
                });
        }
    }

    [HttpGet("sites")]
    public async Task<IActionResult> GetAttendanceSites(
        CancellationToken cancellationToken)
    {
        try
        {
            var devices =
                await _biometricDeviceRepository.GetAllAsync(
                    cancellationToken);

            var sites = devices
                .Where(x => x.Enabled)
                .Where(x => !string.IsNullOrWhiteSpace(x.BranchCode))
                .Select(x => new
                {
                    companyId = x.CompanyId,
                    siteId = x.BranchCode,
                    siteName = string.IsNullOrWhiteSpace(x.BranchName)
                        ? x.BranchCode
                        : x.BranchName
                })
                .Distinct()
                .OrderBy(x => x.companyId)
                .ThenBy(x => x.siteId)
                .ToList();

            _logger.LogInformation(
                "Attendance sites retrieved. Count={Count}",
                sites.Count);

            return Ok(sites);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to retrieve attendance sites.");

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    message = "Failed to retrieve attendance sites."
                });
        }
    }
}