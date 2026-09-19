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
        bool Stock(object? x) => x is DataRowView row && !string.Equals(row["ProductType"].ToString(), "Service", StringComparison.OrdinalIgnoreCase);
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

    private static ContextActionDefinition A(string id, string header, string permission, string icon, int order, ContextActionGroup group, Func<object?, Task<ContextActionResult>> execute, Func<object?, bool>? visible = null, Func<object?, bool>? enabled = null, bool critical = false, string? audit = null, string? entity = null) =>
        new(id, header, permission, icon, order, group, execute, visible, enabled, ContextSelectionMode.Single, true, critical,
            critical ? selected => $"{header} işlemi uygulanacak.\n\n{Display(selected)}\n\nBu işlem ilgili kayıt ve hareketleri etkileyebilir." : null,
            audit, entity, selected => selected?.GetType().GetProperty("Id")?.GetValue(selected)?.ToString() ?? (selected as DataRowView)?["Id"]?.ToString());

    private static string Display(object? selected) => selected switch
    {
        AccountRowViewModel x => $"{x.Code} — {x.Name}",
        CashAccountRowViewModel x => $"{x.Code} — {x.Name}",
        DataRowView x when x.Row.Table.Columns.Contains("Kod") => $"{x["Kod"]} — {(x.Row.Table.Columns.Contains("Ad") ? x["Ad"] : "")}",
        _ => "Seçili kayıt"
    };
}
