/* Jalali (Persian) calendar math and formatting.
   Same algorithm as Core/Jalali.cs (valid for 1178–1633 AP). Dates are exchanged with the
   server as 'YYYY/MM/DD' strings with ASCII digits and shown to users with Persian digits. */
const Jalali = (() => {
  const BREAKS = [-61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178];
  const MONTHS = ['فروردین', 'اردیبهشت', 'خرداد', 'تیر', 'مرداد', 'شهریور', 'مهر', 'آبان', 'آذر', 'دی', 'بهمن', 'اسفند'];
  // Weeks start on Saturday.
  const WEEKDAYS = ['شنبه', 'یکشنبه', 'دوشنبه', 'سه‌شنبه', 'چهارشنبه', 'پنجشنبه', 'جمعه'];
  const WEEKDAYS_SHORT = ['ش', 'ی', 'د', 'س', 'چ', 'پ', 'ج'];
  const FA = '۰۱۲۳۴۵۶۷۸۹';

  const div = (a, b) => Math.trunc(a / b);
  const mod = (a, b) => a - div(a, b) * b;

  function jalCal(jy, withoutLeap) {
    if (jy < BREAKS[0] || jy >= BREAKS[BREAKS.length - 1]) throw new RangeError('Invalid Jalali year ' + jy);
    const gy = jy + 621;
    let leapJ = -14, jp = BREAKS[0], jump = 0;
    for (let i = 1; i < BREAKS.length; i++) {
      const jm = BREAKS[i];
      jump = jm - jp;
      if (jy < jm) break;
      leapJ += div(jump, 33) * 8 + div(mod(jump, 33), 4);
      jp = jm;
    }
    let n = jy - jp;
    leapJ += div(n, 33) * 8 + div(mod(n, 33) + 3, 4);
    if (mod(jump, 33) === 4 && jump - n === 4) leapJ += 1;
    const leapG = div(gy, 4) - div((div(gy, 100) + 1) * 3, 4) - 150;
    const march = 20 + leapJ - leapG;
    if (withoutLeap) return { gy, march };
    if (jump - n < 6) n = n - jump + div(jump + 4, 33) * 33;
    let leap = mod(mod(n + 1, 33) - 1, 4);
    if (leap === -1) leap = 4;
    return { leap, gy, march };
  }

  function g2d(gy, gm, gd) {
    const t = div(gm - 8, 6);
    const d = div((gy + t + 100100) * 1461, 4) + div(153 * mod(gm + 9, 12) + 2, 5) + gd - 34840408;
    return d - div(div(gy + 100100 + t, 100) * 3, 4) + 752;
  }

  function d2g(jdn) {
    let j = 4 * jdn + 139361631;
    j += div(div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908;
    const i = div(mod(j, 1461), 4) * 5 + 308;
    const gd = div(mod(i, 153), 5) + 1;
    const gm = mod(div(i, 153), 12) + 1;
    const gy = div(j, 1461) - 100100 + div(8 - gm, 6);
    return { y: gy, m: gm, d: gd };
  }

  function j2d(jy, jm, jd) {
    const r = jalCal(jy, true);
    return g2d(r.gy, 3, r.march) + (jm - 1) * 31 - div(jm, 7) * (jm - 7) + jd - 1;
  }

  function d2j(jdn) {
    const gy = d2g(jdn).y;
    let jy = gy - 621;
    const r = jalCal(jy);
    let k = jdn - g2d(gy, 3, r.march);
    if (k >= 0) {
      if (k <= 185) return { y: jy, m: 1 + div(k, 31), d: mod(k, 31) + 1 };
      k -= 186;
    } else {
      jy -= 1;
      k += 179;
      if (r.leap === 1) k += 1;
    }
    return { y: jy, m: 7 + div(k, 30), d: mod(k, 30) + 1 };
  }

  const isLeap = y => jalCal(y).leap === 0;
  const daysInMonth = (y, m) => (m <= 6 ? 31 : m <= 11 ? 30 : isLeap(y) ? 30 : 29);
  const isValid = (y, m, d) => {
    try { return Number.isInteger(y) && Number.isInteger(m) && Number.isInteger(d) && m >= 1 && m <= 12 && d >= 1 && d <= daysInMonth(y, m); }
    catch (e) { return false; }
  };

  /** Day number: comparable, and 1 unit = 1 day. */
  const ordinal = (y, m, d) => j2d(y, m, d);
  const fromOrdinal = n => d2j(n);
  const addDays = (date, n) => d2j(j2d(date.y, date.m, date.d) + n);
  const fromGregorian = (gy, gm, gd) => d2j(g2d(gy, gm, gd));
  const toGregorian = (y, m, d) => d2g(j2d(y, m, d));
  const today = () => { const n = new Date(); return fromGregorian(n.getFullYear(), n.getMonth() + 1, n.getDate()); };

  /** 0 = Saturday … 6 = Friday */
  function weekday(y, m, d) {
    const g = toGregorian(y, m, d);
    return (new Date(Date.UTC(g.y, g.m - 1, g.d)).getUTCDay() + 1) % 7;
  }

  const pad = n => String(n).padStart(2, '0');
  const toFa = v => String(v ?? '').replace(/\d/g, c => FA[c]);
  const toEn = v => String(v ?? '').replace(/[۰-۹]/g, c => FA.indexOf(c)).replace(/[٠-٩]/g, c => '٠١٢٣٤٥٦٧٨٩'.indexOf(c));

  /** {y,m,d} -> '1405/07/09' */
  const format = (d) => `${d.y}/${pad(d.m)}/${pad(d.d)}`;

  /** Lenient parse of user text ('۱۴۰۵-۷-۹', '1405/07/09' …) -> {y,m,d} or null. */
  function parse(text) {
    const parts = toEn(text).trim().replace(/[-.\\،]/g, '/').split('/').map(s => s.trim());
    if (parts.length !== 3 || !parts.every(p => /^\d+$/.test(p))) return null;
    const [y, m, d] = parts.map(Number);
    return isValid(y, m, d) ? { y, m, d } : null;
  }

  /** Canonical ASCII form of a stored/typed date, '' for empty, null when invalid. */
  function canonical(text) {
    if (!String(text ?? '').trim()) return '';
    const p = parse(text);
    return p ? format(p) : null;
  }

  /** Persian-digit display of a stored date. */
  const display = text => { const c = canonical(text); return toFa(c === null ? text : c); };
  const compare = (a, b) => ordinal(a.y, a.m, a.d) - ordinal(b.y, b.m, b.d);
  const same = (a, b) => !!a && !!b && a.y === b.y && a.m === b.m && a.d === b.d;
  const monthTitle = (y, m) => `${MONTHS[m - 1]} ${toFa(y)}`;
  const longDate = (d) => `${WEEKDAYS[weekday(d.y, d.m, d.d)]} ${toFa(d.d)} ${MONTHS[d.m - 1]} ${toFa(d.y)}`;

  return { MONTHS, WEEKDAYS, WEEKDAYS_SHORT, isLeap, daysInMonth, isValid, ordinal, fromOrdinal, addDays, fromGregorian, toGregorian,
    today, weekday, toFa, toEn, format, parse, canonical, display, compare, same, monthTitle, longDate };
})();
