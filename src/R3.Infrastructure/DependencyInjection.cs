using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using R3.Application.Abstractions;
using R3.Application.Authentication;
using R3.Infrastructure.Persistence;
using R3.Infrastructure.Security;
using R3.Application.MasterData;
using R3.Infrastructure.MasterData;
using R3.Application.Transactions;
using R3.Infrastructure.Transactions;
using R3.Application.Dashboard;
using R3.Infrastructure.Dashboard;

namespace R3.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddR3Infrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("R3Database")
            ?? throw new InvalidOperationException("R3Database bağlantı dizesi bulunamadı.");

        services.AddDbContext<R3DbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
                sql.CommandTimeout(30);
            }));

        services.AddScoped<IDataConnectionVerifier, DatabaseConnectionVerifier>();
        services.AddScoped<IAuthenticationService, SqlAuthenticationService>();
        services.AddScoped<IRetailMasterDataService, SqlRetailMasterDataService>();
        services.AddScoped<ITradeTransactionService, SqlTradeTransactionService>();
        services.AddScoped<IDashboardService, SqlDashboardService>();
        services.AddSingleton<IUserSession, UserSession>();
        return services;
    }
}
