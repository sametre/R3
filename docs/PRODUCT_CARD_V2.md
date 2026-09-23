# Product Card v2

Replaces the v1 fixed identity-card + short tab-strip layout (`docs/PRODUCT_MODULE.md`) with a full-height tab
strip under a compact identity header, and adds three canonical entities so ASB's `STOKKARTI` /
`STOKBIRIM` / `STOKTEDARIKCI` are migrated into business-meaning tables instead of being reconstructed
column-for-column. Legacy ASB column names are never surfaced as UI labels; see
`docs/ASB_MIGRATION_MAPPING.md` for the exact source-to-target mapping.

## Header

Persistent, non-tab strip above the tabs: code + name + active checkbox, and a static summary line
(primary barcode, base unit, current available stock summed across warehouses). The summary is computed
once when the card opens, not live-bound — it reflects the record as saved, not in-progress edits.

## Tabs

1. **Genel** — code, name, parent code (`STKANAKOD`, free text; full product-family modeling via a real
   `ParentProductId` is deferred), product type, base unit, "tanım tamamlandı" flag, product image.
2. **Sınıflandırma** — brand, category, **stock group** (`products.product_group_id` → `product_groups`,
   with the GTİP customs code) and **origin country** (`products.origin_country_id` → `countries`, ISO-2).
   Both were sample-verified against ASBDB_ERKUR02 on 2026-09-23 before being modeled: `STKGRPREF` →
   `KODSTOKGRUP` resolves for 100% of 35,095 products; `STKULKEREF` → `KODULKE` is filled on ~48%.
   Imported with `R3.AsbMigration --canonical-classifications` (idempotent; run after `--canonical-core`).
   Still **not** modeled: stock/sales class (`STKSTASINIF`/`STKSTSSINIF` are 0/1 flags of unverified
   meaning), attribute group (`STKURUNOZGRPREF` → `URUNOZELLIKGRUP`, used by only 151 products — a spec
   template concept rather than a classification) and department.
3. **Barkod & Birimler** — two inner tabs:
   - **Barkodlar**: existing barcode grid (barcode, unit, variant, primary flag, active).
   - **Birimler**: new `ProductUnit` grid (unit, conversion factor, base/sales/purchase flags, active).
4. **Varyant & Özellikler** — variant grid (code, name, color, size, size type, model).
5. **Ticari Bilgiler** — sales/purchase VAT, excise rate, excise unit price, and four flags: satışa açık
   (`IsSellable`/`CanSell` — the single canonical flag; see below), teklifte kullanılabilir, bedelsiz
   girişe izin, takım/set ürün.
6. **Stok & Sipariş** — minimum/maximum stock, minimum order quantity, order multiple, delivery lead
   time, maximum delivery lead time, lot tracking type, piece count, shipment location.
7. **Tedarikçiler** — new `ProductSupplier` grid, resolved through the canonical `accounts` aggregate
   (`AccountType IN ('Supplier','CustomerAndSupplier')`), not a parallel supplier master.
8. **E-Ticaret** — read-only placeholder over `product_channel_mappings`. No sync engine exists yet.
9. **Stok Durumu** — per-warehouse balance (on hand / reserved / available / status), reusing
   `inventory_balances`. Only populated once the product has been saved.
10. **Hareketler** — read-only ledger from `inventory_transactions`, filtered to the product. The
    "Cari" column from the original spec is intentionally omitted: `inventory_transactions` has no
    account reference today, and guessing one would be wrong more often than it's right.
11. **Geçmiş** — `audit_logs` filtered to `entity_type='Product'`.

Keyboard/dirty-state: every new field (parent code, commercial flags, stock/order policy fields, units,
suppliers) is included in the dialog's dirty-state snapshot, so navigating away without saving still
prompts a confirmation.

## Canonical entities

### `ProductUnit` (`product_units`)

Replaces `STOKBIRIM`. Fields: `ProductId`, `UnitId`, `Sequence`, `ConversionFactor`, `IsBaseUnit`,
`IsSalesUnit`, `IsPurchaseUnit`, `IsActive`, `LegacySource`/`LegacyId`.

Rules (enforced in `LocalProductService.ValidateUnits`, covered by
`StoreDatabaseTests.ProductUnitsEnforceSingleBaseUnitAndPositiveFactor`):

- `ConversionFactor > 0` for every unit row (no exceptions — unlike the product-level order multiple,
  there is no "0 means unconstrained" sentinel here).
- At most one `IsBaseUnit = true` row per product.
- A base unit's `ConversionFactor` must be exactly `1`.
- A unit cannot appear twice for the same product.

§15 cutover (done 2026-09-23): for a barcode of a **non-base** unit (koli, paket...), `ProductUnit.ConversionFactor`
is the single authority. `LocalBarcodeResolver` reads the factor from `product_units`; `LocalProductService.Save`
creates a missing ProductUnit from the barcode's factor and otherwise overwrites the barcode's factor with the
unit's; a one-time alignment runs when a store is upgraded to schema v14. Deliberate exception: **base-unit and
unit-less barcodes keep their stored quantity** - legacy data used "Adet × 12" for pack barcodes and silently
turning that into 1 would change what a scan posts. A non-base barcode with no ProductUnit row at all (e.g.
imported after the upgrade) also falls back to its own quantity. Posting paths are unchanged: they already
take the factor from the resolver.

### `ProductSupplier` (`product_suppliers`)

Replaces `STOKTEDARIKCI`. Fields: `ProductId`, `SupplierAccountId`, `SupplierProductCode`, `IsActive`,
`LeadTimeDays`, `ExtraLeadTimeDays`, `Priority`, `MinimumOrderQuantity` (nullable), `LegacySource`/`LegacyId`.

Rules: `SupplierAccountId` required; lead times `>= 0`; `Priority >= 1`; a supplier cannot appear twice
for the same product (`UNIQUE(product_id, supplier_account_id)` plus a friendly pre-check).

### `ProductInventoryPolicy` (`product_inventory_policies`)

Company-level stock/order policy, one row per product. Per-warehouse min/max overrides live in
`product_warehouse_policies` (product, warehouse, min, max — the R3 equivalent of ASB `STOKSUBEMINMAX`,
which is per-branch there), edited on the Stok & Sipariş tab. Stok Durumu shows the effective value per
warehouse row (`COALESCE(override, product)`), which one applied (`PolitikaKaynagi`) and a Min Altı /
Max Üstü warning. `ProductAggregateEdit.ProductGroupId` / `OriginCountryId` / `WarehousePolicies` are
nullable on purpose: `null` = leave stored values alone (older callers), `""` / `[]` = clear.

**Deliberate simplification**: `MinimumStock`, `MaximumStock`, `MinimumOrderQuantity` and `OrderMultiple`
already lived on `products` before this sprint and are read by existing/tested code paths. Rather than
cut those reads over immediately (risking the "don't break Sales/Inventory" constraint), `LocalProductService.Save`
writes both: `products.*` stays authoritative for anything that already reads it, and
`product_inventory_policies` is written from the same values in the same transaction, so the two never
drift. The genuinely new fields — `DeliveryLeadTimeDays`, `MaximumDeliveryLeadTimeDays`,
`LotTrackingType`, `PieceCount`, `ShipmentLocationType` — live **only** in the policy table; they were
never on `products`. Moving the four legacy fields' authoritative read path onto the policy table (and
retiring the `products` columns) is future cleanup, not this sprint's scope.

`OrderMultiple` validation is `>= 0` (0 = unconstrained), matching the existing UI copy and existing
tests, **not** the stricter `> 0` a literal reading of §45 would imply — a product with no case-pack
constraint is a normal state, and `ProductAggregateEdit`'s own default is `0`. `ConversionFactor` on
`ProductUnit`, by contrast, has no such sentinel and is validated `> 0` strictly, per §46's explicit test
list.

`LotTrackingType` is a string (`None`/`Lot`/`Serial`) rather than a hard enum tied to ASB's `STKLOTTIP`
codes — those codes were never sampled, so the mapping stays an explicit migration-layer decision instead
of a guess (§23).

### `ProductChannelMapping` (`product_channel_mappings`)

Shape-only placeholder for §21: `ProductId`, `ChannelCode`, `ExternalProductId`, `ExternalVariantId`,
`ExternalSku`, `IsPublished`, `InventorySyncedAt`, `PriceSyncedAt`. No writer exists yet; the E-Ticaret tab
reads it read-only.

## The "IsSellable" / "CanSell" decision (§17)

ASB's `STKHKSATILAMAZ` is a negative flag ("cannot sell"). R3 keeps a single positive canonical flag,
`Product.IsSellable`, which already existed before this sprint. The migration transform inverts the
source value; no second flag was added.

## Transactional save (§42/§43)

`LocalProductService.Save` persists `Product`, `ProductVariant`, `ProductBarcode`, `ProductUnit`,
`ProductSupplier` and `ProductInventoryPolicy` in one SQLite transaction; any validation failure or
duplicate (barcode, unit, supplier) rolls back the entire aggregate — see
`StoreDatabaseTests.ProductAggregateSaveRollsBackUnitsAndSuppliersOnChildFailure`. `InventoryBalance` and
`InventoryTransaction` are never touched by this save — stock only changes through `LocalInventoryService`
posting, and the product form cannot edit current stock directly (§43).

## Known gaps / next sprint

- Stock/sales class, attribute group and department classification — see Sınıflandırma above for why.
- Base-unit "Adet × N" pack barcodes are still their own conversion source (see the §15 note above); converting
  them to proper koli units needs a per-product decision, not a blanket migration.
- Only min/max are overridable per warehouse; the other policy fields (lead times, lot tracking...) are
  still company-level. "Minimum altı" on the product list still compares total stock to the product-level
  minimum; the per-warehouse view is Stok Durumu.
- Barcode type (`SBRKBARKODTIP`) is not a stored column: all 8 `STOKBARKOD` rows in the sample have type 0,
  so there is nothing to verify a mapping against.
- E-Ticaret channel sync engine (writer side of `ProductChannelMapping`).
- Production/"Gelişmiş" fields (`STKURDPMKOD`, `STKOZELURETIMTIPI`, `STKPARCATIP`) — not sample-verified.
- Field-level audit diffs (§44 asks for "a reasonable structured change snapshot"; today's audit row
  records a flat key-field snapshot — code, name, active, sellable, min/max stock — not per-field
  old-value/new-value pairs).
