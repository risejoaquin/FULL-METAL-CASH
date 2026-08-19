using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using SolidPOS.PosServer.Api.Endpoints;
using SolidPOS.PosServer.Application.Admin;
using SolidPOS.PosServer.Application.AdminManagement;
using SolidPOS.PosServer.Application.Audit;
using SolidPOS.PosServer.Application.Catalog;
using SolidPOS.PosServer.Application.BuilderUpdates;
using SolidPOS.PosServer.Application.Cash;
using SolidPOS.PosServer.Application.Customers;
using SolidPOS.PosServer.Application.Discounts;
using SolidPOS.PosServer.Application.Auth;
using SolidPOS.PosServer.Application.Abstractions.Tenancy;
using SolidPOS.PosServer.Application.Abstractions.Time;
using SolidPOS.PosServer.Application.Inventory;
using SolidPOS.PosServer.Application.Observability;
using SolidPOS.PosServer.Application.Reports;
using SolidPOS.PosServer.Application.Receipts;
using SolidPOS.PosServer.Application.Returns;
using SolidPOS.PosServer.Application.Security;
using SolidPOS.PosServer.Application.Sales;
using SolidPOS.PosServer.Application.Sync;
using SolidPOS.PosServer.Application.Tenants;
using SolidPOS.PosServer.Application.Terminals;
using SolidPOS.PosServer.Contracts.System;
using SolidPOS.PosServer.Infrastructure.Auth;
using SolidPOS.PosServer.Infrastructure.Admin;
using SolidPOS.PosServer.Infrastructure.AdminManagement;
using SolidPOS.PosServer.Infrastructure.Audit;
using SolidPOS.PosServer.Infrastructure.Catalog;
using SolidPOS.PosServer.Infrastructure.BuilderUpdates;
using SolidPOS.PosServer.Infrastructure.Cash;
using SolidPOS.PosServer.Infrastructure.Customers;
using SolidPOS.PosServer.Infrastructure.Discounts;
using SolidPOS.PosServer.Infrastructure.Inventory;
using SolidPOS.PosServer.Infrastructure.Observability;
using SolidPOS.PosServer.Infrastructure.PostgreSql;
using SolidPOS.PosServer.Infrastructure.Reports;
using SolidPOS.PosServer.Infrastructure.Receipts;
using SolidPOS.PosServer.Infrastructure.Returns;
using SolidPOS.PosServer.Infrastructure.Security;
using SolidPOS.PosServer.Infrastructure.Sales;
using SolidPOS.PosServer.Infrastructure.Sync;
using SolidPOS.PosServer.Infrastructure.Tenants;
using SolidPOS.PosServer.Infrastructure.Terminals;
using SolidPOS.PosServer.Infrastructure.Tenancy;
using SolidPOS.PosServer.Infrastructure.Time;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

PostgreSqlConnectionStringResolution postgresConnectionStringResolution = PostgreSqlConnectionStringResolver.Resolve(builder.Configuration);
if (postgresConnectionStringResolution.IsValid && !string.IsNullOrWhiteSpace(postgresConnectionStringResolution.ConnectionString))
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Postgres"] = postgresConnectionStringResolution.ConnectionString
    });
}

string serviceName = "SolidPOS.PosServer";
string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .Enrich.WithProperty("service", serviceName)
        .Enrich.WithProperty("version", version)
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console(new CompactJsonFormatter());
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<PasswordHashingOptions>(builder.Configuration.GetSection(PasswordHashingOptions.SectionName));
builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthRepository, PostgreSqlAuthRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAuditEventRepository, PostgreSqlAuditEventRepository>();
builder.Services.AddScoped<IAuditEventService, AuditEventService>();
builder.Services.AddScoped<IAdminMutationRepository, PostgreSqlAdminMutationRepository>();
builder.Services.AddScoped<IAdminMutationService, AdminMutationService>();
builder.Services.AddScoped<IAdminManagementRepository, PostgreSqlAdminManagementRepository>();
builder.Services.AddScoped<IAdminManagementService, AdminManagementService>();
builder.Services.AddScoped<IBuilderUpdatesRepository, PostgreSqlBuilderUpdatesRepository>();
builder.Services.AddScoped<IBuilderUpdatesService, BuilderUpdatesService>();
builder.Services.AddSingleton<OperationalMetricsRecorder>();
builder.Services.AddScoped<IOperationalMetricsRepository, PostgreSqlOperationalMetricsRepository>();
builder.Services.AddScoped<IOperationalMetricsService, OperationalMetricsService>();
builder.Services.AddScoped<IAuditEventWriter, PostgreSqlAuditEventWriter>();
builder.Services.AddScoped<ICashShiftRepository, PostgreSqlCashShiftRepository>();
builder.Services.AddScoped<ICashShiftService, CashShiftService>();
builder.Services.AddScoped<ICatalogRuntimeRepository, PostgreSqlCatalogRuntimeRepository>();
builder.Services.AddScoped<ICatalogRuntimeService, CatalogRuntimeService>();
builder.Services.AddScoped<ICustomersRepository, PostgreSqlCustomersRepository>();
builder.Services.AddScoped<ICustomersService, CustomersService>();
builder.Services.AddScoped<IDiscountsRepository, PostgreSqlDiscountsRepository>();
builder.Services.AddScoped<IDiscountsService, DiscountsService>();
builder.Services.AddScoped<IInventoryStockRepository, PostgreSqlInventoryStockRepository>();
builder.Services.AddScoped<IInventoryStockService, InventoryStockService>();
builder.Services.AddScoped<IInventoryAdjustmentRepository, PostgreSqlInventoryAdjustmentRepository>();
builder.Services.AddScoped<IInventoryAdjustmentService, InventoryAdjustmentService>();
builder.Services.AddScoped<IInventoryControlRepository, PostgreSqlInventoryControlRepository>();
builder.Services.AddScoped<IInventoryControlService, InventoryControlService>();
builder.Services.AddScoped<IReportsRepository, PostgreSqlReportsRepository>();
builder.Services.AddScoped<IReportsService, ReportsService>();
builder.Services.AddScoped<IDigitalReceiptRepository, PostgreSqlDigitalReceiptRepository>();
builder.Services.AddScoped<IDigitalReceiptService, DigitalReceiptService>();
builder.Services.AddScoped<IReturnsRepository, PostgreSqlReturnsRepository>();
builder.Services.AddScoped<IReturnsService, ReturnsService>();
builder.Services.AddScoped<ISalesRepository, PostgreSqlSalesRepository>();
builder.Services.AddScoped<ISalesService, SalesService>();
builder.Services.AddScoped<ISyncPushRepository, PostgreSqlSyncPushRepository>();
builder.Services.AddScoped<ISyncPushService, SyncPushService>();
builder.Services.AddScoped<ISyncEventRepository, PostgreSqlSyncEventRepository>();
builder.Services.AddScoped<ISyncEventProcessingService, SyncEventProcessingService>();
builder.Services.AddScoped<ISyncPullRepository, PostgreSqlSyncPullRepository>();
builder.Services.AddScoped<ISyncPullService, SyncPullService>();
builder.Services.AddScoped<ISyncChangeWriter, PostgreSqlSyncChangeWriter>();
builder.Services.AddScoped<ISyncConflictRepository, PostgreSqlSyncConflictRepository>();
builder.Services.AddScoped<ISyncConflictService, SyncConflictService>();
builder.Services.AddScoped<ITenantConfigRepository, PostgreSqlTenantConfigRepository>();
builder.Services.AddScoped<ITenantConfigService, TenantConfigService>();
builder.Services.AddScoped<ITerminalRepository, PostgreSqlTerminalRepository>();
builder.Services.AddScoped<ITerminalEnrollmentService, TerminalEnrollmentService>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton(postgresConnectionStringResolution);
builder.Services.AddSingleton(sp =>
{
    PostgreSqlConnectionStringResolution connectionStringResolution = sp.GetRequiredService<PostgreSqlConnectionStringResolution>();
    return new PostgreSqlReadinessProbe(connectionStringResolution);
});

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions["service"] = serviceName;
    };
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

string[] allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("SolidPosCors", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
        {
            policy.SetIsOriginAllowed(origin => origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) || origin.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase))
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        string key = context.User.FindFirst("tenant_id")?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimits:PermitLimit", 600),
            Window = TimeSpan.FromMinutes(builder.Configuration.GetValue("RateLimits:WindowMinutes", 1)),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = builder.Configuration.GetValue("RateLimits:QueueLimit", 0)
        });
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "SolidPOS PosServer API",
        Version = "v1",
        Description = "Backend API for SolidPOS SaaS POS."
    });
});

if (!postgresConnectionStringResolution.IsConfigured && !builder.Environment.IsEnvironment("Testing"))
{
    throw new InvalidOperationException("ConnectionStrings:Postgres or DATABASE_URL is required.");
}

if (postgresConnectionStringResolution.IsConfigured && !postgresConnectionStringResolution.IsValid && !builder.Environment.IsEnvironment("Testing"))
{
    throw new InvalidOperationException($"PostgreSQL connection string is invalid: {postgresConnectionStringResolution.ErrorMessage}");
}

if (builder.Environment.IsProduction() && !builder.Environment.IsEnvironment("Testing"))
{
    string allowedHosts = builder.Configuration["AllowedHosts"] ?? string.Empty;
    if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Trim() == "*")
    {
        throw new InvalidOperationException("AllowedHosts must be explicitly configured in production.");
    }

    if (!allowedHosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(host => host.Equals("healthcheck.railway.app", StringComparison.OrdinalIgnoreCase)
            || host.Equals("*.railway.app", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".railway.app", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("AllowedHosts must include the Railway application host, healthcheck.railway.app, or *.railway.app in production.");
    }

    if (allowedOrigins.Length == 0)
    {
        throw new InvalidOperationException("Cors:AllowedOrigins must be explicitly configured in production.");
    }
}

JwtOptions jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) && !builder.Environment.IsEnvironment("Testing"))
{
    throw new InvalidOperationException("Jwt:SigningKey is required.");
}

string signingKey = string.IsNullOrWhiteSpace(jwtOptions.SigningKey)
    ? "testing-signing-key-must-be-at-least-32-characters"
    : jwtOptions.SigningKey;

if (Encoding.UTF8.GetByteCount(signingKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (string permission in PermissionCodes.All)
    {
        options.AddPolicy(permission, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new PermissionRequirement(permission));
        });
    }
});

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: version))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter();
    });

WebApplication app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("trace_id", httpContext.TraceIdentifier);
        diagnosticContext.Set("endpoint", $"{httpContext.Request.Method} {httpContext.Request.Path}");
        diagnosticContext.Set("remote_ip", httpContext.Connection.RemoteIpAddress?.ToString());
    };
});

app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        IExceptionHandlerFeature? exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        Exception? exception = exceptionFeature?.Error;
        ILogger<Program> logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

        logger.LogError(exception, "Unhandled exception while processing request");

        ProblemDetails problem = new()
        {
            Title = "Unexpected server error",
            Detail = "An unexpected error occurred. Use traceId to inspect server logs.",
            Status = StatusCodes.Status500InternalServerError,
            Type = "https://solidpos.local/problems/unexpected-error",
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseCors("SolidPosCors");
app.UseRateLimiter();
app.UseMiddleware<RequiredClaimsMiddleware>();
app.UseMiddleware<TerminalValidationMiddleware>();
app.UseMiddleware<RequestLogEnrichmentMiddleware>();
app.UseMiddleware<OperationalMetricsMiddleware>();
app.UseAuthorization();

app.MapGet("/health", (IClock clock) =>
{
    return Results.Ok(new HealthResponse("ok", serviceName, version, clock.UtcNow));
})
.WithName("Health")
.WithTags("Health");


app.MapGet("/health/live", (IClock clock) =>
{
    return Results.Ok(new HealthResponse("alive", serviceName, version, clock.UtcNow));
})
.WithName("Liveness")
.WithTags("Health");

app.MapGet("/health/ready", async (
    PostgreSqlReadinessProbe readinessProbe,
    IClock clock,
    CancellationToken cancellationToken) =>
{
    PostgreSqlReadinessResult readiness = await readinessProbe.CheckAsync(cancellationToken);
    string status = readiness.IsReady ? "ready" : "degraded";
    ReadinessResponse response = new(
        status,
        readiness.Database,
        clock.UtcNow,
        readiness.Detail,
        readiness.ErrorCode,
        readiness.MissingTables,
        readiness.ConnectionStringSource);

    return readiness.IsReady
        ? Results.Ok(response)
        : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
})
.WithName("Readiness")
.WithTags("Health");

RouteGroupBuilder api = app.MapGroup("/api/v1");
api.MapAuthEndpoints();
api.MapTerminalEndpoints();
api.MapTerminalRuntimeEndpoints();
api.MapAdminManagementEndpoints();
api.MapTenantConfigEndpoints();
api.MapCatalogRuntimeEndpoints();
api.MapCustomerEndpoints();
api.MapDiscountEndpoints();
api.MapCashShiftEndpoints();
api.MapInventoryEndpoints();
api.MapSalesEndpoints();
api.MapReceiptEndpoints();
api.MapReturnEndpoints();
api.MapSyncEndpoints();
api.MapAdminMutationEndpoints();
api.MapAuditEndpoints();
api.MapReportsEndpoints();
api.MapBuilderUpdatesEndpoints();
api.MapObservabilityEndpoints();

api.MapGet("/system/info", (IClock clock, IWebHostEnvironment environment) =>
{
    return Results.Ok(new SystemInfoResponse(serviceName, version, environment.EnvironmentName, clock.UtcNow));
})
.WithName("SystemInfo")
.WithTags("System");

app.Run();

public partial class Program;
