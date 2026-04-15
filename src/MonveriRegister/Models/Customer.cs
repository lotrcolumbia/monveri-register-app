namespace MonveriRegister.Models;

public class Customer
{
    public int CustomerId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Company { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? LoyaltyCardNumber { get; set; }
    public int CurrentPoints { get; set; }
    public string? TierName { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;

    public string DisplayName
    {
        get
        {
            var name = $"{FirstName} {LastName}".Trim();
            if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(Company))
                return Company;
            return string.IsNullOrEmpty(name) ? $"Customer #{CustomerId}" : name;
        }
    }
}
