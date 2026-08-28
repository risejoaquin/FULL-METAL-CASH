using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.Hardware;
using SolidPOS.PosCore.Application.Storage;
using SolidPOS.PosCore.Domain;
using SolidPOS.PosCore.Infrastructure.Hardware;
using Xunit;

namespace SolidPOS.PosCore.UnitTests;

public sealed class HardwareRuntimeServiceTests
{
    [Fact]
    public async Task QueueReceiptAsync_CreatesPendingPrintJobAndHardwareEvent()
    {
        var repository = new HardwareRepositoryStub();
        var service = CreateService(repository);

        LocalReceiptPrintJob job = await service.QueueReceiptAsync(Guid.NewGuid(), Guid.NewGuid(), "SP-TEST", "receipt content");

        Assert.Equal(LocalPrintJobStatuses.Pending, job.Status);
        Assert.Single(repository.PrintJobs);
        Assert.Single(repository.HardwareEvents, x => x.EventType == "print_job.queued");
    }

    [Fact]
    public async Task ProcessNextReceiptPrintJob_PrintsAndMarksJobPrinted()
    {
        var repository = new HardwareRepositoryStub();
        var printer = new FakeReceiptPrinter();
        var service = CreateService(repository, printer);
        LocalReceiptPrintJob queued = await service.QueueReceiptAsync(Guid.NewGuid(), Guid.NewGuid(), "SP-TEST", "receipt content");

        LocalReceiptPrintJob? processed = await service.ProcessNextReceiptPrintJobAsync();

        Assert.NotNull(processed);
        Assert.Contains(queued.Id, printer.PrintedJobIds);
        Assert.Equal(LocalPrintJobStatuses.Printed, repository.PrintJobs.Single().Status);
        Assert.Single(repository.HardwareEvents, x => x.EventType == "print_job.printed");
    }

    [Fact]
    public async Task OpenCashDrawerAsync_RecordsHardwareEvent()
    {
        var repository = new HardwareRepositoryStub();
        var service = CreateService(repository);

        LocalCashDrawerCommandResult result = await service.OpenCashDrawerAsync("sale_completed");

        Assert.True(result.Accepted);
        Assert.Single(repository.HardwareEvents, x => x.DeviceType == LocalHardwareDeviceTypes.CashDrawer && x.EventType == "cash_drawer.open");
    }

    private static HardwareRuntimeService CreateService(HardwareRepositoryStub repository, FakeReceiptPrinter? printer = null)
        => new(repository, printer ?? new FakeReceiptPrinter(), new FakeCashDrawerDevice(), new FakeBarcodeScannerDevice("QSR-AMERICANO"), new FakePaymentTerminalDevice(), new FixedClock());

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 20, 5, 0, 0, TimeSpan.Zero);
    }

    private sealed class HardwareRepositoryStub : ILocalPosRepository
    {
        private readonly TerminalBinding _binding = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "unit-test", "token", DateTimeOffset.UtcNow, 4);

        public List<LocalReceiptPrintJob> PrintJobs { get; } = new();
        public List<LocalHardwareEvent> HardwareEvents { get; } = new();

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveTerminalBindingAsync(TerminalBinding binding, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<TerminalBinding?> GetTerminalBindingAsync(CancellationToken cancellationToken = default) => Task.FromResult<TerminalBinding?>(_binding);
        public Task SaveOfflineSaleAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveOfflineSaleWithInventoryAsync(OfflineSaleDraft sale, LocalOutboxEvent outboxEvent, IReadOnlyCollection<LocalInventoryMovement> movements, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveCatalogProductsAsync(IReadOnlyCollection<LocalCatalogProduct> products, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveInventoryRecipeCacheAsync(IReadOnlyCollection<LocalInventoryRecipe> recipes, IReadOnlyCollection<LocalInventoryRecipeItem> recipeItems, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LocalInventoryRecipe?> GetRecipeForOutputAsync(Guid productId, Guid? variantId, CancellationToken cancellationToken = default) => Task.FromResult<LocalInventoryRecipe?>(null);
        public Task<IReadOnlyList<LocalInventoryRecipeItem>> GetRecipeItemsAsync(Guid recipeId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalInventoryRecipeItem>>(Array.Empty<LocalInventoryRecipeItem>());
        public Task<IReadOnlyList<LocalInventoryMovement>> GetInventoryMovementsByLocalSaleIdAsync(Guid localSaleId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalInventoryMovement>>(Array.Empty<LocalInventoryMovement>());
        public Task<int> CountInventoryRecipesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> CountInventoryRecipeItemsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<LocalCatalogProduct?> GetCatalogProductBySkuAsync(string sku, CancellationToken cancellationToken = default) => Task.FromResult<LocalCatalogProduct?>(null);
        public Task<int> CountCatalogProductsAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task SaveOutboxEventAsync(LocalOutboxEvent outboxEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<LocalOutboxEvent>> GetPendingOutboxEventsAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LocalOutboxEvent>>(Array.Empty<LocalOutboxEvent>());
        public Task<LocalOutboxEvent?> GetLatestOutboxEventByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default) => Task.FromResult<LocalOutboxEvent?>(null);
        public Task MarkOutboxSyncedAsync(IEnumerable<Guid> eventIds, DateTimeOffset syncedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkOutboxFailedAsync(Guid eventId, string error, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetOutboxEventToPendingAsync(Guid eventId, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> RetryFailedOutboxEventsAsync(int maxAttempts, string reason, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task SaveSyncAcknowledgementsAsync(IEnumerable<LocalSyncAcknowledgement> acknowledgements, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> CountOutboxByStatusAsync(LocalOutboxStatus status, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task SaveReceiptPrintJobAsync(LocalReceiptPrintJob job, CancellationToken cancellationToken = default)
        {
            PrintJobs.Add(job);
            return Task.CompletedTask;
        }

        public Task<LocalReceiptPrintJob?> GetNextPendingReceiptPrintJobAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PrintJobs.FirstOrDefault(x => x.Status == LocalPrintJobStatuses.Pending));
        }

        public Task MarkReceiptPrintJobPrintedAsync(Guid jobId, DateTimeOffset printedAtUtc, CancellationToken cancellationToken = default)
        {
            int index = PrintJobs.FindIndex(x => x.Id == jobId);
            PrintJobs[index] = PrintJobs[index] with { Status = LocalPrintJobStatuses.Printed, PrintedAtUtc = printedAtUtc, Attempts = PrintJobs[index].Attempts + 1 };
            return Task.CompletedTask;
        }

        public Task MarkReceiptPrintJobFailedAsync(Guid jobId, string error, CancellationToken cancellationToken = default)
        {
            int index = PrintJobs.FindIndex(x => x.Id == jobId);
            PrintJobs[index] = PrintJobs[index] with { Status = LocalPrintJobStatuses.Failed, LastError = error, Attempts = PrintJobs[index].Attempts + 1 };
            return Task.CompletedTask;
        }

        public Task SaveHardwareEventAsync(LocalHardwareEvent hardwareEvent, CancellationToken cancellationToken = default)
        {
            HardwareEvents.Add(hardwareEvent);
            return Task.CompletedTask;
        }
    }
}
