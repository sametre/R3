package r3

import upickle.default.*

case class CustomerSignal(
  customerId: Long,
  invoiceCount: Int,
  totalRevenue: BigDecimal,
  overdueAmount: BigDecimal,
  daysSinceLastPurchase: Int
) derives ReadWriter

case class CustomerScore(
  segment: String,
  riskScore: Int,
  nextBestAction: String
) derives ReadWriter

object AnalyticsApi extends cask.MainRoutes:
  @cask.get("/health")
  def health() = ujson.Obj("status" -> "ready", "engine" -> "r3-scala-analytics")

  @cask.postJson("/v1/customers/score")
  def score(signal: CustomerSignal): CustomerScore =
    val risk =
      math.min(100, (if signal.overdueAmount > 0 then 45 else 0) +
        (if signal.daysSinceLastPurchase > 90 then 30 else 0) +
        (if signal.invoiceCount < 3 then 15 else 0))
    val segment =
      if signal.totalRevenue >= 250000 then "VIP"
      else if signal.totalRevenue >= 50000 then "Sadık"
      else "Gelişen"
    val action =
      if risk >= 60 then "Vadesi geçen bakiye için kontrollü iletişim kur."
      else if signal.daysSinceLastPurchase > 60 then "Müşteriye geri kazanım teklifi hazırla."
      else "İlişkiyi standart planla sürdür."
    CustomerScore(segment, risk, action)

  initialize()
