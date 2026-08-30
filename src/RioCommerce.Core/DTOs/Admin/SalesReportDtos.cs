namespace RioCommerce.Core.DTOs.Admin;

public record SourceRevenue(string Source, int Orders, decimal Revenue);
public record TopProduct(string Title, int Orders, decimal Revenue);
public record DailyRevenue(string Date, decimal Revenue);

public record SalesReport(
    decimal RevenueTotal, decimal RevenueMtd, decimal RevenueToday,
    int OrdersTotal, int OrdersPaid, decimal AvgOrderValue,
    List<SourceRevenue> BySource, List<TopProduct> TopProducts, List<DailyRevenue> Last14Days);
