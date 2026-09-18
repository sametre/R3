using System.Data;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed record MasterRecord(string Code, string Name, string ParentId = "", string Extra = "", bool IsActive = true);
public sealed class LocalMasterDataService(StoreDatabase database)
{
    public DataTable List(string kind, string? search = null)
    {
        var term = search?.Trim() ?? "";
        return kind switch
        {
            "companies" => database.Query("SELECT id AS Id, code AS Kod, name AS Ad, legal_name AS TicariUnvan, tax_office AS VergiDairesi, tax_number AS VergiNo, phone AS Telefon, email AS Eposta, is_active AS Aktif FROM companies WHERE code LIKE $q OR name LIKE $q OR tax_number LIKE $q ORDER BY code", ("$q", $"%{term}%")),
            "branches" => database.Query("SELECT b.id AS Id, b.code AS Kod, b.name AS Ad, c.name AS Firma, b.is_active AS Aktif FROM branches b JOIN companies c ON c.id=b.company_id WHERE b.code LIKE $q OR b.name LIKE $q ORDER BY b.code", ("$q", $"%{term}%")),
            "warehouses" => database.Query("SELECT w.id AS Id, w.code AS Kod, w.name AS Ad, c.name AS Firma, b.name AS Sube, w.warehouse_type AS DepoTipi, w.is_active AS Aktif FROM warehouses w JOIN companies c ON c.id=w.company_id JOIN branches b ON b.id=w.branch_id WHERE w.code LIKE $q OR w.name LIKE $q ORDER BY w.code", ("$q", $"%{term}%")),
            "brands" => database.Query("SELECT id AS Id, code AS Kod, name AS Ad, is_active AS Aktif FROM brands WHERE code LIKE $q OR name LIKE $q ORDER BY code", ("$q", $"%{term}%")),
            "categories" => database.Query("SELECT c.id AS Id, c.code AS Kod, c.name AS Ad, p.name AS UstKategori, c.is_active AS Aktif FROM categories c LEFT JOIN categories p ON p.id=c.parent_id WHERE c.code LIKE $q OR c.name LIKE $q ORDER BY c.code", ("$q", $"%{term}%")),
            "units" => database.Query("SELECT id AS Id, code AS Kod, name AS Ad, decimal_places AS Ondalik, is_active AS Aktif FROM units WHERE code LIKE $q OR name LIKE $q ORDER BY code", ("$q", $"%{term}%")),
            "products" => database.Query("SELECT p.id AS Id, p.code AS Kod, p.name AS Ad, b.name AS Marka, c.name AS Kategori, u.name AS Birim, p.vat_rate AS KDV, p.is_active AS Aktif FROM products p LEFT JOIN brands b ON b.id=p.brand_id LEFT JOIN categories c ON c.id=p.category_id JOIN units u ON u.id=p.base_unit_id WHERE p.code LIKE $q OR p.name LIKE $q ORDER BY p.code", ("$q", $"%{term}%")),
            _ => throw new ArgumentException("Bilinmeyen master data türü.")
        };
    }
    public void Save(string kind, string? id, MasterRecord record, string? companyId = null, string? branchId = null)
    {
        var now = DateTime.UtcNow.ToString("O"); var normalized = record.Code.Trim().ToUpperInvariant(); var entityId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id;
        using var connection = new SqliteConnection($"Data Source={database.Path};Foreign Keys=True;Default Timeout=5"); connection.Open(); using var tx = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = tx;
        if (string.IsNullOrWhiteSpace(companyId)) { using var lookup = connection.CreateCommand(); lookup.CommandText = "SELECT id FROM companies WHERE is_active=1 ORDER BY code LIMIT 1"; companyId = lookup.ExecuteScalar()?.ToString(); }
        if (string.IsNullOrWhiteSpace(branchId) && kind == "warehouses") { using var lookup = connection.CreateCommand(); lookup.CommandText = "SELECT id FROM branches WHERE company_id=$company AND is_active=1 ORDER BY code LIMIT 1"; lookup.Parameters.AddWithValue("$company", companyId ?? ""); branchId = lookup.ExecuteScalar()?.ToString(); }
        command.Parameters.AddWithValue("$id", entityId); command.Parameters.AddWithValue("$code", normalized); command.Parameters.AddWithValue("$name", record.Name.Trim()); command.Parameters.AddWithValue("$active", record.IsActive ? 1 : 0); command.Parameters.AddWithValue("$now", now);
        command.CommandText = kind switch
        {
            "companies" => "INSERT INTO companies(id,code,name,legal_name,is_active,created_at,updated_at) VALUES($id,$code,$name,$name,$active,$now,$now) ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,is_active=$active,updated_at=$now",
            "branches" => "INSERT INTO branches(id,company_id,code,name,is_active,created_at,updated_at) VALUES($id,$company,$code,$name,$active,$now,$now) ON CONFLICT(id) DO UPDATE SET company_id=$company,code=$code,name=$name,is_active=$active,updated_at=$now",
            "warehouses" => "INSERT INTO warehouses(id,company_id,branch_id,code,name,warehouse_type,is_active,created_at,updated_at) VALUES($id,$company,$branch,$code,$name,$extra,$active,$now,$now) ON CONFLICT(id) DO UPDATE SET company_id=$company,branch_id=$branch,code=$code,name=$name,warehouse_type=$extra,is_active=$active,updated_at=$now",
            "brands" => "INSERT INTO brands(id,company_id,code,name,is_active) VALUES($id,$company,$code,$name,$active) ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,is_active=$active",
            "categories" => "INSERT INTO categories(id,company_id,parent_id,code,name,is_active) VALUES($id,$company,$parent,$code,$name,$active) ON CONFLICT(id) DO UPDATE SET parent_id=$parent,code=$code,name=$name,is_active=$active",
            "units" => "INSERT INTO units(id,company_id,code,name,decimal_places,is_active) VALUES($id,$company,$code,$name,$extra,$active) ON CONFLICT(id) DO UPDATE SET code=$code,name=$name,decimal_places=$extra,is_active=$active",
            _ => throw new ArgumentException("Bilinmeyen master data türü.")
        };
        command.Parameters.AddWithValue("$company", (object?)companyId ?? DBNull.Value); command.Parameters.AddWithValue("$branch", (object?)branchId ?? DBNull.Value); command.Parameters.AddWithValue("$parent", string.IsNullOrWhiteSpace(record.ParentId) ? DBNull.Value : record.ParentId); command.Parameters.AddWithValue("$extra", string.IsNullOrWhiteSpace(record.Extra) ? (object)"Main" : record.Extra); command.ExecuteNonQuery(); tx.Commit();
    }
    public void SetActive(string kind, string id, bool active) { using var connection = new SqliteConnection($"Data Source={database.Path};Foreign Keys=True"); connection.Open(); using var cmd = connection.CreateCommand(); cmd.CommandText = $"UPDATE {kind} SET is_active=$active, updated_at=$now WHERE id=$id"; cmd.Parameters.AddWithValue("$active", active ? 1 : 0); cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O")); cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery(); }
}
