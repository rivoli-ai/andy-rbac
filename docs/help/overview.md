---
title: Andy RBAC Overview
slug: andy-rbac-overview
order: 1
tags: [rbac, security, permissions]
---

# Andy RBAC Overview

Andy RBAC is the role-based access control service for the Andy ecosystem. It owns the role catalog, resource-type catalog, and permission evaluation — given a principal, a resource, and an action, RBAC decides whether the call is allowed.

## What it does

- Maintains a versioned catalog of roles, resource types, and the permissions each role grants.
- Evaluates permission checks via `POST /api/check` — the canonical seam every other service uses for authorization.
- Supports role inheritance so derived roles compose without copying permission lists.
- Caches recent evaluations to keep the common-path check under a millisecond.
- Surfaces a read-only catalog the Conductor UI uses to render "what can this user do" panels.

## Key concepts

- **Role** — a named grant, e.g. `workspace.admin`, `repo.viewer`. Roles compose via inheritance.
- **Resource type** — what the action targets, e.g. `workspace`, `repo`, `agent-run`. Each resource type has its own permission verbs.
- **Permission check envelope** — the full `(principal, resource, action)` tuple, plus optional context like organization id.

## Where it fits

Every Andy service that protects a write path calls RBAC before acting. Conductor itself routes UI actions through the action bus, which performs an RBAC check before the underlying service call.

## Configuration

The catalog comes from `config/registration.json` (`rbac.roles` + `rbac.resourceTypes`). Conductor exposes the live catalog at **Settings → Catalogs → RBAC**. Cache TTL and other knobs live under `andy.rbac.*` keys in `andy-settings`.

## Troubleshooting

- **403 from a service that "used to work"** — the role catalog was edited and the affected user lost a permission. Diff `registration.json` against the previous deploy.
- **Slow permission checks** — cache miss storm; check the `andy.rbac.cacheTtlSeconds` setting and verify the in-memory cache is healthy in RBAC's logs.
- **`UnauthorizedAccess` on every call** — RBAC is up but the caller's token has the wrong audience. Look at the `Authorization` header in the failing request.
