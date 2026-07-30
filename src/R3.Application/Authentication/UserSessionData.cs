namespace R3.Application.Authentication;

public sealed record UserSessionData(
    int UserId,
    string UserName,
    string DisplayName,
    int CompanyId,
    string CompanyName,
    int BranchId,
    string BranchCode,
    string BranchName);
