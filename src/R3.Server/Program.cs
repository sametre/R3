using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using R3.Infrastructure;
using R3.Contracts;
using R3.Application;
using FluentValidation;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((_, configuration) => configuration.WriteTo.Console().WriteTo.File("logs/server-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));
var connectionString = builder.Configuration.GetConnectionString("R3") ?? Environment.GetEnvironmentVariable("R3_POSTGRES_CONNECTION");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<R3DbContext>(options => options.UseNpgsql(connectionString));
    builder.Services.AddScoped<OrganizationService>();
}
builder.Services.AddValidatorsFromAssemblyContaining<CreateCompanyValidator>();
var app = builder.Build();
app.UseExceptionHandler(error => error.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var trace = context.TraceIdentifier; context.Response.ContentType = "application/json";
    if (exception is OrganizationRuleException rule) { context.Response.StatusCode = StatusCodes.Status409Conflict; await context.Response.WriteAsJsonAsync(new ApiError(rule.Code, rule.Message, new Dictionary<string, string[]>(), trace)); return; }
    context.Response.StatusCode = StatusCodes.Status500InternalServerError; await context.Response.WriteAsJsonAsync(new ApiError("server.error", "Beklenmeyen bir sunucu hatası oluştu.", new Dictionary<string, string[]>(), trace));
}));
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


