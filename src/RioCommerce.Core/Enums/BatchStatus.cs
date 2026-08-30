namespace RioCommerce.Core.Enums;

/// <summary>Current availability / batch state of a course product. Drives the storefront badge and the OutOfStock add-to-cart guard.</summary>
public enum BatchStatus
{
    Upcoming = 0,
    Ongoing = 1,
    PreRecorded = 2,
    OutOfStock = 3,
    ComingSoon = 4
}
