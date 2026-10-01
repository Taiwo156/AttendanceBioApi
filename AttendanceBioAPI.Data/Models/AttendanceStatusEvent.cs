using System.Text.Json.Serialization;

namespace AttendanceBioApi.Models;

public class AttendanceStatusEvent
{
    [JsonPropertyName("employeeId")]
    public string EmployeeId { get; set; } = string.Empty;

    [JsonPropertyName("employeeName")]
    public string EmployeeName { get; set; } = string.Empty;

    [JsonPropertyName("department")]
    public string Department { get; set; } = string.Empty;

    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("clockinTime")]
    public string? ClockinTime { get; set; }

    [JsonPropertyName("clockoutTime")]
    public string? ClockoutTime { get; set; }

    [JsonPropertyName("overTime")]
    public string? OverTime { get; set; }

    [JsonPropertyName("plannedOtStartTime")]
    public string? PlannedOtStartTime { get; set; }

    [JsonPropertyName("plannedOtEndTime")]
    public string? PlannedOtEndTime { get; set; }

    [JsonPropertyName("actualOtStartTime")]
    public string? ActualOtStartTime { get; set; }

    [JsonPropertyName("actualOtEndTime")]
    public string? ActualOtEndTime { get; set; }
}


