namespace AttendanceBioApi.Models
{
    public class SyncStateRow
    {
        public string DeviceId { get; set; } = string.Empty;

        public DateTime? LastPunchTime { get; set; }

        public DateTime? LastAttemptAt { get; set; }

        public DateTime? LastSuccessAt { get; set; }

        public string? LastStatus { get; set; }

        public string? LastError { get; set; }

        public int? LastFetched { get; set; }

        public int? LastProcessed { get; set; }
    }

}
