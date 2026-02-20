# smtp-gate-sender

Lightweight **HTTP → SMTP gateway** with a disk-based spool queue, idempotency, retry/backoff logic, and retention workers.

Designed as a reliability-focused demo service for homelab and internal tooling scenarios.

---

## Overview

`smtp-gate-sender` accepts HTTP requests, validates and normalizes payloads, stores messages in a filesystem queue (spool), and delivers them asynchronously via SMTP.

Core capabilities:

- HTTP API (ASP.NET Minimal API)
- Filesystem-based spool queue
- Idempotency via `RequestId`
- Asynchronous SMTP delivery worker
- Retry + backoff strategy
- HTML & plain text email support
- Retention / cleanup workers
- Basic rate limiting

Default demo setup uses **MailHog** as SMTP backend.

---

## Architecture

Client → HTTP API → Spool (Filesystem Queue) → Worker → SMTP

Components:

- **API Layer** – request validation & normalization
- **SpoolStore** – disk queue + idempotency tracking
- **SpoolWorker** – asynchronous delivery engine
- **SmtpSender** – SMTP client abstraction
- **RetentionWorker** – cleanup & retention policies

---

## Running (Docker)

docker compose up -d --build

---

## Endpoints / UI

Swagger UI  
http://localhost:8885/swagger

Health Check  
http://localhost:8885/health

Queue Statistics  
http://localhost:8885/stats

MailHog UI  
http://localhost:8884

---

## Response Behavior

- `202 Accepted` → Message queued  
- `200 OK (duplicate=true)` → Idempotency hit  
- `400 BadRequest` → Payload validation error  
- `429 TooManyRequests` → Rate limiting

---

## Persistent Data

- ./data/spool → queued / sent / failed / idempotency  
- ./data/logs → rolling logs

---

## Configuration

Configuration via:

- appsettings.json  
- Environment variables (Docker-friendly)

Default SMTP target (demo):

mailhog:1025

---

## Intended Use Cases

- Homelab SMTP testing  
- Internal notification gateways  
- Queue/retry pattern demonstrations  
- SMTP abstraction layer  
- Reliability / idempotency showcase
