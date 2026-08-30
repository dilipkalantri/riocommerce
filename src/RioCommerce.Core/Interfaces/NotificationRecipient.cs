namespace RioCommerce.Core.Interfaces;

// Per-channel destination for a single notification event. The dispatcher picks the right
// field based on the channel of each matched MessageTemplate (Email/SMS/WhatsApp).
public record NotificationRecipient(string? Email = null, string? Phone = null, string? WhatsApp = null)
{
    public static NotificationRecipient EmailOnly(string email) => new(Email: email);
    public static NotificationRecipient PhoneOnly(string phone) => new(Phone: phone);
}
