using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using R3.Contracts;
using R3.Infrastructure;

namespace R3.Server.Tests;

public sealed class GlobalExceptionHandlingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task UnexpectedException_Returns500_WithGenericMessage_AndLogsErrorWithFullDetail()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var logger = new CapturingLogger<GlobalExceptionHandlingTests>();

        await GlobalExceptionHandling.HandleAsync(
            context, new InvalidOperationException("Host=db;Password=Sup3rSecret failed to connect"), logger);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        var error = await ReadErrorAsync(context);
        Assert.Equal("server.error", error.Code);
        Assert.DoesNotContain("Sup3rSecret", error.Message);
        Assert.DoesNotContain("InvalidOperationException", error.Message);
        Assert.NotEmpty(error.TraceId);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        // The full technical detail must reach the log (that's the point of logging it),
        // just never the HTTP response asserted above.
        Assert.Contains("Sup3rSecret", entry.Exception!.Message);
    }

    [Fact]
    public async Task OrganizationRuleException_Returns409_WithRuleMessage_AndLogsWarningNotError()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var logger = new CapturingLogger<GlobalExceptionHandlingTests>();

        await GlobalExceptionHandling.HandleAsync(
            context, new OrganizationRuleException("company.code_duplicate", "Bu firma kodu zaten kullanılıyor."), logger);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        var error = await ReadErrorAsync(context);
        Assert.Equal("company.code_duplicate", error.Code);
        Assert.Equal("Bu firma kodu zaten kullanılıyor.", error.Message);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
    }

    private static async Task<ApiError> ReadErrorAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<ApiError>(body, JsonOptions)!;
    }
}
