namespace AttendanceBioAPI.Service.ViewModels
{
    public class AttendanceEventDto
    {
        public string EmployeeId { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ClockInTime { get; set; }
        public string? ClockOutTime { get; set; }
        public string? Timestamp { get; set; }
        public double? OverTime { get; set; }

        public double? ActualOvertimeHours { get; set; }
        public double? PlannedOvertimeHours { get; set; }
    }
}


