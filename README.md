# smtp-gate-sender (homelab)

Lokalna bramka HTTP → SMTP z kolejką na dysku (spool), idempotencją (RequestId), retry/backoff i retencją logów/spool.

## Start (Docker)
```bash
docker compose up -d --build
```

## UI / adresy
- Swagger: http://localhost:8885/swagger
- Health:  http://localhost:8885/health
- Stats:   http://localhost:8885/stats
- MailHog UI: http://localhost:8884

## Demo bez curl
W Swaggerze wywołaj `POST /email` (albo legacy `POST /api/email`), a potem pokaż wiadomość w MailHog UI.

## Dane trwałe
- `./data/spool` – kolejka (queued/sent/failed/idem)
- `./data/logs` – logi (rolling)
