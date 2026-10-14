using System.Diagnostics;
using System.Text.Json;
using SolidPOS.PosCore.Application.Abstractions;
using SolidPOS.PosCore.Application.OfflineSales;
using SolidPOS.PosCore.Infrastructure.SQLite;

namespace SolidPOS.PosCore.CrashHarness;

/// <summary>Disposable test process. Do not use with a production database.</summary>
public static class CrashProbeMarker { }

internal sealed class FrozenClock : IClock
{
    public FrozenClock(DateTimeOffset value) => UtcNow = value;
    public DateTimeOffset UtcNow { get; }
}

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 4 || (args[0] != "before" && args[0] != "after"))
        {
            Console.Error.WriteLine("Usage: before|after <disposable-sqlite> <request.json> <marker-file>");
            return 2;
        }

        string mode = args[0];
        string dbPath = Path.GetFullPath(args[1]);
        string requestPath = Path.GetFullPath(args[2]);
        string markerPath = Path.GetFullPath(args[3]);
        // The parent created the temporary fixture. Never create an unknown DB
        // or write to a path that was not already provisioned by the test.
        string allowedTemp = Path.GetFullPath(Path.GetTempPath());
        string parent = Path.GetDirectoryName(dbPath) ?? "";
        string name = Path.GetFileName(parent) ?? string.Empty;
        if (!parent.StartsWith(allowedTemp, StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith("solidpos-cashcheckout-", StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(dbPath), "terminal.sqlite", StringComparison.Ordinal) ||
            !string.Equals(requestPath, dbPath + ".crash-request.json", StringComparison.Ordinal) ||
            !string.Equals(markerPath, dbPath + ".crash-marker", StringComparison.Ordinal) ||
            !File.Exists(dbPath) || !File.Exists(requestPath) || File.Exists(markerPath))
        {
            Console.Error.WriteLine("Refusing to run outside an existing disposable cashcheckout test fixture.");
            return 3;
        }

        var request = JsonSerializer.Deserialize<AtomicCashCheckoutRequest>(
            await File.ReadAllTextAsync(requestPath))
            ?? throw new InvalidOperationException("No checkout request.");
        if (request.Sale.LocalSaleId == Guid.Empty)
            return 4;

        if (mode == "after")
        {
            var database = new SQLiteLocalDatabase(dbPath);
            var checkout = new AtomicCashCheckoutService(
                new SQLiteLocalPosRepository(database),
                new SQLiteAtomicCashCheckoutStore(database),
                new FrozenClock(request.Sale.OccurredAtUtc.AddSeconds(1)));
            await checkout.CheckoutAsync(request);
        }

        // The marker is flushed after the specified crash point so the
        // parent can distinguish a deliberate kill from an unrelated failure.
        await File.WriteAllTextAsync(markerPath, mode);

        // Deliberate OS-level termination; no finally block or normal shutdown.
        // The parent checks SQLite from a new process/connection.
        Process.GetCurrentProcess().Kill();
        await Task.Delay(100);
        return 5;
    }
}
