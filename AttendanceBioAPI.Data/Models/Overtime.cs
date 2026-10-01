namespace AttendanceBioApi.Models;

public class Overtime
{
    public string OvertimeTypeID { get; set; } = string.Empty;

    public double? Duration { get; set; }

    public double? PlannedOvertime { get; set; }

    public DateTime? PlannedOvertimeStartTime { get; set; }

    public DateTime? PlannedOvertimeEndTime { get; set; }

    public DateTime? ActualOvertimeStartTime { get; set; }

    public DateTime? ActualOvertimeEndTime { get; set; }
}