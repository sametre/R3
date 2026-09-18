# Product Module v1

Product is a company-scoped aggregate: `products` owns `product_variants` and `product_barcodes`. The Product Card has active General, Barcodes, and Variants tabs; Prices, Stock, and History remain explicitly future tabs.

Saving the card uses one SQLite transaction. The product row, child variants, child barcodes and audit row commit together; a duplicate barcode, invalid quantity, missing code/name/unit, or invalid VAT rolls back the entire aggregate.

Codes are trimmed and compared uppercase. Barcodes remain text, preserve leading zeroes, must be non-empty and unique, and only one active primary barcode is maintained per product. A barcode may optionally reference a variant and unit; when unit is omitted the product base unit is used by the application model.

The product list searches code, name and barcode, and shows active variant/barcode counts using a single aggregate query. Brand/category/unit lookup fields are company-scoped; the current card uses the active local demo company and first active unit while lookup dialogs are being expanded.

ASB import order is Brand → Category → Unit → Product → ProductVariant → ProductBarcode. Legacy fields remain internal and are not shown in the product card.
