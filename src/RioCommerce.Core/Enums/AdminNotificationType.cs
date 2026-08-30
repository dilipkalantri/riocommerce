namespace RioCommerce.Core.Enums;

// Admin notification-center categories (distinct from the customer messaging engine).
public enum AdminNotificationType
{
    NewOrder,
    PaymentSuccess,
    Refund,
    EnrollmentActivated,
    FranchisePayout,
    SystemAlert
}

public enum NotificationSeverity { Info, Success, Warning, Error }
