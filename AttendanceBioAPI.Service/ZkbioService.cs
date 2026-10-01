using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using AttendanceBioApi.Models;
using AttendanceBioAPI.Service.Interfaces;

namespace AttendanceBioAPI.Service;

public class ZkbioService : IZkbioService
{
    private readonly ILogger<ZkbioService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly string _username;
    private readonly string _password;

    private string? _token;

    public ZkbioService(
        ILogger<ZkbioService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _baseUrl = configuration["Zkbio:BaseUrl"] ?? "http://localhost:8088";
        _username = configuration["Zkbio:Username"] ?? "admin";
        _password = configuration["Zkbio:Password"] ?? "";
    }

    public string? GetAuthToken() => _token;

    public async Task<bool> LoginAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"{_baseUrl}/api-token-auth/",
            new { username = _username, password = _password },
            ct);

        var data = await response.Content.ReadFromJsonAsync<ZkbioLoginResponse>(cancellationToken: ct);

        if (string.IsNullOrEmpty(data?.Token))
        {
            _logger.LogError("ZKBio login failed: {Status}", response.StatusCode);
            return false;
        }

        _token = data.Token;
        _logger.LogInformation("Logged in to ZKBio Time");
        return true;
    }

    public async Task<List<ZkbioEmployee>> GetEmployeesAsync(CancellationToken ct)
    => await GetAllPagesAsync<ZkbioEmployee>($"{_baseUrl}/personnel/api/employees/", ct);

    public async Task<List<ZkbioDepartment>> GetDepartmentsAsync(CancellationToken ct)
        => await GetAllPagesAsync<ZkbioDepartment>($"{_baseUrl}/personnel/api/departments/", ct);

    public async Task<List<ZkbioTransaction>> GetTransactionsAsync(
    DateTime startTime,
    CancellationToken ct)
    {
        var formattedStartTime = FormatDateTime(startTime);

        var url =
            $"{_baseUrl}/iclock/api/transactions/?start_time={Uri.EscapeDataString(formattedStartTime)}";

        _logger.LogInformation(
            "Fetching ZKBio transactions from {StartTime}. URL: {Url}",
            formattedStartTime,
            url);

        var transactions =
            await GetAllPagesAsync<ZkbioTransaction>(url, ct);

        _logger.LogInformation(
            "ZKBio transaction API returned {Count} transaction(s) from {StartTime}",
            transactions.Count,
            formattedStartTime);

        foreach (var transaction in transactions.TakeLast(10))
        {
            _logger.LogInformation(
                "ZKBio transaction: EmpCode={EmpCode}, PunchTime={PunchTime}, PunchState={PunchState}",
                transaction.EmpCode,
                transaction.PunchTime,
                transaction.PunchState);
        }

        return transactions;
    }

    public async Task<List<ZkbioOvertime>> GetOvertimesAsync(DateTime date, CancellationToken ct)
    {
        var dateStr = FormatDateOnly(date);
        var url = $"{_baseUrl}/att/api/overtimes/?start_date={dateStr}&end_date={dateStr}";
        return await GetAllPagesAsync<ZkbioOvertime>(url, ct);
    }

    private async Task<List<T>> GetAllPagesAsync<T>(string url, CancellationToken ct)
    {
        var results = new List<T>();
        string? nextUrl = url;
        var pageGuard = 0;

        while (!string.IsNullOrEmpty(nextUrl) && pageGuard++ < 100)
        {
            var page = await GetAsync<ZkbioPagedResponse<T>>(nextUrl, ct);
            if (page?.Data is not null)
            {
                results.AddRange(page.Data);
            }
            nextUrl = page?.Next;
        }

        if (pageGuard >= 100)
        {
            _logger.LogWarning("Pagination for {Url} stopped after 100 pages — possible pagination loop", url);
        }

        return results;
    }
    private async Task<T?> GetAsync<T>(string url, CancellationToken ct, bool isRetry = false)
    {
        if (_token is null && !await LoginAsync(ct))
        {
            return default;
        }

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Token {_token}");
        var response = await client.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !isRetry)
        {
            _logger.LogWarning("ZKBio token rejected, attempting re-login");
            return await LoginAsync(ct) ? await GetAsync<T>(url, ct, isRetry: true) : default;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("ZKBio request to {Url} returned {Status}", url, response.StatusCode);
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private static string FormatDateTime(DateTime date) => date.ToString("yyyy-MM-dd HH:mm:ss");
    private static string FormatDateOnly(DateTime date) => date.ToString("yyyy-MM-dd");
}

