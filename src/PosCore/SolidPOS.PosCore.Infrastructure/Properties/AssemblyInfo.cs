using System.Runtime.CompilerServices;

// Narrow test-only access for migration/restart compatibility checks.
// Production callers use IControlledLocalSyncRecoveryRepository with RBAC and approval.
[assembly: InternalsVisibleTo("SolidPOS.PosCore.UnitTests")]
