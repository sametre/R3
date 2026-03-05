# R3 AI

- `python`: PDF faturadan belge alanlarını çıkaran FastAPI servisi.
- `scala`: müşteri segmenti, risk ve sonraki en iyi aksiyonu hesaplayan servis.

Python motoru yalnızca bir taslak aday üretir. Kullanıcı önizleme ve onay vermeden ERP faturası kaydedilmez.

```powershell
cd ai/python
python -m venv .venv
.\.venv\Scripts\pip install -r requirements.txt
.\.venv\Scripts\uvicorn app.main:app --port 7311
```

```powershell
cd ai/scala
sbt run
```
