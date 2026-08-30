namespace RioCommerce.Core.DTOs.Admin;

public record MessageTemplateItem(Guid Id, string Key, string Name, string Channel, string Subject, bool IsActive);

public class MessageTemplateEditModel
{
    public Guid? Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Channel { get; set; } = "Email";
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Optional CC addresses, comma-separated, as typed by the admin. Validated on save by
    /// <c>CcRecipients.Parse</c> and stored normalised. Email channel only.</summary>
    public string? CcEmails { get; set; }
}

public record NotificationLogItem(
    Guid Id, string? TemplateKey, string Channel, string Recipient, string? Subject,
    string Status, string? Error, DateTime CreatedAt);

public record NotificationStats(int Templates, int Sent, int Failed);

public record TestSendRequest(string Key, string Recipient);
