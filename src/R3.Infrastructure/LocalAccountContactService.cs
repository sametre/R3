using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record AccountContactEdit(
    string Id, string AccountId, string FirstName, string LastName, string Title, string Department,
    string Phone, string MobilePhone, string Email, bool IsPrimary, bool IsActive = true, string Notes = "");

/// <summary>
/// Account 1 -> N AccountContact. Only one primary contact per account -
/// setting a new primary clears the flag from every other contact of that
/// account in the same transaction.
/// </summary>
public sealed class LocalAccountContactService(StoreDatabase database)
{
    public DataTable List(string accountId) => database.Query("""
        SELECT id AS Id, (first_name || CASE WHEN last_name='' THEN '' ELSE ' '||last_name END) AS AdSoyad,
               title AS Gorev, department AS Departman, phone AS Telefon, mobile_phone AS Cep, email AS Eposta,
               is_primary AS AnaYetkili, is_active AS Aktif
        FROM account_contacts WHERE account_id=$account ORDER BY is_primary DESC, first_name
        """, ("$account", accountId));

    public AccountContactEdit? Get(string id)
    {
        var t = database.Query("""
            SELECT id,account_id,first_name,last_name,title,department,phone,mobile_phone,email,is_primary,is_active,notes
            FROM account_contacts WHERE id=$id
            """, ("$id", id));
        if (t.Rows.Count == 0) return null;
        var r = t.Rows[0];
        return new AccountContactEdit(
            r["id"].ToString()!, r["account_id"].ToString()!, r["first_name"].ToString()!, r["last_name"].ToString()!,
            r["title"].ToString()!, r["department"].ToString()!, r["phone"].ToString()!, r["mobile_phone"].ToString()!,
            r["email"].ToString()!, Convert.ToBoolean(r["is_primary"]), Convert.ToBoolean(r["is_active"]), r["notes"].ToString()!);
    }

    public void Save(AccountContactEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.AccountId)) throw new ArgumentException("Cari seçilmelidir.");
        if (string.IsNullOrWhiteSpace(edit.FirstName)) throw new ArgumentException("Yetkili adı zorunludur.");
        var id = string.IsNullOrWhiteSpace(edit.Id) ? Guid.NewGuid().ToString() : edit.Id;
        var now = DateTime.UtcNow.ToString("O");
        using var c = Open(); using var tx = c.BeginTransaction();

        if (edit.IsPrimary) ClearPrimary(c, tx, edit.AccountId, exceptId: id);

        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO account_contacts(id,account_id,first_name,last_name,title,department,phone,mobile_phone,email,is_primary,is_active,notes,created_at,updated_at)
            VALUES($id,$account,$first,$last,$title,$department,$phone,$mobile,$email,$primary,$active,$notes,$now,$now)
            ON CONFLICT(id) DO UPDATE SET first_name=$first,last_name=$last,title=$title,department=$department,
                phone=$phone,mobile_phone=$mobile,email=$email,is_primary=$primary,is_active=$active,notes=$notes,updated_at=$now
            """;
        Add(cmd, "$id", id); Add(cmd, "$account", edit.AccountId); Add(cmd, "$first", edit.FirstName.Trim()); Add(cmd, "$last", edit.LastName.Trim());
        Add(cmd, "$title", edit.Title); Add(cmd, "$department", edit.Department); Add(cmd, "$phone", edit.Phone);
        Add(cmd, "$mobile", edit.MobilePhone); Add(cmd, "$email", edit.Email); Add(cmd, "$primary", edit.IsPrimary ? 1 : 0);
        Add(cmd, "$active", edit.IsActive ? 1 : 0); Add(cmd, "$notes", edit.Notes); Add(cmd, "$now", now);
        cmd.ExecuteNonQuery();

        Audit(c, tx, edit.AccountId, string.IsNullOrWhiteSpace(edit.Id) ? "ContactAdded" : "ContactUpdated", id, now);
        tx.Commit();
    }

    public void SetActive(string id, bool active)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        using var read = c.CreateCommand(); read.Transaction = tx;
        read.CommandText = "SELECT account_id FROM account_contacts WHERE id=$id"; Add(read, "$id", id);
        string accountId;
        using (var r = read.ExecuteReader())
        {
            if (!r.Read()) throw new KeyNotFoundException("Yetkili bulunamadı.");
            accountId = r.GetString(0);
        }
        var now = DateTime.UtcNow.ToString("O");
        using var upd = c.CreateCommand(); upd.Transaction = tx;
        upd.CommandText = "UPDATE account_contacts SET is_active=$active, is_primary=CASE WHEN $active=0 THEN 0 ELSE is_primary END, updated_at=$now WHERE id=$id";
        Add(upd, "$active", active ? 1 : 0); Add(upd, "$now", now); Add(upd, "$id", id); upd.ExecuteNonQuery();
        Audit(c, tx, accountId, active ? "ContactActivated" : "ContactDeactivated", id, now);
        tx.Commit();
    }

    public void SetPrimary(string id)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        using var read = c.CreateCommand(); read.Transaction = tx;
        read.CommandText = "SELECT account_id FROM account_contacts WHERE id=$id AND is_active=1"; Add(read, "$id", id);
        string accountId;
        using (var r = read.ExecuteReader())
        {
            if (!r.Read()) throw new KeyNotFoundException("Yetkili bulunamadı veya pasif durumda.");
            accountId = r.GetString(0);
        }
        ClearPrimary(c, tx, accountId, exceptId: null);
        var now = DateTime.UtcNow.ToString("O");
        using var set = c.CreateCommand(); set.Transaction = tx;
        set.CommandText = "UPDATE account_contacts SET is_primary=1, updated_at=$now WHERE id=$id";
        Add(set, "$now", now); Add(set, "$id", id); set.ExecuteNonQuery();
        Audit(c, tx, accountId, "ContactUpdated", id, now);
        tx.Commit();
    }

    private static void ClearPrimary(SqliteConnection c, SqliteTransaction tx, string accountId, string? exceptId)
    {
        using var clear = c.CreateCommand(); clear.Transaction = tx;
        clear.CommandText = exceptId is null
            ? "UPDATE account_contacts SET is_primary=0 WHERE account_id=$account"
            : "UPDATE account_contacts SET is_primary=0 WHERE account_id=$account AND id<>$except";
        Add(clear, "$account", accountId);
        if (exceptId is not null) Add(clear, "$except", exceptId);
        clear.ExecuteNonQuery();
    }

    private static void Audit(SqliteConnection c, SqliteTransaction tx, string accountId, string action, string contactId, string now)
    {
        using var audit = c.CreateCommand(); audit.Transaction = tx;
        audit.CommandText = "INSERT INTO audit_logs(id,entity_type,entity_id,action,new_values,created_at) VALUES($id,'AccountContact',$entity,$action,$new,$now)";
        Add(audit, "$id", Guid.NewGuid().ToString()); Add(audit, "$entity", contactId); Add(audit, "$action", action);
        Add(audit, "$new", accountId); Add(audit, "$now", now);
        audit.ExecuteNonQuery();
    }

    private SqliteConnection Open() => database.OpenConnection();
    private static void Add(SqliteCommand c, string n, object v) => c.Parameters.AddWithValue(n, v);
}
