namespace AttendanceBioApi.Models
{
    public class DeviceSyncResult
    {
        public string DeviceId { get; set; } = string.Empty;

        public string DeviceName { get; set; } = string.Empty;

        public string Status { get; set; } = "Success";

        public string Message { get; set; } = string.Empty;

        public int Fetched { get; set; }

        public int NewPunches { get; set; }

        public int Processed { get; set; }

        public int Failed { get; set; }

        public DateTime StartedAt { get; set; }

        public DateTime FinishedAt { get; set; }
    }

}
