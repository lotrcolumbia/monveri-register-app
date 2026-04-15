namespace MonveriRegister.Models;

public class RegisterSession
{
    public int Id { get; set; }
    public int? ServerId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string OpenedAt { get; set; } = string.Empty;
    public string? ClosedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public string? OpeningBreakdown { get; set; } // JSON
    public decimal? ClosingCash { get; set; }
    public string? ClosingBreakdown { get; set; } // JSON
    public string Status { get; set; } = "open";
    public int? SiteId { get; set; }
    public string? SiteName { get; set; }
    public bool IsTraining { get; set; }
    public int? TaxLocationId { get; set; }
    public string? Notes { get; set; }
    public bool IsSynced { get; set; }
}
