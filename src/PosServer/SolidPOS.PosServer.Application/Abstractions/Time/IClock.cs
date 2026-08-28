namespace SolidPOS.PosServer.Application.Abstractions.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
