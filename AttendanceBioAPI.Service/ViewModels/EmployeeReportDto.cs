namespace AttendanceBioAPI.Service.ViewModels
{
    public class EmployeeReportDto
    {
        public string EmployeeId { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public int DaysPresent { get; set; }
        public DateTime? FirstIn { get; set; }
        public DateTime? LastOut { get; set; }
        public int PunchCount { get; set; }
    }
}


