using System.Collections.Specialized;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Auth;
using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Wpf.ViewModels;

/// <summary>
/// Operative local cash checkout. No fake sales, automatic printer success or
/// remote ACK are inferred. A failed/ambiguous commit retains its operation ID.
/// </summary>
public sealed class ProductionCheckoutViewModel : ViewModelBase
{
    private readonly ILocalPosRepository repository;
    private readonly LocalAuthService auth;
    private readonly ProductionLoginViewModel login;
    private readonly ProductionCartViewModel cart;
    private readonly AtomicCashCheckoutService checkout;
    private readonly IClock clock;
    private LocalCashShift? shift;
    private AtomicCashCheckoutRequest? pendingRequest;
    private int openingAmountCents;
    private int tenderedCents;
    private bool busy;
    private string cashStatus = "Inicie sesión para consultar su caja.";
    private string saleStatus = "No hay un cobro confirmado.";

    public ProductionCheckoutViewModel(
        ILocalPosRepository repository, LocalAuthService auth,
        ProductionLoginViewModel login, ProductionCartViewModel cart,
        AtomicCashCheckoutService checkout, IClock clock)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.auth = auth ?? throw new ArgumentNullException(nameof(auth));
        this.login = login ?? throw new ArgumentNullException(nameof(login));
        this.cart = cart ?? throw new ArgumentNullException(nameof(cart));
        this.checkout = checkout ?? throw new ArgumentNullException(nameof(checkout));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        cart.Lines.CollectionChanged += (_, _) => RefreshCommands();
    }

    public int OpeningAmountCents { get => openingAmountCents; set => SetProperty(ref openingAmountCents, value); }
    public int TenderedCents { get => tenderedCents; set => SetProperty(ref tenderedCents, value); }
    public string CashStatus { get => cashStatus; private set => SetProperty(ref cashStatus, value); }
    public string SaleStatus { get => saleStatus; private set => SetProperty(ref saleStatus, value); }
    public bool CanOpenShift => !busy && login.ActiveSession is not null && shift is null && pendingRequest is null;
    public bool CanCheckout => !busy && login.ActiveSession is not null && shift is not null
        && (pendingRequest is not null || cart.Lines.Count > 0);
    public string ShiftLabel => shift is null ? "Sin caja abierta" : $"Caja abierta: {shift.Id}";
    public bool HasUncertainCheckout => pendingRequest is not null;

    public async Task RefreshShiftAsync()
    {
        if (busy) return;
        var session = login.ActiveSession;
        if (session is null)
        {
            shift = null;
            CashStatus = "Sin sesión verificada. No se permite operar caja.";
            RefreshCommands();
            return;
        }

        busy = true;
        RefreshCommands();
        try
        {
            var binding = await repository.GetTerminalBindingAsync();
            var open = await repository.GetOpenLocalCashShiftAsync();
            if (binding is null || binding.SchemaVersion != 4 ||
                binding.TenantId != session.TenantId || binding.StoreId != session.StoreId ||
                open is not null && (open.TenantId != binding.TenantId ||
                                     open.StoreId != binding.StoreId ||
                                     open.TerminalId != binding.TerminalId))
            {
                shift = null;
                CashStatus = "Terminal o caja fuera del ámbito autorizado. Operación bloqueada.";
                return;
            }

            shift = open;
            if (open is null)
                CashStatus = "No existe turno abierto. Requiere permiso cash.open.";
            else
            {
                var summary = await repository.GetLocalCashShiftSummaryAsync(open.Id);
                CashStatus = $"Turno abierto. Fondo={summary.OpeningAmountCents}; ventas efectivo={summary.CashSalesCents}; efectivo esperado={summary.ExpectedCashCents} centavos.";
            }
        }
        catch (Exception)
        {
            shift = null;
            CashStatus = "No se pudo verificar caja en SQLite. Operación bloqueada.";
        }
        finally
        {
            busy = false;
            RefreshCommands();
        }
    }

    public async Task OpenShiftAsync()
    {
        if (!CanOpenShift) return;
        var session = login.ActiveSession!;
        if (OpeningAmountCents < 0)
        {
            CashStatus = "El fondo inicial no puede ser negativo.";
            return;
        }
        busy = true;
        RefreshCommands();
        try
        {
            await auth.RequirePermissionAsync(session.SessionId, "cash.open");
            var binding = await repository.GetTerminalBindingAsync()
                ?? throw new InvalidOperationException("Terminal unbound.");
            if (binding.SchemaVersion != 4 ||
                binding.TenantId != session.TenantId || binding.StoreId != session.StoreId)
                throw new InvalidOperationException("Terminal scope mismatch.");
            var existing = await repository.GetOpenLocalCashShiftAsync();
            if (existing is not null)
                throw new InvalidOperationException("Another shift is already open.");
            var opened = new LocalCashShift(
                Guid.NewGuid(), binding.TenantId, binding.StoreId, binding.TerminalId,
                session.UserId, clock.UtcNow, OpeningAmountCents, "open");
            await repository.OpenLocalCashShiftAsync(opened);
            shift = opened;
            CashStatus = $"Caja abierta y persistida en SQLite: {opened.Id}. Fondo {OpeningAmountCents} centavos.";
        }
        catch (Exception)
        {
            shift = null;
            CashStatus = "No se pudo abrir la caja. Verifique permiso cash.open, terminal y turno previo.";
        }
        finally
        {
            busy = false;
            RefreshCommands();
        }
    }

    public async Task CheckoutCashAsync()
    {
        if (!CanCheckout) return;
        var session = login.ActiveSession!;
        busy = true;
        cart.SetCheckoutLocked(true);
        RefreshCommands();
        try
        {
            await auth.RequirePermissionAsync(session.SessionId, "sales.create");
            var activeShift = shift ?? throw new InvalidOperationException("No cash shift.");
            var binding = await repository.GetTerminalBindingAsync()
                ?? throw new InvalidOperationException("Terminal unbound.");
            if (binding.SchemaVersion != 4 || binding.TenantId != session.TenantId ||
                binding.StoreId != session.StoreId || binding.TerminalId != activeShift.TerminalId)
                throw new InvalidOperationException("Invalid terminal binding.");

            if (pendingRequest is null)
            {
                if (cart.Lines.Count == 0 || cart.TotalCents <= 0 || cart.TotalCents > int.MaxValue ||
                    TenderedCents < cart.TotalCents)
                    throw new InvalidOperationException("Invalid cart amount or insufficient cash.");
                var localSaleId = Guid.NewGuid();
                var sale = new OfflineSaleDraft(
                    localSaleId, binding.TenantId, binding.StoreId, binding.TerminalId,
                    clock.UtcNow,
                    cart.Lines.Select(item => new OfflineSaleLineDraft(
                        item.ProductId, item.VariantId, item.Sku, item.Name,
                        item.Quantity, item.UnitPriceCents)).ToArray(),
                    new[] { new OfflineSalePaymentDraft("cash", (int)cart.TotalCents, Guid.NewGuid()) },
                    "MXN", session.UserId);
                pendingRequest = new AtomicCashCheckoutRequest(
                    sale, session.SessionId, activeShift.Id, TenderedCents,
                    Guid.NewGuid(), Guid.NewGuid());
            }
            else if (pendingRequest.SessionId != session.SessionId)
            {
                throw new InvalidOperationException("Ambiguous prior checkout belongs to another session.");
            }

            var result = await checkout.CheckoutAsync(pendingRequest);
            pendingRequest = null;
            cart.Clear();
            cart.SetCheckoutLocked(false);
            SaleStatus = $"VENTA GUARDADA en SQLite: {result.ReceiptNumber}; total={result.TotalCents}; cambio={result.ChangeCents} centavos. " +
                (result.AlreadyCommitted ? "Reintento reconciliado, sin duplicar cobro. " : "") +
                "Ticket pendiente de impresión. Sin ACK remoto todavía.";
            var summary = await repository.GetLocalCashShiftSummaryAsync(activeShift.Id);
            CashStatus = $"Caja abierta. Efectivo esperado={summary.ExpectedCashCents} centavos; ventas efectivo={summary.CashSalesCents}.";
        }
        catch (Exception)
        {
            SaleStatus = pendingRequest is null
                ? "Cobro rechazado antes de crear la operación. Verifique carrito, efectivo y autorización."
                : $"Resultado de cobro incierto para {pendingRequest.Sale.LocalSaleId}. " +
                  "No haga otro cobro: reintente esta misma operación o solicite reconciliación.";
        }
        finally
        {
            busy = false;
            cart.SetCheckoutLocked(pendingRequest is not null);
            RefreshCommands();
        }
    }

    public void SessionChanged()
    {
        if (login.ActiveSession is null)
        {
            shift = null;
            CashStatus = "No hay sesión; cobro y caja bloqueados.";
            if (pendingRequest is not null)
                SaleStatus = $"La operación {pendingRequest.Sale.LocalSaleId} tiene estado incierto. Requiere reconciliación antes de nueva venta.";
        }
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        RaisePropertyChanged(nameof(CanOpenShift));
        RaisePropertyChanged(nameof(CanCheckout));
        RaisePropertyChanged(nameof(ShiftLabel));
        RaisePropertyChanged(nameof(HasUncertainCheckout));
    }
}
