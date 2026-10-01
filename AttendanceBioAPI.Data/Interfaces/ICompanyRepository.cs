using AttendanceBioApi.Models;

namespace AttendanceBioAPI.Data.Interfaces;

public interface ICompanyRepository
{
    Task<IReadOnlyCollection<Company>> GetCompaniesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<CompanyBranch>> GetBranchesAsync(
        string companyId,
        CancellationToken cancellationToken = default);
}


