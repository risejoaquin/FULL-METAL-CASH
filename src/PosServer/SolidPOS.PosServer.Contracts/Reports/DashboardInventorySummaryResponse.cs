namespace SolidPOS.PosServer.Contracts.Reports;

public sealed record DashboardInventorySummaryResponse(
    long NegativeStockItemCount,
    long SaleRecipeMovementCount,
    long RecipeComponentMovementCount,
    long ModifierAddMovementCount,
    long ModifierSubstituteMovementCount);
