import type { TranslationKey } from '../locales/zh-Hans';
import { tBound } from '../i18n/locale';

export interface TimezoneOption {
  value: string;
  label: string;
  offset: number;
}

/**
 * 时区定义。
 *
 * **稳定 code 是 `value`（IANA 时区值）**，它是查表/存库/接口用的键，永不翻译；
 * `labelKey` 指向真源 `locales/{zh-Hans,en}/format.ts` 里的展示文案。
 * key 在这里显式写全（而不是运行时按 value 拼出 key 再做类型断言）：
 * 断言会绕过 `TranslationKey` 的字面量联合校验，一旦字典缺条，表现只剩"界面渲染出 key 本身"，
 * 正是本批次要防的静默失败。
 */
interface TimezoneDef {
  value: string;
  labelKey: TranslationKey;
  offset: number;
}

const TIMEZONE_DEFS: TimezoneDef[] = [
  { value: 'UTC', labelKey: 'format.timezone.utc', offset: 0 },
  { value: 'Europe/London', labelKey: 'format.timezone.europeLondon', offset: 0 },
  { value: 'Europe/Dublin', labelKey: 'format.timezone.europeDublin', offset: 0 },
  { value: 'Europe/Paris', labelKey: 'format.timezone.europeParis', offset: 1 },
  { value: 'Europe/Berlin', labelKey: 'format.timezone.europeBerlin', offset: 1 },
  { value: 'Europe/Madrid', labelKey: 'format.timezone.europeMadrid', offset: 1 },
  { value: 'Europe/Rome', labelKey: 'format.timezone.europeRome', offset: 1 },
  { value: 'Europe/Amsterdam', labelKey: 'format.timezone.europeAmsterdam', offset: 1 },
  { value: 'Europe/Zurich', labelKey: 'format.timezone.europeZurich', offset: 1 },
  { value: 'Europe/Vienna', labelKey: 'format.timezone.europeVienna', offset: 1 },
  { value: 'Europe/Prague', labelKey: 'format.timezone.europePrague', offset: 1 },
  { value: 'Europe/Warsaw', labelKey: 'format.timezone.europeWarsaw', offset: 1 },
  { value: 'Europe/Budapest', labelKey: 'format.timezone.europeBudapest', offset: 1 },
  { value: 'Europe/Belgrade', labelKey: 'format.timezone.europeBelgrade', offset: 1 },
  { value: 'Europe/Sofia', labelKey: 'format.timezone.europeSofia', offset: 2 },
  { value: 'Europe/Athens', labelKey: 'format.timezone.europeAthens', offset: 2 },
  { value: 'Europe/Istanbul', labelKey: 'format.timezone.europeIstanbul', offset: 3 },
  { value: 'Europe/Moscow', labelKey: 'format.timezone.europeMoscow', offset: 3 },
  { value: 'Asia/Jerusalem', labelKey: 'format.timezone.asiaJerusalem', offset: 2 },
  { value: 'Asia/Dubai', labelKey: 'format.timezone.asiaDubai', offset: 4 },
  { value: 'Asia/Baku', labelKey: 'format.timezone.asiaBaku', offset: 4 },
  { value: 'Asia/Yerevan', labelKey: 'format.timezone.asiaYerevan', offset: 4 },
  { value: 'Asia/Tbilisi', labelKey: 'format.timezone.asiaTbilisi', offset: 4 },
  { value: 'Asia/Kuwait', labelKey: 'format.timezone.asiaKuwait', offset: 3 },
  { value: 'Asia/Riyadh', labelKey: 'format.timezone.asiaRiyadh', offset: 3 },
  { value: 'Asia/Bahrain', labelKey: 'format.timezone.asiaBahrain', offset: 3 },
  { value: 'Asia/Qatar', labelKey: 'format.timezone.asiaQatar', offset: 3 },
  { value: 'Asia/Oman', labelKey: 'format.timezone.asiaOman', offset: 4 },
  { value: 'Asia/Tehran', labelKey: 'format.timezone.asiaTehran', offset: 3.5 },
  { value: 'Asia/Kabul', labelKey: 'format.timezone.asiaKabul', offset: 4.5 },
  { value: 'Asia/Tashkent', labelKey: 'format.timezone.asiaTashkent', offset: 5 },
  { value: 'Asia/Dushanbe', labelKey: 'format.timezone.asiaDushanbe', offset: 5 },
  { value: 'Asia/Ashgabat', labelKey: 'format.timezone.asiaAshgabat', offset: 5 },
  { value: 'Asia/Yekaterinburg', labelKey: 'format.timezone.asiaYekaterinburg', offset: 5 },
  { value: 'Asia/Karachi', labelKey: 'format.timezone.asiaKarachi', offset: 5 },
  { value: 'Asia/Islamabad', labelKey: 'format.timezone.asiaIslamabad', offset: 5 },
  { value: 'Asia/Calcutta', labelKey: 'format.timezone.asiaCalcutta', offset: 5.5 },
  { value: 'Asia/Mumbai', labelKey: 'format.timezone.asiaMumbai', offset: 5.5 },
  { value: 'Asia/Kolkata', labelKey: 'format.timezone.asiaKolkata', offset: 5.5 },
  { value: 'Asia/Colombo', labelKey: 'format.timezone.asiaColombo', offset: 5.5 },
  { value: 'Asia/Kathmandu', labelKey: 'format.timezone.asiaKathmandu', offset: 5.75 },
  { value: 'Asia/Dhaka', labelKey: 'format.timezone.asiaDhaka', offset: 6 },
  { value: 'Asia/Yangon', labelKey: 'format.timezone.asiaYangon', offset: 6.5 },
  { value: 'Asia/Bangkok', labelKey: 'format.timezone.asiaBangkok', offset: 7 },
  { value: 'Asia/Jakarta', labelKey: 'format.timezone.asiaJakarta', offset: 7 },
  { value: 'Asia/Ho_Chi_Minh', labelKey: 'format.timezone.asiaHoChiMinh', offset: 7 },
  { value: 'Asia/Kuala_Lumpur', labelKey: 'format.timezone.asiaKualaLumpur', offset: 8 },
  { value: 'Asia/Singapore', labelKey: 'format.timezone.asiaSingapore', offset: 8 },
  { value: 'Asia/Shanghai', labelKey: 'format.timezone.asiaShanghai', offset: 8 },
  { value: 'Asia/Beijing', labelKey: 'format.timezone.asiaBeijing', offset: 8 },
  { value: 'Asia/Hong_Kong', labelKey: 'format.timezone.asiaHongKong', offset: 8 },
  { value: 'Asia/Taipei', labelKey: 'format.timezone.asiaTaipei', offset: 8 },
  { value: 'Asia/Macau', labelKey: 'format.timezone.asiaMacau', offset: 8 },
  { value: 'Asia/Manila', labelKey: 'format.timezone.asiaManila', offset: 8 },
  { value: 'Asia/Kuching', labelKey: 'format.timezone.asiaKuching', offset: 8 },
  { value: 'Asia/Tokyo', labelKey: 'format.timezone.asiaTokyo', offset: 9 },
  { value: 'Asia/Seoul', labelKey: 'format.timezone.asiaSeoul', offset: 9 },
  { value: 'Asia/Pyongyang', labelKey: 'format.timezone.asiaPyongyang', offset: 9 },
  { value: 'Asia/Dili', labelKey: 'format.timezone.asiaDili', offset: 9 },
  { value: 'Asia/Jayapura', labelKey: 'format.timezone.asiaJayapura', offset: 9 },
  { value: 'Asia/Vladivostok', labelKey: 'format.timezone.asiaVladivostok', offset: 10 },
  { value: 'Asia/Brisbane', labelKey: 'format.timezone.asiaBrisbane', offset: 10 },
  { value: 'Asia/Port_Moresby', labelKey: 'format.timezone.asiaPortMoresby', offset: 10 },
  { value: 'Asia/Hobart', labelKey: 'format.timezone.asiaHobart', offset: 10 },
  { value: 'Asia/Melbourne', labelKey: 'format.timezone.asiaMelbourne', offset: 10 },
  { value: 'Asia/Sydney', labelKey: 'format.timezone.asiaSydney', offset: 10 },
  { value: 'Asia/Canberra', labelKey: 'format.timezone.asiaCanberra', offset: 10 },
  { value: 'Asia/Adelaide', labelKey: 'format.timezone.asiaAdelaide', offset: 9.5 },
  { value: 'Asia/Darwin', labelKey: 'format.timezone.asiaDarwin', offset: 9.5 },
  { value: 'Asia/Auckland', labelKey: 'format.timezone.asiaAuckland', offset: 12 },
  { value: 'Asia/Wellington', labelKey: 'format.timezone.asiaWellington', offset: 12 },
  { value: 'Asia/Chatham', labelKey: 'format.timezone.asiaChatham', offset: 12.75 },
  { value: 'Pacific/Auckland', labelKey: 'format.timezone.pacificAuckland', offset: 12 },
  { value: 'Pacific/Fiji', labelKey: 'format.timezone.pacificFiji', offset: 12 },
  { value: 'Pacific/Tongatapu', labelKey: 'format.timezone.pacificTongatapu', offset: 13 },
  { value: 'Pacific/Apia', labelKey: 'format.timezone.pacificApia', offset: 13 },
  { value: 'Pacific/Kiritimati', labelKey: 'format.timezone.pacificKiritimati', offset: 14 },
  { value: 'America/New_York', labelKey: 'format.timezone.americaNewYork', offset: -5 },
  { value: 'America/Toronto', labelKey: 'format.timezone.americaToronto', offset: -5 },
  { value: 'America/Chicago', labelKey: 'format.timezone.americaChicago', offset: -6 },
  { value: 'America/Denver', labelKey: 'format.timezone.americaDenver', offset: -7 },
  { value: 'America/Phoenix', labelKey: 'format.timezone.americaPhoenix', offset: -7 },
  { value: 'America/Los_Angeles', labelKey: 'format.timezone.americaLosAngeles', offset: -8 },
  { value: 'America/Vancouver', labelKey: 'format.timezone.americaVancouver', offset: -8 },
  { value: 'America/Seattle', labelKey: 'format.timezone.americaSeattle', offset: -8 },
  { value: 'America/San_Francisco', labelKey: 'format.timezone.americaSanFrancisco', offset: -8 },
  { value: 'America/Anchorage', labelKey: 'format.timezone.americaAnchorage', offset: -9 },
  { value: 'America/Juneau', labelKey: 'format.timezone.americaJuneau', offset: -9 },
  { value: 'America/Nome', labelKey: 'format.timezone.americaNome', offset: -9 },
  { value: 'America/Adak', labelKey: 'format.timezone.americaAdak', offset: -10 },
  { value: 'America/Honolulu', labelKey: 'format.timezone.americaHonolulu', offset: -10 },
  { value: 'America/Santiago', labelKey: 'format.timezone.americaSantiago', offset: -4 },
  { value: 'America/Sao_Paulo', labelKey: 'format.timezone.americaSaoPaulo', offset: -3 },
  { value: 'America/Buenos_Aires', labelKey: 'format.timezone.americaBuenosAires', offset: -3 },
  { value: 'America/Lima', labelKey: 'format.timezone.americaLima', offset: -5 },
  { value: 'America/Mexico_City', labelKey: 'format.timezone.americaMexicoCity', offset: -6 },
  { value: 'America/Cancun', labelKey: 'format.timezone.americaCancun', offset: -5 },
  { value: 'America/Caracas', labelKey: 'format.timezone.americaCaracas', offset: -4 },
  { value: 'America/Montevideo', labelKey: 'format.timezone.americaMontevideo', offset: -3 },
  { value: 'America/Asuncion', labelKey: 'format.timezone.americaAsuncion', offset: -4 },
  { value: 'America/Lagos', labelKey: 'format.timezone.americaLagos', offset: 1 },
  { value: 'Africa/Cairo', labelKey: 'format.timezone.africaCairo', offset: 2 },
  { value: 'Africa/Johannesburg', labelKey: 'format.timezone.africaJohannesburg', offset: 2 },
  { value: 'Africa/Nairobi', labelKey: 'format.timezone.africaNairobi', offset: 3 },
  { value: 'Africa/Addis_Ababa', labelKey: 'format.timezone.africaAddisAbaba', offset: 3 },
  { value: 'Africa/Dubai', labelKey: 'format.timezone.africaDubai', offset: 4 },
  { value: 'Africa/Tunis', labelKey: 'format.timezone.africaTunis', offset: 1 },
  { value: 'Africa/Algiers', labelKey: 'format.timezone.africaAlgiers', offset: 1 },
  { value: 'Africa/Casablanca', labelKey: 'format.timezone.africaCasablanca', offset: 0 },
  { value: 'Africa/Windhoek', labelKey: 'format.timezone.africaWindhoek', offset: 2 },
  { value: 'Africa/Maputo', labelKey: 'format.timezone.africaMaputo', offset: 2 },
];

/**
 * 时区列表。
 *
 * `label` 是**读取时**按当前语言解析的 getter（不是模块初始化时快照的字符串）——
 * 现有展示点（`ProfileTab.tsx` / `Settings.tsx` 的下拉）直接渲染 `tz.label`，
 * 这样它们无需改动即可跟随界面语言，也不会渲染出原始 key。
 * 语言来源见 `i18n/locale.ts`（由 `I18nProvider` 绑定）。
 */
export const timezones: TimezoneOption[] = TIMEZONE_DEFS.map((def) => ({
  value: def.value,
  offset: def.offset,
  get label(): string {
    return tBound(def.labelKey);
  },
}));

/** 下拉选项视图（同上：label 也是 getter，避免在模块初始化时把语言定死） */
export const timezoneOptions: { value: string; label: string }[] = timezones.map((tz) => ({
  value: tz.value,
  get label(): string {
    return tz.label;
  },
}));

export function getTimezoneLabel(timezone: string, locale?: string): string {
  const def = TIMEZONE_DEFS.find((d) => d.value === timezone);
  return def ? tBound(def.labelKey, locale) : timezone;
}

export function getTimezoneOffset(timezone: string): number {
  const def = TIMEZONE_DEFS.find((d) => d.value === timezone);
  return def?.offset ?? 0;
}
