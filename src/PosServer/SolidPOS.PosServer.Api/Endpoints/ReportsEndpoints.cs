using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Reports;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Contracts.Reports;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class ReportsEndpoints
{
    public static RouteGroupBuilder MapReportsEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/reports")
            .WithTags("Reports");

        group.MapGet("/sales/range", async Task<IResult> (
            [FromQuery] string? storeId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            IReportsService reportsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuildDateRangeFilters(storeId, from, to, out ReportDateRangeFilters? filters, out IResult? invalid))
            {
                return invalid;
            }

            SalesRangeReportResponse? response = await reportsService.GetSalesRangeAsync(filters, cancellationToken);
            return response is null ? Rejected() : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.ReportsRead)
        .WithName("GetSalesRangeReport");

        group.MapGet("/sales/by-payment-method", async Task<IResult> (
            [FromQuery] string? storeId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            IReportsService reportsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuildDateRangeFilters(storeId, from, to, out ReportDateRangeFilters? filters, out IResult? invalid))
            {
                return invalid;
            }

            IReadOnlyCollection<PaymentMethodReportItemResponse>? response = await reportsService.GetSalesByPaymentMethodAsync(filters, cancellationToken);
            return response is null ? Rejected() : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.ReportsRead)
        .WithName("GetSalesByPaymentMethodReport");

        group.MapGet("/cash/shifts", async Task<IResult> (
            [FromQuery] string? storeId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] int limit,
            IReportsService reportsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuildListFilters(storeId, from, to, limit, out ReportListFilters? filters, out IResult? invalid))
            {
                return invalid;
            }

            IReadOnlyCollection<CashShiftReportItemResponse>? response = await reportsService.GetCashShiftsAsync(filters, cancellationToken);
            return response is null ? Rejected() : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.ReportsRead)
        .WithName("GetCashShiftReport");

        group.MapGet("/products/top", async Task<IResult> (
            [FromQuery] string? storeId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] int limit,
            IReportsService reportsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuildListFilters(storeId, from, to, limit, out ReportListFilters? filters, out IResult? invalid))
            {
                return invalid;
            }

            IReadOnlyCollection<TopProductReportItemResponse>? response = await reportsService.GetTopProductsAsync(filters, cancellationToken);
            return response is null ? Rejected() : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.ReportsRead)
        .WithName("GetTopProductsReport");

        group.MapGet("/inventory/negative", async Task<IResult> (
            [FromQuery] string? storeId,
            [FromQuery] int limit,
            IReportsService reportsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseOptionalGuid(storeId, "storeId", out Guid? parsedStoreId, out IResult? invalid))
            {
                return invalid;
            }

            IReadOnlyCollection<NegativeInventoryItemResponse>? response = await reportsService.GetNegativeInventoryAsync(parsedStoreId, limit, cancellationToken);
            return response is null ? Rejected() : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.ReportsRead)
        .WithName("GetNegativeInventoryReport");

        group.MapGet("/inventory/movements", async Task<IResult> (
            [FromQuery] string? storeId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] int limit,
            IReportsService reportsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuildListFilters(storeId, from, to, limit, out ReportListFilters? filters, out IResult? invalid))
            {
                return invalid;
            }

            IReadOnlyCollection<InventoryMovementReportItemResponse>? response = await reportsService.GetInventoryMovementsAsync(filters, cancellationToken);
            return response is null ? Rejected() : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.ReportsRead)
        .WithName("GetInventoryMovementsReport");

        group.MapGet("/dashboard/overview", async Task<IResult> (
            [FromQuery] string? storeId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] int limit,
            [FromQuery] string? trendBucket,
            IReportsService reportsService,
            CancellationToken cancellationToken) =>
        {
            if (!TryBuildDashboardFilters(storeId, from, to, limit, trendBucket, out DashboardReportFilters? filters, out IResult? invalid))
            {
                return invalid;
            }

            DashboardOverviewResponse? response = await reportsService.GetDashboardOverviewAsync(filters, cancellationToken);
            return response is null ? Rejected() : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.ReportsRead)
        .WithName("GetDashboardOverviewReport");

        return api;
    }

    private static bool TryBuildDateRangeFilters(
        string? storeIdValue,
        DateTimeOffset? from,
        DateTimeOffset? to,
        out ReportDateRangeFilters filters,
        out IResult invalid)
    {
        if (!TryParseOptionalGuid(storeIdValue, "storeId", out Guid? storeId, out invalid))
        {
            filters = null!;
            return false;
        }

        filters = new ReportDateRangeFilters(storeId, from, to);
        return true;
    }

    private static bool TryBuildListFilters(
        string? storeIdValue,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        out ReportListFilters filters,
        out IResult invalid)
    {
        if (!TryParseOptionalGuid(storeIdValue, "storeId", out Guid? storeId, out invalid))
        {
            filters = null!;
            return false;
        }

        filters = new ReportListFilters(storeId, from, to, limit);
        return true;
    }

    private static bool TryBuildDashboardFilters(
        string? storeIdValue,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        string? trendBucket,
        out DashboardReportFilters filters,
        out IResult invalid)
    {
        if (!TryParseOptionalGuid(storeIdValue, "storeId", out Guid? storeId, out invalid))
        {
            filters = null!;
            return false;
        }

        filters = new DashboardReportFilters(storeId, from, to, limit, trendBucket);
        return true;
    }

    private static bool TryParseOptionalGuid(
        string? value,
        string parameterName,
        out Guid? parsed,
        out IResult invalid)
    {
        parsed = null;
        invalid = null!;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Guid.TryParse(value, out Guid guid))
        {
            parsed = guid;
            return true;
        }

        invalid = Results.Problem(
            title: "Invalid report filter",
            detail: $"Query parameter '{parameterName}' must be a valid UUID.",
            statusCode: StatusCodes.Status400BadRequest,
            type: "https://solidpos.local/problems/invalid-report-filter");
        return false;
    }

    private static IResult Rejected()
    {
        return Results.Problem(
            title: "Report unavailable",
            detail: "The tenant context is missing or the report filter range is invalid.",
            statusCode: StatusCodes.Status409Conflict,
            type: "https://solidpos.local/problems/report-unavailable");
    }
}
