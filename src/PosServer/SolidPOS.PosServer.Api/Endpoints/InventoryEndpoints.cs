using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Inventory;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Contracts.Inventory;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class InventoryEndpoints
{
    public static RouteGroupBuilder MapInventoryEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/inventory")
            .WithTags("Inventory");

        group.MapGet("/stock", async Task<IResult> (
            [FromQuery] Guid? storeId,
            IInventoryStockService inventoryStockService,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyCollection<InventoryStockItemResponse>? stock = await inventoryStockService.GetCurrentStockAsync(storeId, cancellationToken);

            return stock is null
                ? Results.Problem(
                    title: "Inventory stock unavailable",
                    detail: "The tenant context is missing or the requested store is not available for this actor.",
                    statusCode: StatusCodes.Status403Forbidden,
                    type: "https://solidpos.local/problems/inventory-stock-unavailable")
                : Results.Ok(stock);
        })
        .RequireAuthorization(PermissionCodes.InventoryRead)
        .WithName("ListInventoryStock");

        group.MapPost("/adjustments", async Task<IResult> (
            [FromBody] CreateInventoryAdjustmentRequest request,
            IInventoryAdjustmentService inventoryAdjustmentService,
            CancellationToken cancellationToken) =>
        {
            InventoryAdjustmentResponse? adjustment = await inventoryAdjustmentService.CreateAsync(request, cancellationToken);

            return adjustment is null
                ? Results.Problem(
                    title: "Inventory adjustment rejected",
                    detail: "The adjustment failed validation. Check tenant, store, user, product, unit, quantity sign and permissions.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/inventory-adjustment-rejected")
                : Results.Created($"/api/v1/inventory/adjustments/{adjustment.Id}", adjustment);
        })
        .RequireAuthorization(PermissionCodes.InventoryAdjust)
        .WithName("CreateInventoryAdjustment");



        group.MapGet("/policy", async Task<IResult> (
            [FromQuery] Guid? storeId,
            IInventoryControlService inventoryControlService,
            CancellationToken cancellationToken) =>
        {
            InventoryPolicyResponse? policy = await inventoryControlService.GetPolicyAsync(storeId, cancellationToken);
            return policy is null
                ? Results.Problem(title: "Inventory policy unavailable", statusCode: StatusCodes.Status403Forbidden, type: "https://solidpos.local/problems/inventory-policy-unavailable")
                : Results.Ok(policy);
        })
        .RequireAuthorization(PermissionCodes.InventoryRead)
        .WithName("GetInventoryPolicy");

        group.MapPatch("/policy", async Task<IResult> (
            [FromBody] UpdateInventoryPolicyRequest request,
            IInventoryControlService inventoryControlService,
            CancellationToken cancellationToken) =>
        {
            InventoryPolicyResponse? policy = await inventoryControlService.UpsertPolicyAsync(request, cancellationToken);
            return policy is null
                ? Results.Problem(title: "Inventory policy rejected", statusCode: StatusCodes.Status409Conflict, type: "https://solidpos.local/problems/inventory-policy-rejected")
                : Results.Ok(policy);
        })
        .RequireAuthorization(PermissionCodes.InventoryControl)
        .WithName("UpdateInventoryPolicy");

        group.MapPost("/counts", async Task<IResult> (
            [FromBody] CreateInventoryCountRequest request,
            IInventoryControlService inventoryControlService,
            CancellationToken cancellationToken) =>
        {
            InventoryCountResponse? count = await inventoryControlService.CreateCountAsync(request, cancellationToken);
            return count is null
                ? Results.Problem(title: "Inventory count rejected", detail: "The count failed validation or inventory policy constraints.", statusCode: StatusCodes.Status409Conflict, type: "https://solidpos.local/problems/inventory-count-rejected")
                : Results.Created($"/api/v1/inventory/counts/{count.Id}", count);
        })
        .RequireAuthorization(PermissionCodes.InventoryCount)
        .WithName("CreateInventoryCount");

        group.MapGet("/counts", async Task<IResult> (
            [FromQuery] Guid? storeId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] int? limit,
            IInventoryControlService inventoryControlService,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyCollection<InventoryCountListItemResponse>? counts = await inventoryControlService.ListCountsAsync(new InventoryControlFilters(storeId, from, to, limit ?? 50), cancellationToken);
            return counts is null
                ? Results.Problem(title: "Inventory counts unavailable", statusCode: StatusCodes.Status403Forbidden, type: "https://solidpos.local/problems/inventory-counts-unavailable")
                : Results.Ok(counts);
        })
        .RequireAuthorization(PermissionCodes.InventoryRead)
        .WithName("ListInventoryCounts");

        group.MapPost("/transfers", async Task<IResult> (
            [FromBody] CreateInventoryTransferRequest request,
            IInventoryControlService inventoryControlService,
            CancellationToken cancellationToken) =>
        {
            InventoryTransferResponse? transfer = await inventoryControlService.CreateTransferAsync(request, cancellationToken);
            return transfer is null
                ? Results.Problem(title: "Inventory transfer rejected", detail: "The transfer failed validation or negative stock policy.", statusCode: StatusCodes.Status409Conflict, type: "https://solidpos.local/problems/inventory-transfer-rejected")
                : Results.Created($"/api/v1/inventory/transfers/{transfer.Id}", transfer);
        })
        .RequireAuthorization(PermissionCodes.InventoryTransfer)
        .WithName("CreateInventoryTransfer");

        group.MapGet("/transfers", async Task<IResult> (
            [FromQuery] Guid? storeId,
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery] int? limit,
            IInventoryControlService inventoryControlService,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyCollection<InventoryTransferListItemResponse>? transfers = await inventoryControlService.ListTransfersAsync(new InventoryControlFilters(storeId, from, to, limit ?? 50), cancellationToken);
            return transfers is null
                ? Results.Problem(title: "Inventory transfers unavailable", statusCode: StatusCodes.Status403Forbidden, type: "https://solidpos.local/problems/inventory-transfers-unavailable")
                : Results.Ok(transfers);
        })
        .RequireAuthorization(PermissionCodes.InventoryRead)
        .WithName("ListInventoryTransfers");

        group.MapGet("/low-stock", async Task<IResult> (
            [FromQuery] Guid? storeId,
            [FromQuery] int? limit,
            IInventoryControlService inventoryControlService,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyCollection<InventoryLowStockItemResponse>? items = await inventoryControlService.GetLowStockAsync(storeId, limit ?? 50, cancellationToken);
            return items is null
                ? Results.Problem(title: "Low stock unavailable", statusCode: StatusCodes.Status403Forbidden, type: "https://solidpos.local/problems/low-stock-unavailable")
                : Results.Ok(items);
        })
        .RequireAuthorization(PermissionCodes.InventoryRead)
        .WithName("ListLowStockInventory");

        return api;
    }
}
