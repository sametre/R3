# R3 Organization API

Base URL development: `http://localhost:5189/` (Desktop reads `R3_SERVER_URL`). All list responses use `{ items, page, pageSize, totalCount, totalPages }`; `pageSize` defaults to 50 and is limited to 500.

## Endpoints

Companies: `GET/POST /api/v1/companies`, `GET/PUT /api/v1/companies/{id}`, `POST /{id}/activate`, `POST /{id}/deactivate`.

Branches: `GET/POST /api/v1/branches`, `GET/PUT /api/v1/branches/{id}`, activate/deactivate endpoints. Filters: `companyId`, `search`, `isActive`.

Warehouses: `GET/POST /api/v1/warehouses`, `GET/PUT /api/v1/warehouses/{id}`, activate/deactivate endpoints. Filters: `companyId`, `branchId`, `search`, `isActive`.

Lookups: `GET /api/v1/lookups/companies` and `GET /api/v1/lookups/branches?companyId={id}`.

Example company request:

```json
{ "code": "ERLER", "name": "Erler AVM", "legalName": "Erler AVM Tic. Ltd. Şti.", "taxOffice": "Kadıköy", "taxNumber": "1234567890", "phone": "0212", "email": "info@example.com", "address": "İstanbul" }
```

Errors are returned as `ApiError`: `code`, `message`, `fieldErrors`, `traceId`. Validation is `400`, missing records are `404`, business conflicts such as duplicate normalized codes are `409`, and unexpected errors are `500` without stack traces.
