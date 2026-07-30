namespace R3.Application.Authentication;

public sealed record LoginResult(
    bool Succeeded,
    string? ErrorMessage,
    UserSessionData? Session)
{
    public static LoginResult Success(UserSessionData session) => new(true, null, session);
    public static LoginResult Failure(string errorMessage) => new(false, errorMessage, null);
}
