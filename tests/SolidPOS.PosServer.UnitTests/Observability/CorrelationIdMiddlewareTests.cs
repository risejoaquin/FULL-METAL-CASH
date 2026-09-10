using SolidPOS.PosServer.Infrastructure.Observability;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Observability;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public void NormalizeIdentifier_strips_unsafe_characters_and_limits_length()
    {
        string candidate = "  tenant\r\n-secret/<script>_ABC:" + new string('x', 200);

        string result = CorrelationIdMiddleware.NormalizeIdentifier(candidate, "fallback");

        Assert.DoesNotContain("\r", result);
        Assert.DoesNotContain("\n", result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("<", result);
        Assert.True(result.Length <= 128);
        Assert.NotEqual("fallback", result);
    }

    [Fact]
    public void NormalizeIdentifier_uses_fallback_when_candidate_has_no_safe_content()
    {
        Assert.Equal("fallback", CorrelationIdMiddleware.NormalizeIdentifier(" <>/ ", "fallback"));
    }
}
