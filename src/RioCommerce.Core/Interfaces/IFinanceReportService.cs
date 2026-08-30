using RioCommerce.Core.DTOs.Admin;
namespace RioCommerce.Core.Interfaces;

// Aggregated finance reporting over the order/payment/refund ledger (projection queries only — no entity loads).
public interface IFinanceReportService
{
    Task<RevenueSummaryDto> GetRevenueSummaryAsync();
    Task<RefundAnalyticsDto> GetRefundAnalyticsAsync();
    Task<PaymentAnalyticsDto> GetPaymentAnalyticsAsync();
    Task<List<NamedRevenueDto>> GetTopCoursesAsync(int take = 5);
    Task<List<NamedRevenueDto>> GetTopFacultyAsync(int take = 5);
    Task<List<NamedRevenueDto>> GetFranchiseRevenueAsync(int take = 5);
    Task<List<RevenueTrendPoint>> GetRevenueTrendAsync(int days = 14);
    Task<FinanceDashboardDto> GetDashboardAsync();
}
