# Azure operations guide (BoardGameMondays)

This app is a Blazor Server app using ASP.NET Core Identity.

You said: **public site anyone can join**, but only **trusted friends are admins**.

## Recommended Azure architecture

### Hosting
- **Azure App Service (Linux)** for the web app.
- Use **deployment slots** (`staging` → swap to `production`) for safe releases.

### Data
- **Azure SQL Database** for application + Identity data.
  - Reason: App Service local storage is not a reliable database disk; Azure SQL gives you automated backups, PITR restore, and simpler ops.

### User-uploaded files
- Uploads are now configurable:
  - **Local filesystem** (default for dev)
  - **Azure Blob Storage** (recommended for production)

Reason: App Service deployments can overwrite local `wwwroot` content; blobs persist independently.

### Observability
- **Application Insights** for:
  - exceptions and traces
  - request timings
  - dependency calls (DB, HTTP)

## Releases (how to ship new versions)

### The simple, safe workflow
1. Push/merge to `main`
2. CI builds + runs checks
3. Deploy to **staging slot**
4. Smoke test
5. Swap slots

This gives near-zero downtime and easy rollback (swap back).

## Config you should expect to manage in Azure

### Connection string
In App Service configuration:
- `ConnectionStrings__DefaultConnection` = your Azure SQL connection string

### Admin assignment (trusted friends)
This repo now supports bootstrapping admins from configuration.

Set in App Service configuration (or `appsettings.Production.json`):
- `Security__Admins__UserNames__0` = `henry`
- `Security__Admins__UserNames__1` = `alex`

How it works:
- On startup, the app ensures the `Admin` role exists.
- For each configured username, if that user exists, it adds them to the `Admin` role.

Operationally:
- Your friends **sign up normally**.
- You add their username to the config list.
- Restart the app (or wait for next deployment) and they become Admin.

## Backups / restore

### Azure SQL
- Azure SQL provides automatic backups + point-in-time restore.
- What you need to do operationally:
  - document how to perform a restore
  - do an occasional restore drill (even once per quarter is good)

### Blob Storage
- Enable soft delete / versioning (recommended).
- Consider replication (GRS) if you need higher durability.

## User deletion: “Can I delete users from Azure?”

It depends on where your users live:

### Option A (current): ASP.NET Identity users stored in your database
- Users are **not** “Azure users”.
- You cannot delete them from Azure Portal.
- You delete/disable them in the app (admin feature) and/or in the database.

### Option B: Microsoft Entra External ID / Entra-managed identities
- Users are managed in Azure.
- You can disable/delete them via Entra administration.
- Admins are typically controlled via Entra groups.

For a public consumer site, Option A is simplest to operate initially, but Option B is best if you strongly want Azure-native account lifecycle and policies.

## Other operational features to consider next

### Admin/user management inside the app (recommended for Option A)
- Add an Admin page to:
  - list users
  - disable/enable accounts
  - delete accounts (careful: consider GDPR-style deletion vs hard delete)
  - grant/revoke Admin

### Audit logging
- Record admin actions (role changes, deleting users, settling bets/winners, editing content).
- Emit structured logs so App Insights queries can answer: “who changed what and when?”

### Abuse/rate limiting
- You already rate limit `/account/*`.
- Consider adding per-user or per-IP limits for:
  - uploads
  - betting endpoints
  - any expensive queries

### WAF / DDoS
- For a public site, consider **Azure Front Door (WAF)** in front of App Service.
  - App-level rate limiting helps, but WAF/CDN is the real protection layer.

## Next implementation steps (if you want me to do them)

### Upload storage config

Default (local dev):
- `Storage__Provider=Local`

Azure Blob (production):
- `Storage__Provider=AzureBlob`
- `Storage__AzureBlob__ConnectionString=...`
- `Storage__AzureBlob__ContainerName=bgm-assets`
- Optional (CDN/custom domain): `Storage__AzureBlob__BaseUrl=https://cdn.example.com`

Notes:
- If you want blobs to be directly reachable by browsers, the container needs to be public (or you need a SAS/proxy approach).
- The app can attempt to create the container if it doesn’t exist; if the identity used doesn’t have permission, create it once manually.

### Roadmap items

1. Add an Admin UI for user management + audit log table.
2. Add GitHub Actions workflow for App Service slot deployment.
