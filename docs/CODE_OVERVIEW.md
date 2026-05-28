# Code Overview

This document maps the main code paths so contributors can quickly locate behavior and extend the API safely.

## Startup and Hosting

- Entry point: `adrapi/Program.cs`
- Dependency/service wiring: `adrapi/Startup.cs`

`Program` builds configuration, configures NLog early, and starts Kestrel listeners for HTTP/HTTPS.

`Startup` registers:

- MVC (without endpoint routing)
- API versioning using `api-version` header
- Authorization policies (`Reading`, `Writting`)
- Basic authentication handler
- Swagger/OpenAPI generation

## Request Pipeline

High-level order in `Startup.Configure`:

1. Exception page or HSTS
2. HTTPS redirection
3. Static file server
4. Authentication
5. Swagger + Swagger UI
6. MVC

## API Layers

Controllers in `adrapi/Controllers` orchestrate use cases and delegate LDAP operations to manager singletons:

- `UsersController`
- `GroupsController`
- `OUsController`
- `InfosController`

Shared request preprocessing lives in `BaseController.ProcessRequest()` which reads requester identity from `api-key` header.

## Domain and LDAP Access

- Domain models: `domain/*.cs`
- LDAP abstractions/utilities: `adrapi/Ldap/*.cs`
- High-level managers:
- `adrapi/UserManager.cs`
- `adrapi/GroupManager.cs`
- `adrapi/OUManager.cs`

Managers encapsulate query construction, mapping (`LdapEntry` -> domain models), and write operations.

## Security Components

- Basic auth handler: `adrapi/Security/BasicAuthenticationHandler.cs`
- API key handling: `adrapi/Security/ApiKeyManager.cs`
- Optional key middleware: `adrapi/Security/KeyAuthenticationMiddleware.cs`

## Logging

NLog is configured through app settings and config files:

- `adrapi/nlog.config`
- `adrapi/nlog.prod.config`

## Testing

- Test project: `tests/tests.csproj`
- Existing suites include LDAP/security-oriented tests (`tests/LdapTests.cs`, `tests/Security.cs`).

## Conventions for Changes

- Keep endpoint behavior version-aware.
- Preserve LDAP filter escaping and DN parsing safeguards.
- Prefer extending manager classes rather than placing LDAP details in controllers.
- Add or update tests for behavior changes whenever local runtime prerequisites are available.
