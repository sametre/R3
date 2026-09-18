using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed class StoreDatabase
{
    public string Path { get; }
    public int SchemaVersion => Convert.ToInt32(Query("PRAGMA user_version").Rows[0][0]);
    public string? LastBackup => Directory.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "Backups")) ? Directory.GetFiles(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "Backups"), "R3_*.db").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
    public StoreDatabase(string? path = null)
    {
        Path = path ?? Environment.GetEnvironmentVariable("R3_SQLITE_PATH") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "R3", "data", "r3.db");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Stores (Id INTEGER PRIMARY KEY, Code TEXT NOT NULL UNIQUE COLLATE NOCASE, Name TEXT NOT NULL, Phone TEXT NOT NULL DEFAULT '', Address TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS Customers (Id INTEGER PRIMARY KEY, Code TEXT NOT NULL UNIQUE COLLATE NOCASE, Name TEXT NOT NULL, Phone TEXT NOT NULL DEFAULT '', Address TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS Movements (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL REFERENCES Customers(Id), StoreId INTEGER NOT NULL REFERENCES Stores(Id), Date TEXT NOT NULL, Type TEXT NOT NULL CHECK(Type IN ('Borç','Tahsilat')), Amount INTEGER NOT NULL CHECK(Amount > 0), Description TEXT NOT NULL DEFAULT '');
            CREATE INDEX IF NOT EXISTS IX_Movements_Customer ON Movements(CustomerId,Date,Id);
            CREATE TABLE IF NOT EXISTS companies (id TEXT PRIMARY KEY, code TEXT NOT NULL COLLATE NOCASE UNIQUE, name TEXT NOT NULL, legal_name TEXT NOT NULL DEFAULT '', tax_office TEXT NOT NULL DEFAULT '', tax_number TEXT NOT NULL DEFAULT '', phone TEXT NOT NULL DEFAULT '', email TEXT NOT NULL DEFAULT '', address TEXT NOT NULL DEFAULT '', is_active INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS branches (id TEXT PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id), code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS warehouses (id TEXT PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id), branch_id TEXT NOT NULL REFERENCES branches(id), code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, warehouse_type TEXT NOT NULL DEFAULT 'Main', is_active INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS brands (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS categories (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, parent_id TEXT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS units (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, decimal_places INTEGER NOT NULL DEFAULT 2, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS products (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, brand_id TEXT NULL, category_id TEXT NULL, base_unit_id TEXT NOT NULL, product_type TEXT NOT NULL DEFAULT 'PRODUCT', vat_rate REAL NOT NULL DEFAULT 0, purchase_vat_rate REAL NOT NULL DEFAULT 0, excise_rate REAL NOT NULL DEFAULT 0, minimum_stock REAL NOT NULL DEFAULT 0, maximum_stock REAL NOT NULL DEFAULT 0, minimum_order_quantity REAL NOT NULL DEFAULT 0, order_multiple REAL NOT NULL DEFAULT 0, is_sellable INTEGER NOT NULL DEFAULT 1, is_active INTEGER NOT NULL DEFAULT 1, legacy_source TEXT NULL, legacy_id INTEGER NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS product_variants (id TEXT PRIMARY KEY, product_id TEXT NOT NULL REFERENCES products(id), code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, size_code TEXT NULL, color_code TEXT NULL, model_code TEXT NULL, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(product_id, code));
            CREATE TABLE IF NOT EXISTS product_barcodes (id TEXT PRIMARY KEY, product_id TEXT NOT NULL REFERENCES products(id), variant_id TEXT NULL, unit_id TEXT NULL, barcode TEXT NOT NULL UNIQUE, quantity REAL NOT NULL CHECK(quantity > 0), is_primary INTEGER NOT NULL DEFAULT 0, is_active INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS accounts (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, account_type TEXT NOT NULL, tax_number TEXT NOT NULL DEFAULT '', phone TEXT NOT NULL DEFAULT '', email TEXT NOT NULL DEFAULT '', is_active INTEGER NOT NULL DEFAULT 1, legacy_source TEXT NULL, legacy_id INTEGER NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS account_addresses (id TEXT PRIMARY KEY, account_id TEXT NOT NULL REFERENCES accounts(id), address_type TEXT NOT NULL, title TEXT NOT NULL, country TEXT NOT NULL DEFAULT 'Türkiye', city TEXT NOT NULL DEFAULT '', district TEXT NOT NULL DEFAULT '', neighborhood TEXT NOT NULL DEFAULT '', address_line TEXT NOT NULL DEFAULT '', postal_code TEXT NOT NULL DEFAULT '', fax TEXT NOT NULL DEFAULT '', website TEXT NOT NULL DEFAULT '', is_default INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS customer_profiles (account_id TEXT PRIMARY KEY REFERENCES accounts(id), customer_group TEXT NULL, region TEXT NULL, payment_term_days INTEGER NOT NULL DEFAULT 0, discount_rate REAL NOT NULL DEFAULT 0, extra_credit_limit REAL NOT NULL DEFAULT 0, blocked_credit REAL NOT NULL DEFAULT 0, kvkk_consent INTEGER NOT NULL DEFAULT 0, scoring_score INTEGER NULL, started_at TEXT NULL);
            CREATE TABLE IF NOT EXISTS supplier_profiles (account_id TEXT PRIMARY KEY REFERENCES accounts(id), supplier_group TEXT NULL, region TEXT NULL, payment_term_days INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS audit_logs (id TEXT PRIMARY KEY, user_id TEXT NULL, company_id TEXT NULL, entity_type TEXT NOT NULL, entity_id TEXT NOT NULL, action TEXT NOT NULL, old_values TEXT NULL, new_values TEXT NULL, created_at TEXT NOT NULL);
             CREATE TABLE IF NOT EXISTS users (id TEXT PRIMARY KEY, username TEXT NOT NULL COLLATE NOCASE UNIQUE, display_name TEXT NOT NULL, password_hash TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS inventory_transactions (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, branch_id TEXT NOT NULL, warehouse_id TEXT NOT NULL, product_id TEXT NOT NULL, variant_id TEXT NULL, transaction_type TEXT NOT NULL, quantity REAL NOT NULL CHECK(quantity > 0), unit_cost REAL NULL, total_cost REAL NULL, document_type TEXT NULL, document_id TEXT NULL, reference_no TEXT NULL, description TEXT NULL, transaction_at TEXT NOT NULL, created_at TEXT NOT NULL, legacy_source TEXT NULL, legacy_id INTEGER NULL, correlation_id TEXT NULL);
            CREATE TABLE IF NOT EXISTS inventory_balances (company_id TEXT NOT NULL, branch_id TEXT NOT NULL, warehouse_id TEXT NOT NULL, product_id TEXT NOT NULL, variant_id TEXT NULL, quantity_on_hand REAL NOT NULL DEFAULT 0, quantity_reserved REAL NOT NULL DEFAULT 0, quantity_available REAL NOT NULL DEFAULT 0, last_transaction_at TEXT NULL, updated_at TEXT NOT NULL, PRIMARY KEY(warehouse_id, product_id, variant_id));
            CREATE INDEX IF NOT EXISTS IX_InventoryTransactions_WarehouseProduct ON inventory_transactions(warehouse_id,product_id,variant_id,transaction_at);
            CREATE INDEX IF NOT EXISTS IX_InventoryTransactions_TypeDocument ON inventory_transactions(transaction_type,document_id);
            CREATE TABLE IF NOT EXISTS sales_documents (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, branch_id TEXT NOT NULL, warehouse_id TEXT NOT NULL, account_id TEXT NOT NULL, document_type TEXT NOT NULL DEFAULT 'Invoice', document_no TEXT NULL, document_date TEXT NOT NULL, posting_date TEXT NULL, status TEXT NOT NULL DEFAULT 'Draft', currency_code TEXT NOT NULL DEFAULT 'TRY', exchange_rate REAL NOT NULL DEFAULT 1, subtotal REAL NOT NULL DEFAULT 0, discount_total REAL NOT NULL DEFAULT 0, tax_total REAL NOT NULL DEFAULT 0, grand_total REAL NOT NULL DEFAULT 0, description TEXT NOT NULL DEFAULT '', posted_at TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, legacy_source TEXT NULL, legacy_id INTEGER NULL);
            CREATE TABLE IF NOT EXISTS sales_document_lines (id TEXT PRIMARY KEY, sales_document_id TEXT NOT NULL REFERENCES sales_documents(id), line_no INTEGER NOT NULL, product_id TEXT NOT NULL, variant_id TEXT NULL, unit_id TEXT NOT NULL, barcode_id TEXT NULL, quantity REAL NOT NULL, quantity_factor REAL NOT NULL DEFAULT 1, base_quantity REAL NOT NULL, unit_price REAL NOT NULL, discount_rate REAL NOT NULL DEFAULT 0, discount_amount REAL NOT NULL DEFAULT 0, vat_rate REAL NOT NULL DEFAULT 0, gross_amount REAL NOT NULL, net_amount REAL NOT NULL, vat_amount REAL NOT NULL, line_total REAL NOT NULL, description TEXT NOT NULL DEFAULT '', UNIQUE(sales_document_id,line_no));
            CREATE TABLE IF NOT EXISTS account_transactions (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, branch_id TEXT NOT NULL, account_id TEXT NOT NULL, transaction_type TEXT NOT NULL, debit REAL NOT NULL DEFAULT 0, credit REAL NOT NULL DEFAULT 0, currency_code TEXT NOT NULL DEFAULT 'TRY', exchange_rate REAL NOT NULL DEFAULT 1, document_type TEXT NULL, document_id TEXT NULL, document_no TEXT NULL, description TEXT NOT NULL DEFAULT '', transaction_at TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS account_balances (company_id TEXT NOT NULL, account_id TEXT NOT NULL, debit REAL NOT NULL DEFAULT 0, credit REAL NOT NULL DEFAULT 0, balance REAL NOT NULL DEFAULT 0, updated_at TEXT NOT NULL, PRIMARY KEY(company_id,account_id));
            CREATE TABLE IF NOT EXISTS number_sequences (company_id TEXT NOT NULL, sequence_type TEXT NOT NULL, year INTEGER NOT NULL, prefix TEXT NOT NULL, last_number INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(company_id,sequence_type,year));
            CREATE INDEX IF NOT EXISTS IX_SalesDocuments_Search ON sales_documents(company_id,document_date,status,account_id);
            CREATE INDEX IF NOT EXISTS IX_SalesLines_Product ON sales_document_lines(product_id,variant_id);
            CREATE INDEX IF NOT EXISTS IX_AccountTransactions_Account ON account_transactions(account_id,transaction_at);
            CREATE INDEX IF NOT EXISTS IX_AccountTransactions_Document ON account_transactions(document_type,document_id);
            CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_at TEXT NOT NULL);
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(1,'initial-canonical-schema',datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(2,'inventory-ledger-and-query-contracts',datetime('now'));
            PRAGMA user_version=2;
            """;
        command.ExecuteNonQuery();
        try { using var alter = connection.CreateCommand(); alter.CommandText = "ALTER TABLE inventory_transactions ADD COLUMN document_line_id TEXT NULL"; alter.ExecuteNonQuery(); } catch (SqliteException) { }
        foreach (var statement in new[] { "ALTER TABLE accounts ADD COLUMN tax_office TEXT NOT NULL DEFAULT ''", "ALTER TABLE accounts ADD COLUMN tax_number TEXT NOT NULL DEFAULT ''", "ALTER TABLE accounts ADD COLUMN identity_number TEXT NOT NULL DEFAULT ''", "ALTER TABLE accounts ADD COLUMN mobile_phone TEXT NOT NULL DEFAULT ''", "ALTER TABLE accounts ADD COLUMN credit_limit REAL NOT NULL DEFAULT 0", "ALTER TABLE accounts ADD COLUMN risk_limit REAL NOT NULL DEFAULT 0" }) { try { using var alter = connection.CreateCommand(); alter.CommandText = statement; alter.ExecuteNonQuery(); } catch (SqliteException) { } }
        foreach (var statement in new[] { "ALTER TABLE products ADD COLUMN purchase_vat_rate REAL NOT NULL DEFAULT 0", "ALTER TABLE products ADD COLUMN excise_rate REAL NOT NULL DEFAULT 0", "ALTER TABLE products ADD COLUMN minimum_stock REAL NOT NULL DEFAULT 0", "ALTER TABLE products ADD COLUMN maximum_stock REAL NOT NULL DEFAULT 0", "ALTER TABLE products ADD COLUMN minimum_order_quantity REAL NOT NULL DEFAULT 0", "ALTER TABLE products ADD COLUMN order_multiple REAL NOT NULL DEFAULT 0", "ALTER TABLE products ADD COLUMN is_sellable INTEGER NOT NULL DEFAULT 1", "ALTER TABLE account_addresses ADD COLUMN country TEXT NOT NULL DEFAULT 'Türkiye'", "ALTER TABLE account_addresses ADD COLUMN neighborhood TEXT NOT NULL DEFAULT ''", "ALTER TABLE account_addresses ADD COLUMN fax TEXT NOT NULL DEFAULT ''", "ALTER TABLE account_addresses ADD COLUMN website TEXT NOT NULL DEFAULT ''" }) { try { using var alter = connection.CreateCommand(); alter.CommandText = statement; alter.ExecuteNonQuery(); } catch (SqliteException) { } }
        using var seed = connection.CreateCommand();
        seed.CommandText = """
            INSERT OR IGNORE INTO companies(id,code,name,legal_name,is_active,created_at,updated_at) VALUES('00000000-0000-0000-0000-000000000001','R3','R3 Demo Firma','R3 Demo Firma',1,datetime('now'),datetime('now'));
            INSERT OR IGNORE INTO branches(id,company_id,code,name,is_active,created_at,updated_at) VALUES('00000000-0000-0000-0000-000000000011','00000000-0000-0000-0000-000000000001','MERKEZ','Merkez Şube',1,datetime('now'),datetime('now'));
            INSERT OR IGNORE INTO warehouses(id,company_id,branch_id,code,name,warehouse_type,is_active,created_at,updated_at) VALUES('00000000-0000-0000-0000-000000000111','00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000011','MERKEZ','Merkez Depo','Main',1,datetime('now'),datetime('now'));
            INSERT OR IGNORE INTO units(id,company_id,code,name,decimal_places,is_active) VALUES('00000000-0000-0000-0000-000000001111','00000000-0000-0000-0000-000000000001','ADET','Adet',0,1);
            """;
        seed.ExecuteNonQuery();
        EnsureDefaultUser(connection);
    }
    public string? Authenticate(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return null;
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT display_name,password_hash FROM users WHERE username=$username AND is_active=1";
        command.Parameters.AddWithValue("$username", username.Trim());
        using var reader = command.ExecuteReader();
        if (!reader.Read() || !VerifyPassword(password, reader.GetString(1))) return null;
        return reader.GetString(0);
    }
    private static void EnsureDefaultUser(SqliteConnection connection)
    {
        using var check = connection.CreateCommand(); check.CommandText = "SELECT COUNT(1) FROM users";
        if (Convert.ToInt32(check.ExecuteScalar()) > 0) return;
        using var insert = connection.CreateCommand(); insert.CommandText = "INSERT INTO users(id,username,display_name,password_hash,is_active,created_at,updated_at) VALUES($id,$username,$name,$hash,1,$now,$now)";
        var now = DateTime.UtcNow.ToString("O"); insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); insert.Parameters.AddWithValue("$username", "admin"); insert.Parameters.AddWithValue("$name", "R3 Yönetici"); insert.Parameters.AddWithValue("$hash", HashPassword("R3Admin2026!")); insert.Parameters.AddWithValue("$now", now); insert.ExecuteNonQuery();
    }
    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16); var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"100000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
    private static bool VerifyPassword(string password, string encoded)
    {
        var parts = encoded.Split('.'); if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
        try { var expected = Convert.FromBase64String(parts[2]); var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[1]), iterations, HashAlgorithmName.SHA256, expected.Length); return CryptographicOperations.FixedTimeEquals(actual, expected); }
        catch (FormatException) { return false; }
    }
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, ForeignKeys = true, DefaultTimeout = 5 }.ToString());
        connection.Open();
        return connection;
    }
    public string Backup(string? directory = null)
    {
        var targetDirectory = directory ?? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "Backups");
        Directory.CreateDirectory(targetDirectory);
        var target = System.IO.Path.Combine(targetDirectory, $"R3_{DateTime.Now:yyyy-MM-dd_HHmmss}.db");
        using var source = Open(); using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target }.ToString()); destination.Open(); source.BackupDatabase(destination); return target;
    }
    public DataTable Query(string sql, params (string, object)[] parameters)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        using var reader = command.ExecuteReader();
        var result = new DataTable();
        result.Load(reader);
        return result;
    }
    public int Execute(string sql, params (string, object)[] parameters)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = sql; foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value); return command.ExecuteNonQuery();
    }
    public DataTable List(bool stores) => Query($"SELECT Id, Code AS Kod, Name AS Ad, Phone AS Telefon, Address AS Adres FROM {(stores ? "Stores" : "Customers")} ORDER BY Code");
    public void Save(bool stores, long? id, string code, string name, string phone, string address)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Kod ve ad alanları zorunludur.");
        using var connection = Open();
        using var command = connection.CreateCommand();
        var table = stores ? "Stores" : "Customers";
        command.CommandText = id.HasValue ? $"UPDATE {table} SET Code=$code, Name=$name, Phone=$phone, Address=$address WHERE Id=$id" : $"INSERT INTO {table}(Code,Name,Phone,Address) VALUES($code,$name,$phone,$address)";
        command.Parameters.AddWithValue("$id", (object?)id ?? DBNull.Value);
        command.Parameters.AddWithValue("$code", code.Trim()); command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$phone", phone.Trim()); command.Parameters.AddWithValue("$address", address.Trim());
        command.ExecuteNonQuery();
    }
    public void AddMovement(long customer, long store, DateTime date, string type, decimal amount, string description)
    {
        if (amount <= 0 || amount > 999999999m || decimal.Round(amount, 2) != amount) throw new ArgumentException("Tutar 0'dan büyük, en fazla 999.999.999 ve iki ondalık basamaklı olmalıdır.");
        if (type is not ("Borç" or "Tahsilat")) throw new ArgumentException("Geçersiz işlem türü.");
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Movements(CustomerId,StoreId,Date,Type,Amount,Description) VALUES($customer,$store,$date,$type,$amount,$description)";
        command.Parameters.AddWithValue("$customer", customer); command.Parameters.AddWithValue("$store", store);
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd")); command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$amount", checked((long)(amount * 100))); command.Parameters.AddWithValue("$description", description.Trim());
        command.ExecuteNonQuery();
    }
    public DataTable Ledger(long customer) => Query("""
        SELECT m.Id AS No, m.Date AS Tarih, s.Name AS Mağaza, m.Type AS İşlem, m.Description AS Açıklama,
        CASE WHEN m.Type='Borç' THEN m.Amount ELSE 0 END AS BorçKuruş,
        CASE WHEN m.Type='Tahsilat' THEN m.Amount ELSE 0 END AS AlacakKuruş
        FROM Movements m JOIN Stores s ON s.Id=m.StoreId WHERE m.CustomerId=$id ORDER BY m.Date,m.Id
        """, ("$id", customer));
}
