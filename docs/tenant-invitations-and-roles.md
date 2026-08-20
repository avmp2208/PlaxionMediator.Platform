# Tenant Invitations and Platform Roles

This document describes how PlaxionMediator.Platform separates **global OIDC authorization** from **in-tenant Owner/Member roles**, and how multi-tenant email invitations work.

## Two authorization domains (keep separate)

| Domain | What it gates | Source of truth |
|--------|---------------|-----------------|
| **OIDC / IdP roles & claims** | Who may **create** new tenants (`POST /v1/tenants`) | `Authorization:TenantCreation` policy (AnyAuthenticatedUser / Role / Claim) |
| **Platform TenantMembership roles** | Who may act **inside** a tenant (view, invite, manage API keys, …) | `TenantMembership.Role` = `Owner` or `Member` |

**Rules:**

- IdP roles are **never** mapped into `TenantMembership`.
- Gaining or losing an IdP role does **not** auto-grant or revoke membership.
- Membership is keyed by stable OIDC identity: `(TenantId, IdentityIssuer, IdentitySubject)` — not by email.
- Email is used only for **invitation matching** at claim time.

`PlatformAdministrator` remains a **break-glass / legacy upgrade** mechanism only. It is unchanged in purpose and is not part of the normal Owner → Invitation → Member path.

## Tenant roles (platform-internal)

| Role | Permissions (current) |
|------|------------------------|
| **Owner** | View tenant; manage invitations; manage telemetry API keys; all future Owner-only permissions |
| **Member** | View tenant-scoped resources only |

Authorization is centralized in `ITenantAuthorizationService` / `TenantPermission`. Controllers should call the service rather than comparing `Role == Owner` inline so new permissions (e.g. `ManageSlo`, `ManageAlerts`) can be added without rewriting every controller. This is intentionally **not** a full RBAC framework.

## Invitation lifecycle

```text
Owner POST /v1/invitations { email, tenantIds[] }
        │
        ▼
  Pending invitation rows (one per tenant)
        │
        │  (no email is sent by the platform)
        │  (no manual “accept” step)
        ▼
Invitee authenticates (OIDC JWT with email claim)
        │
        ▼
GET /v1/tenants  →  JIT claim
        │
        ├─ create TenantMembership Role=Member (idempotent)
        └─ mark invitation Claimed
        │
        ▼
Invitee appears as Member on subsequent tenant-scoped calls
```

### Behaviors

- **No invitation emails** are sent by Platform.
- **No manual acceptance** UI/API beyond authenticating and listing tenants.
- **Multiple `tenantIds`** are supported in one create call.
- Create is **atomic on ownership**: the caller must be **Owner of every** listed tenant; if any id is unknown or not owned, the **entire** request is rejected and **nothing** is written.
- `tenantIds` are **deduplicated**.
- Email is **normalized** (trim + lowercase) before storage and matching.
- Duplicate **Pending** for the same `(TenantId, NormalizedEmail)` is rejected (`409`); enforced in application logic and by a **filtered unique index**.
- Every invitation grants **`Member`** on claim (not Owner).
- **No special IdP role** is required to be a Member.
- If the invitee is already a Member/Owner (detected via a prior Claimed invitation for the same email whose claimer still has membership), create **skips** that tenant and still succeeds for others (`skippedTenantIds`).
- At JIT claim, if membership already exists for `(iss, sub)`, the invitation is still marked Claimed (idempotent); no duplicate membership.
- **Claimed** and **Revoked** invitations are terminal and **cannot be reclaimed**.
- **Revoke** only flips Pending → Revoked; it never deletes an existing membership.
- If the JWT has **no email claim**, JIT claim is skipped silently (list still works for existing memberships).
- **Email change after claim** does not remove access: membership stays on `iss`/`sub`.

## API shape

### Create invitations

```http
POST /v1/invitations
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "email": "user@example.com",
  "tenantIds": [ "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222" ]
}
```

**201** response:

```json
{
  "invitations": [
    {
      "id": "...",
      "tenantId": "...",
      "email": "user@example.com",
      "status": "Pending",
      "createdAt": "...",
      "claimedAt": null
    }
  ],
  "skippedTenantIds": []
}
```

### List invitations (Owner only)

```http
GET /v1/invitations?tenantId={guid}
```

Members and unrelated users receive **403**.

### Revoke invitation (Owner only)

```http
POST /v1/invitations/{invitationId}/revoke
```

### JIT claim (automatic)

```http
GET /v1/tenants
```

Claims all Pending invitations matching the caller’s configured email claim types, then returns memberships as before.

### API keys (Owner for JWT path)

- Machine path: `X-Api-Key` (unchanged).
- Human JWT path: `GET|POST|DELETE /v1/api-keys?tenantId={guid}` requires **Owner** (`ManageApiKeys`).

## Storage model

### `TenantInvitation`

| Field | Notes |
|-------|--------|
| Id | Guid |
| TenantId | Guid |
| NormalizedEmail | trim + lowercase |
| Status | `Pending` / `Claimed` / `Revoked` |
| CreatedByIssuer / CreatedBySubject | inviter identity |
| CreatedAt | |
| ClaimedAt / ClaimedByIssuer / ClaimedBySubject | set on JIT claim |

Unique filtered index: `(TenantId, NormalizedEmail)` where `Status = 'Pending'`.

### `TenantMembership`

Uniqueness: `(TenantId, IdentityIssuer, IdentitySubject)` (already present; concurrency-safe).

## Legacy upgrade compatibility

For pre-OIDC tenants with no membership rows:

1. Enable Authentication (OIDC).
2. Temporarily enable `Authorization:PlatformAdministrator` (Role or Claim) for an operator group.
3. `POST /v1/tenants/{tenantId}/memberships` to establish the initial **Owner** (explicit, auditable).
4. Disable PlatformAdministrator again.
5. Normal **Owner → Invitation → Member** flow takes over.

PlatformAdministrator remains break-glass/legacy-only and is not replaced by invitations.

## Configuration knobs

| Key | Purpose | Default |
|-----|---------|---------|
| `Authorization:TenantCreation:*` | Who may create tenants (OIDC) | `AnyAuthenticatedUser` |
| `Authorization:PlatformAdministrator:*` | Break-glass operator | `Disabled` |
| `Authorization:Invitations:EmailClaimTypes` | Ordered claim types for JIT email match | `email`, `emails`, `preferred_username`, WS-email URI |

Helm charts that package Platform.Api may later expose `Authorization__Invitations__EmailClaimTypes` (array) alongside existing Authentication / TenantCreation / PlatformAdministrator settings. No Helm changes are required in the application repo itself.

## Extensibility

Add a new `TenantPermission` value and map it in `TenantAuthorizationService.RoleGrants`. Wire controllers through `ITenantAuthorizationService.HasPermissionAsync` — do not scatter `if (role == Owner)` checks.
