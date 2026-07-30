using R3.Application.Authentication;

namespace R3.Application.Abstractions;

public interface IUserSession
{
    bool IsAuthenticated { get; }
    UserSessionData Current { get; }
    void Start(UserSessionData session);
    void Clear();
}
