# ADRAPI Curl Collection

Set shared values:

```bash
export ADRAPI_BASE_URL="https://localhost:6001"
export ADRAPI_KEY="<keyId>:<secretKey>"
export ADRAPI_VERSION="2.0"
```

## Health and Discovery

```bash
curl -k "$ADRAPI_BASE_URL/api/infos/version" \
  -H "api-version: $ADRAPI_VERSION" \
  -H "api-key: $ADRAPI_KEY"
```

```bash
curl -k "$ADRAPI_BASE_URL/swagger"
```

## Users

```bash
curl -k "$ADRAPI_BASE_URL/api/users" \
  -H "api-version: $ADRAPI_VERSION" \
  -H "api-key: $ADRAPI_KEY"
```

```bash
curl -k "$ADRAPI_BASE_URL/api/users/jdoe/exists?_attribute=samaccountname" \
  -H "api-version: $ADRAPI_VERSION" \
  -H "api-key: $ADRAPI_KEY"
```

## Groups

Create group:

```bash
curl -k -X POST "$ADRAPI_BASE_URL/api/groups?_listCN=true" \
  -H "Content-Type: application/json" \
  -H "api-version: $ADRAPI_VERSION" \
  -H "api-key: $ADRAPI_KEY" \
  -H "X-Correlation-ID: curl-group-create-001" \
  -d '{"dn":"CN=PlatformTeam,OU=Groups,DC=homologa,DC=br","name":"PlatformTeam","description":"Platform Team","members":["jdoe","maria.silva"]}'
```

List group members:

```bash
curl -k "$ADRAPI_BASE_URL/api/groups/CN=PlatformTeam,OU=Groups,DC=homologa,DC=br/members?_listCN=true" \
  -H "api-version: $ADRAPI_VERSION" \
  -H "api-key: $ADRAPI_KEY"
```

Patch group members:

```bash
curl -k -X PATCH "$ADRAPI_BASE_URL/api/groups/CN=PlatformTeam,OU=Groups,DC=homologa,DC=br/members?_listCN=true" \
  -H "Content-Type: application/json" \
  -H "api-version: $ADRAPI_VERSION" \
  -H "api-key: $ADRAPI_KEY" \
  -H "X-Correlation-ID: curl-group-patch-001" \
  -d '{"add":["new.user"],"remove":["old.user"]}'
```

Replace group members:

```bash
curl -k -X PUT "$ADRAPI_BASE_URL/api/groups/CN=PlatformTeam,OU=Groups,DC=homologa,DC=br/members?_listCN=true" \
  -H "Content-Type: application/json" \
  -H "api-version: $ADRAPI_VERSION" \
  -H "api-key: $ADRAPI_KEY" \
  -H "X-Correlation-ID: curl-group-replace-001" \
  -d '["jdoe","maria.silva"]'
```

Delete group:

```bash
curl -k -X DELETE "$ADRAPI_BASE_URL/api/groups/CN=PlatformTeam,OU=Groups,DC=homologa,DC=br" \
  -H "api-version: $ADRAPI_VERSION" \
  -H "api-key: $ADRAPI_KEY" \
  -H "X-Correlation-ID: curl-group-delete-001"
```

## OUs

Create OU:

```bash
curl -k -X POST "$ADRAPI_BASE_URL/api/ous" \
  -H "Content-Type: application/json" \
  -H "api-version: $ADRAPI_VERSION" \
  -H "api-key: $ADRAPI_KEY" \
  -H "X-Correlation-ID: curl-ou-create-001" \
  -d '{"dn":"OU=Platform,DC=homologa,DC=br","name":"Platform","description":"Platform OU"}'
```

Check OU exists:

```bash
curl -k "$ADRAPI_BASE_URL/api/ous/OU=Platform,DC=homologa,DC=br/exists" \
  -H "api-version: $ADRAPI_VERSION" \
  -H "api-key: $ADRAPI_KEY"
```

Update OU:

```bash
curl -k -X PUT "$ADRAPI_BASE_URL/api/ous/OU=Platform,DC=homologa,DC=br" \
  -H "Content-Type: application/json" \
  -H "api-version: $ADRAPI_VERSION" \
  -H "api-key: $ADRAPI_KEY" \
  -H "X-Correlation-ID: curl-ou-update-001" \
  -d '{"name":"Platform","description":"Platform OU updated"}'
```

Delete OU:

```bash
curl -k -X DELETE "$ADRAPI_BASE_URL/api/ous/OU=Platform,DC=homologa,DC=br" \
  -H "api-version: $ADRAPI_VERSION" \
  -H "api-key: $ADRAPI_KEY" \
  -H "X-Correlation-ID: curl-ou-delete-001"
```
