# Logging Guide - PosServer

## Goal

Every runtime failure must be diagnosable from logs returned by the tester.

## Format

Logs are written to stdout as compact JSON through Serilog.

## Required Fields

| Field | Purpose |
| --- | --- |
| `@t` | timestamp |
| `@mt` | message template |
| `@l` | level |
| `@x` | exception |
| `service` | service name |
| `version` | service version |
| `trace_id` | ASP.NET trace identifier |
| `correlation_id` | stable request correlation id |
| `tenant_id` | tenant context |
| `user_id` | authenticated user |
| `terminal_id` | authenticated terminal |
| `endpoint` | method and path |

## Sensitive Values Never Logged

- passwords
- password hashes
- access tokens
- refresh tokens
- device tokens
- enrollment tokens
- card data

## What To Send Back When Testing

Send:

- exact command
- HTTP method and URL
- status code
- response body
- JSON logs around the error
- `X-Correlation-Id`

Do not send secrets.
