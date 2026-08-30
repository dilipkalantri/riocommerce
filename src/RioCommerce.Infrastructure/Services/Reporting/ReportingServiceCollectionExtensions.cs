using RioCommerce.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace RioCommerce.Infrastructure.Services.Reporting;

/// <summary>
/// Registers the reporting module. One call rather than a dozen lines in <c>Program.cs</c>, and it
/// keeps <see cref="IReportBuilder"/> internal to this assembly — the report builders are an
/// implementation detail of <see cref="IReportingService"/>, not something the API or Web layers
/// should be able to reach past it and resolve individually.
/// </summary>
public static class ReportingServiceCollectionExtensions
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        // Shared line source for the three order-based reports.
        services.AddScoped<OrderLineSource>();

        // The eight builders (§27). Adding a ninth report is one class and one line here.
        services.AddScoped<IReportBuilder, SalesReportBuilder>();
        services.AddScoped<IReportBuilder, GstReportBuilder>();
        services.AddScoped<IReportBuilder, FacultyReportBuilder>();
        services.AddScoped<IReportBuilder, FranchiseeReportBuilder>();
        services.AddScoped<IReportBuilder, ProductSubjectReportBuilder>();
        services.AddScoped<IReportBuilder, ReInvoiceReportBuilder>();
        services.AddScoped<IReportBuilder, ShippingReportBuilder>();
        services.AddScoped<IReportBuilder, TeacherSettlementReportBuilder>();

        services.AddScoped<IReportingService>(sp => new ReportingService(
            sp.GetRequiredService<IEnumerable<IReportBuilder>>(),
            sp.GetRequiredService<Data.RioCommerceDbContext>()));

        services.AddScoped<IReportExportService, ReportExportService>();

        // Document/period services the new reports read from.
        services.AddScoped<IFranchiseReInvoiceService, FranchiseReInvoiceService>();
        services.AddScoped<ITeacherSettlementService, TeacherSettlementService>();

        return services;
    }
}
