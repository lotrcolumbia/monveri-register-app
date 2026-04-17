namespace MonveriRegister.Models;

public class TaxLocation
{
    public int Id { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public decimal TaxRate { get; set; }
    public int IsDefault { get; set; }
    public bool IsDefaultBool => IsDefault != 0;
    public List<TaxRate> Rates { get; set; } = new();
}

public class TaxRate
{
    public int Id { get; set; }
    public int LocationId { get; set; }
    public string TaxTypeName { get; set; } = string.Empty;
    public decimal Rate { get; set; }
}

public class TaxResult
{
    public decimal Total { get; set; }
    public decimal Rate { get; set; }
    public int? LocationId { get; set; }
    public string LocationName { get; set; } = "Unknown";
    public List<TaxBreakdownItem> Breakdown { get; set; } = new();
}

public class TaxBreakdownItem
{
    public string TaxTypeName { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
}
