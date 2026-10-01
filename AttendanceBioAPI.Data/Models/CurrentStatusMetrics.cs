namespace AttendanceBioApi.Models
{
    public class CurrentStatusMetrics
    {
        public int TotalPresent { get; set; }
        public int LateArrivals { get; set; }
        public int AbsentToday { get; set; }
        public int OnLeave { get; set; }
    }
}
