using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

/// <summary>Bundles the Local*Service instances and workspace ids every Cari Yönetimi screen needs,
/// so new dialogs/views don't grow long constructor parameter lists. All services are the same
/// shared instances MainWindow already owns - no new persistence layer is created here.</summary>
public sealed record AccountServices(
    LocalAccountService Accounts,
    LocalAccountAddressService Addresses,
    LocalAccountContactService Contacts,
    LocalAccountBankService Banks,
    LocalAccountNoteService Notes,
    LocalMasterDataService MasterData,
    StoreDatabase Database,
    string CompanyId,
    string BranchId);
