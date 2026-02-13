# adrapi

Active Directory REST API for querying and managing users, groups, and OUs over LDAP.

## Current Runtime/Tooling

- SDK used in this workspace: .NET `10.0.101`
- Application target framework: `net10.0`
- Test target framework: `net10.0`

## What This Service Does

- Exposes versioned REST endpoints for:
- Users
- Groups
- Organizational Units (OUs)
- Performs LDAP-backed read/write operations
- Uses API-key based auth + claims authorization (`isAdministrator`, `isMonitor`)
- Publishes OpenAPI/Swagger docs

## Project Structure

- `adrapi/`: Web API host, controllers, LDAP integration, security middleware, managers
- `domain/`: Domain models and custom exceptions
- `tests/`: Test project and environment-specific test settings
- `build/`: NUKE build tooling

## Configuration

Main runtime configuration lives in:

- `adrapi/appsettings.json`
- `adrapi/appsettings.Development.json`
- `adrapi/security.json`

Important settings:

- `ldap`: LDAP servers and connection details
- `certificate:file` / `certificate:password`: HTTPS certificate for Kestrel
- `AllowedHosts`: Host binding behavior (`*` maps to `0.0.0.0` in startup)

## Security Model

The API expects these headers:

- `api-key`: `keyID:secretKey`
- `api-version`: API version selector

Claims are loaded from `security.json`.

- `isAdministrator`: read/write access
- `isMonitor`: read-oriented access

## Run Locally

```bash
dotnet restore adrapi.sln
dotnet run --project adrapi/adrapi.csproj
```

Default Kestrel bindings are configured in code:

- HTTP: `:6000`
- HTTPS: `:6001`

## API Documentation

- Interactive docs: `/swagger`
- Detailed reference: `/Users/felipe/Dev/adrapi/docs/API_REFERENCE.md`
- Usage guide: `/Users/felipe/Dev/adrapi/docs/USAGE_GUIDE.md`
- Migration notes: `/Users/felipe/Dev/adrapi/docs/MIGRATION_NOTES.md`
- Curl collection: `/Users/felipe/Dev/adrapi/docs/CURL_COLLECTION.md`

## Development Notes

- Use a local `net10.0` runtime/SDK for build and tests.
- The repository includes legacy code paths and compatibility behaviors for older LDAP contracts; keep changes backward-compatible unless intentionally versioned.

## License

Apache License v2.0
