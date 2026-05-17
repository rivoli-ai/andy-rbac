---
title: Présentation d'Andy RBAC
slug: andy-rbac-overview
order: 1
tags: [rbac, security, permissions]
---

# Présentation d'Andy RBAC

Andy RBAC est le service de contrôle d'accès basé sur les rôles de l'écosystème Andy. Il possède le catalogue des rôles, le catalogue des types de ressources et l'évaluation des permissions — étant donné un principal, une ressource et une action, RBAC décide si l'appel est autorisé.

## Ce qu'il fait

- Maintient un catalogue versionné de rôles, de types de ressources et des permissions que chaque rôle accorde.
- Évalue les vérifications de permission via `POST /api/v1/permissions/check` — la couture canonique que chaque autre service utilise pour l'autorisation.
- Prend en charge l'héritage de rôles afin que les rôles dérivés se composent sans copier les listes de permissions.
- Met en cache les évaluations récentes pour garder le chemin commun de vérification sous la milliseconde.
- Expose un catalogue en lecture seule que l'interface Conductor utilise pour afficher les panneaux « ce que cet utilisateur peut faire ».

## Concepts clés

- **Rôle** — un octroi nommé, p. ex. `workspace.admin`, `repo.viewer`. Les rôles se composent via l'héritage.
- **Type de ressource** — ce que cible l'action, p. ex. `workspace`, `repo`, `agent-run`. Chaque type de ressource possède ses propres verbes de permission.
- **Enveloppe de vérification de permission** — le tuple complet `(principal, ressource, action)`, plus le contexte optionnel comme l'ID d'organisation.

## Où il s'intègre

Chaque service Andy qui protège un chemin d'écriture appelle RBAC avant d'agir. Conductor lui-même route les actions UI via le bus d'actions, qui effectue une vérification RBAC avant l'appel au service sous-jacent.

## Configuration

Le catalogue provient de `config/registration.json` (`rbac.roles` + `rbac.resourceTypes`). Conductor expose le catalogue en direct sous **Réglages → Catalogues → RBAC**. Le TTL du cache et les autres réglages résident sous les clés `andy.rbac.*` dans `andy-settings`.

## Dépannage

- **403 d'un service qui « fonctionnait avant »** — le catalogue de rôles a été modifié et l'utilisateur affecté a perdu une permission. Faites un diff de `registration.json` par rapport au déploiement précédent.
- **Vérifications de permission lentes** — tempête de cache miss ; vérifiez le réglage `andy.rbac.cacheTtlSeconds` et confirmez que le cache en mémoire est sain dans les logs de RBAC.
- **`UnauthorizedAccess` à chaque appel** — RBAC est actif mais le jeton de l'appelant a la mauvaise audience. Regardez l'en-tête `Authorization` dans la requête en échec.
