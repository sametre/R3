namespace R3.Application.Abstractions;

public interface IDataConnectionVerifier
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);
}
