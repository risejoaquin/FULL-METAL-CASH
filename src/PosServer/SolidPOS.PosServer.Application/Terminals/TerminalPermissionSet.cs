using SolidPOS.PosServer.Application.Security;

namespace SolidPOS.PosServer.Application.Terminals;

public static class TerminalPermissionSet
{
    public static readonly IReadOnlyCollection<string> Default =
    [
        PermissionCodes.CatalogRead,
        PermissionCodes.InventoryRead,
        PermissionCodes.InventoryCount,
        PermissionCodes.CustomersRead,
        PermissionCodes.CustomersManage,
        PermissionCodes.DiscountsRead,
        PermissionCodes.DiscountsValidate,
        PermissionCodes.SalesCreate,
        PermissionCodes.SalesVoid,
        PermissionCodes.ReturnsCreate,
        PermissionCodes.ReturnsRead,
        PermissionCodes.CashOpen,
        PermissionCodes.CashClose,
        PermissionCodes.CashMove,
        PermissionCodes.SyncPush,
        PermissionCodes.SyncPull
    ];
}
