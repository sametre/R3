# ASB Migration Mapping

Legacy values are transformed into business terminology and retain `legacy_source` / `legacy_id` for idempotency.

| ASB source | R3 target | Transform | Status |
| --- | --- | --- | --- |
| `CRKOD` | `accounts.code` | trim + uppercase comparison | Mapped |
| `CRADI` | `accounts.name` | trim | Mapped |
| `barkod` | `product_barcodes.barcode` | separate entity, unique | Mapped |
| `BrandCode` | `brands.code` | company lookup | Partial |
| `BoyutKodu` | `product_variants.size_code` | variant mapping | Partial |
| `STKREF` | `products.legacy_id` | ASB source retained | Mapped |
| `STKRENK` | `product_variants.color_code` | trim | Partial |
| `STKBEDEN` | `product_variants.size_code` | trim | Partial |
| `SBRKBARKOD` | `product_barcodes.barcode` | leading zero preserved | Mapped |
| `SBRBRM` | `product_units.unit_code` | source unit lookup required | Partial |
| `SBRCARPAN` | `product_barcodes.quantity` | decimal factor, no guessing | Partial |
| `CRDVERD` | `accounts.tax_office` | verified detail field | Mapped |
| `CRDVERN` | `accounts.tax_number` | verified detail field | Mapped |
| `MUSKREDI` | `accounts.credit_limit` | customer profile source | Partial |
| `MUSVADEGUN` | `customer_profiles.payment_term_days` | customer profile source | Partial |
| `FYTFIYAT` | `product_prices` | pricing module | Future |

## Product Card v2 field mapping (§37)

Business meaning, not the legacy column name, is what reaches the UI. `docs/PRODUCT_CARD_V2.md` has the tab-by-tab field list this table backs.

| ASB source | R3 target | Transform | Status |
| --- | --- | --- | --- |
| `STOKKARTI.STKREF` | `products.legacy_id` | ASB source retained | Mapped |
| `STOKKARTI.STKKOD` | `products.code` | trim + uppercase comparison | Mapped |
| `STOKKARTI.STKADI` | `products.name` | trim | Mapped |
| `STOKKARTI.STKANAKOD` | `products.parent_code` | free-text reference; full product-family model (ParentProductId) is a future sprint | Partial |
| `STOKKARTI.STKTIP` | `products.product_type` | Stock/Service mapped; Set/Hammadde/Mamül not modeled yet | Partial |
| `STOKKARTI.STKSTS` | `products.is_active` | boolean | Mapped |
| `STOKKARTI.STKTANIMOK` | `products.is_definition_complete` | boolean | Mapped |
| `STOKKARTI.STKRENK` | `product_variants.color_code` | trim | Partial |
| `STOKKARTI.STKBEDEN` | `product_variants.size_code` | trim | Partial |
| `STOKKARTI.STKBEDENTIPI` | `product_variants.size_type` | trim | Partial |
| `STOKKARTI.STKBOYUTKOD` | `product_variants.size_code` | same field as STKBEDEN pending sample data to tell them apart | Needs Domain Discovery |
| `STOKKARTI.STKKDVORN0` / `STKKDVORN1` | `products.vat_rate` / `products.purchase_vat_rate` | sales vs. purchase VAT split | Mapped |
| `STOKKARTI.STKOTVORN0` / `STKOTVORN1` | `products.excise_rate` | rate-based ÖTV; source distinguishes sales/purchase, R3 currently keeps one rate | Partial |
| `STOKKARTI.STKOTVBRMFYT` | `products.excise_unit_price` | unit-price-based ÖTV | Mapped |
| `STOKKARTI.STKHKSATILAMAZ` | `products.is_sellable` | inverted to a single positive flag (`IsSellable=false`); no separate negative flag kept | Mapped |
| `STOKKARTI.STKFORTEKLIF` | `products.can_quote` | boolean | Mapped |
| `STOKKARTI.STKBDLSZGRS` | `products.allow_free_issue` | boolean | Mapped |
| `STOKKARTI.STKTAKIM` | `products.is_bundle` | boolean | Mapped |
| `STOKKARTI.STKMINM` | `product_inventory_policies.minimum_stock` (mirrored to `products.minimum_stock` for backward compatibility) | decimal | Mapped |
| `STOKKARTI.STKMAXM` | `product_inventory_policies.maximum_stock` (mirrored to `products.maximum_stock`) | decimal | Mapped |
| `STOKKARTI.STKMINSIPMIK` | `product_inventory_policies.minimum_order_quantity` (mirrored to `products.minimum_order_quantity`) | decimal | Mapped |
| `STOKKARTI.STKSIPKAT` | `product_inventory_policies.order_multiple` (mirrored to `products.order_multiple`) | decimal; 0 means unconstrained | Mapped |
| `STOKKARTI.STKSATTESLIMSURE` | `product_inventory_policies.delivery_lead_time_days` | integer days | Mapped |
| `STOKKARTI.STKSATTESLIMSUREMAX` | `product_inventory_policies.maximum_delivery_lead_time_days` | integer days | Mapped |
| `STOKKARTI.STKLOTTIP` | `product_inventory_policies.lot_tracking_type` | explicit map to None/Lot/Serial required; source codes not sampled | Needs Domain Discovery |
| `STOKKARTI.STKSEVKYERI` | `product_inventory_policies.shipment_location_type` | free text pending real code list | Partial |
| `STOKKARTI.STKPARCASAYISI` | `product_inventory_policies.piece_count` | integer | Mapped |
| `STOKKARTI.STKETICSAT` | `product_channel_mappings.is_published` (per-channel, once a channel row exists) | boolean | Partial |
| `STOKKARTI.EticStokId` | `product_channel_mappings.external_product_id` | channel integration not built | Future |
| `STOKKARTI.EnvanterUpdateZmn` | `product_channel_mappings.inventory_synced_at` | channel integration not built | Future |
| `STOKKARTI.STKGRPREF` | `products.product_group_id` | → `KODSTOKGRUP.GRPREF` (`GRPKOD`/`GRPADI`/`GRPGTIP` → `product_groups.code`/`name`/`customs_code`); 100% of products resolve | Mapped |
| `STOKKARTI.STKULKEREF` | `products.origin_country_id` | → `KODULKE.ULKREF` (`ULKKOD` ISO-2 / `ULKADI` → `countries`); ~48% filled | Mapped |
| `STOKKARTI.STKURUNOZGRPREF` | — | → `URUNOZELLIKGRUP`; only 151 of 35k products use it | Not modeled |
| `STOKKARTI.STKSTASINIF` / `STKSTSSINIF` | — | smallint 0/1 flags, meaning not verified | Needs Domain Discovery |
| `STOKSUBEMINMAX` (`SSMMSTKREF`, `SSMMSUBE`, `SSMMMINMIK`, `SSMMMAXMIK`) | `product_warehouse_policies` | per-branch in ASB, per-warehouse in R3; empty in the sample | Modeled, no rows to import |
| `STOKKARTI.STKURDPMKOD` / `STKOZELURETIMTIPI` / `STKPARCATIP` | production/"Gelişmiş" tab | meaning not sample-verified | Needs Domain Discovery |
| `STOKBARKOD.SBRKBARKOD` | `product_barcodes.barcode` | leading zero preserved | Mapped |
| `STOKBARKOD.SBRKBARKODTIP` | `product_barcodes` (barcode type) | not a stored column; all 8 sample rows are type 0 | Needs Domain Discovery |
| `STOKBARKOD.SBRKSCHREF` | `product_barcodes.variant_id` | variant link | Partial |
| `STOKBARKOD.SBRKSTKPARREF` | `product_barcodes.unit_id` | unit link | Partial |
| `STOKBIRIM.SBRBRM` | `product_units.unit_id` | source unit lookup required | Partial |
| `STOKBIRIM.SBRSIRA` | `product_units.sequence` | integer | Mapped |
| `STOKBIRIM.SBRCARPAN` | `product_units.conversion_factor` | decimal factor, base unit forced to 1, no guessing on non-base factors | Partial |
| `STOKTEDARIKCI.STTDCRREF` | `product_suppliers.supplier_account_id` | resolved through canonical `accounts` (AccountType=Supplier), not a parallel supplier master | Mapped |
| `STOKTEDARIKCI.STTDTEDSTKKOD` | `product_suppliers.supplier_product_code` | trim | Mapped |
| `STOKTEDARIKCI.STTDAKTIF` | `product_suppliers.is_active` | boolean | Mapped |
| `STOKTEDARIKCI.STTDGUN` | `product_suppliers.lead_time_days` | integer | Mapped |
| `STOKTEDARIKCI.STTDEKGUN` | `product_suppliers.extra_lead_time_days` | integer | Mapped |
| `STOKTEDARIKCI.STTSIRA` | `product_suppliers.priority` | integer | Mapped |

Source identifiers use `ASB:<database-name>` (for example `ASB:ASBDB_ERKUR01`). Company mapping is configured in `migration.settings.json`; database names are never assumed to equal company identity.
