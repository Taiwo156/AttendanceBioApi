namespace AttendanceBioApi.Models.Request
{
    public class BiometricDeviceRequest
    {
        public string CompanyId { get; set; } = string.Empty;

        public string DivisionId { get; set; } = string.Empty;

        public string DepartmentId { get; set; } = string.Empty;

        public string BranchCode { get; set; } = string.Empty;

        public string DeviceName { get; set; } = string.Empty;

        public string Manufacturer { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public string SerialNumber { get; set; } = string.Empty;

        public int DeviceNumber { get; set; }

        public string DeviceType { get; set; } = string.Empty;

        public string IpAddress { get; set; } = string.Empty;

        public int Port { get; set; } = 4370;

        public int CommKey { get; set; }

        public bool Enabled { get; set; } = true;

        public bool AutomaticSync { get; set; } = true;
    }
}
