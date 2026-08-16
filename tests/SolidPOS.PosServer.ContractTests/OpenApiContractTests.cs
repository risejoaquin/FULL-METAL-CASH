using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using YamlDotNet.Serialization;
using Xunit;

namespace SolidPOS.PosServer.ContractTests;

public sealed class OpenApiContractTests
{
    private static readonly HashSet<string> HttpMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "get",
        "post",
        "put",
        "patch",
        "delete"
    };

    [Fact]
    public void OpenApi_contract_file_exists_and_has_paths()
    {
        OpenApiDocument contract = LoadContract();

        Assert.True(contract.Root.ContainsKey("openapi"));
        Assert.True(contract.Root.ContainsKey("paths"));
        Assert.NotEmpty(contract.Operations);
    }

    [Fact]
    public void Runtime_routes_match_openapi_paths_and_methods()
    {
        OpenApiDocument contract = LoadContract();
        IReadOnlySet<RouteOperation> runtimeRoutes = GetRuntimeRoutes();
        IReadOnlySet<RouteOperation> documentedRoutes = contract.Operations;

        RouteOperation[] implementedButMissingFromOpenApi = runtimeRoutes
            .Except(documentedRoutes)
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Method, StringComparer.Ordinal)
            .ToArray();

        RouteOperation[] documentedButMissingFromRuntime = documentedRoutes
            .Except(runtimeRoutes)
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Method, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            implementedButMissingFromOpenApi.Length == 0,
            "Runtime endpoints missing from OpenAPI: " + string.Join(", ", implementedButMissingFromOpenApi.Select(x => x.ToString()).ToArray()));

        Assert.True(
            documentedButMissingFromRuntime.Length == 0,
            "OpenAPI endpoints missing from runtime: " + string.Join(", ", documentedButMissingFromRuntime.Select(x => x.ToString()).ToArray()));
    }

    [Fact]
    public void OpenApi_operations_define_success_request_and_problem_details_contracts()
    {
        OpenApiDocument contract = LoadContract();

        foreach ((string path, string method, Dictionary<object, object> operation) in contract.RawOperations)
        {
            Assert.True(operation.TryGetValue("operationId", out object? operationId) && !string.IsNullOrWhiteSpace(operationId.ToString()),
                $"{method.ToUpperInvariant()} {path} is missing operationId.");

            Assert.True(operation.TryGetValue("responses", out object? responsesValue),
                $"{method.ToUpperInvariant()} {path} is missing responses.");

            var responses = Assert.IsType<Dictionary<object, object>>(responsesValue);
            bool hasSuccessResponse = responses.Keys
                .Select(x => x.ToString() ?? string.Empty)
                .Any(code => code.StartsWith('2'));
            Assert.True(hasSuccessResponse, $"{method.ToUpperInvariant()} {path} is missing a 2xx response.");

            if (RequiresRequestBody(method, path))
            {
                Assert.True(operation.ContainsKey("requestBody"), $"{method.ToUpperInvariant()} {path} is missing requestBody.");
            }

            bool hasProblemDetailsResponse = responses.Values.Any(IsProblemDetailsResponse);
            Assert.True(hasProblemDetailsResponse, $"{method.ToUpperInvariant()} {path} is missing application/problem+json response.");
        }
    }

    [Fact]
    public void Shared_problem_details_responses_use_application_problem_json()
    {
        OpenApiDocument contract = LoadContract();
        var components = Assert.IsType<Dictionary<object, object>>(contract.Root["components"]);
        var responses = Assert.IsType<Dictionary<object, object>>(components["responses"]);

        foreach (string responseName in new[] { "Unauthorized", "Forbidden", "NotFound", "ValidationError", "Conflict", "UnexpectedError" })
        {
            Assert.True(responses.TryGetValue(responseName, out object? responseValue), $"Missing shared response {responseName}.");
            Assert.True(IsProblemDetailsResponse(responseValue), $"Shared response {responseName} must use application/problem+json ProblemDetails.");
        }
    }

    private static bool RequiresRequestBody(string method, string path)
    {
        if (method.Equals("get", StringComparison.OrdinalIgnoreCase)
            || method.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !path.Equals("/terminals/{terminalId}/revoke", StringComparison.Ordinal)
            && !path.Equals("/receipts/{saleId}/issue", StringComparison.Ordinal);
    }

    private static bool IsProblemDetailsResponse(object? responseValue)
    {
        if (responseValue is not Dictionary<object, object> response)
        {
            return false;
        }

        if (response.TryGetValue("$ref", out object? reference))
        {
            string? value = reference.ToString();
            return value is not null
                && value.StartsWith("#/components/responses/", StringComparison.Ordinal);
        }

        if (!response.TryGetValue("content", out object? contentValue)
            || contentValue is not Dictionary<object, object> content)
        {
            return false;
        }

        if (!content.TryGetValue("application/problem+json", out object? mediaTypeValue)
            || mediaTypeValue is not Dictionary<object, object> mediaType)
        {
            return false;
        }

        if (!mediaType.TryGetValue("schema", out object? schemaValue)
            || schemaValue is not Dictionary<object, object> schema)
        {
            return false;
        }

        return schema.TryGetValue("$ref", out object? schemaReference)
            && string.Equals(schemaReference.ToString(), "#/components/schemas/ProblemDetails", StringComparison.Ordinal);
    }

    private static IReadOnlySet<RouteOperation> GetRuntimeRoutes()
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

        EndpointDataSource endpointDataSource = factory.Services.GetRequiredService<EndpointDataSource>();

        return endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                string path = NormalizeRuntimePath(endpoint.RoutePattern.RawText ?? string.Empty);
                HttpMethodMetadata? methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
                return methods is null
                    ? Enumerable.Empty<RouteOperation>()
                    : methods.HttpMethods.Select(method => new RouteOperation(method.ToUpperInvariant(), path));
            })
            .Where(route => !route.Path.StartsWith("/_", StringComparison.Ordinal))
            .ToHashSet();
    }

    private static OpenApiDocument LoadContract()
    {
        string root = FindRepositoryRoot();
        string contractPath = Path.Combine(root, "contracts", "openapi", "solidpos-api-v1.openapi.yaml");

        Assert.True(File.Exists(contractPath), $"OpenAPI contract not found at {contractPath}");

        string yaml = File.ReadAllText(contractPath);
        Dictionary<object, object>? parsed = new DeserializerBuilder().Build()
            .Deserialize<Dictionary<object, object>>(yaml);

        Assert.NotNull(parsed);
        var paths = Assert.IsType<Dictionary<object, object>>(parsed!["paths"]);

        List<(string Path, string Method, Dictionary<object, object> Operation)> rawOperations = [];
        HashSet<RouteOperation> operations = [];

        foreach ((object pathKey, object pathValue) in paths)
        {
            string path = NormalizeOpenApiPath(pathKey.ToString() ?? string.Empty);
            var pathItem = Assert.IsType<Dictionary<object, object>>(pathValue);

            foreach ((object methodKey, object operationValue) in pathItem)
            {
                string method = methodKey.ToString() ?? string.Empty;
                if (!HttpMethods.Contains(method))
                {
                    continue;
                }

                var operation = Assert.IsType<Dictionary<object, object>>(operationValue);
                operations.Add(new RouteOperation(method.ToUpperInvariant(), path));
                rawOperations.Add((path, method, operation));
            }
        }

        return new OpenApiDocument(parsed, operations, rawOperations);
    }

    private static string NormalizeRuntimePath(string path)
    {
        string normalized = NormalizeOpenApiPath(path);

        if (normalized.StartsWith("/api/v1/", StringComparison.Ordinal))
        {
            normalized = normalized[7..];
        }

        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, "\\{([^}:]+):[^}]+\\}", "{$1}");
        return normalized;
    }

    private static string NormalizeOpenApiPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        string normalized = path.StartsWith('/') ? path : "/" + path;
        return normalized.TrimEnd('/');
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "contracts"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record OpenApiDocument(
        Dictionary<object, object> Root,
        IReadOnlySet<RouteOperation> Operations,
        IReadOnlyCollection<(string Path, string Method, Dictionary<object, object> Operation)> RawOperations);

    private sealed record RouteOperation(string Method, string Path)
    {
        public override string ToString() => $"{Method} {Path}";
    }
}
