namespace R3.Application.Authentication;

public sealed record LoginBranchOption(
    int BranchId,
    string BranchCode,
    string BranchName)
{
    public string DisplayName => $"{BranchCode} • {BranchName}";
}
