using AttendanceBioApi.Models;
using AttendanceBioAPI.Service.ViewModels;

namespace AttendanceBioAPI.Service.Interfaces;

public interface IAttendanceQueryService
{
    Task<CurrentStatusResponse> GetCurrentStatusAsync();
    Task<IEnumerable<EmployeeReportDto>> GetReportAsync(DateTime from, DateTime to, string? employeeId);
    Task<IEnumerable<EmployeeSummaryDto>> GetEmployeesAsync();
    Task<string> GetLeavesAsync(string? startDate, string? endDate);
}

