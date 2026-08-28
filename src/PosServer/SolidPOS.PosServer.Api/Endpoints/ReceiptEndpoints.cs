using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Receipts;
using SolidPOS.PosServer.Application.Sales;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Contracts.Receipts;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class ReceiptEndpoints
{
    public static RouteGroupBuilder MapReceiptEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/receipts")
            .WithTags("Receipts");

        group.MapGet("/public/{publicToken}", async Task<IResult> (
            [FromRoute] string publicToken,
            IDigitalReceiptService digitalReceiptService,
            CancellationToken cancellationToken) =>
        {
            DigitalReceiptResponse? receipt = await digitalReceiptService.GetPublicAsync(publicToken, cancellationToken);
            return receipt is null
                ? Results.NotFound()
                : Results.Ok(receipt);
        })
        .AllowAnonymous()
        .WithName("GetPublicDigitalReceipt");

        group.MapGet("/{saleId:guid}", async Task<IResult> (
            [FromRoute] Guid saleId,
            ISalesService salesService,
            CancellationToken cancellationToken) =>
        {
            ReceiptResponse? receipt = await salesService.GetReceiptAsync(saleId, cancellationToken);
            return receipt is null
                ? Results.NotFound()
                : Results.Ok(receipt);
        })
        .RequireAuthorization(PermissionCodes.SalesRead)
        .WithName("GetReceiptBySaleId");

        group.MapPost("/{saleId:guid}/issue", async Task<IResult> (
            [FromRoute] Guid saleId,
            [FromBody] IssueDigitalReceiptRequest? request,
            HttpContext httpContext,
            IDigitalReceiptService digitalReceiptService,
            CancellationToken cancellationToken) =>
        {
            DigitalReceiptResponse? receipt = await digitalReceiptService.IssueAsync(
                saleId,
                request,
                BuildPublicBaseUrl(httpContext),
                cancellationToken);
            return receipt is null
                ? Results.BadRequest(new { error = "INVALID_DIGITAL_RECEIPT_REQUEST" })
                : Results.Ok(receipt);
        })
        .RequireAuthorization(PermissionCodes.SalesRead)
        .WithName("IssueDigitalReceipt");

        group.MapGet("/{saleId:guid}/digital", async Task<IResult> (
            [FromRoute] Guid saleId,
            IDigitalReceiptService digitalReceiptService,
            CancellationToken cancellationToken) =>
        {
            DigitalReceiptResponse? receipt = await digitalReceiptService.GetBySaleIdAsync(saleId, cancellationToken);
            return receipt is null
                ? Results.NotFound()
                : Results.Ok(receipt);
        })
        .RequireAuthorization(PermissionCodes.SalesRead)
        .WithName("GetDigitalReceiptBySaleId");

        group.MapPost("/{saleId:guid}/email", async Task<IResult> (
            [FromRoute] Guid saleId,
            [FromBody] EmailReceiptRequest request,
            HttpContext httpContext,
            IDigitalReceiptService digitalReceiptService,
            CancellationToken cancellationToken) =>
        {
            EmailReceiptResponse? response = await digitalReceiptService.EmailStubAsync(
                saleId,
                request,
                BuildPublicBaseUrl(httpContext),
                cancellationToken);
            return response is null
                ? Results.BadRequest(new { error = "INVALID_RECEIPT_EMAIL_REQUEST" })
                : Results.Ok(response);
        })
        .RequireAuthorization(PermissionCodes.SalesRead)
        .WithName("EmailDigitalReceiptStub");

        return api;
    }

    private static string BuildPublicBaseUrl(HttpContext httpContext)
    {
        return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    }
}
