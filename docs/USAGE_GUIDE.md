# ADRAPI Usage Guide

This guide explains how to run and consume ADRAPI in development and deployment environments.

## 1. Prerequisites

- .NET SDK `10.0.x`
- Network access to your LDAP/AD servers
- Valid API keys in `security.json`
- Valid LDAP settings in `appsettings.json`

## 2. Configure the Service

Primary config files:

- `adrapi/appsettings.json`
- `adrapi/security.json`

### LDAP settings checklist

- `ldap.servers`: one or more `host:port`
- `ldap.poolSize`: must be `> 0`
- `ldap.bindDn` / `ldap.bindCredentials`: service bind account
- `ldap.searchBase`: base DN
- `ldap.ssl`:
- `true` -> use LDAPS port (typically `636`)
- `false` -> plain LDAP port (typically `389`)

## 3. Run locally

```bash
dotnet restore adrapi.sln
dotnet run --project adrapi/adrapi.csproj
```

By default, the API listens on:

- HTTP `:6000`
- HTTPS `:6001`

## 4. Call the API

Use required headers:

- `api-key: <keyId>:<secretKey>`
- `api-version: 2.0`

### Example: list users

```bash
curl -k -X GET 'https://localhost:6001/api/users' \
  -H 'api-version: 2.0' \
  -H 'api-key: <keyId>:<secretKey>'
```

### Example: paged list users (cookie flow)

```bash
curl -k -X GET 'https://localhost:6001/api/users?_cookie=<cookie>' \
  -H 'api-version: 2.0' \
  -H 'api-key: <keyId>:<secretKey>'
```

### Example: authenticate explicit login

```bash
curl -k -X POST 'https://localhost:6001/api/users/authenticate' \
  -H 'Content-Type: application/json' \
  -H 'api-version: 2.0' \
  -H 'api-key: <keyId>:<secretKey>' \
  -d '{"login":"user","password":"pass"}'
```

### Example: create/update group

```bash
curl -k -X PUT 'https://localhost:6001/api/groups/CN=MyGroup,OU=Groups,DC=homologa,DC=br' \
  -H 'Content-Type: application/json' \
  -H 'api-version: 2.0' \
  -H 'api-key: <adminKeyId>:<secretKey>' \
  -d '{"name":"MyGroup","member":["CN=jdoe,OU=Users,DC=homologa,DC=br"]}'
```

### Example: create group using v2 contract (`POST /api/groups`)

```bash
curl -k -X POST 'https://localhost:6001/api/groups?_listCN=true' \
  -H 'Content-Type: application/json' \
  -H 'api-version: 2.0' \
  -H 'api-key: <adminKeyId>:<secretKey>' \
  -H 'X-Correlation-ID: usage-group-post-001' \
  -d '{"dn":"CN=Product,OU=Groups,DC=homologa,DC=br","name":"Product","description":"Product team","members":["jdoe"]}'
```

### Example: membership sync (replace full set)

```bash
curl -k -X PUT 'https://localhost:6001/api/groups/CN=Product,OU=Groups,DC=homologa,DC=br/members?_listCN=true' \
  -H 'Content-Type: application/json' \
  -H 'api-version: 2.0' \
  -H 'api-key: <adminKeyId>:<secretKey>' \
  -H 'X-Correlation-ID: usage-group-members-001' \
  -d '["jdoe","maria.silva"]'
```

### Example: OU lifecycle

Create:

```bash
curl -k -X POST 'https://localhost:6001/api/ous' \
  -H 'Content-Type: application/json' \
  -H 'api-version: 2.0' \
  -H 'api-key: <adminKeyId>:<secretKey>' \
  -H 'X-Correlation-ID: usage-ou-create-001' \
  -d '{"dn":"OU=Engineering,DC=homologa,DC=br","name":"Engineering","description":"Engineering OU"}'
```

Delete:

```bash
curl -k -X DELETE 'https://localhost:6001/api/ous/OU=Engineering,DC=homologa,DC=br' \
  -H 'api-version: 2.0' \
  -H 'api-key: <adminKeyId>:<secretKey>' \
  -H 'X-Correlation-ID: usage-ou-delete-001'
```

## 5. Swagger UI

Once running, open:

- `https://localhost:6001/swagger`

## 6. Docker usage

Build images:

- Runtime image: `Dockerfile`
- Dev image: `DockerfileDev`

Both are configured to expose ports:

- `6000/tcp`
- `6001/tcp`

Example run:

```bash
docker run --rm -p 6001:6001 -p 6000:6000 <image>
```

## 7. Deployment flow with NUKE

List targets:

```bash
dotnet run --project build/_build.csproj --
```

Build and push release image:

```bash
dotnet run --project build/_build.csproj -- --target Deploy_Docker_Image --configuration Release
```

The deploy target enforces Release configuration and tags:

- `ffquintella/adrapi:<version>`
- `ffquintella/adrapi:latest`

## 8. Troubleshooting

### 500 errors on `/api/users`

- Verify live config inside container:

```bash
docker exec -it <container> cat /app/appsettings.json
```

- Confirm LDAP host resolution and port reachability from container.
- Ensure `ssl` and LDAP port are consistent (`false+389` or `true+636`).
- Ensure bind DN/credentials are valid.

### Wrong config loaded in container

- Check your mount/copy path and container recreation process.
- Recreate container after config/image updates.

### Membership sync returns `422`

- Cause: at least one member cannot be resolved to a valid DN.
- Action:
- if using short identifiers (`jdoe`), call with `_listCN=true`
- verify user/group exists (`GET /api/users/{id}/exists`, `GET /api/groups/{dn}/exists`)
- retry only after resolving all unknown members (writes are all-or-fail)

### OU operations return `409`

- Cause:
- DN format mismatch (`OU=...` expected)
- DN outside configured `ldap.searchBase`
- operation targeting protected/system OU
- Action:
- ensure DN and payload name match
- ensure OU DN is under `ldap.searchBase`
- verify DN is not root/protected OU

### Verify API identity and client IP in logs

- Write operations emit `AUDIT` logs with:
- `requester` (from `api-key` key ID)
- `clientIp` (from `X-Forwarded-For` or remote IP)
- `correlationId` (from `X-Correlation-ID` or request trace id)
- Use this to trace who changed groups/OUs and from which client IP.

## 9. Migration Notes and Curl Collection

- Migration notes: `/Users/felipe/Dev/adrapi/docs/MIGRATION_NOTES.md`
- Curl collection: `/Users/felipe/Dev/adrapi/docs/CURL_COLLECTION.md`
