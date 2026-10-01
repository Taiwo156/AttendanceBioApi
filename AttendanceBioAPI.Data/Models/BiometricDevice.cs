namespace AttendanceBioApi.Models;

public class BiometricDevice
{
    public string DeviceId { get; set; } = string.Empty;

    public string CompanyId { get; set; } = string.Empty;

    public string DivisionId { get; set; } = string.Empty;

    public string DepartmentId { get; set; } = string.Empty;

    public string BranchCode { get; set; } = string.Empty;

    public string BranchName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public int DeviceNumber { get; set; }

    public string DeviceType { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; }

    public int CommKey { get; set; }

    public bool Enabled { get; set; }

    public bool AutomaticSync { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}