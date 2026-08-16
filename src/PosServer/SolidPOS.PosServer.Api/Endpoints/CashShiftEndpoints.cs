using Microsoft.AspNetCore.Mvc;
using SolidPOS.PosServer.Application.Cash;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Contracts.Cash;

namespace SolidPOS.PosServer.Api.Endpoints;

public static class CashShiftEndpoints
{
    public static RouteGroupBuilder MapCashShiftEndpoints(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/cash-drawers/shifts")
            .WithTags("Cash Drawer");

        group.MapGet("/current", async Task<IResult> (
            ICashShiftService cashShiftService,
            CancellationToken cancellationToken) =>
        {
            CashShiftResponse? shift = await cashShiftService.GetCurrentOpenShiftAsync(cancellationToken);

            return shift is null
                ? Results.NotFound(new
                {
                    type = "https://solidpos.local/problems/no-open-cash-shift",
                    title = "No open cash shift",
                    status = StatusCodes.Status404NotFound
                })
                : Results.Ok(shift);
        })
        .RequireAuthorization(PermissionCodes.CashOpen)
        .WithName("GetCurrentCashShift");

        group.MapPost("", async Task<IResult> (
            [FromBody] OpenCashShiftRequest request,
            ICashShiftService cashShiftService,
            CancellationToken cancellationToken) =>
        {
            CashShiftResponse? shift = await cashShiftService.OpenAsync(request, cancellationToken);

            return shift is null
                ? Results.Problem(
                    title: "Cash shift open rejected",
                    detail: "The terminal, user, store or current shift state does not allow opening a new shift.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/cash-shift-open-rejected")
                : Results.Created($"/api/v1/cash-drawers/shifts/{shift.Id}", shift);
        })
        .RequireAuthorization(PermissionCodes.CashOpen)
        .WithName("OpenCashShift");

        group.MapPost("/{shiftId:guid}/movements", async Task<IResult> (
            Guid shiftId,
            [FromBody] CreateCashMovementRequest request,
            ICashShiftService cashShiftService,
            CancellationToken cancellationToken) =>
        {
            CashMovementResponse? movement = await cashShiftService.CreateMovementAsync(shiftId, request, cancellationToken);

            return movement is null
                ? Results.Problem(
                    title: "Cash movement rejected",
                    detail: "The shift is not open or the movement is invalid.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/cash-movement-rejected")
                : Results.Created($"/api/v1/cash-drawers/shifts/{shiftId}/movements/{movement.Id}", movement);
        })
        .RequireAuthorization(PermissionCodes.CashMove)
        .WithName("CreateCashMovement");

        group.MapPost("/{shiftId:guid}/close", async Task<IResult> (
            Guid shiftId,
            [FromBody] CloseCashShiftRequest request,
            ICashShiftService cashShiftService,
            CancellationToken cancellationToken) =>
        {
            CashShiftResponse? shift = await cashShiftService.CloseAsync(shiftId, request, cancellationToken);

            return shift is null
                ? Results.Problem(
                    title: "Cash shift close rejected",
                    detail: "The shift is not open or the close request is invalid.",
                    statusCode: StatusCodes.Status409Conflict,
                    type: "https://solidpos.local/problems/cash-shift-close-rejected")
                : Results.Ok(shift);
        })
        .RequireAuthorization(PermissionCodes.CashClose)
        .WithName("CloseCashShift");

        return api;
    }
}

