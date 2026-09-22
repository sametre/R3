using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using R3.Desktop.Logging;
using R3.Infrastructure;

namespace R3.Desktop.ViewModels;

/// <summary>
/// Backs the Cari Kartı create/edit dialog. All persistence, VKN/TCKN validation and balance/credit
/// math live in <see cref="LocalAccountService"/> (Save/GetDetail) - this view model only shapes
/// input and exposes the conditional-visibility flags the XAML binds to (spec §48 "field engine":
/// plain ObservableProperty + booleans, no separate rules engine).
/// </summary>
public sealed partial class AccountEditViewModel : ObservableObject
{
    private readonly AccountServices _services;
    private readonly string _userName;
    private readonly ILogger<AccountEditViewModel> _logger;
    private string _id;

    public AccountEditViewModel(AccountServices services, string userName, string? accountId, ILogger<AccountEditViewModel>? logger = null)
    {
        _services = services;
        _userName = userName;
        _logger = logger ?? DesktopLogging.CreateLogger<AccountEditViewModel>();
        _id = accountId ?? "";

        AccountGroups = _services.MasterData.List("account_groups").DefaultView;
        Regions = _services.MasterData.List("regions").DefaultView;
        Currencies = _services.MasterData.List("currencies").DefaultView;
        PriceLists = _services.MasterData.List("price_lists").DefaultView;
        SalesRepresentatives = _services.Database.Query("SELECT id AS Id, display_name AS Name FROM users WHERE is_active=1 ORDER BY display_name").DefaultView;

        if (!string.IsNullOrEmpty(_id))
        {
            var detail = _services.Accounts.GetDetail(_services.CompanyId, _id);
            if (detail != null) Load(detail);
        }
        LoadChildViewModels();
        PropertyChanged += (_, e) => { if (e.PropertyName is not (nameof(ErrorMessage) or nameof(StatusMessage) or nameof(IsDirty))) IsDirty = true; };
    }

    /// <summary>Set on any field edit, cleared on a successful Save - backs the §51 "unsaved
    /// changes" close-confirmation in AccountEditDialog's code-behind.</summary>
    [ObservableProperty] private bool _isDirty;

    // --- fixed option lists (not database lookups - spec §14/§25/§27/§29/§41 treat these as enums) ---
    public IReadOnlyList<Option> AccountTypes { get; } =
        [new("Customer", "Müşteri"), new("Supplier", "Tedarikçi"), new("CustomerAndSupplier", "Müşteri + Tedarikçi"), new("Other", "Diğer")];
    public IReadOnlyList<Option> PersonTypes { get; } = [new("LegalEntity", "Tüzel Kişi"), new("Individual", "Gerçek Kişi")];
    public IReadOnlyList<Option> CreditControlTypes { get; } = [new("None", "Kontrol Yok"), new("Warning", "Uyar"), new("Block", "Engelle")];
    public IReadOnlyList<Option> InvoiceScenarios { get; } = [new("Temel", "Temel"), new("Ticari", "Ticari"), new("İhracat", "İhracat"), new("Kamu", "Kamu")];
    public IReadOnlyList<Option> PaymentMethods { get; } =
        [new("Cash", "Nakit"), new("BankTransfer", "Banka"), new("CreditCard", "Kredi Kartı"), new("Cheque", "Çek"), new("PromissoryNote", "Senet")];
    public IReadOnlyList<Option> Languages { get; } = [new("tr", "Türkçe"), new("en", "English"), new("de", "Deutsch")];

    public DataView AccountGroups { get; }
    public DataView Regions { get; }
    public DataView Currencies { get; }
    public DataView PriceLists { get; }
    public DataView SalesRepresentatives { get; }

    public bool IsNew => string.IsNullOrEmpty(_id);
    public bool IsExisting => !IsNew;
    public string AccountId => _id;

    [ObservableProperty] private string _title = "Yeni Cari";

    // Kimlik
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _shortName = "";
    [ObservableProperty] private string _accountType = "Customer";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _mobilePhone = "";
    [ObservableProperty] private string _email = "";

    // Ticari Bilgiler
    [ObservableProperty] private string? _accountGroupId;
    [ObservableProperty] private string? _regionId;
    [ObservableProperty] private string _defaultCurrencyCode = "TRY";
    [ObservableProperty] private string _preferredLanguageCode = "tr";
    [ObservableProperty] private string? _salesRepresentativeId;

    // Vergi Bilgileri
    [ObservableProperty] private string _personType = "LegalEntity";
    [ObservableProperty] private string _taxOffice = "";
    [ObservableProperty] private string _taxNumber = "";
    [ObservableProperty] private string _identityNumber = "";
    [ObservableProperty] private string _legalTitle = "";
    [ObservableProperty] private string _tradeRegistryNumber = "";
    [ObservableProperty] private string _countryCode = "TR";

    // Durum
    [ObservableProperty] private bool _isActive = true;

    // Finans / Kredi-Risk
    [ObservableProperty] private decimal _balance;
    [ObservableProperty] private decimal _creditLimit;
    [ObservableProperty] private decimal _extraCreditLimit;
    [ObservableProperty] private decimal _blockedCreditAmount;
    [ObservableProperty] private decimal _riskLimit;
    [ObservableProperty] private string _creditControlType = "None";

    // Satış Ayarları (Customer)
    [ObservableProperty] private string _customerGroup = "";
    [ObservableProperty] private string _customerRegion = "";
    [ObservableProperty] private string? _priceListId;
    [ObservableProperty] private int _paymentTermDays;
    [ObservableProperty] private decimal _discountRate;
    [ObservableProperty] private string _defaultPaymentMethod = "";
    [ObservableProperty] private bool _isOrderBlocked;
    [ObservableProperty] private bool _isActiveBuyer = true;
    [ObservableProperty] private DateTime? _customerStartedAt;
    [ObservableProperty] private bool _kvkkConsent;

    // Alış Ayarları (Supplier)
    [ObservableProperty] private string _supplierGroup = "";
    [ObservableProperty] private string _supplierRegion = "";
    [ObservableProperty] private string _supplierDefaultCurrencyCode = "TRY";
    [ObservableProperty] private int _supplierPaymentTermDays;
    [ObservableProperty] private int _leadTimeDays;
    [ObservableProperty] private bool _isActiveSupplier = true;
    [ObservableProperty] private string _supplierNotes = "";

    // E-Belge
    [ObservableProperty] private bool _isEInvoiceEnabled;
    [ObservableProperty] private string _einvoiceAlias = "";
    [ObservableProperty] private string _invoiceScenario = "Temel";
    [ObservableProperty] private bool _isEDispatchEnabled;
    [ObservableProperty] private string _edispatchAlias = "";
    [ObservableProperty] private bool _isGibCompliant;
    [ObservableProperty] private bool _printInvoice = true;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;

    public event EventHandler? Saved;
    public event EventHandler? Closed;

    // --- conditional visibility (spec §48) ---
    public bool ShowCustomerFields => AccountType is "Customer" or "CustomerAndSupplier";
    public bool ShowSupplierFields => AccountType is "Supplier" or "CustomerAndSupplier";
    public string AccountTypeLabel => AccountType switch { "Customer" => "Müşteri", "Supplier" => "Tedarikçi", "CustomerAndSupplier" => "Müşteri + Tedarikçi", _ => "Diğer" };
    public bool ShowTaxNumber => PersonType == "LegalEntity";
    public bool ShowNationalIdentity => PersonType == "Individual";
    public bool AllowOrders { get => !IsOrderBlocked; set => IsOrderBlocked = !value; }
    public decimal EffectiveCreditLimit => CreditLimit + ExtraCreditLimit - BlockedCreditAmount;
    public decimal AvailableCredit => EffectiveCreditLimit <= 0 ? 0 : Math.Max(0, EffectiveCreditLimit - Math.Max(0, Balance));
    public string BalanceStatus => Balance switch { > 0 => "Borçlu", < 0 => "Alacaklı", _ => "Dengede" };

    partial void OnAccountTypeChanged(string value) { OnPropertyChanged(nameof(ShowCustomerFields)); OnPropertyChanged(nameof(ShowSupplierFields)); OnPropertyChanged(nameof(AccountTypeLabel)); }
    partial void OnPersonTypeChanged(string value) { OnPropertyChanged(nameof(ShowTaxNumber)); OnPropertyChanged(nameof(ShowNationalIdentity)); }
    partial void OnIsOrderBlockedChanged(bool value) => OnPropertyChanged(nameof(AllowOrders));
    partial void OnCreditLimitChanged(decimal value) => RaiseCreditChanged();
    partial void OnExtraCreditLimitChanged(decimal value) => RaiseCreditChanged();
    partial void OnBlockedCreditAmountChanged(decimal value) => RaiseCreditChanged();
    partial void OnBalanceChanged(decimal value) { RaiseCreditChanged(); OnPropertyChanged(nameof(BalanceStatus)); }
    private void RaiseCreditChanged() { OnPropertyChanged(nameof(EffectiveCreditLimit)); OnPropertyChanged(nameof(AvailableCredit)); }

    public AccountAddressesContactsViewModel? AddressesContacts { get; private set; }
    public AccountBankListViewModel? BankAccounts { get; private set; }
    public AccountNoteListViewModel? Notes { get; private set; }
    public AccountDocumentsViewModel? Documents { get; private set; }
    public AccountHistoryViewModel? History { get; private set; }
    public AccountLedgerViewModel? Ledger { get; private set; }

    private void LoadChildViewModels()
    {
        if (string.IsNullOrEmpty(_id)) return;
        AddressesContacts = AccountAddressesContactsViewModel.Create(_services.Database, _id);
        BankAccounts = new AccountBankListViewModel(_services.Banks, _id, DesktopLogging.CreateLogger<AccountBankListViewModel>());
        Notes = new AccountNoteListViewModel(_services.Notes, _id, _userName, DesktopLogging.CreateLogger<AccountNoteListViewModel>());
        Documents = new AccountDocumentsViewModel(_services.Accounts, _services.CompanyId, _id, DesktopLogging.CreateLogger<AccountDocumentsViewModel>());
        History = new AccountHistoryViewModel(_services.Accounts, _id, DesktopLogging.CreateLogger<AccountHistoryViewModel>());
        Ledger = new AccountLedgerViewModel(_services.Accounts, _services.CompanyId, _id, DesktopLogging.CreateLogger<AccountLedgerViewModel>());
        OnPropertyChanged(nameof(AddressesContacts)); OnPropertyChanged(nameof(Notes)); OnPropertyChanged(nameof(Documents));
        OnPropertyChanged(nameof(BankAccounts));
        OnPropertyChanged(nameof(History)); OnPropertyChanged(nameof(Ledger));
    }

    private void Load(AccountDetail d)
    {
        Title = "Cari Kartı";
        Code = d.Account.Code; Name = d.Account.Name; ShortName = d.Account.ShortName; AccountType = d.Account.AccountType;
        Phone = d.Account.Phone; MobilePhone = d.Account.MobilePhone; Email = d.Account.Email;
        AccountGroupId = d.Account.AccountGroupId; RegionId = d.Account.RegionId; DefaultCurrencyCode = d.Account.DefaultCurrencyCode;
        PreferredLanguageCode = d.Account.PreferredLanguageCode; TaxOffice = d.Account.TaxOffice; TaxNumber = d.Account.TaxNumber;
        IdentityNumber = d.Account.IdentityNumber; IsActive = d.Account.IsActive; CreditLimit = d.Account.CreditLimit; RiskLimit = d.Account.RiskLimit;

        PersonType = d.Tax.PersonType; LegalTitle = d.Tax.LegalTitle; TradeRegistryNumber = d.Tax.TradeRegistryNumber; CountryCode = d.Tax.CountryCode;

        IsEInvoiceEnabled = d.EInvoice.IsEInvoiceEnabled; EinvoiceAlias = d.EInvoice.EInvoiceAlias; InvoiceScenario = d.EInvoice.InvoiceScenario;
        IsEDispatchEnabled = d.EInvoice.IsEDispatchEnabled; EdispatchAlias = d.EInvoice.EDispatchAlias; IsGibCompliant = d.EInvoice.IsGibCompliant; PrintInvoice = d.EInvoice.PrintInvoice;

        if (d.Customer is { } cust)
        {
            CustomerGroup = cust.CustomerGroup; CustomerRegion = cust.Region; SalesRepresentativeId = cust.SalesRepresentativeId; PriceListId = cust.PriceListId;
            PaymentTermDays = cust.PaymentTermDays; DiscountRate = cust.DiscountRate; ExtraCreditLimit = cust.ExtraCreditLimit; BlockedCreditAmount = cust.BlockedCreditAmount;
            CreditControlType = cust.CreditControlType; DefaultPaymentMethod = cust.DefaultPaymentMethod; IsOrderBlocked = cust.IsOrderBlocked;
            IsActiveBuyer = cust.IsActiveBuyer; CustomerStartedAt = cust.StartedAt; KvkkConsent = cust.KvkkConsent;
        }
        if (d.Supplier is { } sup)
        {
            SupplierGroup = sup.SupplierGroup; SupplierRegion = sup.Region; SupplierDefaultCurrencyCode = sup.DefaultCurrencyCode;
            SupplierPaymentTermDays = sup.PaymentTermDays; LeadTimeDays = sup.LeadTimeDays; IsActiveSupplier = sup.IsActiveSupplier; SupplierNotes = sup.Notes;
        }
        Balance = d.Balance;
    }

    [RelayCommand]
    private void Save()
    {
        var isNew = string.IsNullOrEmpty(_id);
        try
        {
            var account = new AccountEdit(_id, _services.CompanyId, Code, Name, AccountType, TaxOffice, TaxNumber, Phone, MobilePhone, Email,
                CreditLimit, RiskLimit, IsActive, IdentityNumber, ShortName, DefaultCurrencyCode, PreferredLanguageCode, AccountGroupId, RegionId);
            var tax = new AccountTaxProfileEdit(PersonType, LegalTitle, TradeRegistryNumber, CountryCode);
            var einvoice = new AccountEInvoiceProfileEdit(IsEInvoiceEnabled, EinvoiceAlias, InvoiceScenario, IsEDispatchEnabled, EdispatchAlias, IsGibCompliant, PrintInvoice);
            var customer = new CustomerProfileEdit(CustomerGroup, CustomerRegion, SalesRepresentativeId, PriceListId, PaymentTermDays, DiscountRate,
                ExtraCreditLimit, BlockedCreditAmount, CreditControlType, DefaultPaymentMethod, IsOrderBlocked, IsActiveBuyer, CustomerStartedAt, KvkkConsent);
            var supplier = new SupplierProfileEdit(SupplierGroup, SupplierRegion, SupplierDefaultCurrencyCode, SupplierPaymentTermDays, LeadTimeDays, IsActiveSupplier, SupplierNotes);

            _services.Accounts.Save(new AccountAggregateEdit(account, tax, einvoice, customer, supplier));
            ErrorMessage = null;

            if (isNew)
            {
                var detail = _services.Accounts.GetDetail(_services.CompanyId, ResolveNewId());
                if (detail != null) { _id = detail.Account.Id; Load(detail); OnPropertyChanged(nameof(IsNew)); OnPropertyChanged(nameof(IsExisting)); OnPropertyChanged(nameof(AccountId)); LoadChildViewModels(); }
                StatusMessage = "Cari kaydedildi. Diğer sekmeler artık kullanılabilir.";
            }
            else
            {
                StatusMessage = "Değişiklikler kaydedildi.";
            }
            IsDirty = false;
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "Account {Action} failed. CompanyId={CompanyId} SqliteErrorCode={SqliteErrorCode}", isNew ? "creation" : "update", _services.CompanyId, ex.SqliteErrorCode);
            ErrorMessage = ex.SqliteErrorCode == 19 ? "Bu cari kodu zaten kullanılıyor." : "Cari kaydı kaydedilirken bir hata oluştu.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Account {Action} failed. CompanyId={CompanyId}", isNew ? "creation" : "update", _services.CompanyId);
            ErrorMessage = "Cari kaydı kaydedilirken beklenmeyen bir hata oluştu.";
        }
    }

    /// <summary>A brand-new account's id is generated inside LocalAccountService.Save; the code we
    /// just typed is still unique per company, so it is the only handle we have back to that row.</summary>
    private string ResolveNewId() =>
        _services.Database.Query("SELECT id FROM accounts WHERE company_id=$c AND code=$code", ("$c", _services.CompanyId), ("$code", Code.Trim().ToUpperInvariant())).Rows[0]["id"].ToString()!;

    public ManualLedgerEntryViewModel? CreateManualEntryViewModel() =>
        IsNew ? null : new ManualLedgerEntryViewModel(_services, _id, Name, DesktopLogging.CreateLogger<ManualLedgerEntryViewModel>());

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);
}
