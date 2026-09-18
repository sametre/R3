using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using R3.Infrastructure;
using R3.Contracts;
using R3.Application;
using R3.Server;
using FluentValidation;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((_, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(
        "logs/r3-server-.log",
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 50 * 1024 * 1024,
        rollOnFileSizeLimit: true));
var connectionString = builder.Configuration.GetConnectionString("R3") ?? Environment.GetEnvironmentVariable("R3_POSTGRES_CONNECTION");
var isDatabaseConfigured = !string.IsNullOrWhiteSpace(connectionString);
if (isDatabaseConfigured)
{
    builder.Services.AddDbContext<R3DbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddScoped<OrganizationService>();
}
builder.Services.AddValidatorsFromAssemblyContaining<CreateCompanyValidator>();
var app = builder.Build();
app.UseExceptionHandler(error => error.Run(context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error
        ?? new Exception("Unknown error (no IExceptionHandlerFeature).");
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    return GlobalExceptionHandling.HandleAsync(context, exception, logger);
}));
app.UseSerilogRequestLogging(options =>
{
    // Default Serilog behavior logs every 5xx as Error, which would make the
    // intentional/expected 503 "database not configured" response (see the
    // gate below) look like a real production incident on every request.
    options.GetLevel = (httpContext, _, exception) => exception is not null
        ? Serilog.Events.LogEventLevel.Error
        : httpContext.Response.StatusCode switch
        {
            StatusCodes.Status503ServiceUnavailable => Serilog.Events.LogEventLevel.Warning,
            >= 500 => Serilog.Events.LogEventLevel.Error,
            >= 400 => Serilog.Events.LogEventLevel.Warning,
            _ => Serilog.Events.LogEventLevel.Information,
        };
});
// Endpoints under /api/v1 (except metadata) depend on OrganizationService, which is only
// registered when a PostgreSQL connection string is configured. Without this gate, calling
// one of them would fail with an internal DI resolution error surfaced as a generic 500.
// Short-circuit here instead, before routing reaches the endpoint, so the client always gets
// a machine-readable 503 and no DI/internal exception detail ever leaks.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (!isDatabaseConfigured && path.StartsWithSegments("/api/v1") && !path.StartsWithSegments("/api/v1/metadata"))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Database is not configured",
            Detail = "Sunucu PostgreSQL bağlantısı olmadan başlatıldı. R3_POSTGRES_CONNECTION ortam değişkenini veya ConnectionStrings:R3 ayarını yapılandırın.",
            Type = "https://r3erp.dev/problems/database-not-configured",
            Extensions = { ["code"] = "database_not_configured" }
        }, options: null, contentType: "application/problem+json");
        return;
    }
    await next();
});
app.MapGet("/health", async (IServiceProvider services, CancellationToken cancellationToken) =>
{
    var db = services.GetService<R3DbContext>();
    var database = db is null ? "not_configured" : await db.Database.CanConnectAsync(cancellationToken) ? "ok" : "unavailable";
    return Results.Ok(new { status = "Healthy", database, utc = DateTime.UtcNow });
});
app.MapGet("/api/v1/metadata", () => Results.Ok(new { name = "R3 ERP", version = "0.3.0", database = "PostgreSQL", modules = new[] { "Organization", "Catalog", "Accounts" } }));
app.MapGet("/api/v1/companies", async ([FromServices] OrganizationService s, int page = 1, int pageSize = 50, string? search = null, bool? isActive = null, CancellationToken ct = default) => Results.Ok(await s.SearchCompaniesAsync(new PagedRequest(page, pageSize, search), isActive, ct)));
app.MapGet("/api/v1/companies/{id:guid}", async (Guid id, [FromServices] OrganizationService s, CancellationToken ct) => (await s.GetCompanyAsync(id, ct)) is { } value ? Results.Ok(value) : Results.NotFound(new ApiError("company.not_found", "Firma bulunamadı.", new Dictionary<string, string[]>(), "")));
app.MapPost("/api/v1/companies", async (CreateCompanyRequest request, [FromServices] IValidator<CreateCompanyRequest> validator, [FromServices] OrganizationService s, CancellationToken ct) => { var v = await validator.ValidateAsync(request, ct); if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary()); return Results.Created("", await s.CreateCompanyAsync(request, ct)); });
app.MapPut("/api/v1/companies/{id:guid}", async (Guid id, UpdateCompanyRequest request, [FromServices] IValidator<UpdateCompanyRequest> validator, [FromServices] OrganizationService s, CancellationToken ct) => { var v = await validator.ValidateAsync(request, ct); if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary()); return Results.Ok(await s.UpdateCompanyAsync(id, request, ct)); });
app.MapPost("/api/v1/companies/{id:guid}/activate", (Guid id, [FromServices] OrganizationService s, CancellationToken ct) => s.SetCompanyActiveAsync(id, true, ct).ContinueWith(_ => Results.NoContent()));
app.MapPost("/api/v1/companies/{id:guid}/deactivate", (Guid id, [FromServices] OrganizationService s, CancellationToken ct) => s.SetCompanyActiveAsync(id, false, ct).ContinueWith(_ => Results.NoContent()));
app.MapGet("/api/v1/branches", async ([FromServices] OrganizationService s, Guid? companyId = null, int page = 1, int pageSize = 50, string? search = null, bool? isActive = null, CancellationToken ct = default) => Results.Ok(await s.SearchBranchesAsync(new PagedRequest(page, pageSize, search), companyId, isActive, ct)));
app.MapGet("/api/v1/branches/{id:guid}", async (Guid id, [FromServices] OrganizationService s, CancellationToken ct) => (await s.GetBranchAsync(id, ct)) is { } value ? Results.Ok(value) : Results.NotFound());
app.MapPost("/api/v1/branches", async (CreateBranchRequest request, [FromServices] IValidator<CreateBranchRequest> validator, [FromServices] OrganizationService s, CancellationToken ct) => { var v = await validator.ValidateAsync(request, ct); if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary()); return Results.Created("", await s.CreateBranchAsync(request, ct)); });
app.MapPut("/api/v1/branches/{id:guid}", async (Guid id, UpdateBranchRequest request, [FromServices] IValidator<UpdateBranchRequest> validator, [FromServices] OrganizationService s, CancellationToken ct) => { var v = await validator.ValidateAsync(request, ct); if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary()); return Results.Ok(await s.UpdateBranchAsync(id, request, ct)); });
app.MapPost("/api/v1/branches/{id:guid}/activate", (Guid id, [FromServices] OrganizationService s, CancellationToken ct) => s.SetBranchActiveAsync(id, true, ct).ContinueWith(_ => Results.NoContent()));
app.MapPost("/api/v1/branches/{id:guid}/deactivate", (Guid id, [FromServices] OrganizationService s, CancellationToken ct) => s.SetBranchActiveAsync(id, false, ct).ContinueWith(_ => Results.NoContent()));
app.MapGet("/api/v1/warehouses", async ([FromServices] OrganizationService s, Guid? companyId = null, Guid? branchId = null, int page = 1, int pageSize = 50, string? search = null, bool? isActive = null, CancellationToken ct = default) => Results.Ok(await s.SearchWarehousesAsync(new PagedRequest(page, pageSize, search), companyId, branchId, isActive, ct)));
app.MapGet("/api/v1/warehouses/{id:guid}", async (Guid id, [FromServices] OrganizationService s, CancellationToken ct) => (await s.GetWarehouseAsync(id, ct)) is { } value ? Results.Ok(value) : Results.NotFound());
app.MapPost("/api/v1/warehouses", async (CreateWarehouseRequest request, [FromServices] IValidator<CreateWarehouseRequest> validator, [FromServices] OrganizationService s, CancellationToken ct) => { var v = await validator.ValidateAsync(request, ct); if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary()); return Results.Created("", await s.CreateWarehouseAsync(request, ct)); });
app.MapPut("/api/v1/warehouses/{id:guid}", async (Guid id, UpdateWarehouseRequest request, [FromServices] IValidator<UpdateWarehouseRequest> validator, [FromServices] OrganizationService s, CancellationToken ct) => { var v = await validator.ValidateAsync(request, ct); if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary()); return Results.Ok(await s.UpdateWarehouseAsync(id, request, ct)); });
app.MapPost("/api/v1/warehouses/{id:guid}/activate", (Guid id, [FromServices] OrganizationService s, CancellationToken ct) => s.SetWarehouseActiveAsync(id, true, ct).ContinueWith(_ => Results.NoContent()));
app.MapPost("/api/v1/warehouses/{id:guid}/deactivate", (Guid id, [FromServices] OrganizationService s, CancellationToken ct) => s.SetWarehouseActiveAsync(id, false, ct).ContinueWith(_ => Results.NoContent()));
app.MapGet("/api/v1/lookups/companies", ([FromServices] OrganizationService s, CancellationToken ct) => s.CompanyLookupAsync(ct));
app.MapGet("/api/v1/lookups/branches", (Guid companyId, [FromServices] OrganizationService s, CancellationToken ct) => s.BranchLookupAsync(companyId, ct));
app.Run();

// Exposes the top-level Program for WebApplicationFactory<Program> in integration tests.
public partial class Program;

