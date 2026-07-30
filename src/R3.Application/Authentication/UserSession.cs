using R3.Application.Abstractions;

namespace R3.Application.Authentication;

public sealed class UserSession : IUserSession
{
    private UserSessionData? _current;

    public bool IsAuthenticated => _current is not null;

    public UserSessionData Current =>
        _current ?? throw new InvalidOperationException("Aktif kullanıcı oturumu bulunamadı.");

    public void Start(UserSessionData session) =>
        _current = session ?? throw new ArgumentNullException(nameof(session));

    public void Clear() => _current = null;
}
