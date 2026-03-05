from decimal import Decimal
from io import BytesIO
import re

from fastapi import FastAPI, File, HTTPException, UploadFile
from pydantic import BaseModel
from pypdf import PdfReader

app = FastAPI(title="R3 AI Document Engine", version="0.1.0")


class InvoiceCandidate(BaseModel):
    invoice_number: str | None = None
    dispatch_number: str | None = None
    tax_number: str | None = None
    invoice_date: str | None = None
    grand_total: Decimal | None = None
    currency: str = "TRY"
    confidence: float
    warnings: list[str]
    raw_text: str


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ready", "engine": "r3-python-document"}


@app.post("/v1/invoices/extract", response_model=InvoiceCandidate)
async def extract_invoice(file: UploadFile = File(...)) -> InvoiceCandidate:
    if file.content_type != "application/pdf":
        raise HTTPException(415, "Yalnızca PDF faturaları desteklenir.")
    payload = await file.read()
    if len(payload) > 20 * 1024 * 1024:
        raise HTTPException(413, "PDF en fazla 20 MB olabilir.")
    try:
        text = "\n".join(page.extract_text() or "" for page in PdfReader(BytesIO(payload)).pages)
    except Exception as exc:
        raise HTTPException(422, "PDF metni okunamadı.") from exc

    invoice_no = _first(text, r"(?:Fatura\s*(?:No|Numarası)|Belge\s*No)\s*[:#]?\s*([A-Z0-9\-\/]+)")
    dispatch_no = _first(text, r"(?:İrsaliye\s*(?:No|Numarası))\s*[:#]?\s*([A-Z0-9\-\/]+)")
    tax_no = _first(text, r"(?:VKN|TCKN|Vergi\s*No)\s*[:#]?\s*(\d{10,11})")
    date = _first(text, r"(?:Fatura\s*Tarihi|Tarih)\s*[:#]?\s*(\d{2}[./-]\d{2}[./-]\d{4})")
    total_text = _first(text, r"(?:Genel\s*Toplam|Ödenecek\s*Tutar)\s*[:#]?\s*([\d.,]+)")
    total = _decimal(total_text)
    found = sum(value is not None for value in (invoice_no, tax_no, date, total))
    warnings = []
    if found < 4:
        warnings.append("Bazı zorunlu alanlar güvenilir biçimde okunamadı.")
    warnings.append("Bu çıktı taslaktır; ERP kaydı öncesinde kullanıcı onayı zorunludur.")
    return InvoiceCandidate(
        invoice_number=invoice_no, dispatch_number=dispatch_no, tax_number=tax_no,
        invoice_date=date, grand_total=total, confidence=round(found / 4, 2),
        warnings=warnings, raw_text=text[:12000],
    )


def _first(text: str, pattern: str) -> str | None:
    match = re.search(pattern, text, re.IGNORECASE)
    return match.group(1).strip() if match else None


def _decimal(value: str | None) -> Decimal | None:
    if not value:
        return None
    normalized = value.replace(".", "").replace(",", ".")
    try:
        return Decimal(normalized)
    except Exception:
        return None
