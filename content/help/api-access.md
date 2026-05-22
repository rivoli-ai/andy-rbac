---
title: "API Access"
order: 5
tags: [api, integrations]
---

# API Access

Andy RBAC exposes a REST API for managing roles, subjects, teams, and permission checks. All endpoints are available under `/api/`.

## Authentication

API requests must include a valid bearer token in the `Authorization` header:

```http
Authorization: Bearer <token>
```

Token validation is handled by the configured identity provider.

## Key Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/check` | POST | Evaluate a permission in real time |
| `/api/roles` | GET / POST | List or create roles |
| `/api/subjects` | GET / POST | List or create subjects |
| `/api/teams` | GET / POST | List or create teams |
| `/api/policies` | GET / POST | Manage policy definitions |

## Client SDKs

You can integrate Andy RBAC using:

- **HTTP clients** — any language with a standard HTTP library
- **OpenAPI generator** — the service publishes an OpenAPI spec at `/swagger/v1/swagger.json`
- **MCP** — model context protocol support is available for AI-driven workflows

## Rate Limiting

Permission checks (`/api/check`) are optimized for high throughput. Management endpoints (roles, subjects) may have stricter rate limits depending on your deployment configuration.
