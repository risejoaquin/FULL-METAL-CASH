using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;

namespace SolidPOS.PosCore.Application.OfflineSales;

public sealed class LocalInventoryConsumptionService
{
    private readonly ILocalPosRepository _repository;
    private readonly IClock _clock;

    public LocalInventoryConsumptionService(ILocalPosRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<IReadOnlyList<LocalInventoryMovement>> BuildMovementsAsync(OfflineSaleDraft sale, CancellationToken cancellationToken = default)
    {
        OfflineSaleCalculator.Validate(sale);
        var movements = new List<LocalInventoryMovement>();

        foreach (OfflineSaleLineDraft line in sale.Lines)
        {
            LocalInventoryRecipe? recipe = await _repository.GetRecipeForOutputAsync(line.ProductId, line.VariantId, cancellationToken).ConfigureAwait(false);
            if (recipe is null)
            {
                continue;
            }

            IReadOnlyList<LocalInventoryRecipeItem> items = await _repository.GetRecipeItemsAsync(recipe.RecipeId, cancellationToken).ConfigureAwait(false);
            decimal wasteMultiplier = 1 + (recipe.WastePercent / 100m);
            foreach (LocalInventoryRecipeItem item in items)
            {
                decimal quantityDelta = -(item.Quantity * line.Quantity * wasteMultiplier);
                movements.Add(new LocalInventoryMovement(
                    Guid.NewGuid(),
                    sale.LocalSaleId,
                    sale.TenantId,
                    sale.StoreId,
                    sale.TerminalId,
                    item.IngredientProductId,
                    item.IngredientVariantId,
                    "sale_recipe_component",
                    decimal.Round(quantityDelta, 4, MidpointRounding.AwayFromZero),
                    item.UnitId,
                    sale.OccurredAtUtc,
                    $"recipe:{recipe.RecipeId}",
                    _clock.UtcNow));
            }
        }

        return movements;
    }
}
