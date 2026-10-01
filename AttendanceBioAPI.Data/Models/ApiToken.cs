namespace AttendanceBioApi.Models
{
    public partial class ApiToken
    {
        public string CompanyId { get; set; }
        public string DivisionId { get; set; }
        public string DepartmentId { get; set; }
        public string Token { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public DateTime? DateCreated { get; set; }
        public DateTime SystemDate { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public string LockedBy { get; set; }
        public DateTime? LockTs { get; set; }
        public string RegCode { get; set; }
        public string RegName { get; set; }
        public int? TotalDays { get; set; }
        public string BranchCode { get; set; }
    }
}
