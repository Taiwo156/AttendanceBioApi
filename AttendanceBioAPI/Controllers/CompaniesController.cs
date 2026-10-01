using AttendanceBioApi.Common.Responses;
using AttendanceBioAPI.Data.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceBioApi.Controllers;

[ApiController]
[Route("api/companies")]
[Authorize]

public class CompaniesController : ControllerBase
{
    private readonly ICompanyRepository _repository;

    public CompaniesController(ICompanyRepository repository)
    {
        _repository = repository;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetCompanies(
        CancellationToken cancellationToken)
    {
        try
        {
            var companies = await _repository.GetCompaniesAsync(
                cancellationToken);

            return Ok(new StatusMessage<IReadOnlyCollection<Models.Company>>
            {
                Status = "Success",
                Code = "200",
                Message = "Companies retrieved successfully.",
                Data = companies
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "500",
                    Message = ex.Message,
                    Data = null
                });
        }
    }

    [HttpGet("{companyId}/branches")]
    public async Task<IActionResult> GetBranches(
        string companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var branches = await _repository.GetBranchesAsync(
                companyId,
                cancellationToken);

            return Ok(new StatusMessage<IReadOnlyCollection<Models.CompanyBranch>>
            {
                Status = "Success",
                Code = "200",
                Message = "Company branches retrieved successfully.",
                Data = branches
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500,
                new StatusMessage<object>
                {
                    Status = "Failed",
                    Code = "500",
                    Message = ex.Message,
                    Data = null
                });
        }
    }
}