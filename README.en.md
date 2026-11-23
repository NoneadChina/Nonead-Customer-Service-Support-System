# NoneadCSS — Customer Service & Support System

> English counterpart of [README.md](README.md) (简体中文). Numbers, paths and commands below are identical to the Chinese README; project documents under `docs/` are currently Chinese-only and are marked **(Chinese)** where they are linked.

> **Version v2.3.0 · 2026-10-23** (current development batch: four new IM channels — Messenger / Telegram / LINE / iMessage — plus direct WeChat customer-service integration, bilingual zh/en OpenAPI across the whole site, and open-source preparation. Batch details live in the *Unreleased* section of [CHANGELOG.md](CHANGELOG.md); no tag has been created yet.)

NoneadCSS (nCSS) is a modern customer service and support system built on .NET 10. It provides end-to-end ticket (conversation) management, mailbox integration and workflow automation, covering 10+ customer channels: Email, online chat, WeChat Official Account, WhatsApp, Messenger, Telegram, LINE, iMessage, DingTalk and Feishu (Lark).

## Features

### Core features
- **Ticket management** — full conversation lifecycle: create, assign, reply, close, clone; bulk operations such as bulk assign, bulk close, bulk priority update and bulk delete; collaboration via internal notes, starring and tags
- **Multi-channel support** — conversations can originate from Email, Phone, Chat, WeChat Official Account, WhatsApp, Facebook Messenger, Telegram, LINE, iMessage, DingTalk and Feishu; customers and conversations from every channel are managed under one model with one shared workflow trigger layer; quick source badges are shown dynamically according to each module's enabled state
- **Email integration** — automatic inbound fetching and outbound sending over SMTP / POP3 / IMAP
- **WeChat Official Account integration** — WeChat messages are pushed into the support system with bidirectional sync; all message types (text, image, voice, video, location, link, events); automatic upload of temporary media (image ≤ 2 MB, voice ≤ 2 MB, video ≤ 10 MB, 3-day validity); automatic customer profile creation (nickname / avatar / OpenID); visualised 48-hour customer-service messaging window (ok / warning / expired three-tier coloured progress bar, with template messages and subscribe notifications as fallback); follow welcome message returned as an XML passive reply; new subscribe-notification mode (`sendSubscribe` API plus a `page` field for page redirects); **Notification messages tab** (switch between the legacy template-message mode and the new subscribe-notification mode, conversation search picker instead of manual ID entry, three preset templates for ticket accepted / resolved / reply reminders, live JSON validation plus a `color` field for coloured card preview, last 10 send history entries); **Custom menu tab** (visual editor with two-way JSON sync, form-based add/delete/edit with ≤3 first-level and ≤5 second-level items, schema validation of required fields by `type` — click / view / scancode_* / pic_* / location_select / media_id / article_id / miniprogram — phone preview simulating WeChat's bottom menu bar with hover expansion of second-level items, fetching the live menu plus one-click load into the editor for modification, then publish / delete)
- **WhatsApp Business Platform integration** — WhatsApp Business via the Meta Cloud API; Webhook HMAC-SHA256 signature verification; inbound support for text, image, audio, video, document, sticker, location, contact and interactive button messages; media replies (image / audio / video / document); visualised 24-hour customer-service window (progress bar plus template-message fallback outside the window); automatic customer profile creation from `wa_id`; template message sending (reach outside the window); delivery receipts (sent / delivered / read) plus read receipts; AES-encrypted storage of sensitive configuration; timer optimisation (chained `setTimeout` instead of `setInterval`, removing leaks and drift)
- **DingTalk integration** — DingTalk enterprise internal application access; configuration items (AppKey, AppSecret, AgentId, RobotCode, CallbackToken, EncodingAESKey) plus message-receiving mode (HTTP callback / Stream WebSocket long connection) and a one-click module start button; in Stream mode the connection credential is registered automatically, with heartbeats, reconnection on disconnect and ACK receipts; module registration (Modules table plus `dingding.view` / `dingding.manage` permissions); the sidebar "DingTalk chat" entry is shown dynamically according to the module's enabled state; bidirectional message sync covering text, emoji, image, voice, video and file; automatic two-way emoji conversion (DingTalk `[微笑]` ↔ Unicode emoji); customer images are downloaded automatically and attached (DownloadCode → download URL → local storage → Attachment record); agent replies support text plus multiple attachments — images are uploaded through the oapi `media/upload` endpoint into an `@media_id` and sent as the flat `sampleImageMsg = {"photoURL":"@media_id"}` structure, files as `sampleFile = {"mediaId":"@…","fileName":"xxx","fileType":"xlsx"}`; agent avatar and nickname follow the user profile (`UserId` bound when the thread is created); when `api.dingtalk.com` is unreachable over IPv6, a custom `SocketsHttpHandler.ConnectCallback` forces IPv4 resolution; configuration is cached with `IMemoryCache` (Singleton) so Scoped instances do not re-read the baseline across requests; front-end mode-selection cards plus a unified "configured / lock icon" layout for sensitive fields; when saving configuration `SystemOptionsRepository` uses a tracked query so `SaveChanges` does not drop the UPDATE; Vite supports `allowedHosts: true` with custom host binding (e.g. `css.example.com`)
- **Feishu (Lark) integration** — Feishu Open Platform access; module registration (Modules table plus `feishu.view` / `feishu.manage` permissions); the sidebar "Feishu chat" entry is shown dynamically according to the module's enabled state; the module title is unified as "Feishu integration" (searchable in Chinese); **receiving modes**: HTTP callback / Stream WebSocket long connection (recommended — no public IP or domain needed); **Stream long connection**: Protobuf frame encoding/decoding implemented by hand (no protobuf dependency), WebSocket establishment plus 30s heartbeat plus exponential-backoff reconnect (1 / 2 / 4 / 8 / 16s, capped at 60s) plus ACK receipts plus event dispatch (v1.0 and v2.0 events); **configuration**: App ID / App Secret / Verification Token / Encrypt Key / linked mailbox / auto-reply message; **front-end 3-tab architecture** (Quick setup / Advanced settings / Usage guide), Feishu brand blue `#3370FF` with accent `#00D6B9`, glassmorphism connection-status card, receiving-mode card switch, an enable-Feishu toggle, and conditional parameter forms (different fields per mode); **bidirectional message sync**: text, rich text (Post), image, file and interactive cards; agent replies support images (base64 + md5) and files (media upload + send), falling back to a plain text notice when upload fails; **event subscription**: `im.message.receive_v1` (message received), `contact.user.updated_v3` (user changed); **auth**: `tenant_access_token` cached and refreshed automatically (2-hour validity); **security**: event signature / token enforced, tokens redacted in logs; **configuration cache**: `FeishuSettingsProvider` Singleton plus IMemoryCache, reloading from the database on cache miss so the toggle never falls back to the baseline and switches itself off
- **Facebook Messenger integration** — Messenger via the Meta Graph API; dual Webhook validation (`hub.challenge` subscription verification plus `X-Hub-Signature-256` HMAC-SHA256 constant-time comparison); inbound text, image, audio, video, file, sticker and location, with `delivery` / `read` receipts persisted; text is automatically split at the Graph limit of 2000 UTF-8 characters; visualised 24-hour customer-service window plus message-tag policy (`HUMAN_AGENT`, `ACCOUNT_UPDATE`, …); inbound media is stored as it arrives (`ResponseHeadersRead` plus an incremental byte cap — over the cap only a placeholder text is kept); PSID customer profiles are created automatically, multi-Page events are filtered by `PageId`, and work is serialised per PSID to avoid duplicate conversations; inbound failures return non-2xx so Meta retries; `messenger.view` / `messenger.manage` module permissions
- **Telegram integration** — Telegram Bot API; Webhook mode (with `X-Telegram-Bot-Api-Secret-Token` verification plus a 1 MiB body cap, secret verified before the body is read) and long-polling mode (a failed handler does not advance `offset`, so updates are not lost); inbound text, image, file, voice and video; streaming download of inbound media (double pre-check on getFile `file_size` and `Content-Length`, plus an incremental cap); outbound 4096-character splitting plus a global 25/s and per-conversation 1/s rate limit (local 429/420 backoff, with queue and agent replies sharing the same send path); `parse_mode` (HTML / Markdown / MarkdownV2) passed through end to end; forum topics land on the right thread via `message_thread_id`; 403 automatically marks a conversation unreachable; `telegram.view` / `telegram.manage`
- **LINE integration** — LINE Messaging API; `X-Line-Signature` HMAC-SHA256 verification; one-shot replies via replyToken (marked `replyTokenUsed` after a successful send, avoiding reuse that causes 409 and wasted quota) plus a push fallback; inbound text, image, video, audio, file, location and sticker; quota parsed according to the official `{"type":"limited"|"none","value":N}` contract and visualised together with monthly consumption; when `mode=standby` (channel standby, another system answers) automatic replies are skipped; follow / unfollow / join / leave event notes are deduplicated by `message.id`; inbound failures return 500 so LINE retries; `line.view` / `line.manage`
- **iMessage integration** — dual mode: BlueBubbles bridge (send/receive by `chatGuid`, attachments, tapbacks and receipts) and Apple Messages for Business (AMB MSP); BlueBubbles Webhook shared-secret check (`X-Webhook-Secret` header or `?secret=`, constant-time comparison); AMB inbound goes through `/v1/preDownload` plus AES-CTR attachment decryption (with no key it explicitly reports "not stored" rather than claiming it was saved); AMB outbound uses the official envelope `{v,id,sourceId,destinationId,locale,body}`, with the message `id` derived deterministically from the queue identifier so retries never duplicate delivery; send results are classified by HTTP status, with 400 / 403 / 404 / 410 treated as terminal and never retried; `imessage.view` / `imessage.manage`
- **Customer management** — full customer profile management with custom fields, bulk delete and bulk CSV export; OpenID / UnionID linkage merges the same customer across channels
- **Team collaboration** — multi-user, team management and role-based permissions; SignalR dual-hub cross-user conversation refresh (every agent in the same mailbox group sees new replies in real time; calendar changes on multiple clients coordinate through SignalR plus a TTL protection bit for local optimistic updates, preventing "stale backend data bouncing back" after an optimistic edit)
- **Drafts** — conversation replies can be saved as drafts, restored automatically and kept across conversations

### Advanced features
- **Workflow automation** — condition-triggered rule engine (12 trigger types + 11 action types, including the `execute_macro` action)
- **SLA policies** — service-level agreement management and breach monitoring
- **Macros** — one-click multi-step commands, also runnable over bulk conversations through workflows
- **Webhook integration** — events pushed to external systems in real time
- **Satisfaction ratings** — 5-star service quality assessment and feedback (including a post-rating thank-you email workflow and dual-path token verification: email & ChannelId, plus a chat-channel push fallback for customers without an email)
- **Virus scanning** — automatic scanning of uploaded attachments and email attachments, blocking malicious and executable files
- **Notification center** — unified in-app notification management with templates, rules and a recycle bin
- **Custom reports** — visual report builder with custom data sources, fields, filters and chart types; scheduled dispatch (Quartz trigger) plus email subscription delivery; PDF / Excel export; **soft delete + trash** (deleted reports go to the trash and can be restored, the trash tab carries a count badge, single restore / permanent delete / empty trash are supported with a second confirmation dialog against accidental clicks; permission checks: ordinary users may only act on reports they created, SuperAdmin may act on all reports)

### System features
- **Real-time notifications** — SignalR dual-hub architecture (NotificationHub for agents + ChatHub for customers) pushing conversation updates, chat messages, @mentions and typing indicators
- **Data import / export** — bulk import and export
- **Audit log** — full operation audit and tracing; **multi-dimensional filtering** (action type / entity type / user ID / entity ID / IP address / keyword search / date range); **global aggregate statistics** (a separate `/stats` endpoint returning total records / distinct users / additions in the last 24h / abnormal events plus top-N action and entity distributions, independent of the current page); **CSV export** (with BOM for Excel friendliness, up to 10000 rows); **HMAC-SHA256 integrity verification** (single and batch, tamper detection, results written back to the `isTampered` field and visualised as ShieldX / ShieldCheck icons plus row background highlighting; v2.2.10 fixed the signature-always-fails issue caused by inconsistent `CreatedAt` Kind and re-signed the 56887 existing records); **related logs** (same entity first, same user as fallback, for tracing operation context); **front-end Chinese labels fully filled** (50 actions + 61 entity types mapped, plus `calendar.` / `time_entry.` prefix fallbacks, so statistic cards no longer show English); OutputCache policy includes every filter parameter to avoid cache-hit collisions
- **Email templates** — customisable notification templates (system templates + custom templates, 14 built-in templates)
- **Internationalization** — Simplified Chinese and English
- **System settings** — system info, personal center, module management, localization
- **Recycle bin** — soft-deleted data can be viewed, restored and permanently deleted; 15 entity types support soft delete (conversations, customers, tags, email templates, custom reports, …)
- **OutputCache** — response caching for read-only data APIs
- **Global search** — unified search across conversations, customers and the knowledge base; **knowledge results jump straight to the article (T000109)** — clicking a search result opens `/knowledge?article={id}` and the article dialog automatically (the old `/knowledge/{id}` route had no handler and returned 404)
- **Rate limiting** — multi-tier rate-limit policies (global / search / reports) against abuse
- **Role permission system** — fine-grained role-based permissions (Super Admin / Service Supervisor / Service Agent / Read-only), with mailbox-level permission isolation
- **Team management** — member management and role assignment, conversations assignable per team
- **Calendar management** — agent schedule and event management with month / week / day / list / overview views; event types (deadline / task / meeting / reminder / personal / travel / work / other) plus multi-mailbox filters; state machine (pending / completed / cancelled); daily / weekly / monthly / yearly recurring schedules plus recurrence exclusions; multi-channel reminders (in-app + email + SMS); ICS import/export (RFC 5545); HMAC-token calendar subscription; **per-user Outlook Azure AD credentials (T000099)** (independent AppId / TenantId / ClientSecret / RedirectUri per user, fixing the 400 configuration error in the OAuth consent dialog); **attendee save and refill fixes (T000100)** (`AttendeesJson` deserialization tolerance via SafeDeserializeList plus complementary double writes in NormalizeAttendeesAndReminderEmails, front-end attendee email merge in openEdit and empty-array clearing in EditorPanel); **global optimistic concurrency** (RowVersion + If-Match / 409 with a latest snapshot written back); soft delete into the recycle bin; **multi-select bulk operations** (bulk delete / restore / change type / change state); **full drag support in month / week / day views** (drag the bottom edge to change duration, drag the whole block to move, cross-day splitting display); **persistent Undo/Redo** (localStorage isolated per user, 50 entries, automatic rollback of the optimistic change on failure); **view position persistence** (stay in the original month/week/day view and date after refresh, week starts on Sunday); **day-view data coverage fully fixed** (incremental merge plus a ±45-day window, events identical across the three views); **bidirectional optimistic updates across the three views** (calendar drag ↔ list edit ↔ overview KPI update instantly; failures roll back to `snapshotBefore`; after a successful API call the latest DTO overwrites the optimistic value a second time and the overview `upcomingEvents` refreshes, fixing "bounce-back after save"); event conflict detection; subtasks; automatic deadline linkage; a random colour assigned per new event; **live Chinese preview of recurrence rules** plus weekday shortcut buttons (weekdays / every day / clear); **reminder type selection** (in-app / email / SMS) with a conversion hint for custom reminder times; **attendee email format validation** plus a clear button and focus retention after adding; **creator shown in the colour-block hover popup**; **full refresh on SignalR reconnect** (avoiding message loss during reconnection) plus automatic reconnect while the page is visible; MemoryCalendarQueryCache Singleton lifetime fix
- **Knowledge base** — multi-level category tree (16-level depth guard, enable/disable isolation), three-state machine (draft / published / archived), FTS5 full-text search with results shown directly in the main area (title / summary / body / tags), **like / unlike toggle (T000101)** (atomic deduplication in the KnowledgeArticleLikes table plus an `IsLiked` echo — a liked button becomes solid, clicking again un-likes and decrements the count), view statistics, Markdown editor (marked + highlight.js + Tailwind style restoration; the 6 formats list / bold / italic / link / image / inline code render correctly), attachment consistency (unsaved attachments kept in localStorage drafts, identical in edit and view modes), mailbox isolation (shared + dedicated categories), disabled categories filtered in the front-end on article filtering / creation / editing plus a second check in the backend write layer, HTML sanitisation against XSS, trash restore
- **Online chat** — real-time chat sessions; message recall (2-minute window), message editing (edited marker), read receipts (double / single tick), typing indicator (2-second throttle), @mentions (toast notification + jump), emoji panel (24 emojis), quick replies, message search, attachment sharing (25 MB), grouping by date, one-click conversion to a ticket
- **Customer chat widget** — embeddable floating widget for third-party sites (360×560) plus a fullscreen mode; anonymous authentication by sessionToken, no customer login needed; four-stage state machine (initiate → chatting → rating → completed); bidirectional real-time communication through SignalR with polling fallback; optimistic send with failure rollback; 1–5 star satisfaction scoring; sliding-window rate limiting (30 requests per IP per minute); **cross-origin embedding** (Nginx CSP `frame-ancestors` allowlist, allowing partner sites such as www.example.com to embed human support in the bottom-right corner)

### Security
- **JWT + HttpOnly Cookie** — tokens stored in HttpOnly Cookies against XSS; the JWT key is UTF-8 encoded and `ClockSkew = Zero` removes the token grace period
- **Refresh-token safety** — token rotation, reuse detection (a revoked token presented in use revokes all tokens), transactional atomic rotation
- **Login failure lockout** — 5 consecutive failures lock the account for 15 minutes against brute force
- **Password-reset safety** — after a reset all unused reset tokens are invalidated and all refresh tokens revoked, preventing session hijacking
- **Password policy** — weak-password dictionary check, common-pattern detection (sequential characters / keyboard sequences / repeated characters), BCrypt hashing
- **API Key authentication** — authentication via the `X-Api-Key` header with fine-grained scopes
- **AES-GCM encryption of sensitive data** — mailbox passwords and similar fields stored with AES-GCM + HKDF; **decryption-failure protection** (after key rotation old ciphertexts never leak into API responses — an empty string is returned); **old-key migration warnings** (startup log plus a first-fallback warning, pushing completion of the HKDF key migration)
- **Security response headers middleware** — six layers of headers: Content-Security-Policy, X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy, X-XSS-Protection
- **Production endpoint tiering** — development-only test endpoints (legacy Tracking routes, Feishu GET echo, WeChat simulated injection) automatically return 404 in production
- **CSRF middleware** — token comparison uses a constant-time algorithm (`CryptographicOperations.FixedTimeEquals`), only valid Bearer tokens are allowed through
- **SSRF protection** — Webhook URL allowlist validation, internal addresses forbidden
- **CRLF injection filtering** — email-header injection protection filtering control characters; all three email-construction paths in EmailService apply `SanitizeHeaderInput` defence-in-depth cleaning (C0 control characters removed + RFC 5322 length limits)
- **XSS protection** — front-end DOMPurify sanitisation + backend `WebUtility.HtmlEncode` for email template variables
- **Global exception message redaction** — production never returns exception details, only status-code-appropriate generic messages
- **ForwardedHeaders configuration** — trusted proxy list read from configuration instead of clearing `KnownProxies`
- **Refresh-token cookie `SameSite=Strict`** — CSRF defence
- **Nonce-based CSP** — content security policy with one-time nonces
- **SecretHasher HMAC-SHA256 signing** — secrets stored HMAC-SHA256-signed
- **Soft delete** — global query filters automatically exclude deleted data
- **Transactions** — critical operations run inside database transactions for atomicity
- **Attachment virus scanning** — uploaded and email attachments scanned, blocking malicious and executable files
- **Attachment validation** — MIME-type allowlist, extension allowlist, file-header signature checks, 25 MB size limit
- **Path traversal protection** — attachment paths strictly validated: `Path.GetFullPath` normalisation + `StartsWith` boundary check
- **Enhanced virus scanning** — first 64 KB and last 4 KB of the file scanned, covering most embedded-malware cases
- **Sensitive configuration permissions** — system options and similar sensitive configuration readable only by Super Admin
- **Front-end permission tamper resistance** — permissions fetched dynamically from the backend API, never stored in localStorage
- **Secure logout flow** — the backend logout API is awaited before local state is cleared, with localStorage fully wiped
- **Customer avatar URL protocol allowlist** — avatar URLs validated on create/update: only `https://` absolute paths, local relative paths and `data:image/*` inline images; `http://`, `javascript:`, `vbscript:`, `data:text/html`, `file://`, `ftp://`, `blob:`, `mailto:` are forbidden
- **Type-safe front-end catch blocks** — `catch (e)` error types narrowed from `any` to `unknown`, using the `getApiErrorMessage` type-guard helper instead of direct `err.message` access

## Technical architecture

### Project layout

```
NoneadCSS/
├── NoneadCSS.Api/              # Web API layer (57 controllers)
│   ├── Controllers/            # REST API controllers
│   ├── Filters/                # Filters (permissions, audit, …)
│   ├── Middleware/             # Middleware (security headers, CSRF, exception redaction, …)
│   ├── Authentication/         # Authentication handlers (API Key)
│   ├── Documentation/          # OpenAPI bilingual documentation and dictionaries
│   └── DatabaseSeeder.cs       # Database seed data
├── NoneadCSS.Application/      # Application service layer
│   ├── Configuration/          # Channel and system options
│   ├── DTOs/                   # Data transfer objects
│   ├── Services/               # Business service interfaces and implementations
│   └── Resources/              # Localization resources
├── NoneadCSS.Domain/           # Domain model layer (56 entities)
│   ├── Entities/               # Entity models
│   ├── Enums/                  # Enum definitions
│   ├── Events/                 # Strongly typed domain events
│   ├── Repositories/           # Repository interfaces
│   ├── IUnitOfWork.cs          # Unit of Work interface
│   └── ISoftDelete.cs          # Soft delete interface
├── NoneadCSS.Infrastructure/   # Infrastructure layer
│   ├── Jobs/                   # Quartz scheduled jobs
│   ├── Listeners/              # Domain event listeners
│   ├── Persistence/            # Database context and migrations
│   ├── Repositories/           # Repository implementations
│   ├── Services/               # Infrastructure services (channel clients, …)
│   └── UnitOfWork.cs           # Unit of Work implementation
├── NoneadCSS.Tests/            # Backend tests (124 test classes / 2233 cases)
├── frontend/                   # Front-end application (55 pages)
│   ├── src/
│   │   ├── pages/              # Page components
│   │   ├── components/         # Shared components
│   │   ├── context/            # Global state
│   │   ├── hooks/              # Custom hooks (useAuth, useSignalR, useConversations — React Query hooks)
│   │   ├── api/                # API client
│   │   └── services/           # Front-end services
│   └── vite.config.ts          # Vite configuration
├── scripts/                    # Ops / development scripts
│   ├── backend-test-runner/    # In-process backend test runner (works around the dotnet test Win32 limitation)
│   ├── css-api/                # Scripts and endpoint inventory for direct production API access
│   └── oss-preflight.mjs       # Open-source release preflight (secret scan / links / i18n coverage)
├── deploy/                     # Deployment scripts and Nginx configuration
├── offline_docker_package/     # Offline Docker packaging
├── data/                       # Runtime data (SQLite, attachments, logs)
├── logs/                       # Runtime logs
└── docs/                       # Project documentation (development, ops, channel integration, release notes)
```

### Backend stack

| Category | Technology |
|------|------|
| Framework | .NET 10 |
| Database | SQLite / SQL Server |
| ORM | Entity Framework Core |
| Authentication | JWT Bearer + HttpOnly Cookie + API Key |
| Real-time | SignalR |
| Scheduled jobs | Quartz.NET |
| API docs | Scalar |
| Logging | Serilog |
| Email | MailKit / MimeKit |
| Encryption | AES-GCM |

### Front-end stack

| Category | Technology |
|------|------|
| Framework | React 18 + TypeScript 5.6 |
| Build tool | Vite 8 |
| UI styling | Tailwind CSS 3 |
| Routing | React Router 6 |
| Data fetching | React Query (@tanstack/react-query) |
| HTTP client | Axios |
| Real-time | SignalR client |
| Internationalization | i18next (Simplified Chinese / English) |
| Icons | Lucide React |
| Rich text | DOMPurify |
| Testing | Vitest 3 (18 test files / 684 cases) |

## Quick start

### Requirements

- .NET 10 SDK
- Node.js >= 20.19 (or >= 22.12; required by Vite 8 — use the current LTS)
- Visual Studio 2022 / Rider
- SQLite (development) or SQL Server (production)

### Backend

```bash
# Clone the project
git clone <repository-url>
cd NoneadCSS

# Restore dependencies
dotnet restore

# Run the API
cd NoneadCSS.Api
dotnet run
```

### Front-end

```bash
cd frontend

# Install dependencies
npm install

# Start the dev server
npm run dev
```

### URLs by environment

| Entry | Development (`restart-dev.ps1`) | Production (`Production/start-prod.ps1` + `stop-prod.ps1`) | Notes |
|------|---------------------------|---------------------------|------|
| Front-end app (hot reload) | http://localhost:5171 | - | Vite dev server; proxies `/api` → backend 5008 automatically; **no Nginx needed in development** |
| Front-end app (static hosting) | - | http://localhost:5172 | `npx vite preview`; does not proxy `/api` — Nginx forwards it |
| Unified entry (Nginx reverse proxy) | - | http://localhost:5173 | proxies `/` → front-end 5172; `/api` + `/hubs` → backend 5009; WebSocket Upgrade supported |
| Backend API (direct) | http://localhost:5008 | http://localhost:5009 | `/health`, `/health/ready`, `/docs` (Scalar) |
| API documentation | http://localhost:5008/docs | http://localhost:5009/docs | Scalar interactive API docs |
| SignalR hub (agent side) | http://localhost:5008/hubs/notifications | http://localhost:5009/hubs/notifications | |
| SignalR hub (customer side) | http://localhost:5008/hubs/chat | http://localhost:5009/hubs/chat | |

> Port convention: backend **dev 5008 / prod 5009**, front-end **dev 5171 / prod 5172**, Nginx **prod 5173**. See [docs/development.md §18.8](docs/development.md#188-端口分配开发-vs-生产) (Chinese).

### Development environment, one click (daemonised + auto restart)

A manually started `dotnet run` / `npm run dev` process is a **child of the current terminal**: closing the terminal — or launching it as a child process from another tool — kills it, and the login page immediately shows "cannot reach the backend service" (the Vite `/api` proxy then returns 502). Use these two scripts when the development environment must stay up for a long time:

```powershell
# Start (build first, then bring up backend 5008 and front-end 5171, with health checks)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-up.ps1

# Restart only the backend (skip the build)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-up.ps1 -SkipBuild -Force

# Stop (the front-end Vite process is kept when -All is not passed)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-down.ps1
```

- The backend is hosted by `scripts/dev-api-run.cmd`: it **restarts automatically after 5 seconds** if the process exits, and logs go to `%TEMP%\noneadcss-dev\api.log`.
- Both processes are created through WMI, so they are **not attached to the caller's process tree** — closing the terminal or restarting the tool does not take them down.
- The health check in `dev-up.ps1` requires `http://127.0.0.1:5008/api/auth/captcha` to return 200 with `svgContent` in the body, rather than merely checking that a port is listening.
- Only this project's own processes are stopped (matched by whether the command line contains `NoneadCSS.Api`), so unrelated processes with the same name are never killed.

### One-click start (recommended)

Two PowerShell restart scripts are provided; each already implements "precise cleanup by port → environment variables injected → services started → health checks until ready", without killing unrelated system processes.

#### Development environment (recommended for daily development)

```powershell
powershell -ExecutionPolicy Bypass -File deploy/restart-dev.ps1
```

Script path: `deploy/restart-dev.ps1`; ports: backend 5008, front-end 5171; no Nginx.

#### Production environment (standalone deployment directory — scripts inside `Production/` recommended)

```powershell
# Stop + clean up leftovers
powershell.exe -ExecutionPolicy Bypass -File C:\noneadcss\Production\stop-prod.ps1

# Start (backend :5009 / vite preview :5172 / Nginx :5173)
powershell.exe -ExecutionPolicy Bypass -File C:\noneadcss\Production\start-prod.ps1
```

Standard script locations:
- Stop: `Production/stop-prod.ps1` (precise cleanup of ports 5009/5172/5173 plus a fallback for leftover processes named NoneadCSS / NoneadCSS.Api / nginx / node)
- Start: `Production/start-prod.ps1` (3-endpoint health check plus front-end reverse-proxy verification built in)

> The historical entry points `deploy/restart-prod.ps1` and `C:\noneadcss_prod\restart-prod.ps1` are kept for compatibility with older deployments; new deployments should use the `Production/` scripts directly.

Deployment directory: `C:\noneadcss\Production\` (`backend/` holds the published exe + dll + appsettings, `frontend/dist/` holds the front-end build served by `vite preview` on :5172, `data/` holds the SQLite database and WAL files, `logs/` holds runtime logs, `nginx/` and `nginx.conf` at the root); ports: backend 5009, vite preview 5172, Nginx 5173. **Note: the real SQLite database path is injected as an absolute path into `ConnectionStrings__SqliteConnection`, eliminating the trap where WorkingDirectory and ContentRoot resolve to two different files (see project_memory.md Lessons Learned).**

**Upgrading from an older version to v2.2.3+ requires an extra DDL patch**: the code adds a `RowVersion BLOB` column to 8 tables (Users / Customers / ChatSessions / ChatMessages / ApiKeys / Webhooks / KnowledgeCategories / KnowledgeArticles). Before startup, run the `_DbPatchRowVersion` console tool as described in [ReleaseNotes_v2.2.3.md §C](./docs/ReleaseNotes_v2.2.3.md#c-生产-sqlite-ddl-补丁工具-_dbpatchrowversion) (idempotent transaction), otherwise startup throws `SQLite Error 1: 'no such column: u.RowVersion'`. The full 7-step release SOP is in [docs/development.md §18.11](./docs/development.md#1811-生产发布-sopv223-固化--7-步强制流程--2026-08-14) (Chinese).

> **Note**: `_DbPatchRowVersion` was a one-off internal patch program run against the company production database and **is not published with this repository**. A fresh installation does not need it (`RowVersion` / `Version` columns are created directly by the EF migration chain); the patch is only needed when carrying a pre-v2.2.3 database onto new code. Per-table procedure and probe SQL: [docs/development.md §18.10](./docs/development.md#1810-旧库升级rowversion-ddl-补丁) (Chinese).

The list of required environment variables is in [docs/development.md §18.12](./docs/development.md#1812-生产必需环境变量用户级--user-scope) (Chinese).

### Health checks

Two health endpoints are provided:

- `GET /health` — **liveness probe**: checks only that the application is alive, no external dependencies
- `GET /health/ready` — **readiness probe**: checks that the application is ready, including the database connection

Suitable for liveness/readiness probe configuration on container orchestration platforms such as Kubernetes.

### Default account

- Username: `admin@example.com`
- Password: configured through the `ADMIN_PASSWORD` environment variable; if unset on first startup a password is generated automatically and printed to the application log

### First-time initialization

On a brand-new database, the Setup API completes the administrator and first mailbox (there is currently no standalone front-end wizard page; request body fields are documented in the `Setup` group of `/docs`):

| Step | Endpoint | Purpose |
|------|------|------|
| 1 | `GET /api/setup/status` | Query whether initialization is still needed and which steps are done |
| 2 | `POST /api/setup/admin` | Create the first administrator account |
| 3 | `POST /api/setup/mailbox` | Create the first inbound mailbox (skippable) |
| 4 | `POST /api/setup/complete` | Mark initialization complete; afterwards the Setup endpoints no longer accept writes |

Production deployments may also create the administrator directly at startup with the `ADMIN_EMAIL` / `ADMIN_PASSWORD` environment variables — either approach is sufficient.

## API documentation

Once the application is running, visit `/docs` for the complete API documentation with online testing.

The documentation page is bilingual (**Simplified Chinese / English**):

- Language switch at the top of the page, or visit `/docs?lang=en` / `/docs?lang=zh-Hans` directly; the choice is stored in cookie `ncss_docs_lang` (`Path=/docs`, 30 days); an unrecognised language value falls back to Simplified Chinese.
- Both OpenAPI documents are generated from the same source: Chinese `/openapi/v1.json` (default document `v1`) and English `/openapi/en.json` (document `en`), with 487 paths mapped one-to-one. The English document is produced by substituting field text through `NoneadCSS.Api/OpenApi/openapi-en-dictionary.json` (906 zh→en entries) at generation time — no second set of comments is maintained.
- Scalar's own UI strings have no language option; in Chinese mode the server injects a translation layer (132 UI labels plus a DOM translation script). Details: [docs/development.md](docs/development.md) §9.6 "OpenAPI / Scalar bilingual documentation" (Chinese).

> When accessing the development environment through Vite, `/docs` and `/openapi/*` are already proxied to `http://127.0.0.1:5008` in `frontend/vite.config.ts`, so `http://127.0.0.1:5171/docs?lang=en` opens the English documentation directly.

### Authentication

Two authentication methods are supported:

#### Method 1: JWT Bearer token (recommended for the front-end)

1. Log in with `POST /api/auth/login`
2. The system sets an HttpOnly Cookie holding the access token automatically
3. Subsequent requests carry the cookie automatically in the browser

#### Method 2: API Key (recommended for third-party integrations)

1. Create an API Key in the admin console
2. Add `X-Api-Key: <your-api-key>` to the request headers
3. The system authenticates automatically and records the call in logs

### Internationalization

Every endpoint accepts a language query parameter:

```
?lang=zh-Hans  # Simplified Chinese (default)
?lang=en       # English
```

The same language may also be set via cookie `.AspNetCore.Culture` or the header `Accept-Language: en`; the front-end attaches `Accept-Language` according to the current UI language automatically. Resolution order is `?lang=` → cookie → `Accept-Language` → default Simplified Chinese.

UI copy (front-end strings, the `/docs` API documentation) is complete in both languages. Backend **validation / exception messages** are replaced at the response exit by exact matching against `NoneadCSS.Api/Localization/api-message-en.json` (**735** zh→en entries); this affects only system text fields in error responses and never rewrites customer-authored content. The dictionary is regenerated from source (the `merge` mode only adds, so hardcoded messages cannot be accidentally deleted); after changing Chinese messages in controllers or services, re-run `node scripts/i18n/api-message-dictionary.mjs` per [docs/development.md](docs/development.md) §9.7 (Chinese). Seed data, chat system messages written to the database, and server-side enum labels remain Simplified Chinese (a documented design limit — see §9.5).

## Configuration

Main configuration lives in `appsettings.json`:

```json
{
  "Database": {
    "UseSqlite": true
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=NoneadCSS;Trusted_Connection=True",
    "SqliteConnection": "Data Source=NoneadCSS.db"
  },
  "Jwt": {
    "SecretKey": "YourSuperSecretKeyForJWTTokenGeneration1234567890",
    "Issuer": "NoneadCSS",
    "Audience": "NoneadCSSApi",
    "AccessTokenExpireMinutes": "60"
  },
  "Email": {
    "MaxAttachmentSize": 20971520,
    "FetchEnabled": true,
    "Quartz": {
      "FetchEmail": "0 0/3 * * * ?",
      "ProcessEmailQueue": "0 * * * * ?"
    }
  },
  "Attachment": {
    "MaxSize": 20971520,
    "AllowedExtensions": [".jpg", ".jpeg", ".png", ".pdf", ...],
    "UploadPath": "wwwroot/attachments"
  },
  "App": {
    "FrontendUrl": "http://localhost:5173",
    "BaseUrl": "http://localhost:5173"
  },
  "Security": {
    "_comment": "HTTPS redirect: in production/Docker, handle HTTPS at the reverse proxy and keep HTTP inside the container",
    "EnableHttpsRedirection": false
  }
}
```

### JWT environment variables

JWT parameters may be configured through environment variables. **Production must use environment variables.**

#### Precedence

Environment variables > appsettings.json

#### Variable names

| Setting | Variable (recommended) | Variable (compat) |
|--------|------------------|------------------|
| JWT key | `JWT__Key` | `JWT_KEY` |
| JWT issuer | `JWT__Issuer` | `JWT_ISSUER` |
| JWT audience | `JWT__Audience` | `JWT_AUDIENCE` |
| Access token lifetime (minutes) | `JWT__AccessTokenExpireMinutes` | `JWT_ACCESSTOKENEXPIREMINUTES` |

#### Examples

**Windows (PowerShell):**
```powershell
$env:JWT__Key="YourSuperSecretKeyForJWTTokenGeneration1234567890"
$env:JWT__Issuer="NoneadCSS"
$env:JWT__Audience="NoneadCSSApi"
$env:JWT__AccessTokenExpireMinutes="60"
```

**Linux/macOS:**
```bash
export JWT__Key="YourSuperSecretKeyForJWTTokenGeneration1234567890"
export JWT__Issuer="NoneadCSS"
export JWT__Audience="NoneadCSSApi"
export JWT__AccessTokenExpireMinutes="60"
```

**Docker:**
```bash
docker run -d \
  -e JWT__Key="YourSuperSecretKeyForJWTTokenGeneration1234567890" \
  -e JWT__Issuer="NoneadCSS" \
  -e JWT__Audience="NoneadCSSApi" \
  -e JWT__AccessTokenExpireMinutes="60" \
  noneadcss:latest
```

#### Security requirements

- **Key length**: at least 32 characters
- **Production**: the key must come from `JWT__Key` or `JWT_KEY`
- **Development**: `appsettings.json` values may be used (not recommended to commit them)

### Administrator password environment variable

The initial administrator password may be set through `ADMIN_PASSWORD`. **Production must use the environment variable.**

#### Configuration

| Setting | Variable | Description |
|--------|----------|------|
| Administrator password | `ADMIN_PASSWORD` | Password of the initial administrator account (`admin@example.com`) |

#### Behaviour

- **Variable set**: the variable value is used as the administrator password
- **Variable unset**: a strong random password is generated on first startup and printed to the application log (printed once only — keep it safely)
- For security, no hardcoded default password remains in the repository

#### Examples

**Windows (PowerShell):**
```powershell
$env:ADMIN_PASSWORD="YourStrongPassword123"
```

**Linux/macOS:**
```bash
export ADMIN_PASSWORD="YourStrongPassword123"
```

**Docker:**
```bash
docker run -d \
  -e ADMIN_PASSWORD="YourStrongPassword123" \
  noneadcss:latest
```

## Database migrations

```bash
# Create a new migration
cd NoneadCSS.Api
dotnet ef migrations add <MigrationName> --project ../NoneadCSS.Infrastructure

# Apply migrations
dotnet ef database update --project ../NoneadCSS.Infrastructure
```

## Main module APIs

| Module | Path | Description |
|------|------|------|
| Authentication | `/api/auth` | Login, registration, password reset, refresh tokens |
| Conversations | `/api/conversations` | Ticket management, replies, assignment, drafts, bulk operations |
| Mailboxes | `/api/mailboxes` | Mailbox configuration, email fetching, connection tests |
| Customers | `/api/customers` | Customer profiles, bulk delete, bulk export |
| Users | `/api/users` | User management, permission configuration |
| Tags | `/api/tags` | Tag management |
| Workflows | `/api/workflows` | Automation rules |
| SLA | `/api/sla` | Service-level agreements |
| Macros | `/api/macros` | One-click commands |
| Webhooks | `/api/webhooks` | Event delivery |
| Reports | `/api/reports` | Statistics reports |
| Search | `/api/search` | Global search (conversations / customers / knowledge base) |
| Canned responses | `/api/canned-responses` | Preset reply templates |
| Email templates | `/api/mail-templates` | Email notification templates |
| Teams | `/api/teams` | Team management |
| Satisfaction | `/api/satisfaction-ratings` | Service ratings |
| Audit logs | `/api/audit-logs` | Operation records |
| API keys | `/api/api-keys` | API Key management |
| Import / export | `/api/import-export` | Data migration |
| Notifications | `/api/notifications` | In-app notifications, templates, rules |
| System options | `/api/system-options` | System configuration items |
| Roles | `/api/roles` | Role and permission management |
| Email signatures | `/api/email-signatures` | Email signature management |
| Custom fields | `/api/custom-fields` | Custom field management |
| Modules | `/api/modules` | Feature module enable/disable |
| Profile | `/api/profile` | Personal details, password, statistics, drafts |
| First-time setup | `/api/setup` | Installation wizard: status query, create administrator, create first mailbox, complete initialization (API only, no standalone front-end page) |
| Third-party OAuth | `/api/oauth` | Outlook and other third-party OAuth authorization and callback |
| Localization | `/api/localization` | Language list, UI string query and maintenance |
| Realtime | `/api/realtime` | Presence and realtime channel helper endpoints |
| Send logs | `/api/send-logs` | Email send record queries |
| Folders | `/api/folders` | Conversation folder tree and counts |
| Time tracking | `/api/time-tracking` | Time entries, statistics and approval |
| System | `/api/system` | System information, maintenance and diagnostics |
| Email test | `/api/email-test` | Outbound connectivity test |
| Calendar | `/api/calendar` | Calendar event CRUD, recurring schedules, reminders, ICS import/export, subscriptions, Outlook sync |
| Knowledge base | `/api/knowledge` | Category tree, article management, full-text search, likes, overview statistics |
| Online chat | `/api/chat` | Agent-side chat sessions, recall/edit, @mentions, conversion to ticket |
| Customer chat widget | `/api/chat/token/{sessionToken}` | Anonymous customer-side chat (authenticated by sessionToken) |
| WeChat | `/api/wechat` | WeChat Official Account push/receive, signature verification, configuration read/write, connection status, customer-service replies, template-message and subscribe-notification dual modes, custom menu CRUD (10 endpoints, including menu publish/fetch/delete/draft save) |
| WhatsApp | `/api/whatsapp` | WhatsApp Business Platform Webhook (HMAC-SHA256 signature verification), configuration management, status checks, conversation replies (text + media), template message sending, 24h window control |
| DingTalk | `/api/dingding` | DingTalk enterprise internal application configuration (AppKey/AppSecret/AgentId), receiving mode (HTTP/Stream), status monitoring, message replies, module permission control |
| Feishu | `/api/feishu` | Feishu Open Platform access, configuration management, message receive/reply, module permission control |
| Messenger | `/api/messenger` | Meta Graph API Webhook (`X-Hub-Signature-256` HMAC-SHA256 verification), configuration management, status checks, conversation replies (text split at 2000 characters + attachments), subscription field management, connection tests |
| Telegram | `/api/telegram` | Telegram Bot Webhook (`X-Telegram-Bot-Api-Secret-Token` verification) and long polling, configuration management, status checks, conversation replies (4096-character splitting + `parse_mode` + forum topics), Webhook register/delete, connection tests |
| LINE | `/api/line` | LINE Messaging API Webhook (`X-Line-Signature` verification), configuration management, quota and status, conversation replies (replyToken → push fallback + attachments), human takeover, connection tests |
| iMessage | `/api/imessage` | BlueBubbles bridge Webhook (shared-secret verification) and Apple Messages for Business Webhook (`WebhookSecret` verification), configuration management, status checks, conversation replies (text + attachments), Webhook registration, connection tests |
| Outlook sync | `/api/outlook-sync` | OAuth authorization, bidirectional calendar sync |

## Development conventions

### Code style

- C# 12 features (top-level statements, file-scoped namespaces)
- Nullable reference types enabled
- .NET naming conventions (PascalCase for types, camelCase for parameters)

### Architecture principles

- Layered architecture: API → Application → Domain → Infrastructure
- Dependency inversion: upper layers depend on abstractions, lower layers implement them
- Domain-driven design: entities carry business logic, avoiding an anemic model
  - Core entities encapsulate business methods: `Conversation`, `ConversationThread`, `Webhook`, `Attachment`, `SendLog`, `SatisfactionRating`, …
  - Repository pattern: dedicated repositories for complex queries, generic repositories for basic CRUD
- Database index optimisation: composite indexes on hot query paths (conversations, threads, starring, attachments, notification rules, …)
  - NotificationRule: 4 new composite indexes covering EventType + IsActive + MailboxId + UserId + DeletedAt query combinations
- EF Core performance optimisation (P1):
  - **AsNoTracking everywhere**: read-only query methods of all repository classes (WorkflowRepository, SlaRepository, NotificationRuleRepository, ChatSessionRepository, ChatMessageRepository and the other 37 repositories) add `.AsNoTracking()`, cutting ChangeTracker memory
  - **Bulk operation optimisation**: ConversationService's `BulkUpdatePriorityAsync` / `BulkDeleteAsync` use EF Core `ExecuteUpdateAsync` to emit a single SQL statement instead of N+1 transactions
  - **ChatSession / ChatMessage repositories**: all read-only queries add AsNoTracking for high-concurrency online chat
- Front-end performance optimisation (P1):
  - **Vite bundle splitting**: `manualChunks` in vite.config.ts splits third-party libraries into 5 chunks — vendor, query, signalr, lucide, utils — improving browser cache hit rate
  - **Timer optimisation**: the WeChat 48-hour and WhatsApp 24-hour window refresh switched from `setInterval` to chained `setTimeout`, eliminating timer leaks and drift
  - **useMemo anti-pattern fix**: ref writes in ConversationDetail moved from useMemo into a separate useEffect so derived-state computation stays pure
- Configuration-driven design (P2-1): hardcoded TimeSpan values centralised through `IOptions<T>` (ChatOptions / CacheOptions / OAuthOptions / IngestionMatchOptions / WebhookOptions / SignalROptions / RetryOptions and other sections), overridable via environment variables
- Docker optimisation: the Dockerfile drops the redundant `wwwroot/attachments` directory creation and Volume declaration; the Data directory casing is unified

### Error handling

- `GlobalExceptionMiddleware` handles exceptions uniformly
- Standardised error response format
- Detailed error logging

### Testing
- **Backend framework**: xUnit + Moq + FluentAssertions
- **Backend scale**: 124 test classes / 2233 cases (2228 passed, 0 failed, 5 skipped; measured on 2026-11-23 with the in-process runner)
- **Backend coverage**: line coverage 5.1%+, branch coverage 13.2%+, method coverage 18.4%+ (2026-08-05 audit snapshot)
- **Front-end tests**: Vitest — 18 test files / 684 cases all passing; the hardcoded-Chinese gate `npm run i18n:check` reports 0 occurrences
- **Core service coverage**: unit-test coverage ≥ 90% for 13+ services (API Key, Dashboard, drafts, assignment, teams, modules, tags, email signatures, canned responses, system configuration, password policy, roles, ChatService)
- **Backend test infrastructure**: `TestAsyncQueryProvider` supports EF Core async LINQ mocking; `InternalsVisibleTo` exposes internal methods for tests
- **Front-end test infrastructure** (P2-3): Vitest + @testing-library/react + happy-dom/jsdom; configuration in `vitest.config.ts` and `src/test/setup.ts`; component and utility-function tests supported
- Details: [development doc, unit-test section](docs/development.md#23-单元测试) (Chinese)

```bash
# Run backend tests (standard way)
dotnet test NoneadCSS.Tests/NoneadCSS.Tests.csproj

# If the local machine cannot execute it due to testhost permission limits (Win32Exception 5), use the in-process runner:
dotnet build NoneadCSS.Tests/NoneadCSS.Tests.csproj -c Release
dotnet NoneadCSS.Tests/bin/Release/net10.0/BackendTestRunner.dll \
       NoneadCSS.Tests/bin/Release/net10.0/NoneadCSS.Tests.dll

# Generate a coverage report
dotnet test NoneadCSS.Tests/NoneadCSS.Tests.csproj --collect:"XPlat Code Coverage"
reportgenerator -reports:"TestResults-Coverage/*/coverage.cobertura.xml" -targetdir:"TestResults-Coverage/Report"

# Run front-end tests
cd frontend
npm run test
```

> Usage and parameters of the in-process runner (`--list` / `--filter` / result file): [scripts/backend-test-runner/README.md](scripts/backend-test-runner/README.md).

### Open-source release preflight (three read-only checks)
Run the aggregate gate once before publishing; any failure exits with code 1, so it can be wired straight into CI:

```bash
node scripts/oss-preflight.mjs          # secret scan + documentation links + copy dictionary coverage
node scripts/oss-preflight.mjs --quick  # skip the slower secret scan, for everyday commits
```

The three checks can also be run individually:

```bash
node scripts/oss-secret-scan.mjs                              # 13 rules: credentials, internal hosts, drive-letter source paths, user directories, PEM, …
node scripts/oss-doc-links.mjs check                         # dead documentation links and historical machine paths (file:///); exit code 1 when any dead link exists
node scripts/i18n/api-message-dictionary.mjs coverage        # whether the response-exit dictionary covers every Chinese string in the source
```

Note: real secret values are never committed (`deploy/secrets.cmd` is ignored, only `deploy/secrets.cmd.example` is committed), and database dumps must not ship with the package; before publishing to a new repository, run `scripts/oss-db-scrub.sql` against a **copy** of the database. See [development doc — open-source preflight](docs/development.md#98-开源发布预检脚本) (Chinese).

## Related documentation

- [Development guide](docs/development.md) (Chinese) — detailed development guide
- [User manual](docs/拓德客户服务与支持系统用户使用手册.md) (Chinese) — operator manual
- [Changelog](CHANGELOG.md) — per-version change records
- [Security audit report (2026-08-06)](docs/security-audit-report-2026-08-06.md) — audit conclusions and fixes
- [Technical debt list, Q4 2026](docs/NoneadCSS_TechnicalDebt_2026Q4.md) — 7 high-risk items (repository-layer transaction nesting, SaveChangesAsync violations, in-memory report materialisation, workflow triggers on bulk operations, uncached permission resolution, missing uniqueness on MessageId, optimistic locking and API versioning)
- [Architecture-level backlog proposal (2026-10-23)](docs/NoneadCSS-架构级遗留项技术方案-20261023.md) — 6 designs: SQLite optimistic locking, workflow persistence, import/export expansion, authorization granularity, group chat, multi-tenancy
- [Channel integration playbook](docs/integration/channel-integration-playbook.md) — shared registration points, file checklist and acceptance checklist for adding an IM channel (Messenger / Telegram / LINE / iMessage were implemented from it)
- [Database backup and restore guide](docs/database-backup-restore-guide.md) — production backup, restore and drill steps
- [API endpoint inventory](scripts/css-api/endpoints.md) — 594 operations / 50 groups exported from the production OpenAPI, for direct scripts and integration checks

## Technical debt

A full code audit completed on 2026-08-05 across 5 dimensions (code quality, security, feature completeness, performance, testing) found 38 issues; 31 were fixed and 7 high-risk / large-scope items were deferred to next-quarter dedicated iterations and recorded in [docs/NoneadCSS_TechnicalDebt_2026Q4.md](docs/NoneadCSS_TechnicalDebt_2026Q4.md):

| ID | Issue | Priority | Effort |
|------|------|--------|--------|
| TD-01 | Repository-layer transaction nesting defect (UnitOfWork does not support nesting) | P0 | 3-5 days |
| TD-02 | Repositories commonly call SaveChangesAsync (30+ violations) | P1 | 1-2 weeks |
| TD-03 | ReportService materialises reports in memory (incl. the SQLite 999-parameter limit bug) | P0 | 1 week |
| TD-04 | ConversationService bulk operations trigger workflows (250+ DB round trips) | P1 | 1-2 weeks |
| TD-05 | UserRoleResolver has no caching at all (dozens of DB round trips per page) | P0 | 3-5 days |
| TD-06 | ConversationThread.MessageId lacks a uniqueness constraint (TOCTOU race) | P0 | 3-5 days |
| TD-07 | High-concurrency tables lack optimistic locking + no API versioning on any controller | P1 | 1-2 weeks |
| TD-07a progress | **Mostly implemented; only API versioning remains**: 13 tables (Users / Customers / ChatSessions / ChatMessages / ApiKeys / Webhooks / KnowledgeCategories / KnowledgeArticles / CalendarEvents / TimeEntries / Conversations / ConversationThreads / Mailboxes) have a RowVersion concurrency token; `ConcurrencyCheckHelper` covers Users / ApiKeys / Webhooks / NotificationRules / Chat / Customers / Knowledge / TimeEntries at the service layer, and the front-end sends `If-Match` on calendar, time-tracking and knowledge-base interactions — conflicts return 409 with the latest snapshot. Controller route API versioning (`/v1/` prefix) is still pending. | | |

## License

**User-segmented dual licensing** (see [LICENSE](LICENSE)):

- **Individual users and organisations with ≤3 people**: AGPL-3.0.
- **Organisations with more than 3 people**: a commercial license is required — contact `service@nonead.com`.
- A commercial license is also appropriate for any organisation size when AGPLv3's source-disclosure obligations (modified versions distributed, or offered as a network service) cannot be met.

---

**NoneadCSS** — Customer Service & Support System · 中文版: [README.md](README.md)
