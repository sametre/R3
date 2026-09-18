# Account Ledger

`account_transactions` gerçek cari hareket kaynağıdır. `account_balances` hızlı okuma projeksiyonudur. Satış faturası Posted olduğunda `SalesInvoice` tipinde Debit hareketi oluşur ve bakiye `Debit - Credit` olarak artırılır.

Cari hesabın şirket kapsamı ve aktifliği post sırasında doğrulanır. Sadece `Customer` ve `CustomerAndSupplier` hesapları satış faturasında kullanılabilir. Rezervasyon ve tahsilat motoru bu sprintin dışındadır.
