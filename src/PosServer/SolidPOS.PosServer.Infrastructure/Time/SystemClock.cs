using SolidPOS.PosServer.Application.Abstractions.Time;

namespace SolidPOS.PosServer.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
