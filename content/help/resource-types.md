---
title: "Resource Types"
order: 4
tags: [rbac, resources]
---

# Resource Types

Resource types define the categories of entities that your permissions protect. They act as the namespace for actions and help keep permissions organized across applications.

## Defining a Resource Type

Each resource type belongs to an application and has a unique code:

```json
{
  "code": "ticket",
  "applicationCode": "issues",
  "name": "Support Ticket",
  "description": "A customer support request"
}
```

## Actions

Common actions include:

- `create` — instantiate a new resource
- `read` — view the resource
- `update` — modify the resource
- `delete` — remove the resource
- `list` — enumerate resources of this type

You can define custom actions per resource type (e.g., `assign`, `transition`, `escalate`).

## Scoping

Resource types are scoped to an application. This means `issues:ticket` and `wiki:page` can coexist without collision, and policies stay clean and predictable.
