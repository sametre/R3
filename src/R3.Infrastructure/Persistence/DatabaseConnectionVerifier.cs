using Microsoft.EntityFrameworkCore;
using R3.Application.Abstractions;

namespace R3.Infrastructure.Persistence;

internal sealed class DatabaseConnectionVerifier(R3DbContext dbContext) : IDataConnectionVerifier
{
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default) =>
        dbContext.Database.CanConnectAsync(cancellationToken);
}
