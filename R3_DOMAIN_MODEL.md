# R3 ERP Domain Model v1

R3 menüleri ASB şema adlarını doğrudan kopyalamaz; iş anlamlarını canonical domain varlıklarına bağlar.

| Menü alanı | Canonical varlıklar |
| --- | --- |
| Organizasyon | `companies`, `branches`, `warehouses` |
| Stok / Varyant / Barkod | `products`, `product_variants`, `variant_attributes`, `product_barcodes`, `units` |
| Fiyat | `price_lists`, `product_prices`, `price_rules` |
| Lojistik | `inventory_transactions`, `inventory_balances` |
| Cari | `accounts`, `account_addresses`, `account_transactions` |
| Satış | `sales_documents`, `sales_document_lines`, `document_relations` |
| Satınalma | `purchase_documents`, `purchase_document_lines` |
| Finans | `cash_accounts`, `bank_accounts`, `financial_transactions` |
| Ödemeler | `payments`, `payment_allocations` |
| Çek / Senet | `cheques`, `promissory_notes` |
| E-Fatura | `einvoice_documents`, `einvoice_events`, `account_einvoice_profiles` |
| E-Ticaret | `integration_channels`, `integration_product_mappings`, `integration_inventory` |
| İK / Bordro | `employees`, `departments`, `payroll_periods`, `payroll_runs`, `payroll_taxes` |
| Güvenlik / Sistem | `users`, `roles`, `permissions`, `settings`, `audit_logs`, `background_jobs` |

Bu sprintte gerçek entity ve EF persistence modeli oluşturuldu: `OrganizationCompany`, `Branch`, `Warehouse`, `Brand`, `Category`, `Unit`, `Product`, `ProductVariant`, `ProductBarcode`, `Account`, `AccountAddress` ve `AuditLog`. Stok ve cari bakiyeler manuel kolonlardan değil hareketlerden türetilir. Migration sırasında önemli varlıklarda `legacy_source` ve `legacy_id` alanları kullanılarak idempotent aktarım sağlanır. PostgreSQL erişimi yalnızca Server/Infrastructure katmanındadır; SQLite 3 yalnızca mevcut offline Mağaza prototipinin geçici veri kaynağıdır.

Tamamlanan çekirdek: canonical entity sınıfları, UUID kimlikler, audit zamanları, snake_case PostgreSQL mapping, ilişkiler, normalized scoped unique indexler, Organization DTO/paging sözleşmeleri, Organization CRUD/activate/deactivate REST servisleri, audit log üretimi, server `/health` endpoint'i ve Serilog server rolling log. Sıradaki adım, mevcut Organization REST sözleşmesini gerçek firma/şube/depo WPF formlarına bağlamaktır; Catalog/Product zinciri bundan sonra başlayacaktır.
