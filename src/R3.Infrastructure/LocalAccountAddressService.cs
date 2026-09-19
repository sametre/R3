using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record AccountAddressEdit(
    string Id, string AccountId, string AddressType, string Title,
    string Country, string City, string District, string Neighborhood, string AddressLine, string PostalCode,
    string ContactName, string Phone, string MobilePhone, string DeliveryRegionCode,
    bool IsDefault, bool IsActive = true);

/// <summary>
/// Account 1 -> N AccountAddress. Only one default address per (account, address type);
/// addresses are never physically deleted, only deactivated - see SetActive.
/// </summary>
public sealed class LocalAccountAddressService(StoreDatabase database)
{
    private static readonly string[] ValidTypes = ["HeadOffice", "Invoice", "Billing", "Shipping", "Branch", "Other"];

    public DataTable List(string accountId) => database.Query("""
        SELECT id AS Id, address_type AS Tip, title AS AdresAdi, city AS Il, district AS Ilce,
               address_line AS Adres, contact_name AS Yetkili, phone AS Telefon,
               is_default AS Varsayilan, is_active AS Aktif
        FROM account_addresses WHERE account_id=$account ORDER BY address_type, is_default DESC, title
        """, ("$account", accountId));

    public AccountAddressEdit? Get(string id)
    {
        var t = database.Query("""
            SELECT id,account_id,address_type,title,country,city,district,neighborhood,address_line,postal_code,
                   contact_name,phone,mobile_phone,delivery_region_code,is_default,is_active
            FROM account_addresses WHERE id=$id
            """, ("$id", id));
        if (t.Rows.Count == 0) return null;
        var r = t.Rows[0];
        return new AccountAddressEdit(
            r["id"].ToString()!, r["account_id"].ToString()!, r["address_type"].ToString()!, r["title"].ToString()!,
            r["country"].ToString()!, r["city"].ToString()!, r["district"].ToString()!, r["neighborhood"].ToString()!,
            r["address_line"].ToString()!, r["postal_code"].ToString()!,
            r["contact_name"].ToString()!, r["phone"].ToString()!, r["mobile_phone"].ToString()!, r["delivery_region_code"].ToString()!,
            Convert.ToBoolean(r["is_default"]), Convert.ToBoolean(r["is_active"]));
    }

    public void Save(AccountAddressEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.AccountId)) throw new ArgumentException("Cari seçilmelidir.");
        if (string.IsNullOrWhiteSpace(edit.Title)) throw new ArgumentException("Adres başlığı zorunludur.");
        if (!ValidTypes.Contains(edit.AddressType)) throw new ArgumentException("Geçersiz adres tipi.");
        var id = string.IsNullOrWhiteSpace(edit.Id) ? Guid.NewGuid().ToString() : edit.Id;
        var now = DateTime.UtcNow.ToString("O");
        using var c = Open(); using var tx = c.BeginTransaction();

        if (edit.IsDefault) ClearDefault(c, tx, edit.AccountId, edit.AddressType, exceptId: id);

        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO account_addresses(id,account_id,address_type,title,country,city,district,neighborhood,address_line,postal_code,contact_name,phone,mobile_phone,delivery_region_code,is_default,is_active,created_at,updated_at)
            VALUES($id,$account,$type,$title,$country,$city,$district,$neighborhood,$line,$postal,$contact,$phone,$mobile,$region,$default,$active,$now,$now)
            ON CONFLICT(id) DO UPDATE SET address_type=$type,title=$title,country=$country,city=$city,district=$district,neighborhood=$neighborhood,
                address_line=$line,postal_code=$postal,contact_name=$contact,phone=$phone,mobile_phone=$mobile,delivery_region_code=$region,
                is_default=$default,is_active=$active,updated_at=$now
            """;
        Add(cmd, "$id", id); Add(cmd, "$account", edit.AccountId); Add(cmd, "$type", edit.AddressType); Add(cmd, "$title", edit.Title.Trim());
        Add(cmd, "$country", edit.Country); Add(cmd, "$city", edit.City); Add(cmd, "$district", edit.District); Add(cmd, "$neighborhood", edit.Neighborhood);
        Add(cmd, "$line", edit.AddressLine); Add(cmd, "$postal", edit.PostalCode); Add(cmd, "$contact", edit.ContactName);
        Add(cmd, "$phone", edit.Phone); Add(cmd, "$mobile", edit.MobilePhone); Add(cmd, "$region", edit.DeliveryRegionCode);
        Add(cmd, "$default", edit.IsDefault ? 1 : 0); Add(cmd, "$active", edit.IsActive ? 1 : 0); Add(cmd, "$now", now);
        cmd.ExecuteNonQuery();

        Audit(c, tx, edit.AccountId, string.IsNullOrWhiteSpace(edit.Id) ? "AddressAdded" : "AddressUpdated", id, now);
        tx.Commit();
    }

    /// <summary>Deactivating the default address promotes the oldest remaining active
    /// address of the same type to default, if one exists.</summary>
    public void SetActive(string id, bool active)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        using var read = c.CreateCommand(); read.Transaction = tx;
        read.CommandText = "SELECT account_id,address_type,is_default FROM account_addresses WHERE id=$id";
        Add(read, "$id", id);
        string accountId; string type; bool wasDefault;
        using (var r = read.ExecuteReader())
        {
            if (!r.Read()) throw new KeyNotFoundException("Adres bulunamadı.");
            accountId = r.GetString(0); type = r.GetString(1); wasDefault = Convert.ToBoolean(r.GetValue(2));
        }
        var now = DateTime.UtcNow.ToString("O");
        using var upd = c.CreateCommand(); upd.Transaction = tx;
        upd.CommandText = "UPDATE account_addresses SET is_active=$active, is_default=CASE WHEN $active=0 THEN 0 ELSE is_default END, updated_at=$now WHERE id=$id";
        Add(upd, "$active", active ? 1 : 0); Add(upd, "$now", now); Add(upd, "$id", id);
        upd.ExecuteNonQuery();

        if (!active && wasDefault)
        {
            using var promote = c.CreateCommand(); promote.Transaction = tx;
            promote.CommandText = """
                UPDATE account_addresses SET is_default=1, updated_at=$now
                WHERE id=(SELECT id FROM account_addresses WHERE account_id=$account AND address_type=$type AND is_active=1 ORDER BY created_at LIMIT 1)
                """;
            Add(promote, "$now", now); Add(promote, "$account", accountId); Add(promote, "$type", type);
            promote.ExecuteNonQuery();
        }

        Audit(c, tx, accountId, active ? "AddressActivated" : "AddressDeactivated", id, now);
        tx.Commit();
    }

    public void SetDefault(string id)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        using var read = c.CreateCommand(); read.Transaction = tx;
        read.CommandText = "SELECT account_id,address_type FROM account_addresses WHERE id=$id AND is_active=1";
        Add(read, "$id", id);
        string accountId; string type;
        using (var r = read.ExecuteReader())
        {
            if (!r.Read()) throw new KeyNotFoundException("Adres bulunamadı veya pasif durumda.");
            accountId = r.GetString(0); type = r.GetString(1);
        }
        ClearDefault(c, tx, accountId, type, exceptId: null);
        var now = DateTime.UtcNow.ToString("O");
        using var set = c.CreateCommand(); set.Transaction = tx;
        set.CommandText = "UPDATE account_addresses SET is_default=1, updated_at=$now WHERE id=$id";
        Add(set, "$now", now); Add(set, "$id", id); set.ExecuteNonQuery();
        Audit(c, tx, accountId, "AddressUpdated", id, now);
        tx.Commit();
    }

    private static void ClearDefault(SqliteConnection c, SqliteTransaction tx, string accountId, string type, string? exceptId)
    {
        using var clear = c.CreateCommand(); clear.Transaction = tx;
        clear.CommandText = exceptId is null
            ? "UPDATE account_addresses SET is_default=0 WHERE account_id=$account AND address_type=$type"
            : "UPDATE account_addresses SET is_default=0 WHERE account_id=$account AND address_type=$type AND id<>$except";
        Add(clear, "$account", accountId); Add(clear, "$type", type);
        if (exceptId is not null) Add(clear, "$except", exceptId);
        clear.ExecuteNonQuery();
    }

    private static void Audit(SqliteConnection c, SqliteTransaction tx, string accountId, string action, string addressId, string now)
    {
        using var audit = c.CreateCommand(); audit.Transaction = tx;
        audit.CommandText = "INSERT INTO audit_logs(id,entity_type,entity_id,action,new_values,created_at) VALUES($id,'AccountAddress',$entity,$action,$new,$now)";
        Add(audit, "$id", Guid.NewGuid().ToString()); Add(audit, "$entity", addressId); Add(audit, "$action", action);
        Add(audit, "$new", accountId); Add(audit, "$now", now);
        audit.ExecuteNonQuery();
    }

    private SqliteConnection Open() => database.OpenConnection();
    private static void Add(SqliteCommand c, string n, object v) => c.Parameters.AddWithValue(n, v);
}
