namespace SolidPOS.PosServer.Application.Security;

public static class PermissionCodes
{
    public const string TenantManage = "tenant.manage";
    public const string StoresManage = "stores.manage";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string TerminalsManage = "terminals.manage";
    public const string TerminalsRegister = "terminals.register";
    public const string CatalogRead = "catalog.read";
    public const string CatalogManage = "catalog.manage";
    public const string CustomersRead = "customers.read";
    public const string CustomersManage = "customers.manage";
    public const string DiscountsRead = "discounts.read";
    public const string DiscountsManage = "discounts.manage";
    public const string DiscountsValidate = "discounts.validate";
    public const string InventoryRead = "inventory.read";
    public const string InventoryAdjust = "inventory.adjust";
    public const string InventoryControl = "inventory.control";
    public const string InventoryCount = "inventory.count";
    public const string InventoryTransfer = "inventory.transfer";
    public const string SalesRead = "sales.read";
    public const string SalesCreate = "sales.create";
    public const string SalesVoid = "sales.void";
    public const string ReturnsCreate = "returns.create";
    public const string ReturnsRead = "returns.read";
    public const string CashOpen = "cash.open";
    public const string CashClose = "cash.close";
    public const string CashMove = "cash.move";
    public const string ReportsRead = "reports.read";
    public const string SyncPush = "sync.push";
    public const string SyncPull = "sync.pull";
    public const string SyncConflictsRead = "sync.conflicts.read";
    public const string SyncConflictsResolve = "sync.conflicts.resolve";
    public const string BuilderManage = "builder.manage";
    public const string UpdatesManage = "updates.manage";
    public const string AuditRead = "audit.read";

    public static readonly IReadOnlyCollection<string> All =
    [
        TenantManage,
        StoresManage,
        UsersManage,
        RolesManage,
        TerminalsManage,
        TerminalsRegister,
        CatalogRead,
        CatalogManage,
        CustomersRead,
        CustomersManage,
        DiscountsRead,
        DiscountsManage,
        DiscountsValidate,
        InventoryRead,
        InventoryAdjust,
        InventoryControl,
        InventoryCount,
        InventoryTransfer,
        SalesRead,
        SalesCreate,
        SalesVoid,
        ReturnsCreate,
        ReturnsRead,
        CashOpen,
        CashClose,
        CashMove,
        ReportsRead,
        SyncPush,
        SyncPull,
        SyncConflictsRead,
        SyncConflictsResolve,
        BuilderManage,
        UpdatesManage,
        AuditRead
    ];
}
