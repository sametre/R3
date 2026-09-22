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
2. **Sınıflandırma** — brand and category (both already FK'd to real master tables). Stock
   group/class, sales class, attribute group, origin country and department are intentionally **not**
   modeled yet: their ASB source (`STKGRPREF` and similar numeric references) hasn't been sampled against
   a real master table, and guessing the mapping would produce a canonical model that has to be redone.
   Flagged as `Needs Domain Discovery` in the mapping doc.
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

`ProductBarcode.UnitId` still points at `units` directly (unchanged, backward-compatible) rather than at
`ProductUnit` — §15's "single authoritative conversion model" migration is **not** done this sprint;
today `ProductBarcode.Quantity` and `ProductUnit.ConversionFactor` are two independent sources for
essentially the same concept. Cutting over `ProductBarcode` to resolve its factor through `ProductUnit`
is the next sprint's job (see "Known gaps" below) — doing it now would touch the sales/inventory posting
paths this sprint was explicitly told to preserve.

### `ProductSupplier` (`product_suppliers`)

Replaces `STOKTEDARIKCI`. Fields: `ProductId`, `SupplierAccountId`, `SupplierProductCode`, `IsActive`,
`LeadTimeDays`, `ExtraLeadTimeDays`, `Priority`, `MinimumOrderQuantity` (nullable), `LegacySource`/`LegacyId`.

Rules: `SupplierAccountId` required; lead times `>= 0`; `Priority >= 1`; a supplier cannot appear twice
for the same product (`UNIQUE(product_id, supplier_account_id)` plus a friendly pre-check).

### `ProductInventoryPolicy` (`product_inventory_policies`)

Company-level stock/order policy, one row per product, shaped so a future `WarehouseProductPolicy` can
override individual fields per warehouse without changing what this table means (§19 — not built this
sprint).

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

- Classification master data (stock group/class, sales class, origin country, department) — blocked on
  ASB master-table sampling (see `docs/ASB_LEGACY_ANALYSIS_STATUS.md`).
- `ProductBarcode` → `ProductUnit` single-conversion-model cutover (§15).
- Warehouse-level policy override (`WarehouseProductPolicy`, §19).
- Barcode type (`SBRKBARKODTIP`) is not yet a stored column.
- E-Ticaret channel sync engine (writer side of `ProductChannelMapping`).
- Production/"Gelişmiş" fields (`STKURDPMKOD`, `STKOZELURETIMTIPI`, `STKPARCATIP`) — not sample-verified.
- Column chooser / optional hidden columns on the product list (§30/§31) — not implemented.
- Product Copy (§29) — not implemented.
- Field-level audit diffs (§44 asks for "a reasonable structured change snapshot"; today's audit row
  records a flat key-field snapshot — code, name, active, sellable, min/max stock — not per-field
  old-value/new-value pairs).
