namespace AttendanceBioApi.Common.Responses;

public class StatusMessage<T>
{
    public string Status { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string Message { get; set; } = string.Empty;

    public T? Data { get; set; }
}