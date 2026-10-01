namespace AttendanceBioAPI.Service.ViewModels
{
    public class EmployeeSummaryDto
    {
        public string EmployeeId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? LastSeen { get; set; }
    }
}


