using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

/// <summary>Bundles the Local*Service instances and workspace ids Kasa Yönetimi screens need -
/// same purpose as <see cref="AccountServices"/> for the Cari module.</summary>
public sealed record CashServices(
    LocalCashService Cash,
    LocalAccountService Accounts,
    LocalMasterDataService MasterData,
    StoreDatabase Database,
    string CompanyId,
    string BranchId,
    string UserName);
