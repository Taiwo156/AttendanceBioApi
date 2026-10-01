namespace AttendanceBioApi.Models
{
    public class PayrollAttStatus
    {
        public DateTime? TimeIn { get; set; }

        public DateTime? ClockOutTimeOut { get; set; }

        public bool? ClockedIn { get; set; }

        public bool? ClockedOut { get; set; }
    }
}
