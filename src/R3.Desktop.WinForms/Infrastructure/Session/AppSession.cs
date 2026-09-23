using R3.Application.Security;
using R3.Infrastructure;

namespace R3.Desktop.WinForms.Infrastructure.Session;

/// <summary>
/// Signed-in context for one run: the database the user logged into, company/branch, user and permissions.
/// Screens get their company scope and permission checks from here - never from a hard-coded database.
/// </summary>
public sealed class AppSession(StoreDatabase database, string companyId, string companyName, string branchId, string branchName, string loginName, string displayName, string roleName)
{
    private IPermissionService? _permissions;

    public StoreDatabase Database { get; } = database;
    public string CompanyId { get; } = companyId;
    public string CompanyName { get; } = companyName;
    public string BranchId { get; } = branchId;
    public string BranchName { get; } = branchName;
    /// <summary>users.username - the key for permission lookups and audit.</summary>
    public string LoginName { get; } = loginName;
    public string DisplayName { get; } = displayName;
    public string RoleName { get; } = roleName;

    public IPermissionService Permissions => _permissions ??= new LocalPermissionService(Database, LoginName);

    public bool Can(string permissionCode) => Permissions.HasPermission(permissionCode);
}
