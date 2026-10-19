# IronForge — Authoritative Context for AI Coding Agents

This file is the source of truth for new sessions. Read it before changing code. If code and this file disagree, verify in code, fix the code or this file, and say which you changed.

---

## 1. Goal and objectives

IronForge is a **multi-tenant, agentic gym management SaaS**. Each tenant is a gym. The platform is being made production-ready for real gym customers.

Objectives (the locked v1 scope):
1. **Gym staff** (Admin, Trainer) run their gym from the **web dashboard**:
   - members (with photos), join requests, membership plans, invoices and payments
   - check-ins: front desk (pass scan or name search), unattended kiosks (`/kiosk`), check-in devices
   - ZKTeco fingerprint terminals (module `gym.access`): terminal users, linking, terminal simulator
   - exercise library and routines, announcements, settings, staff, AI agents, approvals, activity log
2. **Gym members** use the **MAUI mobile app**:
   - Check in tab: rotating QR pass (works offline) and/or scanning the gym's entrance code, as the gym chose; photo
   - membership and invoices with "Pay now"
   - routines, workout logging, progress (weight and volume charts), goals
   - visits, notifications, profile, and an **AI Fitness Coach**
3. **Platform operators** (PlatformAdmin) use the separate **Platform console**:
   - overview, gyms (onboard, suspend, plan, status, add-on modules, token limit, admin invites)
   - plans, modules and agent-template catalogue with rollout
   - cross-tenant audit log
4. **AI agents** with governed tools and human approvals:
   - Gym Admin Agent (staff)
   - Fitness Coach (members)
   - Insights Agent (admins, read-only)
5. **Realistic demo data** for showcasing the platform, gyms, agents and members.

**Out of scope for v1** (do not add unless the user asks):
- class booking, POS/shop, payroll, nutrition tracking, push notifications, streaming chat
- staff shift clock-in (check-in means staff checking members in)
- other access hardware than ZKTeco push-protocol terminals (RFID-only readers, other brands' turnstile controllers); IronForge never stores biometric templates
- automated billing of tenants by the platform (operators set tenant subscription status manually)
- a tenant-less platform AI agent, multiple currencies per gym

Work branch: `feature/gym-platform`. Do not commit or push unless the user asks.

---

## 2. Solution structure (`IronForge.slnx`; server-only filter `IronForge.Server.slnf`)

| Project | Type | Role |
|---|---|---|
| `IronForge.Shared` | Razor class library | Contracts shared by every front end:<br>- DTOs in `Models/Gym/*`, `Models/Tenants/*`, `Models/Agents/*`<br>- `Authorization/Permissions.cs`, `TenantRoles.cs`<br>- `Subscriptions/SubscriptionCatalog.cs` (modules and plans)<br>- API clients in `Services/*` and `Services/Gym/GymApiClients.cs`<br>- auth (`AuthService`, `JwtAuthenticationStateProvider`, `JwtAuthorizationHandler`, `JwtClaims`)<br>- shared pages (Login, TenantSelection, AcceptInvitation, ForgotPassword, ResetPassword, RedirectToLogin)<br>- UI kit `Components/Gym/*` (StatTile, BarChart, LineChart, Badge, Modal, NotificationBell, NotificationList, PasswordInput (Show/Hide toggle; use it for every password field), QrScanner, CheckInResultPanel, PhotoPicker, GymFormat, Svg)<br>- `CheckIns/MemberPassCode.cs` (pass code format, used by app and server), `wwwroot/js/qr-scanner.js` + vendored `js/vendor/jsQR.js` (Apache-2.0)<br>- global CSS `wwwroot/css/ironforge-app.css` |
| `IronForge.UI.Admin` | RCL | Gym dashboard pages and layout (`Layout/MainLayout`, `NavMenu`, `NavItem`); hosted by `IronForge.Web` |
| `IronForge.UI.Member` | RCL | Member app pages and layout (`Layout/MemberLayout`, bottom tabs); hosted by MAUI `IronForge` |
| `IronForge.UI.Platform` | RCL | Operator console pages and layout (`Layout/PlatformLayout`, indigo `platform-theme`); hosted by `IronForge.Platform` |
| `IronForge.Web` | Blazor Web App (Interactive Server, prerender off) | Gym dashboard host; `ClientAudience.Staff` |
| `IronForge.Platform` | Blazor Web App | Operator console host; `ClientAudience.Platform` |
| `IronForge` | .NET MAUI Blazor Hybrid (Android, iOS, MacCatalyst, Windows) | Member app host; `ClientAudience.Member` |
| `IronForge.Api` | ASP.NET Core Web API | REST API, rate limiting, Stripe webhook, simulated payment pages, background jobs |
| `IronForge.Application` | Class library | **Domain entities** (`Entities/*`, `Entities/Gym/*`; there is no separate Domain project) and application services:<br>- `Gym/*`: Common, Members, Billing, Fitness, Notifications, Settings, AgentTools<br>- `Agents/*`, `Tenants/*`, `Auth/*`, `Auditing/*` |
| `IronForge.Infrastructure` | Class library | EF Core 10 + Npgsql:<br>- `Data/IronForgeDbContext.cs` plus the partial `IronForgeDbContext.Gym.cs`<br>- `Migrations/`, `Persistence/*` stores<br>- Stripe and simulated gateways (`Payments/*`), SMTP email (`Email/*`)<br>- background jobs (`Background/*`), demo seeder (`Data/Seeding/DemoDataSeeder.cs`)<br>- DI extension `GymPlatformServiceCollectionExtensions.cs` |
| `IronForge.Mcp` | Web | MCP server exposing the same governed tool catalogue to external agents |
| `IronForge.Mcp.Client` | Web | Demo external MCP client |
| `IronForge.Migrator` | Console | Applies migrations; `--seed-demo [--reset] [--force]` |
| `tests/IronForge.Application.Tests` | xUnit + Moq | Unit tests |
| `tests/IronForge.Application.IntegrationTests` | xUnit + Testcontainers.PostgreSql | Integration tests against real PostgreSQL (needs Docker) |

Dependency direction: Api, Mcp and Migrator → Infrastructure → Application → Shared. Web, Platform and MAUI → their UI RCL → Shared. The front ends never reference Application.

---

## 3. Architecture and core conventions

### 3.1 Multi-tenancy
- **Tenant = gym.** The JWT claim `tenant_id` is resolved by `ExecutionContextMiddleware` into a scoped `IExecutionContextAccessor` (`ExecutionContext { TenantId, Actor { UserId, Username, Role, UserPermissions, Agent }, Source }`).
- **EF global query filters** (`CurrentTenantId.HasValue && x.TenantId == CurrentTenantId`) apply to every tenant-owned entity. With no tenant in context, filtered sets return nothing.
- Unfiltered on purpose: `Tenant`, `User`, `TenantMembership`, `RefreshToken`, `GymSettings` (looked up by join code and by jobs), `EmailOutboxMessage`, `ProcessedWebhookEvent`, `PasswordResetToken`. `Exercise` is filtered as `TenantId == null || TenantId == current`, so the shared library is visible to every gym.
- **Cross-tenant or anonymous reads** go only through explicit Infrastructure stores that call `IgnoreQueryFilters()` and add an explicit tenant condition:
  - `GymDirectory` (`IGymDirectory`): join, invitation accept, billing job tenant list, admin recipients
  - `PlatformInsightsService` (`IPlatformInsightsService`): operator views, permission-checked
  - `TenantInvitationStore`
  - `CheckInDeviceDirectory` (`ICheckInDeviceDirectory`): a kiosk looks itself up by the hash of its pairing code or device key; a fingerprint terminal by its serial number
- **Background jobs and webhooks** set the context themselves:
  ```
  accessor.Set(new ExecutionContext { TenantId = x, Source = ExecutionSource.Background })
  ```
  This happens in a fresh DI scope per tenant. `ExecutionContextAccessor.Set` may be called only once per scope.

### 3.2 Persistence
- `IRepository<T>` (`EfRepository<T>`) is the data access abstraction. The Application layer does **not** reference EF Core.
- For server-side composed queries use:
  - `QueryAsync<TResult>(q => q.Where(...).OrderBy(...).Select(...))` (no tracking)
  - `QueryTrackedAsync`, `QueryCountAsync`, `QuerySumAsync`

  Prefer projections to DTOs inside `QueryAsync`. Navigations such as `MemberProfile.Invoices` and `MemberProfile.CheckIns` exist for correlated subqueries.
- `SaveChangesAsync` turns `DbUpdateConcurrencyException` into `ConcurrencyConflictException`. `Invoice.Version` and `TenantInvitation.Version` map to Postgres `xmin`.
- `IUnitOfWork.ExecuteInTransactionAsync` covers multi-save operations. All repositories in a scope share one DbContext.
- Money is `numeric(12,2)`. Enums are stored as strings via `HasConversion<string>()`. Instants are UTC (`DateTime`). Calendar dates (billing, due dates, workouts) are `DateOnly` in the **gym's time zone**; use `GymCalendar.Today(clock, tz)`.
- `TimeProvider` is injected for all "now" logic (`TimeProvider.System` in production, `TestClock` in tests).
- Migrations live in `IronForge.Infrastructure/Migrations`; the latest is `AccessTerminalsAndPhoneBinding`. Add one with:
  ```
  ConnectionStrings__DefaultConnection="Host=localhost;Database=design;Username=x;Password=x" dotnet ef migrations add <Name> --project IronForge.Infrastructure --startup-project IronForge.Infrastructure
  ```

### 3.3 Service pattern (follow it for new features)
- **Interface and implementation together** in `IronForge.Application/<Area>/...`. Methods return `ServiceResult<T>` (`Success`, `Data`, `ErrorMessage`, `StatusCode`).
- **Authorization inside services**, never only in controllers. Use `IGymAccessService`:
  - `AuthorizeAsync(permission[, module])` checks:
    - a gym is selected
    - internal agent: the agent holds the permission (derived from its tool bindings) **and** the user does
    - external (MCP) agent: user ∩ agent ∩ delegation scopes
    - optional subscription module enabled
  - `GetCurrentMemberAsync(permission, tracked, ct, module)` resolves the **caller's own `MemberProfile`**. Self-service methods never accept a member id.
  - Failures go through `GymResults.Forbidden / NotFound / Invalid / Conflict / From<T,TOther>`.
- **Audit** significant actions through `IAuditService.RecordAsync(AuditCategory.X, "ActionName", AuditOutcome.Y, resourceType:, resourceId:, reason:)`. Gym categories: `Membership`, `Billing`, `Coaching`, `Notification`.
- **Controllers** are thin: `this.ToActionResult(await service.X())`. A failure returns a plain-text body with its status code. Routes: staff `api/gym/*`, member self-service `api/me/*`, notifications `api/notifications`, anonymous `api/join`, `api/auth/forgot-password|reset-password`, operator `api/platform/*`.
- **Registration**:
  - Gym services and tools go in `IronForge.Infrastructure/GymPlatformServiceCollectionExtensions.AddGymPlatform()`, used by Api and Mcp.
  - Jobs go in `AddGymBackgroundJobs()` (Api only).
  - Agent tools are listed in `Application/Gym/AgentTools/GymAgentToolCatalog.Types`; DI and tests read this list.

### 3.4 Auth, roles and permissions
- **Custom JWT**: no ASP.NET Identity store; `IPasswordHasher<User>` only. Access token plus rotating refresh-token families.
- **Sign-in flow**:
  1. Login (username **or email**) returns a tenantless token that carries platform permissions only.
  2. `POST api/tenants/select` issues a tenant-scoped token with `tenant_id`, `ClaimTypes.Role` and `permission` claims from `RolePermissions.GetPermissions(role)`.
- **Tenant roles** (`TenantRoles`):
  - **Admin**: all `gym.*` staff permissions, agents, MCP, approvals.decide, audit.read, tenant.members.*
  - **Trainer**: gym.members.read, gym.checkins.manage, gym.routines.manage, agents.read
  - **Member**: gym.self.read, gym.self.workouts.write, gym.self.payments, gym.self.checkin
- **Platform role** `PlatformAdmin` grants `platform.tenants.manage` regardless of tenant.
- **Permission constants** are in `IronForge.Shared/Authorization/Permissions.cs`. Front ends gate UI with `[Authorize(Policy = Permissions.X)]` and `<AuthorizeView Policy=...>` through `PermissionPolicyProvider`.
- **Subscription access**: `Tenant.HasAccess(now)` requires IsActive and status Active, PastDue, or Trial-not-ended. It is checked at tenant selection and token refresh.

### 3.5 Subscriptions (code-defined, locked) — `SubscriptionCatalog.cs`
- **Modules**: `gym.core`, `gym.billing`, `gym.coaching`, `gym.engagement`, `audit`, `gym.access` (fingerprint terminals; no agent tools). Custom per-client modules are possible as add-ons.

| Plan | Modules | Tokens / month | Model profiles |
|---|---|---|---|
| Starter | core + billing + engagement | 500k | economy |
| Professional (default) | + coaching + audit | 2M | standard + economy |
| Enterprise | Professional + access | 10M | standard + economy |

- A tool is offered only if its module is enabled.
- An agent template is installed only if **all** its required `Modules` are enabled. `OptionalModules` (e.g. coaching for the Gym Admin agent) do not block the install; their tools are dropped until the module is enabled. A unit test enforces that `Modules` + `OptionalModules` equal its tools' modules.
- Agents follow the subscription: `UpdateSubscriptionAsync` calls `AgentTemplateRolloutService.RolloutToTenantAsync`, so an upgrade or add-on installs the newly covered agents at once. A template agent whose required modules are no longer enabled stays installed but is hidden from `GetAvailableAgentsAsync` and refused by `DatabaseAgentDefinitionProvider`.

---

## 4. Business features and implementation flows

### 4.1 Tenant onboarding (operator)
`TenantProvisioningService.ProvisionAsync` runs in one transaction:
1. Create the `Tenant` (plan, trial).
2. `IGymSettingsFactory.CreateAsync(tenantId, currency, timeZone)` creates `GymSettings` with a unique 8-character join code (alphabet without 0/O/1/I).
3. Install `AgentTemplates.Default` filtered by the plan's modules, adjusting the model profile to the plan.
4. Issue the Admin invitation. It is emailed through the outbox, and the link is also returned.

Subscription changes, suspension and template rollout go through `TenantProvisioningService` and `AgentTemplateRolloutService` (all tenants, or one tenant after its subscription changes).

### 4.2 Members and joining
- **Entities**: `MembershipPlan` (price, BillingInterval Monthly/Quarterly/Yearly, IsActive) and `MemberProfile`:
  - TenantId, nullable UserId, FullName, Email (lower-case, unique per gym), plan
  - Status (Pending, Active, Frozen, Expired, Cancelled)
  - PaidThrough, NextBillingDate, FrozenUntil, AnonymizedAtUtc
- **Join with code** (`GymJoinService`, anonymous, rate-limited):
  1. The code resolves `GymSettings` to a tenant with access.
  2. Create a `User`, or verify the existing account's username and password when the email already has one.
  3. Create a `TenantMembership(Member)` and a Pending `MemberProfile`.
  4. Notify the admins (in-app and email).
- **Staff add** (`MemberService.CreateAsync`):
  1. Create an Active profile with NextBillingDate = start date.
  2. Issue the first invoice immediately.
  3. Optionally send an app invitation. On accept, `TenantInvitationService.LinkMemberProfileAsync` links the profile by email; with no profile, it creates a Pending one.
  4. A staff-added member without an account gets credentials only through an invitation: staff use "Send app invitation" on the member page (`POST api/gym/members/{id}/app-invitation`, `MemberService.SendAppInvitationAsync`; emails the link and returns it to share; a new link revokes the old one).
- **Join with code by a staff-added member** (approved profile, no `UserId`): no request is created and the email alone never hands over the profile. With no account for the email, an invitation is emailed (`GymJoinResultDto.InvitationEmailed`). With an existing account, its username and password link it and grant access (`AccessGranted`).
- **Approve**: Pending → Active, NextBillingDate ??= today, first invoice issued, welcome notification. **Reject**: → Cancelled.
- **Freeze**: Active → Frozen until a date; NextBillingDate and PaidThrough shift forward by the freeze length. The billing job unfreezes the member after FrozenUntil.
- **Cancel**: voids Open invoices for future periods; NextBillingDate becomes null.
- **Reactivate**: Cancelled or Expired → Active. A cancelled member restarts from today, or from PaidThrough + 1.
- **Anonymize** (data deletion request): delete metrics, goals, workouts and routines; deactivate the membership; scrub personal fields. Invoices and payments are kept.
- **Approval gates app access**: a member's `TenantMembership.IsActive` mirrors approval (`MemberProfile.ApprovedAtUtc != null`, see `IsApproved`).
  - Join requests and member invites without a staff-created profile start inactive.
  - Approve and Reactivate activate the membership; Decline deactivates it (`MemberService.SetAppAccessAsync`).
  - A declined request (Cancelled with no approval) can be made again; the same profile is reopened as Pending.
  - `AuthService.SignInAsync` refuses sign-in (403 plus the reason) when an account has no active gym membership and only pending or declined requests. Wrong passwords stay 401 "Invalid username or password". PlatformAdmins are exempt.
- **A new gym has no membership plans.** Members cannot be added or approved until an Admin creates one; the dashboard, Members and Join requests pages say so and link to `/plans`.
- **Check-in** (`Gym/Members/CheckInService`, `Gym/CheckIns/*`): every method goes through `CheckInPolicy` and returns `CheckInResultDto` (Allowed / AlreadyCheckedIn / Denied; a refusal is a 200, not an error).
  - **Admins choose the self check-in methods** (`GymSettings.MemberPassCheckInEnabled`, default on; `GymQrCheckInEnabled`, default off; `PUT api/gym/settings/check-in-methods`, Settings page, audited `CheckInMethodsChanged`). A switched-off method answers 409 everywhere (pass download, staff and kiosk scans, entrance code, member gym-QR check-in). Front desk by name always works. The kiosk re-reads the switches every minute; the member app on each visit to the Check in tab (and forgets a cached pass when the pass is off).
  - **Rule**: only Active members. An overdue invoice still allows entry **during `GracePeriodDays` with a warning**; past it, entry is refused (`GracePeriodEnded`) even before the billing job marks the member Expired.
  - A second check-in within 30 minutes returns `AlreadyCheckedIn` and records nothing.
  - Every refusal is a `CheckInDenial` row (kept apart from `CheckIn`, so attendance counts only visits); copied or forged passes are also audited (`PassReuseRejected`, `PassForgeryRejected`).
  - **Methods** (`CheckIn.Method`): `FrontDesk` (staff picked by name, `POST api/gym/checkins`), `PassScan` (staff scanned the pass, `POST api/gym/checkins/scan`), `Kiosk` (a paired device scanned the pass, `POST api/kiosk/check-ins`), `GymQr` (the member scanned the entrance code, `POST api/me/check-ins`, permission `gym.self.checkin`).
- **Entrance code / gym QR** (`GymQrCode`): `IFG1.{tenant}.{device}.{step}.{hmac}`, step 15 s, accepted for the current and previous step (≤ 30 s). The API creates it (`GET api/kiosk/gym-qr`, device key) and checks it; its secret (`ICheckInKeys.GymQrSecret(tenant, device)`) never leaves the API. The kiosk fetches a code every 5 s. The device must be paired and not revoked; codes of another gym, forged or expired codes are refusals (`InvalidGymQr`, `ExpiredGymQr`). The check-in records the device that showed the code and no `RecordedByUserId`. It proves presence at the screen; identity is the member's own sign-in.
- **Member pass** (`MemberPassService`, `MemberPassCode`): `IF1.{tenant}.{member}.{keyVersion}.{step}.{hmac}` with a new step every 30 s.
  - The secret is derived per gym, member and `MemberProfile.PassKeyVersion` from `CheckIns:PassSigningKey` (fallback: the JWT key) by `ICheckInKeys` (`CheckInKeys`); nothing per member is stored.
  - The app downloads it (`GET api/me/pass`, approved members only), caches it in `SecureStorage` (cleared on sign-out) and computes codes offline.
  - The server accepts steps from 2 back to 1 ahead, and refuses a step ≤ `LastPassStep` (copied code). Reset (`POST api/me/pass/reset`, staff `POST api/gym/members/{id}/pass/reset`) increments the version; anonymizing also does.
- **Check-in devices** (`CheckInDeviceService`, `KioskService`): admins (`gym.settings.manage`) add a device and get a 15-minute one-time pairing code; the kiosk swaps it for a long device key (`ifk_…`, only the SHA-256 is stored).
  - The key is sent in `X-Device-Key`; `KioskService.AuthenticateAsync` sets the execution context as the device (`Actor.DeviceId`, `Source = Device`, permission `gym.checkins.device` only, never held by a role). `GymAccessService` lets a device do nothing else.
  - A request carrying both a JWT and a device key is refused (400). Revoking a device stops it at once; a suspended gym's devices get 403.
  - Kiosk UI: `/kiosk` in the gym dashboard host (`KioskLayout`, anonymous; key in `ProtectedLocalStorage` via `IKioskKeyStore`). Depending on the switches it shows the entrance code (`QrSvg`, QRCoder), scans passes with the camera or a USB/Bluetooth scanner that types like a keyboard, both side by side, or "check in at the front desk". It never shows the photo or the name behind a copied or forged pass.
- **Against handing the phone over** (member pass and entrance code):
  - **Phone unlock**: `GymSettings.RequirePhoneUnlock` (default on, Settings → Check-in methods). The app asks `IPhoneUnlock` (MAUI `MauiPhoneUnlock`: Android BiometricPrompt with screen-lock fallback, API 29+; iOS/Mac LocalAuthentication; Windows Hello) before showing the pass (it hides again after 60 s), scanning, moving or resetting. No screen lock set up → refused; platform cannot check → allowed with a note. Client-side control: the server cannot verify it.
  - **One phone per pass**: the app sends a random installation id (`X-Phone-Id`, `IMemberPassStore.GetPhoneIdAsync`); `MemberProfile.PassPhoneIdHash` binds the first phone. Another phone gets 423 for `GET api/me/pass` and `POST api/me/check-ins` until `POST api/me/pass/move-here`, which resets the pass (old phone's codes die), emails the member and audits `PassMovedToAnotherPhone`. Staff reset clears the binding.
- **Fingerprint terminals** (`Gym/Access/*`, module `gym.access`): ZKTeco push protocol (ADMS/iclock) at `/iclock/cdata|getrequest|devicecmd` (`ZkTecoController`, plain text, anonymous, rate limit `device`; HTTPS redirection is skipped for `/iclock`, and Caddy serves `http://api.…/iclock/*`).
  - A terminal is a `CheckInDevice` with `Kind = ZkTecoTerminal`, registered by an admin by serial number (unique platform-wide, `POST api/gym/access/terminals`), optional `AllowedIp`. `TerminalProtocolService.AuthenticateAsync` finds it by SN (`ICheckInDeviceDirectory`), checks access and module, and sets the device context with permission `gym.access.terminal` only.
  - **Migration** of an existing installation: on first contact the terminal is sent `DATA QUERY USERINFO`; its OPERLOG upload (USER lines) creates `TerminalUser` rows (PIN, terminal name, `HasFingerprint`). FP/BIODATA templates are parsed only to set `HasFingerprint` and dropped (`ZkTecoProtocol.ParseUsers`); tests assert no template reaches the database. Also CSV import (`PIN,Name`). Staff link PINs to members on `/access/terminal-users` (exact normalized-name suggestions, bulk "link all", "Not a member" for staff, "add a member" = next PIN, then enrol a finger on the terminal).
  - **Sync / offline decisions**: on a poll at most once a minute (immediately after a link change), `SyncAsync` compares `TerminalAccessRules.For` (CheckInPolicy + end of the grace period of the next or oldest overdue invoice) with `TerminalUserSync` and queues `DATA UPDATE USERINFO PIN=…\tName=…\tStartDatetime=…\tEndDatetime=…` (`TerminalCommand`). A refused member gets yesterday as end date, so the terminal refuses them by itself offline. Unlinked/ignored users are never touched; IronForge never sends `DATA DELETE` (it would delete fingerprints). Field support (StartDatetime/EndDatetime) varies by firmware: verify on the gym's model.
  - **Entries**: ATTLOG lines become `CheckIn` (`Method = Terminal`, device, gym-local time converted to UTC, 30-minute duplicate window, unknown PINs listed for linking). If the rules would have refused, the visit is kept with `CheckIn.Warning`, audited `TerminalEntryNotAllowed`, and the terminal is synced at once.
  - **Simulator** (`/access/terminal-simulator`, `UI.Admin/Simulator`, `TerminalSimulatorHub` singleton in the Web host, `TerminalSimulator:Enabled`): a virtual terminal speaking the same protocol to the API, with its own user list, offline switch and clock offset. Demo: Iron Paradise has the add-on and a registered terminal `SIMPARADISE01`.
- **Member app camera** (entrance code): the shared `QrScanner` runs in the BlazorWebView. Android: `CAMERA` permission plus `CameraWebChromeClient` (grants only video capture, only to the app's own pages, asks for the runtime permission; forwards file choosing etc. to the original client), hooked in `MainPage.xaml.cs`. iOS/Mac: `NSCameraUsageDescription` and inline media playback. Windows: `webcam` capability.
- **Member photos** (`MemberPhotoService`, `MemberPhoto` table, bytea, ≤ 1 MB): JPEG/PNG/WebP detected from the bytes; clients shrink to 640 px first (`PhotoPicker`).
  - Staff can set or remove a photo (`api/gym/members/{id}/photo`); a staff photo is locked for the member. A member may set their own (`api/me/photo`) until staff set one.
  - Shown on the check-in result. Anonymizing deletes it.

### 4.3 Billing (`Gym/Billing/*`)
- **`IBillingEngine`** holds the rules with no authorization. Its methods mutate tracked entities but **do not save**; the caller saves once, so the change, notifications and emails commit together.
  - `IssueNextInvoiceAsync(gym, member, plan)`:
    - period [NextBillingDate ?? today, PeriodEnd(start, interval)]
    - due date = max(start, today)
    - number from `IInvoiceNumberGenerator`: an atomic SQL `UPDATE "GymSettings" SET "LastInvoiceNumber" = "LastInvoiceNumber" + 1 ... RETURNING`, formatted `INV-000123`
    - advances NextBillingDate
    - notifies the member
  - `ApplyPayment(...)`: full payment only. Invoice → Paid, a `Payment` row is added, PaidThrough is extended, Pending or Expired → Active, and a receipt notification is sent.
  - `QueueReminder(gym, invoice, member, kind)`: adds an `InvoiceReminder` (unique per invoice and kind) and a notification plus email.
  - `RunAsync(tenantId)`, the daily rules, each idempotent:
    1. Unfreeze members whose freeze has ended.
    2. Issue invoices for periods starting within `InvoiceLeadDays`, catching up at most 3 periods.
    3. Open invoices past their due date → Overdue.
    4. Members overdue for more than `GracePeriodDays` → Expired, with a notification.
    5. Reminders `before:{ReminderDaysBeforeDue}` and `after:{n}` for each n in `ReminderDaysAfterDue`, deduplicated.
- **`BillingBackgroundService`** runs hourly and only once across instances (`DistributedLock` = Postgres `pg_try_advisory_lock`). It loops over `IGymDirectory.ListServedTenantIdsAsync` (tenants with access), one scope per tenant with the tenant execution context set.
- **`BillingService`** wraps the engine with authorization:
  - staff: list invoices, record payment (Cash or BankTransfer only; the amount must equal the total), void, remind, payments, revenue
  - member: my invoices, my payments, `StartMyCheckoutAsync`
- **Payments**:
  - `IPaymentGateway` is `StripePaymentGateway` when `Stripe:SecretKey` is set, otherwise `SimulatedPaymentGateway`.
  - **Stripe**: Checkout Session with metadata `tenant_id` and `invoice_id`, using a direct charge on the gym's Connect (Standard) account when `GymSettings.StripeChargesEnabled`. Connect onboarding: `POST api/gym/settings/stripe/connect` returns an AccountLink, and the account id is stored; `.../stripe/refresh` checks `ChargesEnabled`.
  - **Webhook** `POST api/payments/stripe/webhook`: `StripeWebhookHandler` verifies the signature. For `checkout.session.completed` with paid status, it sets the tenant context from metadata and calls `IPaymentConfirmationService.ConfirmCheckoutAsync`.
  - **Confirmation**:
    - idempotent via `ProcessedWebhookEvent`
    - a transaction around the work
    - amount and currency must match the invoice
    - an invoice already paid or voided is audited and ignored
  - **Simulated**: the checkout URL is `{ApiBaseUrl}/payments/simulated?tenant&invoice&session` (an HTML page with a Pay button). The POST confirms through the same confirmation service and redirects to `/payments/complete?status=…`. The session id is a random value stored on `Invoice.CheckoutSessionId`. In non-Development environments the API logs a warning that payments are simulated.

### 4.4 Fitness / coaching (module `gym.coaching`)
- **Entities**: `Exercise` (TenantId null = shared library), `WorkoutRoutine` → `RoutineDay` → `RoutineExercise`, `WorkoutLog` → `WorkoutSet`, `BodyMetric` (one per day; a second entry that day replaces the first), `FitnessGoal` (at most 5 active). Every child row carries a TenantId for filtering.
- **Services**:
  - `RoutineService`: library, gym-custom exercises, trainer assign (notifies the member), member create (Source Self or Ai).
  - `ProgressService`: log workout (validates exercise ids and routine-day ownership; set numbers per exercise), metrics, goals, progress summary (weekly volume for 12 weeks, weekly streak via `GymCalendar.WeeklyStreak`).

### 4.5 Notifications and email
- **`INotificationService.Queue(tenantId, recipients, NotificationMessage)`** adds in-app `Notification` rows (needs a UserId) and `EmailOutboxMessage` rows (needs an email and `SendEmail`). It **does not save**; the caller's save commits it.
- `EmailTemplate.Render` produces HTML-encoded email bodies.
- Inbox API: list, unread count, mark read, mark all read.
- **`EmailOutboxProcessor`** runs every 15 s, once across instances (advisory lock). It sends batches of 25 through `IEmailSender`:
  - `SmtpEmailSender` (MailKit) when `Email:Host` is set, otherwise `LoggingEmailSender`
  - backoff 1, 2, 4, 8, 16 minutes; Failed after 6 attempts
- **`AnnouncementService`**: audience All, Active, Overdue, or one Plan; in-app plus optional email.
- **`PasswordResetService`**: one-hour hashed token, always 202 (no account enumeration), the reset revokes every refresh token.

### 4.6 AI agents (`Application/Agents/*`)
- **Runtime**:
  - Agents are DB rows (`Agent` plus `AgentToolBindingEntity`) per tenant, created from code templates `AgentDefinitions` / `AgentTemplates.Default`. Ids are in `AgentIds`: `gym-admin`, `fitness-coach`, `insights-agent`.
  - `AgentRuntime` uses Microsoft.Extensions.AI on OpenAI (Responses API), model profiles (`standard`, `economy`) and the tenant token quota (`AgentQuotaService`, 429 when exceeded).
  - `AllowedRoles` per agent:
    - gym-admin: Admin, Trainer
    - fitness-coach: Member
    - insights: Admin
- **Tools**: `AgentTool` subclasses with an `AgentToolDescriptor` (Name, Description, RequiredPermission, Module, RiskLevel, RequiresApproval, MaximumAmount).
  - Handlers call domain services and return `GymToolResult.From(serviceResult)`.
  - Use **string parameters for enums** and parse them.
  - Tool errors are returned to the model as `{Success=false, Error}` by `ConversationToolExecutionService`.
- **Governance** (`AgentToolRegistry` + policies):
  - The model is offered only tools the user may use.
  - `UserPermissionPolicy` → `AgentApprovalPolicy` → `MaximumAmountPolicy`, which reads an argument named `amount` or `price`.
  - Approval: Medium risk needs the requester's own approval. High and Critical need a second user with `agents.approvals.decide` (four-eyes, `ApprovalQueueService`).
  - MCP agents cannot run tools that need approval.
- **Gym Admin tools**:
  - read: search_members, get_member_details, get_dashboard_summary, get_attendance_summary, list_expiring_memberships, list_overdue_invoices, get_revenue_summary
  - coaching (optional module, gym.members.read so Trainers can use them): list_exercises, get_member_routines
  - The instructions tell every agent to call tools again for each data question rather than reuse numbers from earlier messages.
  - freeze_membership (Medium, approval)
  - send_payment_reminder (Medium, approval)
  - record_cash_payment (High, approval; binding MaximumAmount 2000)
  - send_announcement (High, approval)
- **Fitness Coach tools** (all self-scoped, Low risk): get_my_profile_and_goals, get_my_routines, get_exercise_library, get_my_workout_history, get_my_progress, log_workout, record_body_metric, set_fitness_goal, create_routine_for_me, get_my_membership_and_invoices.
- **Adding a tool**:
  1. Create the class in `Gym/AgentTools`.
  2. Add it to `GymAgentToolCatalog.Types`.
  3. Bind it in an `AgentDefinition`.
  4. Bump `AgentTemplate.Version`.
  5. Update the template `Modules` (or `OptionalModules`) to match.
  6. Run the rollout from the Platform console.

### 4.7 Platform operator
`PlatformController` exposes `api/platform/overview|tenants/{id}|catalog|audit`, backed by `PlatformInsightsService` (permission `platform.tenants.manage`, cross-tenant). Tenant mutations stay in `PlatformTenantsController` (`api/platform/tenants/*`).

---

## 5. Front ends

- **Shared client registration**: `services.AddIronForgeApiClients(new ClientAppProfile { Audience, AppName, HomeRoute })`.
  - It registers every API client, wrapping the JWT handler (`JwtAuthorizationHandler`: attach the token, refresh once on 401, retry) **in the UI's own DI scope**, on top of the named HttpClient `IronForgeApi`.
  - The host registers that named HttpClient, `ApiSettings`, `ITokenStore` and, optionally, `IExternalLinkOpener` **before** calling it.
- **Audience routing** (`ClientAppProfile`):
  - Login → `Platform`: requires the platform permission and goes to `/`. Gym apps go to `/select-tenant`, which shows only gyms whose role `AcceptsRole` (Staff = Admin or Trainer, Member = Member), auto-selects a single gym, then goes to `HomeRoute`.
  - A wrong-app user sees `WrongAppMessage`.
- **Layouts** (`MainLayout`, `MemberLayout`) re-evaluate in `OnParametersSetAsync`, because a layout survives navigation. They must **render `@Body` when signed out**, so the router's `<NotAuthorized><RedirectToLogin/>` content runs. They show a "Loading" state only while a navigation is in flight. Ignoring this caused an infinite "Loading…" bug.
- **Routes**: each host's `Routes.razor` lists Shared plus its own RCL in `AdditionalAssemblies` and sets the RCL layout as `DefaultLayout`. Routes must not collide across the assemblies a host loads. Login is `/login` only; `/` belongs to the app RCL.
- **Styling**: Bootstrap plus `ironforge-app.css` tokens and classes:
  - `if-page`, `if-card`, `if-stat`, `if-table`, `if-badge`, `if-btn`, `if-field`, `if-modal`, `if-chat`
  - mobile `m-*`
  - platform overrides `platform-theme`

  Charts are dependency-free SVG components. Razor reserves `<text>`, so SVG labels go through `Svg.Text(...)`.
- **Web hosts**:
  - Interactive Server, prerender off (the JWT is in `ProtectedLocalStorage` via `WebTokenStore`).
  - Page endpoints `AllowAnonymous`; authorization happens in the circuit.
  - Optional `DataProtection:KeysPath` persists keys.
- **MAUI**:
  - Tokens in `SecureStorage` (`SecureTokenStore`).
  - Payment checkout opens in the system browser (`MauiLinkOpener`).
  - API URL: Debug uses `https://localhost:7226/`. Release reads `Resources/Raw/appsettings.json` (`ApiSettings:BaseUrl`), defaulting to `https://api.mohtashim.dev/`. On the Android emulator, `localhost` is rewritten to `10.0.2.2`; a physical device keeps `localhost` and uses `adb reverse tcp:7226 tcp:7226`.
  - The dev certificate (CN=localhost) is trusted only for localhost, 127.0.0.1 and 10.0.2.2.
  - **Windows** runs **unpackaged** (`WindowsPackageType=None`, self-contained Windows App SDK) when not publishing, so no Developer Mode is needed. Publishing produces a signed MSIX (thumbprint in the csproj, skipped when `CI=true`). `launchSettings.json` uses `commandName: Project`.
  - **Android**: Release produces a signed AAB when `%USERPROFILE%\IronForgeSigning\ironforge-release.keystore` exists.
- **Client claims**: `JwtClaims.Normalize` maps short JWT claim names (role, unique_name) to `ClaimTypes.*`; the server currently writes the full names.

---

## 6. Configuration

### 6.1 API (`IronForge.Api/appsettings*.json` + environment / user-secrets)

| Key | Notes |
|---|---|
| `ConnectionStrings:DefaultConnection` | From secrets or the environment. The local `.env` points at a **remote Supabase DB**: never wipe or seed it without explicit user approval. |
| `JwtSettings:{Key,Issuer,Audience,ExpirationMinutes}` | Key from secrets/env (≥ 32 characters) |
| `AISettings:{ApiKey,Model,ModelProfiles}` | OpenAI |
| `AgentQuotas:DefaultMonthlyTokenLimit` | |
| `CheckIns:PassSigningKey` | Master key for member pass secrets (≥ 32 characters). Empty uses `JwtSettings:Key`; changing either invalidates every pass until the app is online again |
| `AppUrls:{WebBaseUrl,ResetPasswordUrl,ApiBaseUrl}` | Used in emails, Stripe redirects and simulated pay pages. Dev: web `https://localhost:7056`, API `https://localhost:7226` |
| `Invitations:{AcceptUrl,LifetimeDays}` | |
| `Stripe:{SecretKey,WebhookSecret}` | Empty selects the simulated gateway |
| `Email:{Host,Port,Security(None/StartTls/SslOnConnect),Username,Password,FromAddress,FromName}` | Empty Host logs emails. Dev uses Mailpit `localhost:1025` (UI `:8025`) |
| `BillingJob:{Enabled,IntervalMinutes}` | |
| `Seed:Demo` | Development only: migrate and seed on startup |
| `Cors:AllowedOrigins` | Dev: 7056, 5033, 7090, 5090. Prod: `https://mohtashim.dev`, `https://platform.mohtashim.dev` |
| `OpenTelemetry:Otlp:*` | Grafana Cloud; headers from env |
| `ReverseProxy:TrustedProxyIp` | |

Rate-limit policies (`RateLimitPolicies`): `auth` 10/min (also kiosk pairing), `anonymous` 30/min, `webhook` 300/min, `device` 120/min per IP.

### 6.2 Front ends
- `ApiSettings:BaseUrl` (Web/Platform: dev `https://localhost:7226/`, prod `http://ironforge-api:8080`)
- `DataProtection:KeysPath` (prod `/keys`)
- `Terminals:ServerAddress` (shown in the terminal setup steps; prod `api.mohtashim.dev`), `TerminalSimulator:Enabled`
- Local ports: Web 7056/5033, Platform 7090/5090, API 7226/5022

### 6.3 Environment templates
`.env.example` and `deploy/production.env.example` contain connection string, JWT key, AI key, OTLP headers, invitation URL, Stripe keys and email settings.

---

## 7. Development workflow

```bash
docker run -d --name ironforge-db -e POSTGRES_PASSWORD=dev -p 5432:5432 postgres:17
docker run -d --name ironforge-mail -p 1025:1025 -p 8025:8025 axllent/mailpit:v1.21
export ConnectionStrings__DefaultConnection="Host=localhost;Database=ironforge;Username=postgres;Password=dev"
DOTNET_ENVIRONMENT=Development dotnet run --project IronForge.Migrator -- --seed-demo --reset
dotnet run --project IronForge.Api --launch-profile https      # then Web / Platform / MAUI
dotnet test IronForge.Server.slnf                              # 269 tests; Docker required
```

- **Demo data** (`DemoDataSeeder`): deterministic `Random(42)`, idempotent (skips when `operator@ironforge.dev` exists), refuses to run in Production without `--force`. Every password is `Password123!`.
  - operator `operator@ironforge.dev`
  - gym accounts `admin@<slug>.demo`, `trainer1@<slug>.demo`
  - showcase member `member@ironparadise.demo`
  - each gym has a paired "Front entrance kiosk" whose key was never kept: issue a new pairing code under Check-in devices to use `/kiosk`. Check-ins use all four methods (gym QR only at Iron Paradise, the only demo gym with the entrance code switched on); frozen/expired members have refused kiosk attempts.

| Gym (slug, join code) | Plan | Status | Members |
|---|---|---|---|
| Iron Paradise Gym (`ironparadise`, `PARADISE`, PKR, Asia/Karachi) | Professional | Active | 35 |
| FlexZone Fitness (`flexzone`, `FLEXZONE`, USD) | Starter | Trial, ends in 5 days | 15 |
| Titan Strength Club (`titanstrength`, `TITAN777`, GBP) | Enterprise | PastDue | 25 |
| Old Barn Gym (`oldbarn`, `OLDBARN1`) | Starter | Canceled | 5 |

- **Tests**:
  - unit: rules, calendar, role matrix, template/module consistency
  - integration (`TestServiceFactory` builds real services over one DbContext; `TestClock`, `TestPaymentGateway`):
    - tenant isolation and member self-scoping
    - joining, billing job, payments idempotency
    - governance, seeder, platform insights

  Add tests for every new rule.
- **Tooling quirks on this Windows machine**:
  - Bash heredocs whose body contains apostrophes fail to parse. Write a Python or edit script to the scratchpad and run it, or use Edit/Write.
  - Avoid building while Visual Studio is building the same project (file locks in `obj`); use `--artifacts-path` with a **short** path, since Android aapt2 fails on long paths.

---

## 8. Production / deployment

- **CI** (`.github/workflows/ci.yml`):
  - build and test `IronForge.Server.slnf`
  - vulnerable-package check (keep transitive packages patched, e.g. `System.Security.Cryptography.Pkcs` is pinned)
  - publish Api, Web, Platform, Mcp, Mcp.Client
  - docker compose config and image builds (api, web, platform, mcp, mcp-client, migrator)
  - unsigned MAUI Android and Windows Release compile checks
- **CD** (`cd.yml`): push images to GHCR `ghcr.io/<owner>/ironforge-*:<sha>`, then SSH to the VPS and run `deploy/scripts/deploy.sh <sha>`, which pulls, migrates, starts, health-checks and rolls back on failure.
- **`deploy/compose.production.yml`**: api, web, platform, mcp, mcp-client, migrator, caddy. Volumes `web_keys` and `platform_keys` hold the Data Protection keys.
- **`deploy/Caddyfile`**: `mohtashim.dev` → web, `platform.` → platform, `api.` → api, `mcp.` and `mcp-client.`; all with security headers (HSTS, nosniff, DENY frames, referrer and permissions policies). Only the gym dashboard (`security_headers_camera`) allows `camera=(self)`, for pass scanning; the others block it.
- **Local `compose.yaml`**: same services plus Mailpit; Caddy serves `localhost`, `platform.localhost`, `api.localhost`.
- **Before onboarding real gyms**:
  - set Stripe keys and webhook secret (Connect enabled)
  - set SMTP credentials
  - ensure production CORS and AppUrls are correct
  - run the migrator (never seed production)
- **API health**: `/health/live`, `/health/ready` (database).

---

## 9. Rules for agents working on this repo

1. Keep within the locked v1 scope unless the user expands it.
2. Put business rules in Application services, authorization in services through `IGymAccessService`, and data access through `IRepository<T>.QueryAsync`. Bypass tenant filters only in dedicated, permission-checked Infrastructure stores.
3. Every new tenant-owned entity needs a TenantId, a query filter, a tenant FK, indexes, a migration and an isolation test.
4. Self-service APIs never accept member ids; they resolve the caller.
5. Side effects (notifications, emails, reminders) go through `Queue` and the caller's single save. Never send email inline.
6. Use `TimeProvider` and `GymCalendar` for dates; UTC instants; `DateOnly` in gym-local time for calendar values.
7. Never expose secrets. Never wipe or seed a non-local database without explicit approval. Do not commit unless asked.
8. After changes: `dotnet build IronForge.Server.slnf` and `dotnet test IronForge.Server.slnf`. For MAUI, build per target framework (`-f net10.0-android` / `-f net10.0-windows10.0.19041.0`).
9. Plan and progress history: `C:\Users\mohta\.claude\plans\i-want-to-turn-functional-wren.md` (approved plan). The user's README has run instructions and the demo matrix.
