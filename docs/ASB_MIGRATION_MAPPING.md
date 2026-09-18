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

Source identifiers use `ASB:<database-name>` (for example `ASB:ASBDB_ERKUR01`). Company mapping is configured in `migration.settings.json`; database names are never assumed to equal company identity.
