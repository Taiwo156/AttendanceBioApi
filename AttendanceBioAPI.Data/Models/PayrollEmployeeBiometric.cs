namespace AttendanceBioApi.Models;

public class PayrollEmployeeBiometric
{
    public string CompanyId { get; set; } = string.Empty;
    public string DivisionId { get; set; } = string.Empty;
    public string DepartmentId { get; set; } = string.Empty;

    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;

    public string EmployeeBiometricId { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;

    public bool Active { get; set; }
}