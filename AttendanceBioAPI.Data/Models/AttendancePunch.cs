namespace AttendanceBioApi.Models;

public class AttendancePunch
{
    public string BiometricId { get; set; } = string.Empty;

    public DateTime PunchTime { get; set; }

    public string PunchState { get; set; } = string.Empty;

    public string? DeviceId { get; set; }

    public string? SiteId { get; set; }
}