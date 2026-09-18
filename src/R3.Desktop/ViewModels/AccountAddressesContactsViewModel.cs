using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

/// <summary>
/// Composes the two independent list view models the İletişim &amp; Adres tab
/// shows side by side. No logic of its own - each half owns its own state.
/// </summary>
public sealed class AccountAddressesContactsViewModel(
    AccountAddressListViewModel addressList,
    AccountContactListViewModel contactList)
{
    public AccountAddressListViewModel AddressList { get; } = addressList;
    public AccountContactListViewModel ContactList { get; } = contactList;

    public static AccountAddressesContactsViewModel Create(StoreDatabase database, string accountId) => new(
        new AccountAddressListViewModel(new LocalAccountAddressService(database), accountId, Logging.DesktopLogging.CreateLogger<AccountAddressListViewModel>()),
        new AccountContactListViewModel(new LocalAccountContactService(database), accountId, Logging.DesktopLogging.CreateLogger<AccountContactListViewModel>()));
}
