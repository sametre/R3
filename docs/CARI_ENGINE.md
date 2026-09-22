# R3 Cari Motoru

Bu belge R3 cari hesabının gerçek çalışma sözleşmesidir. Legacy ASB tarafında tek bir tabloya sıkıştırılmaz; `CARIKART` ortak kimlik, `CARIKARTDTY` iletişim/vergi detayları, `MUSTERI` müşteri rolü, `TEDARIKCI` tedarikçi rolü ve alt kayıtlar canonical R3 modeline ayrılır.

## 1. Ana kayıt ve alanlar

`accounts` cari ana kaydıdır. Firma kapsamında benzersiz `code`, görünen `name`, `account_type`, aktiflik, vergi dairesi/no, TCKN, telefonlar, e-posta, varsayılan para birimi, dil, grup, bölge ve kredi/risk limitlerini tutar. Aynı hesap hem müşteri hem tedarikçi olabilir; paralel ikinci bir cari açılmaz.

`account_tax_profiles` tüzel/gerçek kişi tipi, ticari unvan, ticaret sicil no ve ülke kodunu tutar. `account_einvoice_profiles` e-Fatura/e-İrsaliye alias, senaryo ve yazdırma tercihlerini tutar.

## 2. Rol profilleri

`customer_profiles`: müşteri grubu, müşteri bölgesi, satış temsilcisi, fiyat listesi, vade günü, iskonto, ek/bloke kredi, kredi kontrolü, varsayılan ödeme yöntemi, sipariş blokesi, aktif alıcı, başlangıç tarihi ve KVKK onayı.

`supplier_profiles`: tedarikçi grubu/bölgesi, alış para birimi, vade, termin günü, aktif tedarikçi ve notlar.

Profil satırı yalnızca ilgili rol varsa yazılır. Rol sonradan kaldırılırsa geçmiş profil fiziksel olarak silinmez; tekrar açıldığında geri kullanılabilir.

## 3. Alt kayıtlar

- `account_addresses`: merkez/fatura/muhasebe/sevk/şube adresleri; adres tipi başına tek varsayılan aktif adres.
- `account_contacts`: yetkili, görev, departman, iletişim bilgileri; cari başına tek aktif ana yetkili.
- `account_banks`: MDF `CARIBANKA` karşılığı; banka, şube, IBAN, hesap no, para birimi ve varsayılan hesap.
- `account_notes`: operasyonel not ve sabitleme bilgileri.

## 4. Finans motoru

`account_transactions` değişmez gerçek hareket kaynağıdır. `account_balances` bu kaynaktan türetilen hızlı okuma projeksiyonudur. Bakiye formülü `Borç - Alacak`tır. Satış faturası post edildiğinde müşteri hesabına borç, tahsilat/mahsup işleminde alacak; alış faturası ve tedarikçi ödeme akışları ilgili belge kurallarına göre hareket üretir. Cari ekranı bakiyeyi elle değiştirmez; manuel hareket yalnızca yetkili finans akışından geçer.

## 5. Ekran sözleşmesi

Cari Kartı ekranı şu sekmeleri içerir: Genel, Adresler & Yetkililer, Banka Hesapları, Finans, Satış / Alış, E-Belge, Hareketler, Belgeler, Notlar ve Geçmiş.

Yeni kayıtta önce Genel alanları zorunludur. Kaydetmeden adres, yetkili, banka, belge ve hareket sekmeleri aktif olmaz. Müşteri olmayan kayıtta müşteri alanları; tedarikçi olmayan kayıtta tedarikçi alanları görünmez.

## 6. Doğrulama ve iş kuralları

- Cari kodu firma içinde benzersizdir ve kaydetmede büyük harf karşılaştırması kullanılır.
- Tüzel kişide vergi no girilmişse 10 hane; gerçek kişide TCKN girilmişse 11 hane beklenir.
- Vade, iskonto, limit ve termin negatif olamaz; kredi kontrol tipi `None`, `Warning` veya `Block` değerlerinden biridir.
- Satış faturası yalnızca müşteri veya çift rollü cari kabul eder; satınalma yalnızca tedarikçi veya çift rollü cari kabul eder.
- Banka IBAN girilmişse Türkiye IBAN biçimi `TR` + 24 rakam olarak doğrulanır. Aynı cari içindeki varsayılan banka hesabı tekilleştirilir.
- Adres ve yetkili pasife alınır; tarihçe ve finans hareketleri silinmez.
- Aktif olmayan cari yeni belge seçimlerinde listelenmez, ancak geçmiş belgelerde görünmeye devam eder.

## 7. MDF kapsamı ve sınır

MDF üzerinde SQL Server çalışma zamanı bulunmadığı için ham veri erişimi statik katalog/string analizi ve mevcut şema dokümanlarıyla doğrulanmıştır. Doğrulanmayan legacy kodlar R3 alanı olarak tahmin edilmez; anlamı veri örneklemesiyle kesinleştiğinde ayrı migration mapping eklenir.
