namespace AttendanceBioApi.Models
{
    public class DeviceSyncStatus
    {
        public string DeviceId { get; set; } = string.Empty;

        public string ConnectionStatus { get; set; } = "Unknown";

        public bool IsSyncing { get; set; }

        public DateTime? LastAttemptAt { get; set; }

        public DateTime? LastSuccessAt { get; set; }

        public DateTime? LastPunchTime { get; set; }

        public string? LastStatus { get; set; }

        public string? LastError { get; set; }

        public int LastFetched { get; set; }

        public int LastProcessed { get; set; }
    }

}
