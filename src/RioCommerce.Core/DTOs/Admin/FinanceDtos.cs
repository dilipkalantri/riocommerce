namespace RioCommerce.Core.DTOs.Admin;

public record RevenueSummaryDto(
    decimal GrossRevenue, decimal NetRevenue, decimal RefundTotal, decimal PendingPayments,
    decimal Today, decimal Week, decimal Month);

public record RevenueTrendPoint(DateTime Date, decimal Revenue);

public record RefundAnalyticsDto(decimal TotalRefunded, int RefundCount, decimal RefundRate, int PendingApprovals);

public record MethodSliceDto(string Method, int Count, decimal Amount);

public record PaymentAnalyticsDto(int Total, int Success, int Failed, int Pending, decimal SuccessRate, List<MethodSliceDto> ByMethod);

public record NamedRevenueDto(string Name, int Orders, decimal Revenue);

public record FinanceDashboardDto(
    RevenueSummaryDto Revenue, RefundAnalyticsDto Refunds, PaymentAnalyticsDto Payments,
    List<NamedRevenueDto> TopCourses, List<NamedRevenueDto> TopFaculty, List<NamedRevenueDto> Franchises,
    List<RevenueTrendPoint> Trend);
