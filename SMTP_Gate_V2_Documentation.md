# smtp-gate-sender – dokumentacja techniczna (homelab)

## 1. Cel
`smtp-gate-sender` to lokalna bramka **HTTP → SMTP**, przygotowana pod demo w homelabie.

System:
- przyjmuje żądania HTTP (`/api/email` oraz `/email`)
- normalizuje i waliduje payload
- kolejkowuje wiadomości na dysku (spool)
- wysyła e-maile asynchronicznie przez SMTP (worker)
- obsługuje idempotencję (RequestId), retry/backoff, HTML i plain text
- czyści spool i logi (retencja)

W trybie homelab domyślnym SMTP wskazuje na **MailHog** (przechwytuje maile i pokazuje je w web UI), ale mechanika wysyłki SMTP pozostaje ta sama.

---

## 2. Architektura

Klient HTTP → API → Spool (filesystem) → Worker → SMTP

Komponenty:
- ASP.NET Minimal API
- `SpoolStore` (FS queue + idempotencja)
- `SpoolWorker` (retry + backoff)
- `SmtpSender` (SMTP client)
- `RetentionWorker` (sprzątanie logów/spool)

---

## 3. Endpointy

### GET /health
Status + liczniki z kolejki.

### GET /stats
Szczegółowe statystyki spool.

### POST /api/email (legacy-compatible)
Payload kompatybilny wstecznie.

Przykład (plain text):
```json
{
  "Subject": "Test legacy",
  "Body": "Plain text",
  "ToEmails": ["demo@example.local"],
  "RequestId": "legacy-001",
  "Client": "demo-client"
}
```

### POST /email (alias)
To samo co `/api/email`, tylko bez prefiksu.

Przykład (HTML):
```json
{
  "From": "demo@homelab.local",
  "Subject": "HTML test",
  "BodyHtml": "<h1>Header</h1><p>Treść</p>",
  "ToEmails": ["demo@example.local"],
  "RequestId": "html-002",
  "Client": "demo-client"
}
```

Odpowiedzi:
- `202 Accepted` – wrzucone do kolejki
- `200 OK` + `duplicate=true` – wykryta duplikacja w oknie idempotencji
- `400 BadRequest` – walidacja payload
- `429` – rate limit per IP

---

## 4. Konfiguracja (homelab)

Konfiguracja jest w `appsettings.json`, a wartości można nadpisać zmiennymi środowiskowymi (Docker).

### Port API
- `ListenUrls` (np. `http://0.0.0.0:8885`)
- alternatywnie `ASPNETCORE_URLS`

### SMTP
Sekcja `Smtp`:
- `Host` / `Port`
- `UseSsl`
- `User` / `Pass` (opcjonalne)
- `From`

Domyślnie (demo): `mailhog:1025`.

### Spool
Sekcja `Spool`:
- `Root` – domyślnie `./data/spool` (w kontenerze mapowane na wolumin)
- `IdempotencyHours`

### Retencja
Sekcja `Retention`:
- czyszczenie logów i katalogów spool wg dni

---

## 5. Uruchomienie w Docker (rekomendowane)

W repo/katalogu `smtp-gate-sender`:
- `docker compose up -d --build`

Domyślne porty hosta:
- API/Swagger: `http://localhost:8885/swagger`
- MailHog UI: `http://localhost:8884`
- SMTP: `localhost:8883` (jeżeli chcesz testować z zewnątrz)

---

## 6. Demo bez curl
Najprościej używać:
- **Swagger UI**: `/swagger`
- oraz podglądu maili w MailHog UI.

To pozwala pokazać cały przepływ „klik → enqueue → worker → mail w UI” bez zewnętrznych usług.
