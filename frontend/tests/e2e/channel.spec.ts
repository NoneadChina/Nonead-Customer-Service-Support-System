import { test, expect, type APIRequestContext, type Page } from '@playwright/test'

/**
 * IM channel modules — nav visibility, route gating and API module gate.
 *
 * Preconditions for running this suite
 * ------------------------------------
 * 1. Playwright infra is NOT in this repo yet: `frontend/package.json` has no `@playwright/test`
 *    dependency and there is no `playwright.config.ts`. The lead must add both; this spec assumes
 *    the config declares a `webServer` that auto-starts the Vite dev server on port 5173
 *    (`npm run dev`) and that the browser context runs at a desktop viewport (>= 1280x720), because
 *    it asserts against the desktop sidebar nav.
 * 2. The backend API must be up and reachable through the dev server's `/api` proxy
 *    (Vite proxy target comes from `VITE_DEV_PROXY_TARGET`); every assertion below that hits
 *    `/api/...` goes through `http://localhost:5173`, not the API port directly.
 * 3. `ADMIN_EMAIL` / `ADMIN_PASSWORD` env vars must name a seeded SuperAdmin account. SuperAdmin
 *    short-circuits permission checks (`RoleRepository.cs:103`), which is what lets this spec read
 *    `/api/modules/all`.
 * 4. Login always requires a captcha — there is no dev/test bypass (`AuthService.cs:59-64`
 *    validates it first). `login()` below solves it by reading the SVG the `/api/auth/captcha`
 *    response returns, so it stays correct only while `CaptchaService.GenerateSvg` keeps emitting
 *    one `<text>` node per character.
 * 5. Database must be seeded: the channel module rows must EXIST, and they are seeded with
 *    `IsActive = 0` (`Migrations/2026_11_16_add_{messenger,telegram,line,imessage}_module.sql`,
 *    `DatabaseSeeder.cs:2118-2248`). Note: in a default seed ALL EIGHT channel modules are
 *    inactive — not only the four new ones — so the gating tests skip themselves if a developer
 *    has locally enabled a channel (the seeder never flips an existing row).
 * 6. Locale is not pinned: the app falls back to `zh-Hans` (`I18nContext.tsx:18-25`), so every
 *    text locator accepts both the en and zh-Hans spelling.
 */

const BASE_URL = process.env.E2E_BASE_URL ?? 'http://localhost:5173'
const ADMIN_EMAIL = process.env.ADMIN_EMAIL ?? ''
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD ?? ''

test.use({ baseURL: BASE_URL })

const escapeRe = (value: string) => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
/** Accept any of the supported-locale spellings of a label. */
const anyLabel = (...labels: string[]) => new RegExp(`^(${labels.map(escapeRe).join('|')})$`)

type Channel = {
  /** registered protected route — `routes/index.tsx:110-117` */
  path: string
  /** module name in `menuModuleMap` / `Modules.Name` */
  module: string
  /** GET endpoint carrying `[HttpGet("settings")] [RequireModule("<module>")]` */
  gate: string
  /** the page's <h1> text in both locales */
  heading: RegExp
  /** sidebar label; `null` = the `nav.*` key is absent from BOTH locale dictionaries */
  navLabel: RegExp | null
  /** the four channels added most recently */
  isNew: boolean
}

const CHANNELS: Channel[] = [
  { path: '/wechat', module: 'wechat', gate: '/api/wechat/settings', heading: anyLabel('WeChat', '微信聊天'), navLabel: anyLabel('WeChat', '微信聊天'), isNew: false },
  { path: '/whatsapp', module: 'whatsapp', gate: '/api/whatsapp/settings', heading: anyLabel('WhatsApp integration', 'WhatsApp 集成'), navLabel: anyLabel('WhatsApp'), isNew: false },
  { path: '/feishu', module: 'feishu', gate: '/api/feishu/settings', heading: anyLabel('Feishu', '飞书聊天'), navLabel: anyLabel('Feishu', '飞书聊天'), isNew: false },
  { path: '/dingding', module: 'dingding', gate: '/api/dingding/settings', heading: anyLabel('DingTalk integration', '钉钉集成'), navLabel: anyLabel('DingTalk', '钉钉聊天'), isNew: false },
  { path: '/messenger', module: 'messenger', gate: '/api/messenger/settings', heading: anyLabel('Messenger integration', 'Messenger 集成'), navLabel: null, isNew: true },
  { path: '/telegram', module: 'telegram', gate: '/api/telegram/settings', heading: anyLabel('Telegram integration', 'Telegram集成'), navLabel: null, isNew: true },
  { path: '/line', module: 'line', gate: '/api/line/settings', heading: anyLabel('LINE integration', 'LINE集成'), navLabel: null, isNew: true },
  { path: '/imessage', module: 'imessage', gate: '/api/imessage/settings', heading: anyLabel('iMessage integration', 'iMessage集成'), navLabel: null, isNew: true },
]

const NEW_CHANNELS = CHANNELS.filter((c) => c.isNew)
const LEGACY_CHANNELS = CHANNELS.filter((c) => !c.isNew)

const featuresButton = (page: Page) => page.getByRole('button', { name: /Features|功能模块/ })

/**
 * Login through the real form. The captcha challenge must be captured before navigating,
 * because `Login.tsx` requests it on mount.
 */
async function login(page: Page): Promise<void> {
  if (!ADMIN_EMAIL || !ADMIN_PASSWORD) {
    throw new Error('ADMIN_EMAIL and ADMIN_PASSWORD env vars are required (seeded SuperAdmin) — see the header comment')
  }

  for (let attempt = 1; attempt <= 3; attempt += 1) {
    const challenge = page.waitForResponse((res) => new URL(res.url()).pathname === '/api/auth/captcha')
    await page.goto(`${BASE_URL}/login`, { waitUntil: 'domcontentloaded' })

    const svg = await (await challenge).text()
    const code = [...svg.matchAll(/<text[^>]*>([^<])<\/text>/g)].map((m) => m[1]).join('')
    if (code.length !== 4) {
      throw new Error(`could not read the captcha out of the SVG response (got "${code}", expected 4 characters)`)
    }

    await page.locator('#login-email').fill(ADMIN_EMAIL)
    await page.locator('#login-password').fill(ADMIN_PASSWORD)
    await page.locator('#login-captcha').fill(code)
    await page.locator('button[type="submit"]').click()

    const landed = await page
      .waitForURL((url) => !url.pathname.startsWith('/login'), { timeout: 8000 })
      .then(() => true, () => false)
    if (landed) return
  }

  throw new Error('login failed after 3 attempts — check ADMIN_EMAIL/ADMIN_PASSWORD and that the API is reachable through the dev server /api proxy')
}

let moduleFlags: Map<string, boolean> | null = null

/** `GET /api/modules/all` → name → isActive (cached per worker; the endpoint is output-cached too). */
async function readModuleFlags(request: APIRequestContext): Promise<Map<string, boolean>> {
  if (moduleFlags) return moduleFlags
  const res = await request.get(`${BASE_URL}/api/modules/all`)
  if (!res.ok()) {
    throw new Error(`GET /api/modules/all returned ${res.status()} — ADMIN_EMAIL must be a SuperAdmin account`)
  }
  const body: unknown = await res.json()
  const rows = Array.isArray(body) ? body : ((body as { data?: unknown[]; items?: unknown[] }).data ?? (body as { items?: unknown[] }).items ?? [])
  moduleFlags = new Map(
    (rows as Array<{ name?: string; isActive?: boolean }>).map((row) => [String(row.name), Boolean(row.isActive)]),
  )
  return moduleFlags
}

/** Navigate to the dashboard and wait until the sidebar nav has actually rendered. */
async function openDashboard(page: Page): Promise<void> {
  await page.goto(`${BASE_URL}/`, { waitUntil: 'domcontentloaded' })
  await expect(featuresButton(page)).toBeVisible()
}

/** Skip a gating test when this DB has the module enabled (a default seed leaves it off). */
async function skipUnlessInactive(page: Page, request: APIRequestContext, channel: Channel): Promise<void> {
  const flags = await readModuleFlags(request)
  const testInfo = test.info()
  if (flags.get(channel.module) !== false) {
    testInfo.skip(true, `module '${channel.module}' is not inactive in this database; this test only covers the seeded state`)
  }
}

test.describe('channel modules are seeded and discoverable', () => {
  test('every channel route has a seeded module row', async ({ page, request }) => {
    await login(page)
    const flags = await readModuleFlags(request)
    for (const channel of CHANNELS) {
      expect(flags.has(channel.module), `no Modules row named '${channel.module}' — the DB is not seeded (see precondition 5)`).toBe(true)
    }
  })
})

test.describe('inactive channel modules: nav hidden, route redirects', () => {
  for (const channel of CHANNELS) {
    test(`${channel.path} is not linked in the nav and redirects to the dashboard`, async ({ page, request }) => {
      await login(page)
      await skipUnlessInactive(page, request, channel)

      await openDashboard(page)
      // Expand the group that would contain the item: collapsed children stay in the DOM inside a
      // max-h-0 container, so they are "hidden" without being absent. Absence must be asserted after
      // the group is opened, otherwise the assertion passes for the wrong reason.
      await featuresButton(page).click()
      await expect(page.locator(`a[href="${channel.path}"]`)).toHaveCount(0)

      // `ModuleProtectedRoute.tsx` redirects an inactive module to "/" — NOT to a 404 page; every
      // channel path is registered in `routes/index.tsx:110-117`.
      await page.goto(`${BASE_URL}${channel.path}`, { waitUntil: 'domcontentloaded' })
      await expect(page).toHaveURL(new RegExp(`^${escapeRe(BASE_URL)}/$`))
      await expect(page.getByRole('heading', { level: 1, name: channel.heading })).toHaveCount(0)
    })
  }
})

test.describe('inactive channel modules: the API gate answers, it does not 404', () => {
  for (const channel of CHANNELS) {
    test(`GET ${channel.gate} → 403 with the disabled-module payload`, async ({ page, request }) => {
      await login(page)
      await skipUnlessInactive(page, request, channel)

      const res = await request.get(`${BASE_URL}${channel.gate}`)
      // `ModuleAuthorizationFilter.cs:41-55`: an existing-but-inactive module answers
      // 403 + { message: "模块 '<x>' 未启用…", moduleName } — a 404 here would mean the module row
      // is missing entirely, which the seed scripts contradict.
      expect(res.status(), `expected 403 for inactive module '${channel.module}'`).toBe(403)
      expect(res.status(), 'the module gate must not answer 404').not.toBe(404)
      const payload = JSON.stringify(await res.json().catch(() => ({})))
      expect(payload).toContain(channel.module)
    })
  }
})

test.describe('only unregistered paths reach the 404 page', () => {
  test('/line-bot renders the NotFound route', async ({ page }) => {
    await login(page)
    await page.goto(`${BASE_URL}/line-bot`, { waitUntil: 'domcontentloaded' })
    await expect(page.getByText('404').first()).toBeVisible()
  })
})

test.describe('enabled channel modules (skipped on a default seed)', () => {
  for (const channel of LEGACY_CHANNELS) {
    test(`${channel.path} links the nav item and renders its heading when the module is enabled`, async ({ page, request }) => {
      const flags = await readModuleFlags(request)
      if (flags.get(channel.module) !== true) {
        test.info().skip(true, `module '${channel.module}' is inactive in this database`)
      }
      await login(page)
      await openDashboard(page)
      await featuresButton(page).click()
      await expect(page.getByRole('link', { name: channel.navLabel ?? channel.path }).first()).toBeVisible()
      await page.goto(`${BASE_URL}${channel.path}`, { waitUntil: 'domcontentloaded' })
      await expect(page.getByRole('heading', { level: 1, name: channel.heading })).toBeVisible()
    })
  }
})

// The four new channels cannot be covered by the two assertions above as things stand:
// (a) their module rows are seeded IsActive = 0, so the settings page is never reachable, and
// (b) `Layout.tsx:367-370` labels them with nav.messenger / nav.telegram / nav.line / nav.imessage,
//     which exist in neither `locales/en/nav.ts` nor `locales/zh-Hans/nav.ts` — under the dev server
//     the i18n layer throws on a missing key (`translate.ts:25-48`), and in a production build the
//     raw key would be rendered instead of a label.
test.fixme('an enabled new channel shows its nav item (blocked: nav.* label keys missing in both locales)', async ({ page }) => {
  // needs stable selector + the four missing nav keys; would click the Features group and expect the link label
  await openDashboard(page)
  await featuresButton(page).click()
  await expect(page.getByRole('link', { name: /Messenger/i }).first()).toBeVisible()
})

// `grep data-testid frontend/src/pages/**` → no matches: no page in this app exposes a test id, and
// page files must not be touched from here, so the settings forms below have no dependable locator.
// `Messenger.tsx` also has no label/aria association for its inputs (only one `<label>` at :368), so
// getByLabel cannot be used either; `Telegram.tsx` wraps fields in bare `<label className="block">`.
for (const channel of NEW_CHANNELS) {
  test.fixme(`${channel.path} settings form loads and saves (needs stable selector + an enabled module)`, async ({ page }) => {
    await login(page)
    await page.goto(`${BASE_URL}${channel.path}`, { waitUntil: 'domcontentloaded' })
    await expect(page.getByRole('heading', { level: 1, name: channel.heading })).toBeVisible()
  })
}
