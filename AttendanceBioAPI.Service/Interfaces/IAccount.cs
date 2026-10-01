using AttendanceBioApi.Models;

namespace AttendanceBioAPI.Service.Interfaces
{
    public interface IAccount
    {
        Task<ApiToken> GetAccess(string token);
    }
}

