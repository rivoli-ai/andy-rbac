---
title: "Permissions"
order: 3
tags: [rbac, permissions]
---

# Permissions

A permission is a rule that answers: *Can this subject perform this action on this resource?*

## Permission Model

Each permission has four components:

| Component | Description |
|-----------|-------------|
| **Effect** | `allow` or `deny` |
| **Resource Type** | The kind of resource (e.g., `issue`, `team`) |
| **Action** | The operation (e.g., `read`, `create`, `delete`) |
| **Conditions** | Optional constraints (e.g., `owner == subject`) |

## Evaluation Logic

When a subject requests access, Andy RBAC evaluates all matching permissions in this order:

1. **Deny overrides allow** — an explicit deny always wins.
2. **Most specific wins** — conditions are preferred over broad rules.
3. **Default deny** — if no permission matches, access is denied.

## Checking Access

Call the check endpoint:

```http
POST /api/check
Content-Type: application/json

{
  "subjectId": "user-123",
  "resourceType": "issue",
  "action": "delete",
  "resourceId": "issue-456"
}
```

The response returns `allowed: true` or `allowed: false`.
