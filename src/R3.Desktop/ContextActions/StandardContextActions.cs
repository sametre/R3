using System.Data;
using R3.Desktop.ViewModels;

namespace R3.Desktop.ContextActions;

public static class StandardContextActions
{
    private static Task<ContextActionResult> Run(Action action, bool refresh = false) { action(); return Task.FromResult(ContextActionResult.Ok(refresh: refresh)); }

    public static IReadOnlyList<ContextActionDefinition> Accounts(
        Action open, Action edit, Action statement, Action transactions, Action receipt, Action payment,
        Func<bool, Task> setActive, Action relatedDetails)
    {
        bool Active(object? x) => x is AccountRowViewModel { IsActive: true };
        bool Customer(object? x) => x is AccountRowViewModel { AccountType: "Customer" or "CustomerAndSupplier" };
        bool Supplier(object? x) => x is AccountRowViewModel { AccountType: "Supplier" or "CustomerAndSupplier" };
        return
        [
            A("accounts.open", "Cari Kartını Aç", "accounts.view", "", 10, ContextActionGroup.Primary, _ => Run(open)),
            A("accounts.edit", "Cariyi Düzenle", "accounts.edit", "", 20, ContextActionGroup.Primary, _ => Run(edit)),
            A("accounts.statement", "Cari Ekstre", "accounts.statement.view", "", 10, ContextActionGroup.Related, _ => Run(statement)),
            A("accounts.transactions", "Cari Hareketleri", "accounts.transaction.view", "", 20, ContextActionGroup.Related, _ => Run(transactions)),
            A("accounts.details", "Adresler / Yetkililer / E-Belge / Notlar", "accounts.view", "", 30, ContextActionGroup.Related, _ => Run(relatedDetails)),
            A("accounts.receipt", "Tahsilat Yap", "accounts.receipt.create", "", 10, ContextActionGroup.Financial, _ => Run(receipt, true), Customer, Active),
            A("accounts.payment", "Ödeme Yap", "accounts.payment.create", "", 20, ContextActionGroup.Financial, _ => Run(payment, true), Supplier, Active),
            A("accounts.activate", "Aktif Yap", "accounts.edit", "", 10, ContextActionGroup.Critical, async _ => { await setActive(true); return ContextActionResult.Ok(refresh: true); }, x => !Active(x)),
            A("accounts.deactivate", "Pasife Al", "accounts.deactivate", "", 20, ContextActionGroup.Critical, async _ => { await setActive(false); return ContextActionResult.Ok(refresh: true); }, Active, Active, true, "AccountDeactivated", "Account"),
            A("accounts.audit", "Geçmiş / Audit", "accounts.audit.view", "", 10, ContextActionGroup.Audit, _ => Run(open))
        ];
    }

    public static IReadOnlyList<ContextActionDefinition> Cash(
        Action open, Action edit, Action transactions, Action statement, Action cashIn, Action cashOut,
        Action transfer, Func<bool, Task> setActive)
    {
        bool Active(object? x) => x is CashAccountRowViewModel { IsActive: true };
        return
        [
            A("cash.open", "Kasayı Aç", "cash.view", "", 10, ContextActionGroup.Primary, _ => Run(open)),
            A("cash.edit", "Kasayı Düzenle", "cash.edit", "", 20, ContextActionGroup.Primary, _ => Run(edit)),
            A("cash.transactions", "Kasa Hareketleri", "cash.transaction.view", "", 10, ContextActionGroup.Related, _ => Run(transactions)),
            A("cash.statement", "Kasa Ekstresi", "cash.statement.view", "", 20, ContextActionGroup.Related, _ => Run(statement)),
            A("cash.in", "Nakit Giriş", "cash.transaction.in", "", 10, ContextActionGroup.Financial, _ => Run(cashIn, true), null, Active),
            A("cash.out", "Nakit Çıkış", "cash.transaction.out", "", 20, ContextActionGroup.Financial, _ => Run(cashOut, true), null, Active),
            A("cash.transfer", "Başka Kasaya Transfer", "cash.transfer.create", "", 10, ContextActionGroup.Operational, _ => Run(transfer, true), null, Active),
            A("cash.activate", "Aktif Yap", "cash.edit", "", 10, ContextActionGroup.Critical, async _ => { await setActive(true); return ContextActionResult.Ok(refresh: true); }, x => !Active(x)),
            A("cash.deactivate", "Pasife Al", "cash.deactivate", "", 20, ContextActionGroup.Critical, async _ => { await setActive(false); return ContextActionResult.Ok(refresh: true); }, Active, Active, true, "CashAccountDeactivated", "CashAccount"),
            A("cash.audit", "Geçmiş / Audit", "cash.audit.view", "", 10, ContextActionGroup.Audit, _ => Run(open))
        ];
    }

    public static IReadOnlyList<ContextActionDefinition> Products(Action open, Action edit, Action movements, Action balances, Action receive, Action issue, Action transfer, Action count)
    {
        bool Active(object? x) => x is DataRowView row && Convert.ToBoolean(row["Aktif"]);
        // LocalProductService's list query projects product_type AS UrunTipi, not "ProductType" -
        // reading the wrong column name threw ArgumentException (crashing the whole app, since a
        // context menu evaluation exception reaches the fatal DispatcherUnhandledException handler)
        // every time a product row's context menu was opened.
        bool Stock(object? x) => x is DataRowView row && !string.Equals(row["UrunTipi"].ToString(), "Service", StringComparison.OrdinalIgnoreCase);
        bool Posting(object? x) => Active(x) && Stock(x);
        return
        [
            A("inventory.product.open", "Ürün Kartını Aç", "inventory.product.view", "", 10, ContextActionGroup.Primary, _ => Run(open)),
            A("inventory.product.edit", "Ürünü Düzenle", "inventory.product.edit", "", 20, ContextActionGroup.Primary, _ => Run(edit)),
            A("inventory.movements", "Stok Hareketleri", "inventory.transaction.view", "", 10, ContextActionGroup.Related, _ => Run(movements)),
            A("inventory.balances", "Depo Stokları", "inventory.transaction.view", "", 20, ContextActionGroup.Related, _ => Run(balances)),
            A("inventory.receive", "Stok Girişi", "inventory.transaction.receive", "", 10, ContextActionGroup.Operational, _ => Run(receive, true), Stock, Posting),
            A("inventory.issue", "Stok Çıkışı", "inventory.transaction.issue", "", 20, ContextActionGroup.Operational, _ => Run(issue, true), Stock, Posting),
            A("inventory.transfer", "Depolar Arası Transfer", "inventory.transfer.create", "", 30, ContextActionGroup.Operational, _ => Run(transfer, true), Stock, Posting),
            A("inventory.count", "Sayım Düzeltmesi", "inventory.count.adjust", "", 40, ContextActionGroup.Operational, _ => Run(count, true), Stock, Posting),
            A("inventory.audit", "Geçmiş / Audit", "inventory.audit.view", "", 10, ContextActionGroup.Audit, _ => Run(open))
        ];
    }

    // Shared by the ~12 "Tanımlar" screens behind OpenMasterCrud (Firma, Şube, Depo, Marka, Kategori,
    // Birim, Cari Grubu, Bölge, Sevk Bölgesi, Fiyat Listesi, Döviz, Kasa Grubu, Ürün Özelliği, Varyant
    // Tanımı) - every LocalMasterDataService.List(kind) result has the same Kod/Ad/Aktif columns
    // regardless of kind, so one action set covers all of them. LocalMasterDataService.SetActive
    // already existed but nothing in the UI called it before this - these screens could create and
    // edit records but never deactivate one without hand-editing the database.
    public static IReadOnlyList<ContextActionDefinition> MasterData(Action open, Action edit, Func<bool, Task> setActive)
    {
        bool Active(object? x) => x is DataRowView row && (!row.Row.Table.Columns.Contains("Aktif") || Convert.ToBoolean(row["Aktif"]));
        return
        [
            A("master.open", "Kartı Aç", "", "", 10, ContextActionGroup.Primary, _ => Run(open)),
            A("master.edit", "Düzenle", "", "", 20, ContextActionGroup.Primary, _ => Run(edit)),
            A("master.activate", "Aktif Yap", "", "", 10, ContextActionGroup.Critical, async _ => { await setActive(true); return ContextActionResult.Ok(refresh: true); }, x => !Active(x)),
            A("master.deactivate", "Pasife Al", "", "", 20, ContextActionGroup.Critical, async _ => { await setActive(false); return ContextActionResult.Ok(refresh: true); }, Active, Active, true)
        ];
    }

    // Satış faturaları listesi. DurumKod, ekranda gösterilen Türkçe durumdan bağımsız
    // iş kuralı değerlendirmesi için kullanılır.
    public static IReadOnlyList<ContextActionDefinition> SalesInvoices(Action open, Action openAccount, Action accountTransactions, Action? post = null, Action? accountStatement = null, Action? createDespatch = null)
    {
        bool HasAccount(object? x) => x is DataRowView row && row.Row.Table.Columns.Contains("CariId") && !string.IsNullOrWhiteSpace(row["CariId"]?.ToString());
        string Status(object? x) => x is DataRowView row && row.Row.Table.Columns.Contains("DurumKod") ? row["DurumKod"]?.ToString() ?? "" : x is DataRowView fallback ? fallback["Durum"]?.ToString() ?? "" : "";
        bool Draft(object? x) => string.Equals(Status(x), "Draft", StringComparison.OrdinalIgnoreCase);
        bool Posted(object? x) => string.Equals(Status(x), "Posted", StringComparison.OrdinalIgnoreCase);
        return
        [
            A("sales.open", "Faturayı Aç", "", "", 10, ContextActionGroup.Primary, _ => Run(open)),
            A("sales.post", "Faturayı Kes", "sales.invoice.post", "", 20, ContextActionGroup.Primary, _ => Run(post ?? (() => { }), true), Draft, Draft),
            A("sales.despatch.create", "İrsaliye Oluştur", "despatches.create", "", 30, ContextActionGroup.Document, _ => Run(createDespatch ?? (() => { }), true), Posted, Posted, true, "DespatchCreatedFromInvoice", "DespatchDocument"),
            A("sales.account.open", "Cariyi Aç", "", "", 10, ContextActionGroup.Related, _ => Run(openAccount), HasAccount),
            A("sales.account.transactions", "Cari Hareketlerini Aç", "", "", 20, ContextActionGroup.Related, _ => Run(accountTransactions), HasAccount),
            A("sales.account.statement", "Cari Ekstresini Aç", "accounts.statement.view", "", 30, ContextActionGroup.Related, _ => Run(accountStatement ?? (() => { })), x => HasAccount(x) && accountStatement != null)
        ];
    }

    // Cari Hareketler / Cari Ekstre / Kasa Hareketleri / Kasa Ekstresi / Risk & Kredi - read-only
    // report rows that point at an account, a source document, or the cash/bank account on the other
    // side of the posting. Each action only appears when its row actually has that link.
    // canOpenSource says whether a desktop screen exists for the row's document type.
    public static IReadOnlyList<ContextActionDefinition> LedgerLinks(
        Action<string> openAccount, Action<string> accountStatement, Action<string>? accountTransactions,
        Func<string, bool> canOpenSource, Action<string, string> openSource,
        Action<string>? cashTransactions = null, Action<string>? cashStatement = null, Action<string>? bankTransactions = null)
    {
        static bool Has(object? x, Func<ILedgerLinkRow, string> id) => x is ILedgerLinkRow row && !string.IsNullOrWhiteSpace(id(row));
        static ILedgerLinkRow Row(object? x) => (ILedgerLinkRow)x!;
        var actions = new List<ContextActionDefinition>
        {
            A("ledger.account.open", "Cari Kartını Aç", "accounts.view", "", 10, ContextActionGroup.Primary, x => Run(() => openAccount(Row(x).AccountId)), x => Has(x, r => r.AccountId)),
            A("ledger.source.open", "Kaynak Belgeyi Aç", "", "", 20, ContextActionGroup.Primary, x => Run(() => openSource(Row(x).SourceType, Row(x).SourceId)),
                x => x is ILedgerLinkRow row && !string.IsNullOrWhiteSpace(row.SourceType) && canOpenSource(row.SourceType)),
            A("ledger.account.statement", "Cari Ekstresi", "accounts.statement.view", "", 10, ContextActionGroup.Related, x => Run(() => accountStatement(Row(x).AccountId)), x => Has(x, r => r.AccountId))
        };
        if (accountTransactions != null)
            actions.Add(A("ledger.account.transactions", "Cari Hareketleri", "accounts.transaction.view", "", 20, ContextActionGroup.Related, x => Run(() => accountTransactions(Row(x).AccountId)), x => Has(x, r => r.AccountId)));
        if (cashTransactions != null)
            actions.Add(A("ledger.cash.transactions", "Kasa Hareketleri", "cash.transaction.view", "", 30, ContextActionGroup.Related, x => Run(() => cashTransactions(Row(x).CashAccountId)), x => Has(x, r => r.CashAccountId)));
        if (cashStatement != null)
            actions.Add(A("ledger.cash.statement", "Kasa Ekstresi", "cash.statement.view", "", 40, ContextActionGroup.Related, x => Run(() => cashStatement(Row(x).CashAccountId)), x => Has(x, r => r.CashAccountId)));
        if (bankTransactions != null)
            actions.Add(A("ledger.bank.transactions", "Banka Hareketleri", "", "", 50, ContextActionGroup.Related, x => Run(() => bankTransactions(Row(x).BankAccountId)), x => Has(x, r => r.BankAccountId)));
        return actions;
    }

    // Depo Yönetimi (LocalWarehouseService.Search: Id/DepoKodu/DepoAdi/Aktif).
    public static IReadOnlyList<ContextActionDefinition> Warehouses(Action locations, Action balances, Action movements, Action edit) =>
    [
        A("warehouse.locations", "Lokasyonları Yönet", "", "", 10, ContextActionGroup.Primary, _ => Run(locations)),
        A("warehouse.edit", "Depo Tanımları", "", "", 20, ContextActionGroup.Primary, _ => Run(edit)),
        A("warehouse.balances", "Depo Stok Durumu", "inventory.product.view", "", 10, ContextActionGroup.Related, _ => Run(balances)),
        A("warehouse.movements", "Depo Stok Hareketleri", "inventory.transaction.view", "", 20, ContextActionGroup.Related, _ => Run(movements))
    ];

    // Kullanıcı ve Yetkiler > Kullanıcılar (LocalUserAdminService.SearchUsers columns).
    public static IReadOnlyList<ContextActionDefinition> Users(Action edit, Action resetPassword, Func<bool, Task> setActive)
    {
        bool Active(object? x) => x is DataRowView row && Convert.ToBoolean(row["Aktif"]);
        return
        [
            A("users.edit", "Kullanıcıyı Düzenle", "", "", 10, ContextActionGroup.Primary, _ => Run(edit)),
            A("users.password", "Şifre Sıfırla", "", "", 10, ContextActionGroup.Operational, _ => Run(resetPassword)),
            A("users.activate", "Aktif Yap", "", "", 10, ContextActionGroup.Critical, async _ => { await setActive(true); return ContextActionResult.Ok(refresh: true); }, x => !Active(x)),
            A("users.deactivate", "Pasife Al", "", "", 20, ContextActionGroup.Critical, async _ => { await setActive(false); return ContextActionResult.Ok(refresh: true); }, Active, Active, true)
        ];
    }

    // Read-only inventory reports (Stok Durumu, Stok Hareketleri) show a product by name only - this
    // gives their rows a way back to the actual product card without duplicating Products() above,
    // which needs a Kod/Ad/Aktif/UrunTipi-shaped row these reports don't have.
    public static IReadOnlyList<ContextActionDefinition> ProductLink(Action openProduct)
    {
        bool HasProduct(object? x) => x is DataRowView row && row.Row.Table.Columns.Contains("UrunId") && !string.IsNullOrWhiteSpace(row["UrunId"]?.ToString());
        return [A("product.open", "Ürün Kartını Aç", "", "", 10, ContextActionGroup.Primary, _ => Run(openProduct), HasProduct)];
    }

    private static ContextActionDefinition A(string id, string header, string permission, string icon, int order, ContextActionGroup group, Func<object?, Task<ContextActionResult>> execute, Func<object?, bool>? visible = null, Func<object?, bool>? enabled = null, bool critical = false, string? audit = null, string? entity = null) =>
        new(id, header, permission, icon, order, group, execute, visible, enabled, ContextSelectionMode.Single, true, critical,
            critical ? selected => $"{header} işlemi uygulanacak.\n\n{Display(selected)}\n\nBu işlem ilgili kayıt ve hareketleri etkileyebilir." : null,
            audit, entity, selected => selected?.GetType().GetProperty("Id")?.GetValue(selected)?.ToString() ?? (selected as DataRowView)?["Id"]?.ToString());

    private static string Display(object? selected) => selected switch
    {
        AccountRowViewModel x => $"{x.Code} — {x.Name}",
        CashAccountRowViewModel x => $"{x.Code} — {x.Name}",
        DataRowView x when x.Row.Table.Columns.Contains("DepoKodu") => $"{x["DepoKodu"]} — {x["DepoAdi"]}",
        DataRowView x when x.Row.Table.Columns.Contains("KullaniciAdi") => $"{x["KullaniciAdi"]} — {x["AdSoyad"]}",
        DataRowView x when x.Row.Table.Columns.Contains("Kod") => $"{x["Kod"]} — {(x.Row.Table.Columns.Contains("Ad") ? x["Ad"] : "")}",
        _ => "Seçili kayıt"
    };
}
