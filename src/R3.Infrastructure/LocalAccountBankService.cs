using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record AccountBankEdit(
    string Id, string AccountId, string BankName, string BranchName, string BranchCode,
    string AccountName, string Iban, string AccountNumber, string CurrencyCode,
    bool IsDefault, bool IsActive = true, string Notes = "");

/// <summary>
/// MDF CARIBANKA karşılığıdır. Cari başına birden fazla banka hesabı tutulur;
/// aynı cari içinde aynı anda yalnızca bir aktif varsayılan hesap olabilir.
/// Fiziksel silme yoktur, hesap pasife alınır.
/// </summary>
public sealed class LocalAccountBankService(StoreDatabase database)
{
    public DataTable List(string accountId) => database.Query("""
        SELECT id AS Id, bank_name AS Banka, branch_name AS Sube, branch_code AS SubeKodu,
               account_name AS HesapAdi, iban AS IBAN, currency_code AS Doviz,
               is_default AS Varsayilan, is_active AS Aktif
        FROM account_banks WHERE account_id=$account
        ORDER BY is_default DESC, bank_name, branch_name
        """, ("$account", accountId));

    public AccountBankEdit? Get(string id)
    {
        var t = database.Query("""
            SELECT id,account_id,bank_name,branch_name,branch_code,account_name,iban,account_number,
                   currency_code,is_default,is_active,notes
            FROM account_banks WHERE id=$id
            """, ("$id", id));
        if (t.Rows.Count == 0) return null;
        var r = t.Rows[0];
        return new AccountBankEdit(r["id"].ToString()!, r["account_id"].ToString()!, r["bank_name"].ToString()!,
            r["branch_name"].ToString()!, r["branch_code"].ToString()!, r["account_name"].ToString()!,
            r["iban"].ToString()!, r["account_number"].ToString()!, r["currency_code"].ToString()!,
            Convert.ToBoolean(r["is_default"]), Convert.ToBoolean(r["is_active"]), r["notes"].ToString()!);
    }

    public void Save(AccountBankEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.AccountId)) throw new ArgumentException("Cari seçilmelidir.");
        if (string.IsNullOrWhiteSpace(edit.BankName)) throw new ArgumentException("Banka adı zorunludur.");
        if (string.IsNullOrWhiteSpace(edit.CurrencyCode)) throw new ArgumentException("Para birimi zorunludur.");
        var iban = NormalizeIban(edit.Iban);
        if (iban.Length > 0 && !IsValidIban(iban)) throw new ArgumentException("IBAN formatı geçersizdir.");

        var id = string.IsNullOrWhiteSpace(edit.Id) ? Guid.NewGuid().ToString() : edit.Id;
        var now = DateTime.UtcNow.ToString("O");
        using var c = database.OpenConnection(); using var tx = c.BeginTransaction();
        if (edit.IsDefault)
        {
            using var clear = c.CreateCommand(); clear.Transaction = tx;
            clear.CommandText = "UPDATE account_banks SET is_default=0,updated_at=$now WHERE account_id=$account AND id<>$id";
            Add(clear, "$now", now); Add(clear, "$account", edit.AccountId); Add(clear, "$id", id); clear.ExecuteNonQuery();
        }

        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO account_banks(id,account_id,bank_name,branch_name,branch_code,account_name,iban,account_number,currency_code,is_default,is_active,notes,created_at,updated_at)
            VALUES($id,$account,$bank,$branch,$branchcode,$name,$iban,$number,$currency,$default,$active,$notes,$now,$now)
            ON CONFLICT(id) DO UPDATE SET bank_name=$bank,branch_name=$branch,branch_code=$branchcode,account_name=$name,
                iban=$iban,account_number=$number,currency_code=$currency,is_default=$default,is_active=$active,notes=$notes,updated_at=$now
            """;
        Add(cmd, "$id", id); Add(cmd, "$account", edit.AccountId); Add(cmd, "$bank", edit.BankName.Trim());
        Add(cmd, "$branch", edit.BranchName.Trim()); Add(cmd, "$branchcode", edit.BranchCode.Trim()); Add(cmd, "$name", edit.AccountName.Trim());
        Add(cmd, "$iban", iban); Add(cmd, "$number", edit.AccountNumber.Trim()); Add(cmd, "$currency", edit.CurrencyCode.Trim().ToUpperInvariant());
        Add(cmd, "$default", edit.IsDefault ? 1 : 0); Add(cmd, "$active", edit.IsActive ? 1 : 0); Add(cmd, "$notes", edit.Notes.Trim()); Add(cmd, "$now", now);
        cmd.ExecuteNonQuery();
        using var audit = c.CreateCommand(); audit.Transaction = tx;
        audit.CommandText = "INSERT INTO audit_logs(id,entity_type,entity_id,action,new_values,created_at) VALUES($id,'AccountBank',$entity,$action,$new,$now)";
        Add(audit, "$id", Guid.NewGuid().ToString()); Add(audit, "$entity", id); Add(audit, "$action", string.IsNullOrWhiteSpace(edit.Id) ? "BankAdded" : "BankUpdated"); Add(audit, "$new", edit.BankName); Add(audit, "$now", now); audit.ExecuteNonQuery();
        tx.Commit();
    }

    public void SetActive(string id, bool active)
    {
        using var c = database.OpenConnection(); using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE account_banks SET is_active=$active,is_default=CASE WHEN $active=0 THEN 0 ELSE is_default END,updated_at=$now WHERE id=$id";
        Add(cmd, "$active", active ? 1 : 0); Add(cmd, "$now", DateTime.UtcNow.ToString("O")); Add(cmd, "$id", id); cmd.ExecuteNonQuery();
    }

    private static string NormalizeIban(string value) => new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    private static bool IsValidIban(string iban) => iban.StartsWith("TR", StringComparison.Ordinal) && iban.Length == 26 && iban.Skip(2).All(char.IsDigit);
    private static void Add(SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);
}
