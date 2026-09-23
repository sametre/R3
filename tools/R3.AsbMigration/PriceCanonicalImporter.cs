using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using R3.Infrastructure;

/// <summary>
/// ASB → R3 fiyat aktarımı (--canonical-prices). Sample-verified against ASBDB_ERKUR02 on 2026-09-23:
///   KODFIYAT (17 rows) → price_lists: FYKKOD/FYKADI, FYKGC 1=Sales 0=Purchase (every "ALIŞ ..." list is 0),
///            FYKKDVDH D=KDV dahil / H=hariç, FYKDVZ (TL → TRY), FYKBASTAR/FYKBITTAR validity, FYKSIRANO, FYKAKTIF
///   FIYAT    (37,778)   → product_prices: FYTREF = KODFIYAT.FYKREF (the list, not a row id), FYTSTKREF = product, FYTFIYAT, FYTZMN, FYTUSR
///   LOGFIYAT (262,845)  → product_price_history (only the new price is logged in ASB, so old_price stays NULL)
/// Idempotent: lists upsert by legacy id, prices upsert by (list, product), imported history rows are
/// replaced on every run. Run after --canonical-core (products must exist; unmatched STKREFs are counted).
/// </summary>
public sealed class PriceCanonicalImporter(string sourceName, StoreDatabase target, string companyId, SqlConnection source)
{
    public async Task<(int Lists, int Prices, int History, int Unmatched, int HistoryForDeletedProducts)> ImportAsync()
    {
        using var c = target.OpenConnection();
        using var tx = c.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");

        var lists = new Dictionary<long, string>();
        await using (var cmd = new SqlCommand("SELECT FYKREF, FYKKOD, FYKADI, ISNULL(FYKGC,'1'), ISNULL(FYKKDVDH,'D'), ISNULL(FYKDVZ,'TL'), FYKBASTAR, FYKBITTAR, ISNULL(FYKSIRANO,0), ISNULL(FYKAKTIF,1) FROM dbo.KODFIYAT", source))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                var legacy = Convert.ToInt64(r.GetValue(0)); var code = r.GetString(1).Trim().ToUpperInvariant();
                var id = Find(c, tx, "SELECT id FROM price_lists WHERE legacy_source=$s AND legacy_id=$l", ("$s", sourceName), ("$l", legacy))
                         ?? Find(c, tx, "SELECT id FROM price_lists WHERE company_id=$c AND code=$code", ("$c", companyId), ("$code", code))
                         ?? StableId("price-list", legacy);
                var currency = r.GetString(5).Trim().ToUpperInvariant() is "TL" or "" ? "TRY" : r.GetString(5).Trim().ToUpperInvariant();
                Exec(c, tx, """
                    INSERT INTO price_lists(id,company_id,code,name,is_active,price_type,vat_included,currency_code,valid_from,valid_to,sequence,legacy_source,legacy_id,updated_at)
                    VALUES($id,$c,$code,$name,$active,$type,$vat,$cur,$from,$to,$seq,$s,$l,$now)
                    ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,is_active=$active,price_type=$type,vat_included=$vat,currency_code=$cur,valid_from=$from,valid_to=$to,sequence=$seq,legacy_source=$s,legacy_id=$l,updated_at=$now
                    """, ("$id", id), ("$c", companyId), ("$code", code), ("$name", r.GetString(2).Trim()), ("$active", Convert.ToBoolean(r.GetValue(9)) ? 1 : 0),
                    ("$type", r.GetValue(3).ToString()!.Trim() == "0" ? "Purchase" : "Sales"), ("$vat", r.GetString(4).Trim() == "H" ? 0 : 1), ("$cur", currency),
                    ("$from", r.IsDBNull(6) ? DBNull.Value : r.GetDateTime(6).ToString("yyyy-MM-dd")), ("$to", r.IsDBNull(7) ? DBNull.Value : r.GetDateTime(7).ToString("yyyy-MM-dd")),
                    ("$seq", Convert.ToInt32(r.GetValue(8))), ("$s", sourceName), ("$l", legacy), ("$now", now));
                lists[legacy] = id;
            }

        var products = new Dictionary<long, string>();
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx; q.CommandText = "SELECT legacy_id, id FROM products WHERE company_id=$c AND legacy_source=$s AND legacy_id IS NOT NULL";
            q.Parameters.AddWithValue("$c", companyId); q.Parameters.AddWithValue("$s", sourceName);
            using var r = q.ExecuteReader(); while (r.Read()) products[r.GetInt64(0)] = r.GetString(1);
        }

        int prices = 0, unmatched = 0;
        await using (var cmd = new SqlCommand("SELECT FYTREF, FYTSTKREF, FYTFIYAT, FYTZMN, ISNULL(FYTUSR,'') FROM dbo.FIYAT", source))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                if (!lists.TryGetValue(Convert.ToInt64(r.GetValue(0)), out var list) || !products.TryGetValue(Convert.ToInt64(r.GetValue(1)), out var product)) { unmatched++; continue; }
                var price = r.IsDBNull(2) ? 0m : Math.Max(0m, Convert.ToDecimal(r.GetValue(2)));
                Exec(c, tx, """
                    INSERT INTO product_prices(id,price_list_id,product_id,price,updated_at,updated_by) VALUES($id,$l,$p,$price,$at,$user)
                    ON CONFLICT(price_list_id,product_id) DO UPDATE SET price=$price,updated_at=$at,updated_by=$user
                    """, ("$id", StableId("price", Convert.ToInt64(r.GetValue(0)) * 100_000_000 + Convert.ToInt64(r.GetValue(1)))), ("$l", list), ("$p", product), ("$price", price),
                    ("$at", r.IsDBNull(3) ? now : r.GetDateTime(3).ToUniversalTime().ToString("O")), ("$user", r.GetString(4).Trim()));
                prices++;
            }

        foreach (var list in lists.Values) Exec(c, tx, "DELETE FROM product_price_history WHERE price_list_id=$l AND source='Import'", ("$l", list));
        int history = 0, historySkipped = 0;
        await using (var cmd = new SqlCommand("SELECT LOGFYT_FYTREF, LOGFYT_STKREF, LOGFYT_FIYAT, LOGFYT_ZMN, ISNULL(LOGFYT_USR,'') FROM dbo.LOGFIYAT", source))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                // ~95% of LOGFIYAT (250,780 of 262,845 in the sample) belongs to products no longer in STOKKARTI - history for a deleted card has nothing to attach to.
                if (r.IsDBNull(0) || r.IsDBNull(1) || !lists.TryGetValue(Convert.ToInt64(r.GetValue(0)), out var list) || !products.TryGetValue(Convert.ToInt64(r.GetValue(1)), out var product)) { historySkipped++; continue; }
                Exec(c, tx, "INSERT INTO product_price_history(id,price_list_id,product_id,old_price,new_price,changed_at,changed_by,source) VALUES($id,$l,$p,NULL,$new,$at,$user,'Import')",
                    ("$id", Guid.NewGuid().ToString()), ("$l", list), ("$p", product), ("$new", r.IsDBNull(2) ? DBNull.Value : Convert.ToDecimal(r.GetValue(2))),
                    ("$at", r.IsDBNull(3) ? now : r.GetDateTime(3).ToUniversalTime().ToString("O")), ("$user", r.GetString(4).Trim()));
                history++;
            }

        tx.Commit();
        return (lists.Count, prices, history, unmatched, historySkipped);
    }

    private static string? Find(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] parameters)
    {
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql;
        foreach (var (n, v) in parameters) q.Parameters.AddWithValue(n, v);
        return q.ExecuteScalar()?.ToString();
    }

    private static void Exec(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] parameters)
    {
        using var q = c.CreateCommand(); q.Transaction = tx; q.CommandText = sql;
        foreach (var (n, v) in parameters) q.Parameters.AddWithValue(n, v);
        q.ExecuteNonQuery();
    }

    private static string StableId(string kind, long legacy) => new Guid(MD5.HashData(Encoding.UTF8.GetBytes("R3:" + kind + ":" + legacy))).ToString();
}
