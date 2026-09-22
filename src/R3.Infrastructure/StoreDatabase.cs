using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace R3.Infrastructure;

public sealed class StoreDatabase
{
    public sealed record UserRole(string Code, string Name);
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
            CREATE TABLE IF NOT EXISTS warehouses (id TEXT PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id), branch_id TEXT NOT NULL REFERENCES branches(id), code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, warehouse_type TEXT NOT NULL DEFAULT 'Main', address TEXT NOT NULL DEFAULT '', responsible_user_id TEXT NULL, phone TEXT NOT NULL DEFAULT '', email TEXT NOT NULL DEFAULT '', is_active INTEGER NOT NULL DEFAULT 1, is_default_inbound INTEGER NOT NULL DEFAULT 0, is_default_outbound INTEGER NOT NULL DEFAULT 0, allow_negative_stock INTEGER NOT NULL DEFAULT 0, requires_location INTEGER NOT NULL DEFAULT 0, description TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL, updated_at TEXT NOT NULL, created_by TEXT NULL, updated_by TEXT NULL, legacy_source TEXT NULL, legacy_id INTEGER NULL, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS warehouse_locations (id TEXT PRIMARY KEY, warehouse_id TEXT NOT NULL REFERENCES warehouses(id), code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, location_type TEXT NOT NULL DEFAULT 'General', parent_location_id TEXT NULL REFERENCES warehouse_locations(id), aisle TEXT NOT NULL DEFAULT '', rack TEXT NOT NULL DEFAULT '', shelf TEXT NOT NULL DEFAULT '', bin TEXT NOT NULL DEFAULT '', barcode TEXT NULL COLLATE NOCASE, capacity REAL NULL CHECK(capacity IS NULL OR capacity >= 0), is_active INTEGER NOT NULL DEFAULT 1, is_default_inbound INTEGER NOT NULL DEFAULT 0, is_default_outbound INTEGER NOT NULL DEFAULT 0, description TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL, updated_at TEXT NOT NULL, legacy_source TEXT NULL, legacy_id INTEGER NULL, UNIQUE(warehouse_id, code), UNIQUE(barcode));
            CREATE TABLE IF NOT EXISTS warehouse_location_balances (id TEXT PRIMARY KEY, warehouse_id TEXT NOT NULL REFERENCES warehouses(id), location_id TEXT NOT NULL REFERENCES warehouse_locations(id), product_id TEXT NOT NULL REFERENCES products(id), variant_id TEXT NULL, lot_id TEXT NULL, quantity_on_hand REAL NOT NULL DEFAULT 0, quantity_reserved REAL NOT NULL DEFAULT 0, quantity_available REAL NOT NULL DEFAULT 0, updated_at TEXT NOT NULL, UNIQUE(location_id,product_id,variant_id,lot_id));
            CREATE INDEX IF NOT EXISTS IX_WarehouseLocations_Warehouse ON warehouse_locations(warehouse_id,is_active,code);
            CREATE TABLE IF NOT EXISTS brands (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS categories (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, parent_id TEXT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS units (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, decimal_places INTEGER NOT NULL DEFAULT 2, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS products (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, brand_id TEXT NULL, category_id TEXT NULL, base_unit_id TEXT NOT NULL, product_type TEXT NOT NULL DEFAULT 'PRODUCT', vat_rate REAL NOT NULL DEFAULT 0, purchase_vat_rate REAL NOT NULL DEFAULT 0, excise_rate REAL NOT NULL DEFAULT 0, minimum_stock REAL NOT NULL DEFAULT 0, maximum_stock REAL NOT NULL DEFAULT 0, minimum_order_quantity REAL NOT NULL DEFAULT 0, order_multiple REAL NOT NULL DEFAULT 0, is_sellable INTEGER NOT NULL DEFAULT 1, is_active INTEGER NOT NULL DEFAULT 1, image_path TEXT NOT NULL DEFAULT '', legacy_source TEXT NULL, legacy_id INTEGER NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS product_variants (id TEXT PRIMARY KEY, product_id TEXT NOT NULL REFERENCES products(id), code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, size_code TEXT NULL, color_code TEXT NULL, model_code TEXT NULL, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(product_id, code));
            CREATE TABLE IF NOT EXISTS product_barcodes (id TEXT PRIMARY KEY, product_id TEXT NOT NULL REFERENCES products(id), variant_id TEXT NULL, unit_id TEXT NULL, barcode TEXT NOT NULL UNIQUE, quantity REAL NOT NULL CHECK(quantity > 0), is_primary INTEGER NOT NULL DEFAULT 0, is_active INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS accounts (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, account_type TEXT NOT NULL, tax_number TEXT NOT NULL DEFAULT '', phone TEXT NOT NULL DEFAULT '', email TEXT NOT NULL DEFAULT '', is_active INTEGER NOT NULL DEFAULT 1, legacy_source TEXT NULL, legacy_id INTEGER NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS account_addresses (id TEXT PRIMARY KEY, account_id TEXT NOT NULL REFERENCES accounts(id), address_type TEXT NOT NULL, title TEXT NOT NULL, country TEXT NOT NULL DEFAULT 'Türkiye', city TEXT NOT NULL DEFAULT '', district TEXT NOT NULL DEFAULT '', neighborhood TEXT NOT NULL DEFAULT '', address_line TEXT NOT NULL DEFAULT '', postal_code TEXT NOT NULL DEFAULT '', fax TEXT NOT NULL DEFAULT '', website TEXT NOT NULL DEFAULT '', is_default INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS customer_profiles (account_id TEXT PRIMARY KEY REFERENCES accounts(id), customer_group TEXT NULL, region TEXT NULL, payment_term_days INTEGER NOT NULL DEFAULT 0, discount_rate REAL NOT NULL DEFAULT 0, extra_credit_limit REAL NOT NULL DEFAULT 0, blocked_credit REAL NOT NULL DEFAULT 0, kvkk_consent INTEGER NOT NULL DEFAULT 0, scoring_score INTEGER NULL, started_at TEXT NULL);
            CREATE TABLE IF NOT EXISTS supplier_profiles (account_id TEXT PRIMARY KEY REFERENCES accounts(id), supplier_group TEXT NULL, region TEXT NULL, payment_term_days INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS audit_logs (id TEXT PRIMARY KEY, user_id TEXT NULL, company_id TEXT NULL, entity_type TEXT NOT NULL, entity_id TEXT NOT NULL, action TEXT NOT NULL, old_values TEXT NULL, new_values TEXT NULL, created_at TEXT NOT NULL);
             CREATE TABLE IF NOT EXISTS users (id TEXT PRIMARY KEY, username TEXT NOT NULL COLLATE NOCASE UNIQUE, display_name TEXT NOT NULL, password_hash TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS roles (id TEXT PRIMARY KEY, code TEXT NOT NULL COLLATE NOCASE UNIQUE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS permissions (id TEXT PRIMARY KEY, permission_key TEXT NOT NULL COLLATE NOCASE UNIQUE, name TEXT NOT NULL, legacy_key TEXT NULL, module TEXT NOT NULL DEFAULT 'General');
            CREATE TABLE IF NOT EXISTS user_roles (user_id TEXT NOT NULL REFERENCES users(id), role_id TEXT NOT NULL REFERENCES roles(id), PRIMARY KEY(user_id,role_id));
            CREATE TABLE IF NOT EXISTS role_permissions (role_id TEXT NOT NULL REFERENCES roles(id), permission_id TEXT NOT NULL REFERENCES permissions(id), is_allowed INTEGER NOT NULL DEFAULT 1, PRIMARY KEY(role_id,permission_id));
            CREATE TABLE IF NOT EXISTS user_grid_layouts (id TEXT PRIMARY KEY, user_id TEXT NOT NULL REFERENCES users(id), view_key TEXT NOT NULL, layout_json TEXT NOT NULL, is_default INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(user_id,view_key));
             CREATE TABLE IF NOT EXISTS inventory_transactions (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, branch_id TEXT NOT NULL, warehouse_id TEXT NOT NULL, location_id TEXT NULL, product_id TEXT NOT NULL, variant_id TEXT NULL, transaction_type TEXT NOT NULL, quantity REAL NOT NULL CHECK(quantity > 0), base_quantity REAL NULL, unit_id TEXT NULL, lot_id TEXT NULL, serial_number TEXT NULL, unit_cost REAL NULL, total_cost REAL NULL, document_type TEXT NULL, document_id TEXT NULL, document_line_id TEXT NULL, reference_no TEXT NULL, description TEXT NULL, transaction_at TEXT NOT NULL, created_by TEXT NULL, created_at TEXT NOT NULL, legacy_source TEXT NULL, legacy_id INTEGER NULL, correlation_id TEXT NULL);
            CREATE TABLE IF NOT EXISTS inventory_balances (company_id TEXT NOT NULL, branch_id TEXT NOT NULL, warehouse_id TEXT NOT NULL, product_id TEXT NOT NULL, variant_id TEXT NULL, quantity_on_hand REAL NOT NULL DEFAULT 0, quantity_reserved REAL NOT NULL DEFAULT 0, quantity_available REAL NOT NULL DEFAULT 0, last_transaction_at TEXT NULL, updated_at TEXT NOT NULL, PRIMARY KEY(warehouse_id, product_id, variant_id));
            CREATE TABLE IF NOT EXISTS inventory_documents (id TEXT PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id), branch_id TEXT NOT NULL REFERENCES branches(id), warehouse_id TEXT NOT NULL REFERENCES warehouses(id), target_warehouse_id TEXT NULL REFERENCES warehouses(id), document_type TEXT NOT NULL, document_no TEXT NOT NULL, document_date TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'Draft' CHECK(status IN ('Draft','Approved','Cancelled','Reversed')), account_id TEXT NULL, source_document_id TEXT NULL, reference_no TEXT NULL, description TEXT NOT NULL DEFAULT '', created_by TEXT NOT NULL DEFAULT '', approved_by TEXT NULL, approved_at TEXT NULL, cancelled_at TEXT NULL, correlation_id TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE(company_id, document_no));
             CREATE TABLE IF NOT EXISTS inventory_document_lines (id TEXT PRIMARY KEY, inventory_document_id TEXT NOT NULL REFERENCES inventory_documents(id), line_no INTEGER NOT NULL, product_id TEXT NOT NULL REFERENCES products(id), variant_id TEXT NULL, barcode_id TEXT NULL, lot_id TEXT NULL, serial_number TEXT NULL, unit_id TEXT NOT NULL REFERENCES units(id), quantity REAL NOT NULL CHECK(quantity > 0), base_quantity REAL NOT NULL CHECK(base_quantity > 0), unit_cost REAL NULL CHECK(unit_cost IS NULL OR unit_cost >= 0), currency_code TEXT NOT NULL DEFAULT 'TRY', discount_rate REAL NOT NULL DEFAULT 0, discount_amount REAL NOT NULL DEFAULT 0, vat_rate REAL NOT NULL DEFAULT 0, location_id TEXT NULL, lot_no TEXT NULL, serial_no TEXT NULL, expiry_date TEXT NULL, description TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL DEFAULT '', UNIQUE(inventory_document_id,line_no));
             CREATE TABLE IF NOT EXISTS transfer_documents (id TEXT PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id), branch_id TEXT NOT NULL REFERENCES branches(id), transfer_no TEXT NOT NULL, transfer_date TEXT NOT NULL, source_warehouse_id TEXT NOT NULL REFERENCES warehouses(id), source_location_id TEXT NULL REFERENCES warehouse_locations(id), target_warehouse_id TEXT NOT NULL REFERENCES warehouses(id), target_location_id TEXT NULL REFERENCES warehouse_locations(id), status TEXT NOT NULL DEFAULT 'Draft' CHECK(status IN ('Draft','Approved','Cancelled','Reversed')), description TEXT NOT NULL DEFAULT '', requested_by TEXT NOT NULL DEFAULT '', approved_by TEXT NULL, created_at TEXT NOT NULL, approved_at TEXT NULL, cancelled_at TEXT NULL, correlation_id TEXT NULL, UNIQUE(company_id,transfer_no));
             CREATE TABLE IF NOT EXISTS transfer_document_lines (id TEXT PRIMARY KEY, transfer_document_id TEXT NOT NULL REFERENCES transfer_documents(id), line_no INTEGER NOT NULL, product_id TEXT NOT NULL REFERENCES products(id), variant_id TEXT NULL, unit_id TEXT NOT NULL REFERENCES units(id), quantity REAL NOT NULL CHECK(quantity > 0), base_quantity REAL NOT NULL CHECK(base_quantity > 0), lot_id TEXT NULL, serial_number TEXT NULL, expiry_date TEXT NULL, description TEXT NOT NULL DEFAULT '', UNIQUE(transfer_document_id,line_no));
             CREATE INDEX IF NOT EXISTS IX_TransferDocuments_Search ON transfer_documents(company_id,status,transfer_date);
            CREATE INDEX IF NOT EXISTS IX_InventoryDocuments_Search ON inventory_documents(company_id,document_type,status,document_date);
            CREATE INDEX IF NOT EXISTS IX_InventoryDocumentLines_Product ON inventory_document_lines(product_id,variant_id);
            CREATE INDEX IF NOT EXISTS IX_InventoryTransactions_WarehouseProduct ON inventory_transactions(warehouse_id,product_id,variant_id,transaction_at);
            CREATE INDEX IF NOT EXISTS IX_InventoryTransactions_TypeDocument ON inventory_transactions(transaction_type,document_id);
            CREATE TABLE IF NOT EXISTS sales_documents (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, branch_id TEXT NOT NULL, warehouse_id TEXT NOT NULL, account_id TEXT NOT NULL, document_type TEXT NOT NULL DEFAULT 'Invoice', document_no TEXT NULL, document_date TEXT NOT NULL, posting_date TEXT NULL, status TEXT NOT NULL DEFAULT 'Draft', currency_code TEXT NOT NULL DEFAULT 'TRY', exchange_rate REAL NOT NULL DEFAULT 1, subtotal REAL NOT NULL DEFAULT 0, discount_total REAL NOT NULL DEFAULT 0, tax_total REAL NOT NULL DEFAULT 0, grand_total REAL NOT NULL DEFAULT 0, description TEXT NOT NULL DEFAULT '', posted_at TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, legacy_source TEXT NULL, legacy_id INTEGER NULL);
            CREATE TABLE IF NOT EXISTS sales_document_lines (id TEXT PRIMARY KEY, sales_document_id TEXT NOT NULL REFERENCES sales_documents(id), line_no INTEGER NOT NULL, product_id TEXT NOT NULL, variant_id TEXT NULL, unit_id TEXT NOT NULL, barcode_id TEXT NULL, quantity REAL NOT NULL, quantity_factor REAL NOT NULL DEFAULT 1, base_quantity REAL NOT NULL, unit_price REAL NOT NULL, discount_rate REAL NOT NULL DEFAULT 0, discount_amount REAL NOT NULL DEFAULT 0, vat_rate REAL NOT NULL DEFAULT 0, gross_amount REAL NOT NULL, net_amount REAL NOT NULL, vat_amount REAL NOT NULL, line_total REAL NOT NULL, description TEXT NOT NULL DEFAULT '', UNIQUE(sales_document_id,line_no));
            CREATE TABLE IF NOT EXISTS account_transactions (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, branch_id TEXT NOT NULL, account_id TEXT NOT NULL, transaction_type TEXT NOT NULL, debit REAL NOT NULL DEFAULT 0, credit REAL NOT NULL DEFAULT 0, currency_code TEXT NOT NULL DEFAULT 'TRY', exchange_rate REAL NOT NULL DEFAULT 1, document_type TEXT NULL, document_id TEXT NULL, document_no TEXT NULL, description TEXT NOT NULL DEFAULT '', transaction_at TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS account_balances (company_id TEXT NOT NULL, account_id TEXT NOT NULL, debit REAL NOT NULL DEFAULT 0, credit REAL NOT NULL DEFAULT 0, balance REAL NOT NULL DEFAULT 0, updated_at TEXT NOT NULL, PRIMARY KEY(company_id,account_id));
            CREATE TABLE IF NOT EXISTS number_sequences (company_id TEXT NOT NULL, sequence_type TEXT NOT NULL, year INTEGER NOT NULL, prefix TEXT NOT NULL, last_number INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(company_id,sequence_type,year));
            CREATE INDEX IF NOT EXISTS IX_SalesDocuments_Search ON sales_documents(company_id,document_date,status,account_id);
            CREATE INDEX IF NOT EXISTS IX_SalesLines_Product ON sales_document_lines(product_id,variant_id);
            CREATE TABLE IF NOT EXISTS purchase_documents (id TEXT PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id), branch_id TEXT NOT NULL REFERENCES branches(id), warehouse_id TEXT NOT NULL REFERENCES warehouses(id), supplier_id TEXT NOT NULL REFERENCES accounts(id), source_order_id TEXT NULL REFERENCES purchase_documents(id), document_type TEXT NOT NULL DEFAULT 'Order', document_no TEXT NULL, document_date TEXT NOT NULL, expected_date TEXT NULL, status TEXT NOT NULL DEFAULT 'Draft', currency_code TEXT NOT NULL DEFAULT 'TRY', subtotal REAL NOT NULL DEFAULT 0, discount_total REAL NOT NULL DEFAULT 0, tax_total REAL NOT NULL DEFAULT 0, grand_total REAL NOT NULL DEFAULT 0, description TEXT NOT NULL DEFAULT '', approved_at TEXT NULL, approved_by TEXT NULL, posted_at TEXT NULL, posted_by TEXT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, legacy_source TEXT NULL, legacy_id INTEGER NULL);
            CREATE TABLE IF NOT EXISTS purchase_document_lines (id TEXT PRIMARY KEY, purchase_document_id TEXT NOT NULL REFERENCES purchase_documents(id), line_no INTEGER NOT NULL, product_id TEXT NOT NULL REFERENCES products(id), variant_id TEXT NULL, unit_id TEXT NOT NULL REFERENCES units(id), quantity REAL NOT NULL CHECK(quantity > 0), received_quantity REAL NOT NULL DEFAULT 0 CHECK(received_quantity >= 0), unit_price REAL NOT NULL DEFAULT 0 CHECK(unit_price >= 0), discount_rate REAL NOT NULL DEFAULT 0, discount_amount REAL NOT NULL DEFAULT 0, vat_rate REAL NOT NULL DEFAULT 0, gross_amount REAL NOT NULL DEFAULT 0, net_amount REAL NOT NULL DEFAULT 0, vat_amount REAL NOT NULL DEFAULT 0, line_total REAL NOT NULL DEFAULT 0, description TEXT NOT NULL DEFAULT '', UNIQUE(purchase_document_id,line_no));
            CREATE INDEX IF NOT EXISTS IX_PurchaseDocuments_Search ON purchase_documents(company_id,document_type,status,document_date,supplier_id);
            CREATE INDEX IF NOT EXISTS IX_PurchaseLines_Product ON purchase_document_lines(product_id,variant_id);
            CREATE INDEX IF NOT EXISTS IX_AccountTransactions_Account ON account_transactions(account_id,transaction_at);
            CREATE INDEX IF NOT EXISTS IX_AccountTransactions_Document ON account_transactions(document_type,document_id);
            CREATE TABLE IF NOT EXISTS account_contacts (id TEXT PRIMARY KEY, account_id TEXT NOT NULL REFERENCES accounts(id), first_name TEXT NOT NULL, last_name TEXT NOT NULL DEFAULT '', title TEXT NOT NULL DEFAULT '', department TEXT NOT NULL DEFAULT '', phone TEXT NOT NULL DEFAULT '', mobile_phone TEXT NOT NULL DEFAULT '', email TEXT NOT NULL DEFAULT '', is_primary INTEGER NOT NULL DEFAULT 0, is_active INTEGER NOT NULL DEFAULT 1, notes TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL DEFAULT '', updated_at TEXT NOT NULL DEFAULT '');
            CREATE INDEX IF NOT EXISTS IX_AccountContacts_Account ON account_contacts(account_id);
            CREATE INDEX IF NOT EXISTS IX_AccountAddresses_Account ON account_addresses(account_id);
            CREATE TABLE IF NOT EXISTS account_banks (id TEXT PRIMARY KEY, account_id TEXT NOT NULL REFERENCES accounts(id), bank_name TEXT NOT NULL, branch_name TEXT NOT NULL DEFAULT '', branch_code TEXT NOT NULL DEFAULT '', account_name TEXT NOT NULL DEFAULT '', iban TEXT NOT NULL DEFAULT '', account_number TEXT NOT NULL DEFAULT '', currency_code TEXT NOT NULL DEFAULT 'TRY', is_default INTEGER NOT NULL DEFAULT 0, is_active INTEGER NOT NULL DEFAULT 1, notes TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_AccountBanks_Account ON account_banks(account_id,is_active,bank_name);
            CREATE TABLE IF NOT EXISTS account_tax_profiles (account_id TEXT PRIMARY KEY REFERENCES accounts(id), person_type TEXT NOT NULL DEFAULT 'LegalEntity', legal_title TEXT NOT NULL DEFAULT '', trade_registry_number TEXT NOT NULL DEFAULT '', country_code TEXT NOT NULL DEFAULT 'TR');
            CREATE TABLE IF NOT EXISTS account_einvoice_profiles (account_id TEXT PRIMARY KEY REFERENCES accounts(id), is_einvoice_enabled INTEGER NOT NULL DEFAULT 0, einvoice_alias TEXT NOT NULL DEFAULT '', invoice_scenario TEXT NOT NULL DEFAULT 'Temel', is_edispatch_enabled INTEGER NOT NULL DEFAULT 0, edispatch_alias TEXT NOT NULL DEFAULT '', is_gib_compliant INTEGER NOT NULL DEFAULT 0, print_invoice INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS account_groups (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS regions (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS delivery_regions (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS price_lists (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS currencies (id TEXT PRIMARY KEY, code TEXT NOT NULL COLLATE NOCASE UNIQUE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS account_notes (id TEXT PRIMARY KEY, account_id TEXT NOT NULL REFERENCES accounts(id), note_type TEXT NOT NULL DEFAULT 'Genel', title TEXT NOT NULL DEFAULT '', content TEXT NOT NULL DEFAULT '', is_pinned INTEGER NOT NULL DEFAULT 0, created_by TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_AccountNotes_Account ON account_notes(account_id);
            CREATE TABLE IF NOT EXISTS cash_account_groups (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS cash_accounts (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, branch_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, currency_code TEXT NOT NULL DEFAULT 'TRY', cash_account_type TEXT NOT NULL DEFAULT 'MainCash', group_id TEXT NULL, is_active INTEGER NOT NULL DEFAULT 1, allow_negative_balance INTEGER NOT NULL DEFAULT 0, description TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL, created_by TEXT NOT NULL DEFAULT '', updated_at TEXT NOT NULL, updated_by TEXT NOT NULL DEFAULT '', legacy_id INTEGER NULL, legacy_code TEXT NULL, UNIQUE(company_id, branch_id, code));
            CREATE TABLE IF NOT EXISTS cash_transactions (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, branch_id TEXT NOT NULL, cash_account_id TEXT NOT NULL REFERENCES cash_accounts(id), transaction_type TEXT NOT NULL, direction TEXT NOT NULL CHECK(direction IN ('In','Out')), amount REAL NOT NULL CHECK(amount > 0), currency_code TEXT NOT NULL, exchange_rate REAL NOT NULL DEFAULT 1, local_amount REAL NOT NULL, transaction_date TEXT NOT NULL, document_type TEXT NULL, document_id TEXT NULL, document_number TEXT NULL, account_id TEXT NULL, account_transaction_id TEXT NULL, bank_transaction_id TEXT NULL, cheque_promissory_note_id TEXT NULL, reference_transaction_id TEXT NULL, target_cash_account_id TEXT NULL, description TEXT NOT NULL DEFAULT '', status TEXT NOT NULL DEFAULT 'Posted', created_at TEXT NOT NULL, created_by TEXT NOT NULL DEFAULT '', posted_at TEXT NOT NULL, posted_by TEXT NOT NULL DEFAULT '', legacy_id INTEGER NULL);
            CREATE INDEX IF NOT EXISTS IX_CashTransactions_Account ON cash_transactions(cash_account_id,transaction_date);
            CREATE INDEX IF NOT EXISTS IX_CashTransactions_Document ON cash_transactions(document_type,document_id);
            CREATE TABLE IF NOT EXISTS cash_balances (company_id TEXT NOT NULL, cash_account_id TEXT NOT NULL, currency_code TEXT NOT NULL DEFAULT 'TRY', total_in REAL NOT NULL DEFAULT 0, total_out REAL NOT NULL DEFAULT 0, balance REAL NOT NULL DEFAULT 0, last_transaction_at TEXT NULL, updated_at TEXT NOT NULL, PRIMARY KEY(company_id,cash_account_id));

            -- Banka (Bank) engine: same shape as cash_accounts/cash_transactions/cash_balances above -
            -- one company/branch-scoped account per bank IBAN, a ledger of postings, and a rebuildable
            -- balance projection. Mirrors ASB's BANKAHESABI/BANKAHAR (SqlData\ASBDB_ERKUR02.mdf) at the
            -- concept level - canonical field names, not a verbatim column-for-column port, per this
            -- codebase's "canonical model, not an ASB clone" convention (see docs/PRODUCT_CARD_V2.md).
            CREATE TABLE IF NOT EXISTS bank_accounts (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, branch_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, bank_name TEXT NOT NULL DEFAULT '', bank_branch_name TEXT NOT NULL DEFAULT '', bank_branch_code TEXT NOT NULL DEFAULT '', account_number TEXT NOT NULL DEFAULT '', iban TEXT NOT NULL DEFAULT '', currency_code TEXT NOT NULL DEFAULT 'TRY', account_type TEXT NOT NULL DEFAULT 'Checking', is_active INTEGER NOT NULL DEFAULT 1, description TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL, created_by TEXT NOT NULL DEFAULT '', updated_at TEXT NOT NULL, updated_by TEXT NOT NULL DEFAULT '', legacy_id INTEGER NULL, legacy_code TEXT NULL, UNIQUE(company_id, branch_id, code));
            CREATE TABLE IF NOT EXISTS bank_transactions (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, branch_id TEXT NOT NULL, bank_account_id TEXT NOT NULL REFERENCES bank_accounts(id), transaction_type TEXT NOT NULL, direction TEXT NOT NULL CHECK(direction IN ('In','Out')), amount REAL NOT NULL CHECK(amount > 0), currency_code TEXT NOT NULL, exchange_rate REAL NOT NULL DEFAULT 1, local_amount REAL NOT NULL, transaction_date TEXT NOT NULL, value_date TEXT NULL, document_type TEXT NULL, document_id TEXT NULL, document_number TEXT NULL, account_id TEXT NULL, account_transaction_id TEXT NULL, cash_transaction_id TEXT NULL, cheque_id TEXT NULL, reference_transaction_id TEXT NULL, target_bank_account_id TEXT NULL, target_cash_account_id TEXT NULL, description TEXT NOT NULL DEFAULT '', status TEXT NOT NULL DEFAULT 'Posted', created_at TEXT NOT NULL, created_by TEXT NOT NULL DEFAULT '', posted_at TEXT NOT NULL, posted_by TEXT NOT NULL DEFAULT '', legacy_id INTEGER NULL);
            CREATE INDEX IF NOT EXISTS IX_BankTransactions_Account ON bank_transactions(bank_account_id,transaction_date);
            CREATE INDEX IF NOT EXISTS IX_BankTransactions_Document ON bank_transactions(document_type,document_id);
            CREATE TABLE IF NOT EXISTS bank_balances (company_id TEXT NOT NULL, bank_account_id TEXT NOT NULL, currency_code TEXT NOT NULL DEFAULT 'TRY', total_in REAL NOT NULL DEFAULT 0, total_out REAL NOT NULL DEFAULT 0, balance REAL NOT NULL DEFAULT 0, last_transaction_at TEXT NULL, updated_at TEXT NOT NULL, PRIMARY KEY(company_id,bank_account_id));

            -- Çek/Senet (checks + promissory notes) engine, one portfolio table with an
            -- instrument_type discriminator - mirrors ASB's CEKSENET, which also keeps both in a
            -- single table (CSCS: Ç/S). status is a lifecycle, not a free-text field: a received
            -- instrument moves Portfolio -> DepositedForCollection -> Collected/Bounced/Endorsed/
            -- ReturnedToDrawer; a given instrument moves Portfolio -> Paid/ReturnedToDrawer. Every
            -- transition is recorded in cheque_status_history so "who changed this and when" is
            -- always answerable, matching audit_logs' role elsewhere in the schema.
            CREATE TABLE IF NOT EXISTS cheques (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, branch_id TEXT NOT NULL, instrument_type TEXT NOT NULL CHECK(instrument_type IN ('Cheque','PromissoryNote')), direction TEXT NOT NULL CHECK(direction IN ('Received','Given')), status TEXT NOT NULL DEFAULT 'Portfolio', account_id TEXT NULL, amount REAL NOT NULL CHECK(amount > 0), currency_code TEXT NOT NULL DEFAULT 'TRY', due_date TEXT NOT NULL, issue_date TEXT NULL, cheque_number TEXT NOT NULL DEFAULT '', drawer_name TEXT NOT NULL DEFAULT '', bank_name TEXT NOT NULL DEFAULT '', bank_branch_name TEXT NOT NULL DEFAULT '', bank_account_number TEXT NOT NULL DEFAULT '', endorser TEXT NOT NULL DEFAULT '', cash_account_id TEXT NULL REFERENCES cash_accounts(id), bank_account_id TEXT NULL REFERENCES bank_accounts(id), settlement_date TEXT NULL, description TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL, created_by TEXT NOT NULL DEFAULT '', updated_at TEXT NOT NULL, updated_by TEXT NOT NULL DEFAULT '', legacy_id INTEGER NULL);
            CREATE INDEX IF NOT EXISTS IX_Cheques_Status ON cheques(company_id,status,due_date);
            CREATE INDEX IF NOT EXISTS IX_Cheques_Account ON cheques(account_id);
            CREATE TABLE IF NOT EXISTS cheque_status_history (id TEXT PRIMARY KEY, cheque_id TEXT NOT NULL REFERENCES cheques(id), from_status TEXT NOT NULL, to_status TEXT NOT NULL, changed_at TEXT NOT NULL, changed_by TEXT NOT NULL DEFAULT '', note TEXT NOT NULL DEFAULT '');
            CREATE INDEX IF NOT EXISTS IX_ChequeStatusHistory_Cheque ON cheque_status_history(cheque_id);

            CREATE TABLE IF NOT EXISTS shipment_orders (id TEXT PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id), branch_id TEXT NOT NULL REFERENCES branches(id), warehouse_id TEXT NOT NULL REFERENCES warehouses(id), account_id TEXT NOT NULL REFERENCES accounts(id), source_document_id TEXT NULL REFERENCES sales_documents(id), shipment_no TEXT NULL, order_date TEXT NOT NULL, planned_shipment_date TEXT NULL, status TEXT NOT NULL DEFAULT 'Pending', delivery_region_code TEXT NOT NULL DEFAULT '', carrier_code TEXT NOT NULL DEFAULT '', vehicle_plate TEXT NOT NULL DEFAULT '', driver_name TEXT NOT NULL DEFAULT '', delivery_contact TEXT NOT NULL DEFAULT '', description TEXT NOT NULL DEFAULT '', legacy_source TEXT NULL, legacy_id INTEGER NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS shipment_order_lines (id TEXT PRIMARY KEY, shipment_order_id TEXT NOT NULL REFERENCES shipment_orders(id), source_line_id TEXT NULL REFERENCES sales_document_lines(id), product_id TEXT NOT NULL REFERENCES products(id), variant_id TEXT NULL REFERENCES product_variants(id), planned_quantity REAL NOT NULL CHECK(planned_quantity > 0), shipped_quantity REAL NOT NULL DEFAULT 0 CHECK(shipped_quantity >= 0), unit_code TEXT NOT NULL DEFAULT '', stock_status TEXT NOT NULL DEFAULT 'Unknown', workflow_status TEXT NOT NULL DEFAULT 'Pending', legacy_id INTEGER NULL);
            CREATE TABLE IF NOT EXISTS shipment_deliveries (id TEXT PRIMARY KEY, shipment_order_id TEXT NOT NULL REFERENCES shipment_orders(id), delivered_at TEXT NULL, received_by TEXT NOT NULL DEFAULT '', result_code TEXT NOT NULL DEFAULT '', notes TEXT NOT NULL DEFAULT '', is_final INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_ShipmentOrders_Queue ON shipment_orders(company_id,status,planned_shipment_date);
            CREATE INDEX IF NOT EXISTS IX_ShipmentOrders_Account ON shipment_orders(account_id,order_date);
            CREATE INDEX IF NOT EXISTS IX_ShipmentLines_Order ON shipment_order_lines(shipment_order_id,workflow_status);
            CREATE TABLE IF NOT EXISTS electronic_documents (id TEXT PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id), branch_id TEXT NOT NULL REFERENCES branches(id), document_type TEXT NOT NULL, direction TEXT NOT NULL, source_entity_type TEXT NOT NULL, source_entity_id TEXT NOT NULL, account_id TEXT NULL REFERENCES accounts(id), document_number TEXT NULL, uuid TEXT NOT NULL UNIQUE, envelope_id TEXT NULL, provider_document_id TEXT NULL, provider_type TEXT NOT NULL DEFAULT '', status TEXT NOT NULL DEFAULT 'Draft', issue_date TEXT NOT NULL, issue_time TEXT NULL, currency_code TEXT NOT NULL DEFAULT 'TRY', payable_amount REAL NULL, recipient_snapshot_json TEXT NOT NULL DEFAULT '', generated_at TEXT NULL, queued_at TEXT NULL, sent_at TEXT NULL, delivered_at TEXT NULL, accepted_at TEXT NULL, rejected_at TEXT NULL, cancelled_at TEXT NULL, send_attempt_count INTEGER NOT NULL DEFAULT 0, last_attempt_at TEXT NULL, next_retry_at TEXT NULL, last_error_code TEXT NULL, last_error_message TEXT NULL, created_at TEXT NOT NULL, created_by TEXT NOT NULL DEFAULT '', updated_at TEXT NOT NULL, updated_by TEXT NOT NULL DEFAULT '');
            CREATE UNIQUE INDEX IF NOT EXISTS UX_ElectronicDocuments_Source ON electronic_documents(company_id,document_type,source_entity_type,source_entity_id) WHERE status<>'Cancelled';
            CREATE INDEX IF NOT EXISTS IX_ElectronicDocuments_Status ON electronic_documents(company_id,status,document_type);
            CREATE INDEX IF NOT EXISTS IX_ElectronicDocuments_Account ON electronic_documents(account_id,issue_date);
            CREATE TABLE IF NOT EXISTS electronic_document_events (id TEXT PRIMARY KEY, electronic_document_id TEXT NOT NULL REFERENCES electronic_documents(id), event_type TEXT NOT NULL, old_status TEXT NULL, new_status TEXT NULL, provider_code TEXT NULL, provider_message TEXT NULL, occurred_at TEXT NOT NULL, user_id TEXT NULL, raw_response_reference TEXT NULL);
            CREATE INDEX IF NOT EXISTS IX_ElectronicDocumentEvents_Document ON electronic_document_events(electronic_document_id,occurred_at);
            CREATE TABLE IF NOT EXISTS electronic_document_payloads (id TEXT PRIMARY KEY, electronic_document_id TEXT NOT NULL REFERENCES electronic_documents(id), payload_type TEXT NOT NULL, content TEXT NOT NULL, content_hash TEXT NOT NULL, mime_type TEXT NOT NULL, version INTEGER NOT NULL DEFAULT 1, is_signed INTEGER NOT NULL DEFAULT 0, created_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS IX_ElectronicDocumentPayloads_Document ON electronic_document_payloads(electronic_document_id,payload_type,version);
            CREATE TABLE IF NOT EXISTS electronic_document_company_profiles (company_id TEXT PRIMARY KEY REFERENCES companies(id), tax_number TEXT NOT NULL DEFAULT '', legal_title TEXT NOT NULL DEFAULT '', default_einvoice_alias TEXT NOT NULL DEFAULT '', default_edespatch_alias TEXT NOT NULL DEFAULT '', provider_type TEXT NOT NULL DEFAULT '', environment TEXT NOT NULL DEFAULT 'Test', auto_send INTEGER NOT NULL DEFAULT 0, auto_check_recipient INTEGER NOT NULL DEFAULT 1, default_invoice_scenario TEXT NOT NULL DEFAULT 'Temel', earchive_sender_email TEXT NOT NULL DEFAULT '', earchive_unit_code TEXT NOT NULL DEFAULT '', internet_sales_unit_code TEXT NOT NULL DEFAULT '', internet_website TEXT NOT NULL DEFAULT '', carrier_tax_number TEXT NOT NULL DEFAULT '', carrier_title TEXT NOT NULL DEFAULT '', updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS electronic_document_outbox (id TEXT PRIMARY KEY, electronic_document_id TEXT NOT NULL REFERENCES electronic_documents(id), operation_type TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'Pending', attempt_count INTEGER NOT NULL DEFAULT 0, max_attempts INTEGER NOT NULL DEFAULT 8, created_at TEXT NOT NULL, available_at TEXT NOT NULL, locked_at TEXT NULL, locked_by TEXT NULL, started_at TEXT NULL, completed_at TEXT NULL, last_attempt_at TEXT NULL, next_attempt_at TEXT NULL, last_error_code TEXT NULL, last_error_message TEXT NULL, idempotency_key TEXT NOT NULL, correlation_id TEXT NULL);
            CREATE INDEX IF NOT EXISTS IX_ElectronicDocumentOutbox_Claim ON electronic_document_outbox(status,available_at);
            CREATE INDEX IF NOT EXISTS IX_ElectronicDocumentOutbox_Document ON electronic_document_outbox(electronic_document_id);
            CREATE UNIQUE INDEX IF NOT EXISTS UX_ElectronicDocumentOutbox_ActiveOperation ON electronic_document_outbox(electronic_document_id,operation_type) WHERE status IN ('Pending','Processing');
            CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS legacy_migration_runs (id TEXT PRIMARY KEY, source_name TEXT NOT NULL, target_company_code TEXT NULL, mode TEXT NOT NULL, status TEXT NOT NULL, started_at TEXT NOT NULL, finished_at TEXT NULL, table_count INTEGER NOT NULL DEFAULT 0, row_count INTEGER NOT NULL DEFAULT 0, error_count INTEGER NOT NULL DEFAULT 0, error_message TEXT NULL);
            CREATE TABLE IF NOT EXISTS legacy_tables (source_name TEXT NOT NULL, schema_name TEXT NOT NULL, table_name TEXT NOT NULL, columns_json TEXT NOT NULL, row_count INTEGER NOT NULL DEFAULT 0, status TEXT NOT NULL DEFAULT 'Discovered', last_run_id TEXT NULL, PRIMARY KEY(source_name,schema_name,table_name));
            CREATE TABLE IF NOT EXISTS legacy_rows (source_name TEXT NOT NULL, schema_name TEXT NOT NULL, table_name TEXT NOT NULL, row_number INTEGER NOT NULL, row_json TEXT NOT NULL, imported_at TEXT NOT NULL, run_id TEXT NOT NULL, PRIMARY KEY(source_name,schema_name,table_name,row_number));
            CREATE INDEX IF NOT EXISTS IX_LegacyRows_Table ON legacy_rows(source_name,schema_name,table_name);
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(1,'initial-canonical-schema',datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(2,'inventory-ledger-and-query-contracts',datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(3,'account-address-and-contact-engine',datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(4,'account-profile-and-lookup-engine',datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(5,'central-action-permission-framework',datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(5,'cash-ledger-engine',datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(6,'electronic-document-core',datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(7,'electronic-document-outbox',datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(8,'electronic-document-company-address',datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(9,'shipment-order-and-delivery-engine',datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(10,'purchase-order-and-invoice-engine',datetime('now'));
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(11,'purchase-receipt-and-posting-engine',datetime('now'));
            CREATE TABLE IF NOT EXISTS product_units (id TEXT PRIMARY KEY, product_id TEXT NOT NULL REFERENCES products(id), unit_id TEXT NOT NULL REFERENCES units(id), sequence INTEGER NOT NULL DEFAULT 1, conversion_factor REAL NOT NULL CHECK(conversion_factor > 0), is_base_unit INTEGER NOT NULL DEFAULT 0, is_sales_unit INTEGER NOT NULL DEFAULT 1, is_purchase_unit INTEGER NOT NULL DEFAULT 1, is_active INTEGER NOT NULL DEFAULT 1, legacy_source TEXT NULL, legacy_id INTEGER NULL, UNIQUE(product_id, unit_id));
            CREATE INDEX IF NOT EXISTS IX_ProductUnits_Product ON product_units(product_id, sequence);
            CREATE TABLE IF NOT EXISTS product_suppliers (id TEXT PRIMARY KEY, product_id TEXT NOT NULL REFERENCES products(id), supplier_account_id TEXT NOT NULL REFERENCES accounts(id), supplier_product_code TEXT NOT NULL DEFAULT '', is_active INTEGER NOT NULL DEFAULT 1, lead_time_days INTEGER NOT NULL DEFAULT 0, extra_lead_time_days INTEGER NOT NULL DEFAULT 0, priority INTEGER NOT NULL DEFAULT 1, minimum_order_quantity REAL NULL, legacy_source TEXT NULL, legacy_id INTEGER NULL, UNIQUE(product_id, supplier_account_id));
            CREATE INDEX IF NOT EXISTS IX_ProductSuppliers_Product ON product_suppliers(product_id, priority);
            CREATE INDEX IF NOT EXISTS IX_ProductSuppliers_Supplier ON product_suppliers(supplier_account_id);
            CREATE TABLE IF NOT EXISTS product_inventory_policies (product_id TEXT PRIMARY KEY REFERENCES products(id), minimum_stock REAL NOT NULL DEFAULT 0, maximum_stock REAL NOT NULL DEFAULT 0, minimum_order_quantity REAL NOT NULL DEFAULT 0, order_multiple REAL NOT NULL DEFAULT 0, delivery_lead_time_days INTEGER NOT NULL DEFAULT 0, maximum_delivery_lead_time_days INTEGER NOT NULL DEFAULT 0, lot_tracking_type TEXT NOT NULL DEFAULT 'None', piece_count INTEGER NOT NULL DEFAULT 0, shipment_location_type TEXT NOT NULL DEFAULT '', updated_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS product_channel_mappings (id TEXT PRIMARY KEY, product_id TEXT NOT NULL REFERENCES products(id), channel_code TEXT NOT NULL, external_product_id TEXT NOT NULL DEFAULT '', external_variant_id TEXT NOT NULL DEFAULT '', external_sku TEXT NOT NULL DEFAULT '', is_published INTEGER NOT NULL DEFAULT 0, inventory_synced_at TEXT NULL, price_synced_at TEXT NULL, UNIQUE(product_id, channel_code));
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(12,'product-unit-supplier-inventory-policy-canonical',datetime('now'));
            CREATE TABLE IF NOT EXISTS product_attributes (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(company_id, code));
            CREATE TABLE IF NOT EXISTS variant_definitions (id TEXT PRIMARY KEY, company_id TEXT NOT NULL, definition_type TEXT NOT NULL DEFAULT 'Renk', code TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, UNIQUE(company_id, definition_type, code));
            CREATE INDEX IF NOT EXISTS IX_VariantDefinitions_Type ON variant_definitions(company_id, definition_type);
            INSERT OR IGNORE INTO schema_migrations(version,name,applied_at) VALUES(13,'product-attribute-and-variant-definition-masters',datetime('now'));
            PRAGMA user_version=13;
            """;
        command.ExecuteNonQuery();
        try { using var alter = connection.CreateCommand(); alter.CommandText = "ALTER TABLE inventory_transactions ADD COLUMN document_line_id TEXT NULL"; alter.ExecuteNonQuery(); } catch (SqliteException) { }
        foreach (var statement in new[] {
            "ALTER TABLE inventory_transactions ADD COLUMN base_quantity REAL NULL",
            "ALTER TABLE inventory_transactions ADD COLUMN unit_id TEXT NULL",
            "ALTER TABLE inventory_transactions ADD COLUMN lot_id TEXT NULL",
             "ALTER TABLE inventory_transactions ADD COLUMN serial_number TEXT NULL",
             "ALTER TABLE inventory_transactions ADD COLUMN location_id TEXT NULL",
            "ALTER TABLE inventory_transactions ADD COLUMN created_by TEXT NULL",
            "ALTER TABLE inventory_documents ADD COLUMN target_warehouse_id TEXT NULL",
            "ALTER TABLE inventory_documents ADD COLUMN account_id TEXT NULL",
            "ALTER TABLE inventory_documents ADD COLUMN source_document_id TEXT NULL",
            "ALTER TABLE inventory_documents ADD COLUMN approved_by TEXT NULL",
            "ALTER TABLE inventory_documents ADD COLUMN approved_at TEXT NULL",
            "ALTER TABLE inventory_documents ADD COLUMN cancelled_at TEXT NULL",
            "ALTER TABLE inventory_documents ADD COLUMN correlation_id TEXT NULL",
            "ALTER TABLE inventory_document_lines ADD COLUMN barcode_id TEXT NULL",
            "ALTER TABLE inventory_document_lines ADD COLUMN lot_id TEXT NULL",
            "ALTER TABLE inventory_document_lines ADD COLUMN serial_number TEXT NULL",
            "ALTER TABLE inventory_document_lines ADD COLUMN currency_code TEXT NOT NULL DEFAULT 'TRY'",
            "ALTER TABLE inventory_document_lines ADD COLUMN discount_rate REAL NOT NULL DEFAULT 0",
            "ALTER TABLE inventory_document_lines ADD COLUMN discount_amount REAL NOT NULL DEFAULT 0",
            "ALTER TABLE inventory_document_lines ADD COLUMN location_id TEXT NULL",
            "ALTER TABLE inventory_document_lines ADD COLUMN created_at TEXT NOT NULL DEFAULT ''"
        }) { try { using var alter = connection.CreateCommand(); alter.CommandText = statement; alter.ExecuteNonQuery(); } catch (SqliteException) { } }
        foreach (var statement in new[] {
            "ALTER TABLE warehouses ADD COLUMN address TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE warehouses ADD COLUMN responsible_user_id TEXT NULL",
            "ALTER TABLE warehouses ADD COLUMN phone TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE warehouses ADD COLUMN email TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE warehouses ADD COLUMN is_default_inbound INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE warehouses ADD COLUMN is_default_outbound INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE warehouses ADD COLUMN allow_negative_stock INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE warehouses ADD COLUMN requires_location INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE warehouses ADD COLUMN description TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE warehouses ADD COLUMN created_by TEXT NULL",
            "ALTER TABLE warehouses ADD COLUMN updated_by TEXT NULL",
            "ALTER TABLE warehouses ADD COLUMN legacy_source TEXT NULL",
            "ALTER TABLE warehouses ADD COLUMN legacy_id INTEGER NULL"
        }) { try { using var alter = connection.CreateCommand(); alter.CommandText = statement; alter.ExecuteNonQuery(); } catch (SqliteException) { } }
        foreach (var statement in new[] { "ALTER TABLE accounts ADD COLUMN tax_office TEXT NOT NULL DEFAULT ''", "ALTER TABLE accounts ADD COLUMN tax_number TEXT NOT NULL DEFAULT ''", "ALTER TABLE accounts ADD COLUMN identity_number TEXT NOT NULL DEFAULT ''", "ALTER TABLE accounts ADD COLUMN mobile_phone TEXT NOT NULL DEFAULT ''", "ALTER TABLE accounts ADD COLUMN credit_limit REAL NOT NULL DEFAULT 0", "ALTER TABLE accounts ADD COLUMN risk_limit REAL NOT NULL DEFAULT 0" }) { try { using var alter = connection.CreateCommand(); alter.CommandText = statement; alter.ExecuteNonQuery(); } catch (SqliteException) { } }
        foreach (var statement in new[] { "ALTER TABLE products ADD COLUMN purchase_vat_rate REAL NOT NULL DEFAULT 0", "ALTER TABLE products ADD COLUMN excise_rate REAL NOT NULL DEFAULT 0", "ALTER TABLE products ADD COLUMN minimum_stock REAL NOT NULL DEFAULT 0", "ALTER TABLE products ADD COLUMN maximum_stock REAL NOT NULL DEFAULT 0", "ALTER TABLE products ADD COLUMN minimum_order_quantity REAL NOT NULL DEFAULT 0", "ALTER TABLE products ADD COLUMN order_multiple REAL NOT NULL DEFAULT 0", "ALTER TABLE products ADD COLUMN is_sellable INTEGER NOT NULL DEFAULT 1", "ALTER TABLE products ADD COLUMN image_path TEXT NOT NULL DEFAULT ''", "ALTER TABLE account_addresses ADD COLUMN country TEXT NOT NULL DEFAULT 'Türkiye'", "ALTER TABLE account_addresses ADD COLUMN neighborhood TEXT NOT NULL DEFAULT ''", "ALTER TABLE account_addresses ADD COLUMN fax TEXT NOT NULL DEFAULT ''", "ALTER TABLE account_addresses ADD COLUMN website TEXT NOT NULL DEFAULT ''" }) { try { using var alter = connection.CreateCommand(); alter.CommandText = statement; alter.ExecuteNonQuery(); } catch (SqliteException) { } }
        foreach (var statement in new[] { "ALTER TABLE purchase_documents ADD COLUMN source_order_id TEXT NULL", "ALTER TABLE purchase_documents ADD COLUMN posted_at TEXT NULL", "ALTER TABLE purchase_documents ADD COLUMN posted_by TEXT NULL" }) { try { using var alter = connection.CreateCommand(); alter.CommandText = statement; alter.ExecuteNonQuery(); } catch (SqliteException) { } }
        foreach (var statement in new[] { "ALTER TABLE account_addresses ADD COLUMN contact_name TEXT NOT NULL DEFAULT ''", "ALTER TABLE account_addresses ADD COLUMN phone TEXT NOT NULL DEFAULT ''", "ALTER TABLE account_addresses ADD COLUMN mobile_phone TEXT NOT NULL DEFAULT ''", "ALTER TABLE account_addresses ADD COLUMN delivery_region_code TEXT NOT NULL DEFAULT ''", "ALTER TABLE account_addresses ADD COLUMN is_active INTEGER NOT NULL DEFAULT 1", "ALTER TABLE account_addresses ADD COLUMN created_at TEXT NOT NULL DEFAULT ''", "ALTER TABLE account_addresses ADD COLUMN updated_at TEXT NOT NULL DEFAULT ''" }) { try { using var alter = connection.CreateCommand(); alter.CommandText = statement; alter.ExecuteNonQuery(); } catch (SqliteException) { } }
        foreach (var statement in new[] {
            "ALTER TABLE accounts ADD COLUMN short_name TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE accounts ADD COLUMN default_currency_code TEXT NOT NULL DEFAULT 'TRY'",
            "ALTER TABLE accounts ADD COLUMN preferred_language_code TEXT NOT NULL DEFAULT 'tr'",
            "ALTER TABLE accounts ADD COLUMN account_group_id TEXT NULL",
            "ALTER TABLE accounts ADD COLUMN region_id TEXT NULL",
            "ALTER TABLE customer_profiles ADD COLUMN sales_representative_id TEXT NULL",
            "ALTER TABLE customer_profiles ADD COLUMN price_list_id TEXT NULL",
            "ALTER TABLE customer_profiles ADD COLUMN default_payment_method TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE customer_profiles ADD COLUMN is_order_blocked INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE customer_profiles ADD COLUMN is_active_buyer INTEGER NOT NULL DEFAULT 1",
            "ALTER TABLE customer_profiles ADD COLUMN credit_control_type TEXT NOT NULL DEFAULT 'None'",
            "ALTER TABLE supplier_profiles ADD COLUMN default_currency_code TEXT NOT NULL DEFAULT 'TRY'",
            "ALTER TABLE supplier_profiles ADD COLUMN lead_time_days INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE supplier_profiles ADD COLUMN is_active_supplier INTEGER NOT NULL DEFAULT 1",
            "ALTER TABLE supplier_profiles ADD COLUMN notes TEXT NOT NULL DEFAULT ''"
        }) { try { using var alter = connection.CreateCommand(); alter.CommandText = statement; alter.ExecuteNonQuery(); } catch (SqliteException) { } }
        foreach (var statement in new[] {
            "ALTER TABLE products ADD COLUMN parent_code TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE products ADD COLUMN is_definition_complete INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE products ADD COLUMN can_quote INTEGER NOT NULL DEFAULT 1",
            "ALTER TABLE products ADD COLUMN allow_free_issue INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE products ADD COLUMN is_bundle INTEGER NOT NULL DEFAULT 0",
            "ALTER TABLE products ADD COLUMN excise_unit_price REAL NOT NULL DEFAULT 0",
            "ALTER TABLE product_variants ADD COLUMN size_type TEXT NOT NULL DEFAULT ''"
        }) { try { using var alter = connection.CreateCommand(); alter.CommandText = statement; alter.ExecuteNonQuery(); } catch (SqliteException) { } }
        // Structured seller postal address for UBL generation (Phase 6 gap: companies.address was a single
        // free-text field, branches had no address at all - neither can back a UBL cac:PostalAddress).
        // Same shape as account_addresses so the two map to UBL PostalAddress the same way.
        foreach (var statement in new[] {
            "ALTER TABLE electronic_document_company_profiles ADD COLUMN address_line TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE electronic_document_company_profiles ADD COLUMN city TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE electronic_document_company_profiles ADD COLUMN district TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE electronic_document_company_profiles ADD COLUMN postal_code TEXT NOT NULL DEFAULT ''",
            "ALTER TABLE electronic_document_company_profiles ADD COLUMN country TEXT NOT NULL DEFAULT 'Türkiye'"
        }) { try { using var alter = connection.CreateCommand(); alter.CommandText = statement; alter.ExecuteNonQuery(); } catch (SqliteException) { } }
        using var seed = connection.CreateCommand();
        seed.CommandText = """
            INSERT OR IGNORE INTO companies(id,code,name,legal_name,is_active,created_at,updated_at) VALUES('00000000-0000-0000-0000-000000000001','R3','R3 Demo Firma','R3 Demo Firma',1,datetime('now'),datetime('now'));
            INSERT OR IGNORE INTO branches(id,company_id,code,name,is_active,created_at,updated_at) VALUES('00000000-0000-0000-0000-000000000011','00000000-0000-0000-0000-000000000001','MERKEZ','Merkez Şube',1,datetime('now'),datetime('now'));
            INSERT OR IGNORE INTO warehouses(id,company_id,branch_id,code,name,warehouse_type,is_active,created_at,updated_at) VALUES('00000000-0000-0000-0000-000000000111','00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000011','MERKEZ','Merkez Depo','Main',1,datetime('now'),datetime('now'));
            INSERT OR IGNORE INTO units(id,company_id,code,name,decimal_places,is_active) VALUES('00000000-0000-0000-0000-000000001111','00000000-0000-0000-0000-000000000001','ADET','Adet',0,1);
            INSERT OR IGNORE INTO currencies(id,code,name,is_active) VALUES('00000000-0000-0000-0000-000000002001','TRY','Türk Lirası',1);
            INSERT OR IGNORE INTO currencies(id,code,name,is_active) VALUES('00000000-0000-0000-0000-000000002002','USD','ABD Doları',1);
            INSERT OR IGNORE INTO currencies(id,code,name,is_active) VALUES('00000000-0000-0000-0000-000000002003','EUR','Euro',1);
            INSERT OR IGNORE INTO currencies(id,code,name,is_active) VALUES('00000000-0000-0000-0000-000000002004','GBP','İngiliz Sterlini',1);
            """;
        seed.ExecuteNonQuery();
        EnsureDefaultUser(connection);
        EnsureDefaultRoles(connection);
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

    public UserRole GetUserRole(string username)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.code, r.name
            FROM users u
            JOIN user_roles ur ON ur.user_id=u.id
            JOIN roles r ON r.id=ur.role_id AND r.is_active=1
            WHERE u.username=$username AND u.is_active=1
            ORDER BY CASE WHEN r.code='ADMIN' THEN 0 WHEN r.code='CASHIER' THEN 1 ELSE 2 END, r.name
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$username", username.Trim());
        using var reader = command.ExecuteReader();
        if (reader.Read()) return new UserRole(reader.GetString(0), reader.GetString(1));
        return string.Equals(username.Trim(), "admin", StringComparison.OrdinalIgnoreCase)
            ? new UserRole("ADMIN", "Yönetici")
            : new UserRole("USER", "Standart Kullanıcı");
    }
    private static void EnsureDefaultUser(SqliteConnection connection)
    {
        using var check = connection.CreateCommand(); check.CommandText = "SELECT COUNT(1) FROM users";
        if (Convert.ToInt32(check.ExecuteScalar()) > 0) return;
        using var insert = connection.CreateCommand(); insert.CommandText = "INSERT INTO users(id,username,display_name,password_hash,is_active,created_at,updated_at) VALUES($id,$username,$name,$hash,1,$now,$now)";
        var now = DateTime.UtcNow.ToString("O"); insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); insert.Parameters.AddWithValue("$username", "admin"); insert.Parameters.AddWithValue("$name", "R3 Yönetici"); insert.Parameters.AddWithValue("$hash", HashPassword("R3Admin2026!")); insert.Parameters.AddWithValue("$now", now); insert.ExecuteNonQuery();
    }

    private static void EnsureDefaultRoles(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO roles(id,code,name,is_active) VALUES
              ('00000000-0000-0000-0000-000000009001','ADMIN','Yönetici',1),
              ('00000000-0000-0000-0000-000000009002','CASHIER','Kasa Kullanıcısı',1),
              ('00000000-0000-0000-0000-000000009003','USER','Standart Kullanıcı',1);
            INSERT OR IGNORE INTO user_roles(user_id,role_id)
              SELECT u.id,'00000000-0000-0000-0000-000000009001' FROM users u WHERE u.username='admin';
            """;
        command.ExecuteNonQuery();
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
    // Single place every Local*Service opens a connection from, so the connection string and every
    // per-connection PRAGMA (synchronous is NOT persisted in the database file the way journal_mode
    // is - it must be reissued on every new connection) are applied consistently everywhere instead of
    // being copy-pasted ~15 times with drift (some call sites previously omitted Default Timeout,
    // silently getting Microsoft.Data.Sqlite's 60s default instead of the 5s used everywhere else).
    // synchronous=NORMAL is WAL's documented pairing (still crash-safe against corruption; the only
    // risk is losing the last few not-yet-checkpointed commits on true power loss, not database
    // corruption) - measured ~6.7x faster per committed transaction than the FULL default under R3's
    // actual write shape (many short, separately-committed transactions), see
    // docs/architecture/DATABASE-ENGINE.md.
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, ForeignKeys = true, DefaultTimeout = 5 }.ToString());
        connection.Open();
        using (var pragma = connection.CreateCommand()) { pragma.CommandText = "PRAGMA synchronous=NORMAL"; pragma.ExecuteNonQuery(); }
        return connection;
    }
    private SqliteConnection Open() => OpenConnection();
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
        LoadSafely(result, reader);
        return result;
    }

    // DataTable.Load(reader) infers each column's AllowDBNull/unique constraints from the source
    // table's schema (via GetSchemaTable()), not from the query's actual result shape. For a LEFT
    // JOIN against a table that declares a column NOT NULL (e.g. account_tax_profiles.person_type),
    // a row with no match on the joined side is legitimately NULL here, but Load() still built that
    // DataColumn as non-nullable - so committing the row threw "ConstraintException: Failed to
    // enable constraints" and crashed the whole app (reached an unhandled-exception path in several
    // callers, e.g. opening an existing account that has no tax/e-invoice/customer/supplier profile
    // row yet). Building columns from the reader's runtime field types instead of its schema table
    // sidesteps that entirely - every DataColumn here defaults to nullable, matching what LEFT JOIN
    // queries actually produce.
    // Several queries also select same-named columns from different joined tables without aliasing
    // (e.g. LocalBarcodePrintService selects both p.name and u.name as bare "name") and read the
    // result back by ordinal, never by name - DataTable.Load(reader) silently renamed the
    // duplicates ("name" -> "name1"), but DataColumnCollection.Add throws DuplicateNameException on
    // a second unqualified "Add(\"name\", ...)". Mirror Load's own renaming so both duplicate
    // columns and NOT-NULL-from-LEFT-JOIN are safe.
    // Public/static so every other Local*Service that builds its own DataTable from a raw
    // IDataReader (async methods that can't go through Query above) shares this instead of
    // re-hitting the same crash via System.Data.DataTable.Load(reader).
    public static void LoadSafely(DataTable table, IDataReader reader)
    {
        var usedNames = new Dictionary<string, int>();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            if (usedNames.TryGetValue(name, out var count)) { usedNames[name] = count + 1; name += count; }
            else usedNames[name] = 1;
            table.Columns.Add(name, reader.GetFieldType(i));
        }
        while (reader.Read())
        {
            var row = table.NewRow();
            for (var i = 0; i < reader.FieldCount; i++) row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            table.Rows.Add(row);
        }
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
