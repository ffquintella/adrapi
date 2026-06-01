# Entra ID Integration — Stage 8: Observability and Auditability

Status: **complete**. Deliverable: actionable, auditable logs for the Entra ID
backend — every Graph operation produces a structured record carrying the
requester, correlation id, client IP, target object, and the Graph `request-id`.

---

## What was implemented

| Concern | Where |
|---|---|
| Ambient audit context (requester / correlation / client IP) | `adrapi/Directory/DirectoryOperationContext.cs` |
| Structured Graph operation record + sink | `adrapi/Directory/DirectoryAudit.cs` |
| Per-operation audit emission (success & failure) | `adrapi/Entra/GraphClient.cs` |
| Controller seam to open the scope | `adrapi/Controllers/BaseController.cs` (`BeginDirectoryScope`) |
| Tests | `tests/GraphAuditTests.cs` (4 unit) |

## 1. Ambient correlation context

`DirectoryOperationContext` is an `AsyncLocal` scope holding `Requester`,
`CorrelationId`, and `ClientIp` — the same identity/correlation fields the
controllers already stamp on LDAP audit logs (`BaseController.LogAudit`). The HTTP
layer opens a scope for the duration of a request; backends read
`DirectoryOperationContext.Current`. Outside any scope it is `Unknown`, so logging
never NREs.

`BaseController.BeginDirectoryScope()` builds the scope from the request metadata:

```csharp
ProcessRequest();
using (BeginDirectoryScope())
{
    // provider/Graph calls here are audited with requester+correlationId+clientIp
}
```

## 2. Structured per-operation logs + Graph request IDs

`GraphClient` emits one `GraphOperationLog` per completed operation — on success,
on a terminal error response, and on transport exhaustion — through an
`IDirectoryAuditSink`. The default `NLogDirectoryAuditSink` writes a single
structured line under the `GraphAudit` logger (Info for success, Warn for
failure):

```
GRAPH op=GET path=v1.0/users/ada@contoso.com status=200 outcome=success
      graphRequestId=<id> graphClientRequestId=<id>
      requester=key-1 correlationId=corr-1 clientIp=10.0.0.9
```

Fields:

- **op / path** — HTTP method and the target object. The path is logged **without
  its query string**, so `$filter`/`$search` values (potential PII) are not
  recorded.
- **status / outcome** — HTTP status and `success`/`error`.
- **graphRequestId / graphClientRequestId** — the Graph `request-id` /
  `client-request-id` headers, for **cross-correlation with Microsoft support**.
- **requester / correlationId / clientIp** — from the ambient context.

The sink is injectable (constructor parameter, defaults to NLog), so it is unit
tested with a capturing fake and no network.

## Testing

- `dotnet test --filter FullyQualifiedName~GraphAuditTests` — 4 unit tests
  (scope set/restore, success audit with context + request-id, error audit,
  unknown identity outside a scope).

## Out of scope (later stages)

- Wiring `BeginDirectoryScope()` into the controllers happens with the provider
  dispatch deferred from Stage 6 (gated on the Stage 9 test tenant).
- A dedicated NLog target/file for the `GraphAudit` logger can be added in
  `nlog.config` per deployment; records currently flow to the configured targets.
