namespace RioCommerce.Core.Entities;
public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueType { get; set; } = "string";
    public string? Category { get; set; }
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
}
