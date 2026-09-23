using System.Data;

namespace R3.Infrastructure;

public sealed record SessionOption(string Id, string Code, string Name)
{
    public override string ToString() => $"{Code} — {Name}";
}

public sealed record SignInResult(bool Success, string? Error, string DisplayName = "", string RoleCode = "", string RoleName = "");

/// <summary>
/// Sign-in lookups and checks shared by desktop clients (the queries the WPF login window runs inline):
/// active companies, a company's active branches, the user's default branch, and the credential + branch-access
/// check in one call.
/// </summary>
public sealed class LocalSessionService(StoreDatabase database)
{
    public IReadOnlyList<SessionOption> Companies() =>
        Options(database.Query("SELECT id, code, name FROM companies WHERE is_active=1 ORDER BY code"));

    public IReadOnlyList<SessionOption> Branches(string companyId) =>
        Options(database.Query("SELECT id, code, name FROM branches WHERE company_id=$c AND is_active=1 ORDER BY code", ("$c", companyId)));

    public string? DefaultBranchId(string userName)
    {
        var rows = database.Query("SELECT default_branch_id FROM users WHERE username=$u AND default_branch_id IS NOT NULL", ("$u", userName.Trim()));
        return rows.Rows.Count == 0 ? null : rows.Rows[0][0]?.ToString();
    }

    /// <summary>Credentials, then şube erişimi (null allowed-set = unrestricted), then the role.</summary>
    public SignInResult SignIn(string userName, string password, string branchId)
    {
        var displayName = database.Authenticate(userName, password);
        if (displayName == null) return new(false, "Kullanıcı adı veya şifre hatalı.");
        if (new LocalUserAdminService(database).AllowedBranchIds(userName) is { } allowed && !allowed.Contains(branchId))
            return new(false, "Seçilen şubeye giriş yetkiniz yok. Yetkili olduğunuz bir şube seçin.");
        var role = database.GetUserRole(userName);
        return new(true, null, displayName, role.Code, role.Name);
    }

    private static List<SessionOption> Options(DataTable table) =>
        table.Rows.Cast<DataRow>().Select(r => new SessionOption(r[0].ToString()!, r[1].ToString()!, r[2].ToString()!)).ToList();
}
