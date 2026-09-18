# Inventory Engine v1

`inventory_transactions` is the immutable business ledger. `inventory_balances` is a derived projection used for fast reads; no UI writes balance quantities directly.

The active transaction types are `OpeningBalance`, `ManualIn`, `ManualOut`, `TransferOut`, `TransferIn`, `CountIncrease`, and `CountDecrease`. Purchase/sale types are reserved for future document modules. Quantity is always positive; direction is carried by the transaction type. Quantities are normalized to the product base unit before posting.

Every post runs in one SQLite transaction: validate company/product/warehouse, check inactive/service products and negative-stock policy, insert the ledger row, update the balance projection, and commit. Transfer writes paired out/in rows with one correlation id atomically. Count writes only the difference. Historical ledger rows are never edited or deleted.

`LocalInventoryService.RebuildInventoryBalancesAsync` can rebuild projections from the ledger. SQLite uses transaction locking for local single-machine concurrency; PostgreSQL can later replace this with row-level locking/atomic updates without changing the service contract.

Barcode quantity factors (for example 2 cases × 12 base units) should be converted before constructing `InventoryPost`. ASB cutover can import a dated warehouse/product quantity as `OpeningBalance`; historical movement migration remains a separate strategy.
