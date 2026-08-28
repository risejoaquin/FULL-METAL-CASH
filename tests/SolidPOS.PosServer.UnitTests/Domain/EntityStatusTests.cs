using SolidPOS.PosServer.Domain.Common;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Domain;

public sealed class EntityStatusTests
{
    [Fact]
    public void Active_status_value_is_stable()
    {
        Assert.Equal(1, (int)EntityStatus.Active);
    }
}
