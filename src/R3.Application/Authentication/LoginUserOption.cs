namespace R3.Application.Authentication;

public sealed record LoginUserOption(
    int UserId,
    string UserName,
    string DisplayName)
{
    public string DisplayText => $"{DisplayName} ({UserName})";
}
