namespace AttendanceBioApi.Models
{
    public class AttendanceProcessingAlert
    {
        public string EmployeeCode { get; set; } = string.Empty;
        public string? EmployeeName { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
    }
}
