namespace MonveriRegister.Models;

public class QuickButton
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string ButtonType { get; set; } = "product"; // product or category
    public string? CategoryId { get; set; }
    public string Color { get; set; } = "btn-success";
    public int SortOrder { get; set; }
}
