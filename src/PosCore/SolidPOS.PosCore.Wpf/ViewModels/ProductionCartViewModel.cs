using System.Collections.ObjectModel;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Wpf.ViewModels;

/// <summary>Commercial cart reads actual SQLite catalog records; it does not commit sales.</summary>
public sealed class ProductionCartViewModel : ViewModelBase
{
    private readonly ILocalPosRepository repository;
    private readonly ProductionLoginViewModel login;
    private readonly Dictionary<string, LocalCatalogProduct> products = new(StringComparer.OrdinalIgnoreCase);
    private string sku = string.Empty;
    private string status = "Autentíquese para consultar el catálogo.";
    private long totalCents;
    private bool busy;

    public ProductionCartViewModel(ILocalPosRepository repository, ProductionLoginViewModel login)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.login = login ?? throw new ArgumentNullException(nameof(login));
    }

    public ObservableCollection<ProductionCartLine> Lines { get; } = new();
    public string Sku { get => sku; set => SetProperty(ref sku, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public long TotalCents { get => totalCents; private set => SetProperty(ref totalCents, value); }
    public bool CanEdit => login.IsAuthenticated && !busy;

    public async Task AddBySkuAsync()
    {
        if (!CanEdit) { Status = "Inicie sesión antes de agregar productos."; return; }
        var requestedSku = Sku.Trim();
        if (requestedSku.Length == 0) { Status = "Capture un SKU."; return; }
        busy = true;
        try
        {
            var product = await repository.GetCatalogProductBySkuAsync(requestedSku);
            if (product is null || !product.IsSellable || !string.Equals(product.Currency, "MXN", StringComparison.OrdinalIgnoreCase))
            {
                Status = "SKU inexistente, inactivo o con moneda no admitida.";
                return;
            }
            if (!login.IsAuthenticated) { Status = "La sesión expiró. Vuelva a iniciar sesión."; Clear(); return; }
            var identity = product.ProductId.ToString("N") + ":" + product.VariantId?.ToString("N");
            var previous = Lines.FirstOrDefault(x => x.Identity == identity);
            if (previous is null)
            {
                products[identity] = product;
                Lines.Add(new ProductionCartLine(identity, product.Sku, product.Name, 1, product.PriceCents));
            }
            else
            {
                int nextQuantity = checked(previous.Quantity + 1);
                int index = Lines.IndexOf(previous);
                Lines[index] = previous with { Quantity = nextQuantity };
            }
            Recalculate();
            Sku = string.Empty;
            Status = "Producto consultado y agregado desde SQLite. Pago no disponible.";
        }
        catch (OverflowException) { Status = "Límite monetario o de cantidad excedido."; }
        catch (Exception) { Status = "No se pudo consultar el catálogo local."; }
        finally { busy = false; RaisePropertyChanged(nameof(CanEdit)); }
    }

    public void Clear()
    {
        Lines.Clear();
        products.Clear();
        TotalCents = 0;
        Status = "Carrito vacío. No se registró venta.";
    }

    private void Recalculate()
    {
        long sum = 0;
        foreach (var line in Lines)
            sum = checked(sum + checked((long)line.Quantity * line.UnitPriceCents));
        TotalCents = sum;
    }
}

public sealed record ProductionCartLine(string Identity, string Sku, string Name, int Quantity, int UnitPriceCents)
{
    public long SubtotalCents => checked((long)Quantity * UnitPriceCents);
}
