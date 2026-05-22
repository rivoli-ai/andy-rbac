---
title: "Roles"
order: 2
tags: [rbac, roles]
---

# Roles

Roles are the bridge between subjects and permissions. Instead of granting permissions directly to every user, you group permissions into roles and assign those roles to subjects or teams.

## Role Hierarchy

Andy RBAC supports hierarchical roles. A child role inherits all permissions from its parent, making it easy to model variations like:

- `admin` → full access
- `editor` → inherits from `viewer` plus write permissions
- `viewer` → read-only access

## Creating a Role

Use the `POST /api/roles` endpoint or the admin UI:

```json
{
  "name": "Project Manager",
  "applicationCode": "issues",
  "permissions": [
    { "resourceType": "project", "action": "read" },
    { "resourceType": "project", "action": "update" }
  ]
}
```

## Best Practices

- Keep roles application-scoped when possible.
- Use descriptive names (`issues:admin` rather than `admin`).
- Prefer composition over duplication — reuse base roles.
