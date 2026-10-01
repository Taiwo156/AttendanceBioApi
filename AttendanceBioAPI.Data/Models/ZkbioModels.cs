using System.Text.Json.Serialization;

namespace AttendanceBioApi.Models;

public class ZkbioDepartment
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("dept_name")]
    public string? DeptName { get; set; }

    [JsonPropertyName("parent_dept")]
    public ZkbioParentDeptRef? ParentDept { get; set; }

    [JsonIgnore]
    public int? ParentDeptId => ParentDept?.Id;
}

public class ZkbioParentDeptRef
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

public class ZkbioPagedResponse<T>
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("data")]
    public List<T>? Data { get; set; }
}
public class ZkbioEmployee
{
    [JsonPropertyName("emp_code")]
    public string EmpCode { get; set; } = string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("department")]
    public ZkbioDepartment? Department { get; set; }
}

public class ZkbioTransaction
{
    [JsonPropertyName("emp_code")]
    public string EmpCode { get; set; } = string.Empty;

    [JsonPropertyName("punch_time")]
    public string PunchTime { get; set; } = string.Empty;

    [JsonPropertyName("punch_state")]
    public string PunchState { get; set; } = string.Empty;
}

public class ZkbioOvertime
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("emp_code")]
    public string EmpCode { get; set; } = string.Empty;

    [JsonPropertyName("overtime_type")]
    public int OvertimeType { get; set; }

    [JsonPropertyName("start_time")]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("end_time")]
    public string EndTime { get; set; } = string.Empty;

    [JsonPropertyName("approval_status")]
    public int ApprovalStatus { get; set; }
}

public class ZkbioLoginResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}

public class ZkbioEmployeeListResponse
{
    [JsonPropertyName("data")]
    public List<ZkbioEmployee>? Data { get; set; }
}

public class ZkbioTransactionListResponse
{
    [JsonPropertyName("data")]
    public List<ZkbioTransaction>? Data { get; set; }
}

public class ZkbioOvertimeListResponse
{
    [JsonPropertyName("data")]
    public List<ZkbioOvertime>? Data { get; set; }
}
