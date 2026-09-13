using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Terminals;
using SolidPOS.PosServer.Infrastructure.Security;
using Xunit;

namespace SolidPOS.PosServer.UnitTests.Terminals;

public sealed class TerminalValidationMiddlewareTests
{
    [Fact]
    public async Task Unauthenticated_request_bypasses_terminal_validation()
    {
        DefaultHttpContext context = new();
        Mock<ITenantContext> tenantContext = new();
        Mock<ITerminalRepository> repository = new();

        bool nextCalled = false;
        TerminalValidationMiddleware middleware = new(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Mock.Of<ILogger<TerminalValidationMiddleware>>());

        await middleware.InvokeAsync(context, tenantContext.Object, repository.Object);

        Assert.True(nextCalled);
        repository.Verify(x => x.IsTerminalActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Authenticated_non_terminal_request_bypasses_terminal_validation()
    {
        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], "Bearer"));
        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TerminalId).Returns((Guid?)null);
        tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        Mock<ITerminalRepository> repository = new();

        bool nextCalled = false;
        TerminalValidationMiddleware middleware = new(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Mock.Of<ILogger<TerminalValidationMiddleware>>());

        await middleware.InvokeAsync(context, tenantContext.Object, repository.Object);

        Assert.True(nextCalled);
        repository.Verify(x => x.IsTerminalActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Active_terminal_request_passes_validation_and_invokes_next()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();

        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("terminal_id", terminalId.ToString())], "Bearer"));

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(terminalId);

        Mock<ITerminalRepository> repository = new();
        repository.Setup(x => x.IsTerminalActiveAsync(tenantId, terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        bool nextCalled = false;
        TerminalValidationMiddleware middleware = new(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Mock.Of<ILogger<TerminalValidationMiddleware>>());

        await middleware.InvokeAsync(context, tenantContext.Object, repository.Object);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Revoked_terminal_request_is_rejected_with_401_and_inactive_terminal_problem()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();

        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("terminal_id", terminalId.ToString())], "Bearer"));
        context.Response.Body = new MemoryStream();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(terminalId);

        Mock<ITerminalRepository> repository = new();
        repository.Setup(x => x.IsTerminalActiveAsync(tenantId, terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Revoked terminal is not active

        bool nextCalled = false;
        TerminalValidationMiddleware middleware = new(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Mock.Of<ILogger<TerminalValidationMiddleware>>());

        await middleware.InvokeAsync(context, tenantContext.Object, repository.Object);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using JsonDocument doc = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("https://solidpos.local/problems/inactive-terminal", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("Inactive terminal", doc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Disabled_terminal_request_is_rejected_with_401_and_inactive_terminal_problem()
    {
        Guid tenantId = Guid.NewGuid();
        Guid terminalId = Guid.NewGuid();

        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("terminal_id", terminalId.ToString())], "Bearer"));
        context.Response.Body = new MemoryStream();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        tenantContext.SetupGet(x => x.TerminalId).Returns(terminalId);

        Mock<ITerminalRepository> repository = new();
        repository.Setup(x => x.IsTerminalActiveAsync(tenantId, terminalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // Disabled terminal is not active

        bool nextCalled = false;
        TerminalValidationMiddleware middleware = new(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Mock.Of<ILogger<TerminalValidationMiddleware>>());

        await middleware.InvokeAsync(context, tenantContext.Object, repository.Object);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using JsonDocument doc = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal("https://solidpos.local/problems/inactive-terminal", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Terminal_request_missing_tenant_id_is_rejected_with_401()
    {
        Guid terminalId = Guid.NewGuid();

        DefaultHttpContext context = new();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("terminal_id", terminalId.ToString())], "Bearer"));
        context.Response.Body = new MemoryStream();

        Mock<ITenantContext> tenantContext = new();
        tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null); // Missing tenant
        tenantContext.SetupGet(x => x.TerminalId).Returns(terminalId);

        Mock<ITerminalRepository> repository = new();

        bool nextCalled = false;
        TerminalValidationMiddleware middleware = new(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Mock.Of<ILogger<TerminalValidationMiddleware>>());

        await middleware.InvokeAsync(context, tenantContext.Object, repository.Object);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }
}
