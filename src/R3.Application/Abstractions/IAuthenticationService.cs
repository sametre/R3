using R3.Application.Authentication;

namespace R3.Application.Abstractions;

public interface IAuthenticationService
{
    Task<IReadOnlyList<LoginBranchOption>> GetBranchesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LoginUserOption>> GetUsersAsync(
        int branchId,
        CancellationToken cancellationToken = default);

    Task<LoginResult> AuthenticateAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);
}
