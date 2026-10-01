using AttendanceBioApi.Common.Responses;
using AttendanceBioApi.Models;
using AttendanceBioApi.Models.Request;
using AttendanceBioAPI.Data.Interfaces;
using AttendanceBioAPI.Service.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceBioApi.Controllers;

[ApiController]
[Route("api/devices")]
[Authorize]

public class DevicesController : ControllerBase
{
    private readonly IBiometricDeviceRepository _repository;
    private readonly ILogger<DevicesController> _logger;
    private readonly IBiometricDeviceFactory _deviceFactory;
    private readonly IDeviceSyncService _deviceSyncService;

    public DevicesController(
        IBiometricDeviceRepository repository,
        ILogger<DevicesController> logger,
        IBiometricDeviceFactory deviceFactory,
        IDeviceSyncService deviceSyncService
        )
    {
        _repository = repository;
        _logger = logger;
        _deviceFactory = deviceFactory;
        _deviceSyncService = deviceSyncService;
    }

    [HttpGet]
    public async Task<ActionResult<StatusMessage<IReadOnlyCollection<BiometricDevice>>>>
        GetAll(CancellationToken cancellationToken)
    {
        try
        {
            var devices = await _repository.GetAllAsync(cancellationToken);

            return Ok(new StatusMessage<IReadOnlyCollection<BiometricDevice>>
            {
                Status = "Success",
                Code = "200",
                Message = "Biometric devices retrieved successfully.",
                Data = devices
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve biometric devices.");

            return StatusCode(500,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "500",
                    Message = "Failed to retrieve biometric devices.",
                    Data = null
                });
        }
    }

    [HttpGet("{deviceId}")]
    public async Task<ActionResult<StatusMessage<BiometricDevice>>>
        GetById(
            string deviceId,
            CancellationToken cancellationToken)
    {
        try
        {
            var device = await _repository.GetByIdAsync(
                deviceId,
                cancellationToken);

            if (device is null)
            {
                return NotFound(
                    new StatusMessage<object>
                    {
                        Status = "Failed",
                        Code = "404",
                        Message = "Biometric device not found.",
                        Data = null
                    });
            }

            return Ok(new StatusMessage<BiometricDevice>
            {
                Status = "Success",
                Code = "200",
                Message = "Biometric device retrieved successfully.",
                Data = device
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to retrieve biometric device {DeviceId}.",
                deviceId);

            return StatusCode(500,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "500",
                    Message = "Failed to retrieve biometric device.",
                    Data = null
                });
        }
    }

    [HttpPost]
    public async Task<ActionResult<StatusMessage<BiometricDevice>>>
        Create(
            [FromBody] BiometricDeviceRequest request,
            CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.DeviceName))
            {
                return BadRequest(
                    new StatusMessage<object>
                    {
                        Status = "Failed",
                        Code = "400",
                        Message = "Device name is required.",
                        Data = null
                    });
            }

            if (string.IsNullOrWhiteSpace(request.CompanyId) ||
                string.IsNullOrWhiteSpace(request.DivisionId) ||
                string.IsNullOrWhiteSpace(request.DepartmentId) ||
                string.IsNullOrWhiteSpace(request.BranchCode))
            {
                return BadRequest(
                    new StatusMessage<object>
                    {
                        Status = "Failed",
                        Code = "400",
                        Message = "Company, division, department and branch are required.",
                        Data = null
                    });
            }



            var device = new BiometricDevice
            {
                DeviceId = Guid.NewGuid().ToString(),

                CompanyId = request.CompanyId.Trim(),
                DivisionId = request.DivisionId.Trim(),
                DepartmentId = request.DepartmentId.Trim(),
                BranchCode = request.BranchCode.Trim(),

                DeviceName = request.DeviceName.Trim(),
                Manufacturer = request.Manufacturer.Trim(),
                Model = request.Model.Trim(),
                SerialNumber = request.SerialNumber.Trim(),

                DeviceNumber = request.DeviceNumber,
                DeviceType = request.DeviceType.Trim(),

                IpAddress = request.IpAddress.Trim(),
                Port = request.Port <= 0 ? 4370 : request.Port,
                CommKey = request.CommKey,

                Enabled = request.Enabled,
                AutomaticSync = request.AutomaticSync
            };

            var created = await _repository.CreateAsync(
                device,
                cancellationToken);

            return StatusCode(201,
                new StatusMessage<BiometricDevice>
                {
                    Status = "Success",
                    Code = "201",
                    Message = "Biometric device created successfully.",
                    Data = created
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create biometric device.");

            return StatusCode(500,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "500",
                    Message = "Failed to create biometric device.",
                    Data = null
                });
        }
    }

    [HttpPut("{deviceId}")]
    public async Task<ActionResult<StatusMessage<object>>> Update(string deviceId, [FromBody] BiometricDeviceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _repository.GetByIdAsync(
                deviceId,
                cancellationToken);

            if (existing is null)
            {
                return NotFound(
                    new StatusMessage<object>
                    {
                        Status = "Failed",
                        Code = "404",
                        Message = "Biometric device not found.",
                        Data = null
                    });
            }

            existing.CompanyId = request.CompanyId.Trim();
            existing.DivisionId = request.DivisionId.Trim();
            existing.DepartmentId = request.DepartmentId.Trim();
            existing.BranchCode = request.BranchCode.Trim();

            existing.DeviceName = request.DeviceName.Trim();
            existing.Manufacturer = request.Manufacturer.Trim();
            existing.Model = request.Model.Trim();
            existing.SerialNumber = request.SerialNumber.Trim();

            existing.DeviceNumber = request.DeviceNumber;
            existing.DeviceType = request.DeviceType.Trim();

            existing.IpAddress = request.IpAddress.Trim();
            existing.Port = request.Port <= 0 ? 4370 : request.Port;
            existing.CommKey = request.CommKey;

            existing.Enabled = request.Enabled;
            existing.AutomaticSync = request.AutomaticSync;

            var updated = await _repository.UpdateAsync(existing, cancellationToken);
            _logger.LogInformation("UPDATE DEVICE RESULT: DeviceId={DeviceId}, Updated={Updated}",
                                deviceId,
                                updated);

            if (!updated)
            {
                return NotFound(
                    new StatusMessage<object>
                    {
                        Status = "Failed",
                        Code = "404",
                        Message = "Biometric device could not be updated.",
                        Data = null
                    });
            }

            return Ok(
                new StatusMessage<BiometricDevice>
                {
                    Status = "Success",
                    Code = "200",
                    Message = "Biometric device updated successfully.",
                    Data = existing
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to update biometric device {DeviceId}.",
                deviceId);

            return StatusCode(500,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "500",
                    Message = "Failed to update biometric device.",
                    Data = null
                });
        }
    }

    [HttpDelete("{deviceId}")]
    public async Task<ActionResult<StatusMessage<object>>>
        Delete(
            string deviceId,
            CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _repository.DeleteAsync(
                deviceId,
                cancellationToken);

            if (!deleted)
            {
                return NotFound(
                    new StatusMessage<object>
                    {
                        Status = "Failed",
                        Code = "404",
                        Message = "Biometric device not found.",
                        Data = null
                    });
            }

            return Ok(
                new StatusMessage<object>
                {
                    Status = "Success",
                    Code = "200",
                    Message = "Biometric device deleted successfully.",
                    Data = null
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to delete biometric device {DeviceId}.",
                deviceId);

            return StatusCode(500,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "500",
                    Message = "Failed to delete biometric device.",
                    Data = null
                });
        }
    }

    [HttpPost("{deviceId}/test-connection")]
    public async Task<ActionResult<StatusMessage<object>>>
    TestConnection(string deviceId, [FromServices] IBiometricDeviceFactory deviceFactory, CancellationToken cancellationToken)
    {
        try
        {
            var device = await deviceFactory.CreateAsync(
                deviceId,
                cancellationToken);

            if (device is null)
            {
                return NotFound(
                    new StatusMessage<object>
                    {
                        Status = "Failed",
                        Code = "404",
                        Message = "Biometric device not found.",
                        Data = null
                    });
            }

            var connected = await device.ConnectAsync(
                cancellationToken);

            if (!connected)
            {
                return StatusCode(
                    503,
                    new StatusMessage<object>
                    {
                        Status = "Failed",
                        Code = "503",
                        Message = "Could not connect to the biometric device.",
                        Data = null
                    });
            }

            await device.DisconnectAsync();

            return Ok(
                new StatusMessage<object>
                {
                    Status = "Success",
                    Code = "200",
                    Message = "Biometric device connection successful.",
                    Data = new
                    {
                        DeviceId = deviceId
                    }
                });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(
                499,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "499",
                    Message = "Biometric device connection was cancelled.",
                    Data = null
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to test biometric device {DeviceId}.",
                deviceId);

            return StatusCode(
                500,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "500",
                    Message = "An error occurred while testing the biometric device connection.",
                    Data = null
                });
        }
    }

    [HttpPost("test-connection")]
    public async Task<ActionResult<StatusMessage<object>>>
    TestConnectionBeforeSave([FromBody] BiometricDeviceRequest request, [FromServices] IBiometricDeviceFactory deviceFactory, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Manufacturer))
            {
                return BadRequest(
                    new StatusMessage<object>
                    {
                        Status = "Failed",
                        Code = "400",
                        Message = "Biometric manufacturer is required.",
                        Data = null
                    });
            }

            if (string.IsNullOrWhiteSpace(request.IpAddress))
            {
                return BadRequest(
                    new StatusMessage<object>
                    {
                        Status = "Failed",
                        Code = "400",
                        Message = "IP address is required.",
                        Data = null
                    });
            }

            if (request.Port <= 0)
            {
                return BadRequest(
                    new StatusMessage<object>
                    {
                        Status = "Failed",
                        Code = "400",
                        Message = "A valid device port is required.",
                        Data = null
                    });
            }

            var temporaryDevice = new BiometricDevice
            {
                DeviceId = Guid.NewGuid().ToString(),

                CompanyId = request.CompanyId?.Trim() ?? string.Empty,
                DivisionId = request.DivisionId?.Trim() ?? string.Empty,
                DepartmentId = request.DepartmentId?.Trim() ?? string.Empty,
                BranchCode = request.BranchCode?.Trim() ?? string.Empty,

                DeviceName = request.DeviceName?.Trim() ?? "Connection Test",
                Manufacturer = request.Manufacturer.Trim(),
                Model = request.Model?.Trim() ?? string.Empty,
                SerialNumber = request.SerialNumber?.Trim() ?? string.Empty,

                DeviceNumber = request.DeviceNumber,
                DeviceType = request.DeviceType?.Trim() ?? string.Empty,

                IpAddress = request.IpAddress.Trim(),
                Port = request.Port,
                CommKey = request.CommKey,

                Enabled = true,
                AutomaticSync = false
            };

            var device = deviceFactory.CreateForTest(
                temporaryDevice);

            var connected = await device.ConnectAsync(
                cancellationToken);

            if (!connected)
            {
                return StatusCode(
                    503,
                    new StatusMessage<object>
                    {
                        Status = "Failed",
                        Code = "503",
                        Message = "Could not connect to the biometric device.",
                        Data = null
                    });
            }

            await device.DisconnectAsync();

            return Ok(
                new StatusMessage<object>
                {
                    Status = "Success",
                    Code = "200",
                    Message = "Biometric device connection successful.",
                    Data = new
                    {
                        IpAddress = request.IpAddress,
                        Port = request.Port,
                        Manufacturer = request.Manufacturer
                    }
                });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(
                499,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "499",
                    Message = "Biometric device connection was cancelled.",
                    Data = null
                });
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(
                ex,
                "Unsupported biometric manufacturer during connection test.");

            return BadRequest(
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "400",
                    Message = ex.Message,
                    Data = null
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to test biometric device connection before save.");

            return StatusCode(
                500,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "500",
                    Message = "An error occurred while testing the biometric device connection.",
                    Data = null
                });
        }
    }

    [HttpPost("{deviceId}/test-attendance")]
    public async Task<IActionResult> TestAttendance(
    string deviceId,
    CancellationToken cancellationToken)
    {
        try
        {
            var device = await _deviceFactory.CreateAsync(
                deviceId,
                cancellationToken);

            if (device is null)
            {
                return NotFound(new
                {
                    message = $"Biometric device '{deviceId}' was not found."
                });
            }

            var connected = await device.ConnectAsync(cancellationToken);

            if (!connected)
            {
                return StatusCode(503, new
                {
                    message = "Could not connect to biometric device."
                });
            }

            try
            {
                var punches = await device.GetAttendancePunchesAsync(
                    cancellationToken);

                return Ok(new
                {
                    message = "Attendance records retrieved successfully.",
                    count = punches.Count,
                    data = punches
                });
            }
            finally
            {
                await device.DisconnectAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to read attendance records from biometric device {DeviceId}",
                deviceId);

            return StatusCode(500, new
            {
                message = ex.Message,
                type = ex.GetType().FullName
            });
        }
    }

    [HttpGet("sync-status")]
    public async Task<ActionResult<IReadOnlyCollection<DeviceSyncStatus>>> GetSyncStatus(
       CancellationToken cancellationToken)
    {
        try
        {
            var statuses = await _deviceSyncService.GetStatusesAsync(
                cancellationToken);

            return Ok(statuses);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(
                StatusCodes.Status408RequestTimeout,
                new
                {
                    message = "The request was cancelled or timed out."
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to retrieve biometric device sync statuses.");

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    message = "Unable to retrieve device sync status."
                });
        }
    }

    [HttpPost("{deviceId}/sync")] 
    public async Task<IActionResult> SyncDevice(
        [FromRoute] string deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _deviceSyncService.SyncDeviceAsync(deviceId, true, cancellationToken);

            if (result.Status == "NotFound")
            {
                return NotFound(new { status = "error", code = "404", message = result.Message });
            }

            if (result.Status == "Busy")
            {
                return Conflict(new { status = "error", code = "409", message = result.Message });
            }

            return Ok(new
            {
                status = "success",
                code = "200",
                message = "Sync executed completed.",
                data = result
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout, new
            {
                status = "error",
                code = "408",
                message = "The sync request timed out or was cancelled."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual sync failed for device {DeviceId}", deviceId);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                status = "error",
                code = "500",
                message = "An internal error occurred during sync."
            });
        }
    }
}