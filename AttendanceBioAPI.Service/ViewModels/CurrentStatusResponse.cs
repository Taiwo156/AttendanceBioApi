using AttendanceBioApi.Models;
using AttendanceBioAPI.Service.ViewModels;

namespace AttendanceBioAPI.Service.ViewModels
{
    public class CurrentStatusResponse
    {
        public CurrentStatusMetrics Metrics { get; set; } = new();
        public List<AttendanceEventDto> Events { get; set; } = new();
    }
}



