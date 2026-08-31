namespace RioCommerce.Core.Enums;

/// <summary>What a verification code authorises once verified.</summary>
public enum VerificationPurpose
{
    CustomerSignup = 0,
    FranchiseeApplication = 1,
    PasswordReset = 2,
    /// <summary>
    /// First password for a customer imported from the old website. Kept distinct from
    /// <see cref="PasswordReset"/> so the email reads "set up your password" rather than "reset",
    /// and so a code issued on one flow cannot be spent on the other.
    /// </summary>
    LegacyPasswordSetup = 3,
    SchoolRegistration = 4,
}

/// <summary>Delivery channel for a verification code.</summary>
public enum VerificationChannel
{
    Email = 0,
    Sms = 1,
}
