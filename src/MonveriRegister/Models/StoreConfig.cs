namespace MonveriRegister.Models;

public class StoreConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public bool NickelRoundingEnabled { get; set; } = true;
    public bool LoyaltyEnabled { get; set; }
    public bool ServiceFeesEnabled { get; set; }
    public decimal ServiceFeePercent { get; set; }
    public bool ServiceFeeTaxable { get; set; }
}

public class Employee
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string PinHash { get; set; } = string.Empty;
    public int Level { get; set; }
    public string Status { get; set; } = "active";
}

public class SyncQueueItem
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty; // transaction, register_session
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty; // create, update, void, refund
    public string Payload { get; set; } = string.Empty; // JSON
    public string CreatedAt { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public string? LastAttempt { get; set; }
    public string? LastError { get; set; }
    public string Status { get; set; } = "pending"; // pending, synced, failed
}
