---
title: "Getting Started"
order: 1
tags: [onboarding, quickstart]
---

# Getting Started with Andy RBAC

Andy RBAC is a centralized role-based access control service that lets you manage who can do what across your applications.

## Core Concepts

- **Subjects** — Users, services, or API keys that request access.
- **Roles** — Named collections of permissions assigned to subjects.
- **Permissions** — Allow or deny rules tied to a resource type and action.
- **Resource Types** — The kinds of entities you protect (e.g., `issue`, `project`).

## Quick Start

1. Define your **resource types** (e.g., `ticket`, `repository`).
2. Create **roles** with the permissions you need.
3. Assign roles to **subjects** or **teams**.
4. Call the `/api/check` endpoint to evaluate access at runtime.

## Next Steps

- Read about [Roles](roles.md) to learn how to build role definitions.
- Explore [Permissions](permissions.md) to understand the evaluation logic.
