using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using R3.Infrastructure;

/// <summary>
/// ASB → R3 ürün sınıflandırma aktarımı (--canonical-classifications). Mappings sample-verified against
/// ASBDB_ERKUR02 on 2026-09-23 before being modeled:
///   KODSTOKGRUP (GRPREF, GRPKOD, GRPADI, GRPGTIP)         → product_groups (22 rows; STOKKARTI.STKGRPREF resolves for 100% of products)
///   KODULKE     (ULKREF, ULKKOD ISO-2, ULKADI)              → countries      (236 rows; STKULKEREF filled on ~48% of products)
///   STOKKARTI   (STKREF, STKGRPREF, STKULKEREF)             → products.product_group_id / origin_country_id, matched on legacy_id
/// Idempotent: groups/countries are upserted by legacy id (countries also by ISO code, so the seeded
/// Türkiye row is adopted rather than duplicated), and product links are simply re-set.
/// GRPDU (all 'D' in the sample) is not interpreted - groups import as active.
/// Requires products to have been imported first (--canonical-core); unmatched STKREFs are counted, not failed.
/// </summary>
public sealed class ClassificationCanonicalImporter(string sourceName, StoreDatabase target, string companyId, SqlConnection source)
{
    public async Task<(int Groups, int Countries, int LinkedProducts, int UnmatchedProducts)> ImportAsync()
    {
        using var c = target.OpenConnection();
        using var tx = c.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");

        var groups = new Dictionary<long, string>();
        await using (var cmd = new SqlCommand("SELECT GRPREF, GRPKOD, GRPADI, ISNULL(GRPGTIP,'') FROM dbo.KODSTOKGRUP", source))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                var legacy = Convert.ToInt64(r.GetValue(0));
                var id = Find(c, tx, "SELECT id FROM product_groups WHERE legacy_source=$s AND legacy_id=$l", ("$s", sourceName), ("$l", legacy))
                         ?? Find(c, tx, "SELECT id FROM product_groups WHERE company_id=$c AND code=$code", ("$c", companyId), ("$code", r.GetString(1).Trim()))
                         ?? StableId("product-group", legacy);
                Exec(c, tx, """
                    INSERT INTO product_groups(id,company_id,code,name,customs_code,is_active,legacy_source,legacy_id,created_at,updated_at)
                    VALUES($id,$c,$code,$name,$gtip,1,$s,$l,$now,$now)
                    ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,customs_code=$gtip,legacy_source=$s,legacy_id=$l,updated_at=$now
                    """, ("$id", id), ("$c", companyId), ("$code", r.GetString(1).Trim()), ("$name", r.GetString(2).Trim()), ("$gtip", r.GetString(3).Trim()), ("$s", sourceName), ("$l", legacy), ("$now", now));
                groups[legacy] = id;
            }

        var countries = new Dictionary<long, string>();
        await using (var cmd = new SqlCommand("SELECT ULKREF, ULKKOD, ULKADI FROM dbo.KODULKE WHERE ISNULL(ULKKOD,'')<>''", source))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                var legacy = Convert.ToInt64(r.GetValue(0));
                var code = r.GetString(1).Trim().ToUpperInvariant();
                var id = Find(c, tx, "SELECT id FROM countries WHERE legacy_source=$s AND legacy_id=$l", ("$s", sourceName), ("$l", legacy))
                         ?? Find(c, tx, "SELECT id FROM countries WHERE code=$code", ("$code", code))
                         ?? StableId("country", legacy);
                Exec(c, tx, """
                    INSERT INTO countries(id,code,name,is_active,legacy_source,legacy_id,created_at,updated_at)
                    VALUES($id,$code,$name,1,$s,$l,$now,$now)
                    ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,legacy_source=$s,legacy_id=$l,updated_at=$now
                    """, ("$id", id), ("$code", code), ("$name", r.GetString(2).Trim()), ("$s", sourceName), ("$l", legacy), ("$now", now));
                countries[legacy] = id;
            }

        var products = new Dictionary<long, string>();
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx; q.CommandText = "SELECT legacy_id, id FROM products WHERE company_id=$c AND legacy_source=$s AND legacy_id IS NOT NULL";
            q.Parameters.AddWithValue("$c", companyId); q.Parameters.AddWithValue("$s", sourceName);
            using var r = q.ExecuteReader();
            while (r.Read()) products[r.GetInt64(0)] = r.GetString(1);
        }

        int linked = 0, unmatched = 0;
        await using (var cmd = new SqlCommand("SELECT STKREF, STKGRPREF, STKULKEREF FROM dbo.STOKKARTI", source))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync())
            {
                if (!products.TryGetValue(Convert.ToInt64(r.GetValue(0)), out var productId)) { unmatched++; continue; }
                var group = r.IsDBNull(1) ? null : groups.GetValueOrDefault(Convert.ToInt64(r.GetValue(1)));
                var country = r.IsDBNull(2) ? null : countries.GetValueOrDefault(Convert.ToInt64(r.GetValue(2)));
                Exec(c, tx, "UPDATE products SET product_group_id=$g, origin_country_id=$o WHERE id=$id",
                    ("$g", (object?)group ?? DBNull.Value), ("$o", (object?)country ?? DBNull.Value), ("$id", productId));
                linked++;
            }

        tx.Commit();
        return (groups.Count, countries.Count, linked, unmatched);
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

    // Same stable-id scheme as CoreCanonicalImporter, so re-runs map to the same rows.
    private static string StableId(string kind, long legacy) => new Guid(MD5.HashData(Encoding.UTF8.GetBytes("R3:" + kind + ":" + legacy))).ToString();
}
