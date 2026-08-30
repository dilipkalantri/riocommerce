namespace RioCommerce.Core.Entities;
public class MessageTemplate : BaseEntity
{
    public string Key { get; set; } = string.Empty;     // unique trigger key, e.g. "order_confirmation"
    public string Name { get; set; } = string.Empty;
    public string Channel { get; set; } = "Email";       // Email | WhatsApp | SMS
    public string Subject { get; set; } = string.Empty;  // supports {{tokens}}
    public string Body { get; set; } = string.Empty;     // supports {{tokens}}
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optional comma-separated CC addresses for the Email channel — staff who should receive a copy
    /// of every message this template sends. Null/empty on every template unless an admin sets it,
    /// which is why it is nullable rather than defaulted.
    /// <para>Parsed and validated by <c>CcRecipients</c>; ignored by the SMS/WhatsApp channels.</para>
    /// </summary>
    public string? CcEmails { get; set; }
}
