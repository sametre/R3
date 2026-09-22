# Database Schema v1

EF Core `R3DbContext` creates the first PostgreSQL tables in schema `r3`:

`companies`, `branches`, `warehouses`, `brands`, `categories`, `units`, `products`, `product_variants`, `product_barcodes`, `accounts`, `account_addresses`, and `audit_logs`.

Companies own branches and warehouses. A branch belongs to one company and a warehouse belongs to one branch and company. Products belong to a company and may have many variants and barcodes. Accounts belong to a company and may have many addresses.

Unique indexes protect normalized company codes, company-scoped normalized branch and warehouse codes, product-scoped variant codes, and system-wide barcodes. Normalized codes are trimmed and upper-cased by the Organization service, so `ERLER` and `erler` collide deterministically. Product/account legacy lookups use `(legacy_source, legacy_id)` indexes. Decimal quantities and VAT/risk values are mapped with explicit precision. Account type is stored as a stable string enum. Organization create/update/activate/deactivate operations append `audit_logs` rows.

The first development migration should be generated with:

```powershell
dotnet ef migrations add InitialCanonicalModel --project src/R3.Infrastructure --startup-project src/R3.Server
dotnet ef database update --project src/R3.Infrastructure --startup-project src/R3.Server
```

No production database or password is embedded in the repository.

Inventory adds `inventory_transactions` (immutable ledger) and `inventory_balances` (derived projection). Ledger quantities are positive and `transaction_type` supplies direction. A composite warehouse/product/variant lookup index supports balance reads; the application uses null-safe variant matching because SQLite NULL unique semantics differ from PostgreSQL.

Product Card v2 (see `docs/PRODUCT_CARD_V2.md`) adds four canonical tables instead of widening `products`
further: `product_units` (multi-unit conversion, replaces ASB `STOKBIRIM`), `product_suppliers`
(per-product supplier relationships resolved through `accounts`, replaces ASB `STOKTEDARIKCI`),
`product_inventory_policies` (stock/order policy, one row per product; the four fields it shares with
`products` — minimum/maximum stock, minimum order quantity, order multiple — are written to both tables
in the same transaction rather than migrated in one step), and `product_channel_mappings` (e-commerce
channel mapping shape, no writer yet). `products` itself gained `parent_code`, `is_definition_complete`,
`can_quote`, `allow_free_issue`, `is_bundle` and `excise_unit_price`; `product_variants` gained
`size_type`. SQLite schema version 12.
