using System.Runtime.CompilerServices;

// Unit tests retain internal legacy financial-fixture access.
// Public callers must use SignedOfflineCartDiscountCheckout.
[assembly: InternalsVisibleTo("SolidPOS.PosCore.UnitTests")]
