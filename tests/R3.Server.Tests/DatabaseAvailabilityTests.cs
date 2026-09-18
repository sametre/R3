using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace R3.Server.Tests;

public sealed class DatabaseAvailabilityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DatabaseAvailabilityTests(WebApplicationFactory<Program> factory)
    {
        // No ConnectionStrings:R3 / R3_POSTGRES_CONNECTION is configured for this factory,
        // matching a real deployment that was started without PostgreSQL configured.
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReportsNotConfigured_WhenDatabaseIsMissing()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("not_configured", body!.Database);
    }

    [Fact]
    public async Task Metadata_RemainsAvailable_WhenDatabaseIsMissing()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/metadata");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/companies")]
    [InlineData("/api/v1/branches")]
    [InlineData("/api/v1/warehouses")]
    [InlineData("/api/v1/lookups/companies")]
    public async Task OrganizationEndpoints_Return503ProblemDetails_WhenDatabaseIsMissing(string path)
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem!.Status);
        Assert.Equal("database_not_configured", problem.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task CreateCompany_Returns503ProblemDetails_WhenDatabaseIsMissing()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/companies", new
        {
            code = "TEST",
            name = "Test Firma",
            legalName = "",
            taxOffice = "",
            taxNumber = "",
            phone = "",
            email = "",
            address = ""
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("database_not_configured", problem!.Extensions["code"]?.ToString());
    }

    private sealed record HealthResponse(string Status, string Database, DateTime Utc);
}
