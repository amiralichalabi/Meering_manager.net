/* Persian / English switch. The program is written in Persian; English is applied by replacing known Persian
   phrases (Core/i18n/en.json, served at /api/i18n/en) in the page text and in placeholder/title/aria-label
   attributes, both now and for everything rendered later. Stored values (statuses, dates) never change.
   Switching the language reloads the page. Load this script first. */
const I18N = (() => {
  const lang = localStorage.getItem('mm_lang') === 'en' ? 'en' : 'fa';
  document.documentElement.lang = lang;
  document.documentElement.dir = lang === 'en' ? 'ltr' : 'rtl';
  const api = { lang, locale: lang === 'en' ? 'en-GB' : 'fa-IR', tr: s => s, toggle() { localStorage.setItem('mm_lang', lang === 'en' ? 'fa' : 'en'); location.reload(); } };
  // The two language buttons name the language you would switch to.
  document.addEventListener('DOMContentLoaded', () => {
    const b = document.getElementById('langBtn'), l = document.getElementById('langLogin');
    if (b) b.textContent = lang === 'en' ? 'FA' : 'EN';
    if (l) l.textContent = lang === 'en' ? 'فارسی' : 'English';
  });
  if (lang !== 'en') return api;

  let EN = {};
  try {
    const x = new XMLHttpRequest(); // synchronous on purpose: the dictionary must exist before the first render
    x.open('GET', '/api/i18n/en', false); x.send();
    if (x.status === 200) EN = JSON.parse(x.responseText);
  } catch { /* keep Persian */ }

  const L = '\\u0600-\\u06FF\\u200c';
  const keys = Object.keys(EN).filter(k => k && !/[{}]/.test(k)).sort((a, b) => b.length - a.length);
  const esc = k => k.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const re = keys.length ? new RegExp(`(?<![${L}])(?:${keys.map(esc).join('|')})(?![${L}])`, 'g') : null;
  const PERSIAN = /[؀-ۿ]/;
  const FA = '۰۱۲۳۴۵۶۷۸۹', AR = '٠١٢٣٤٥٦٧٨٩';
  const digits = s => s.replace(/[۰-۹٠-٩]/g, c => { const i = FA.indexOf(c); return i >= 0 ? i : AR.indexOf(c); });
  const tr = s => (typeof s === 'string' && PERSIAN.test(s) ? digits(re ? s.replace(re, m => EN[m]) : s) : s);
  api.tr = tr;

  const ATTRS = ['placeholder', 'title', 'aria-label', 'alt'];
  const attrs = el => {
    for (const a of ATTRS) {
      const v = el.getAttribute(a);
      if (v) { const t = tr(v); if (t !== v) el.setAttribute(a, t); }
    }
    if (el.tagName === 'INPUT' && /^(button|submit)$/.test(el.type)) { const t = tr(el.value); if (t !== el.value) el.value = t; }
  };
  const walk = n => {
    if (n.nodeType === 3) { const t = tr(n.nodeValue); if (t !== n.nodeValue) n.nodeValue = t; return; }
    if (n.nodeType !== 1 || n.tagName === 'SCRIPT' || n.tagName === 'STYLE') return;
    attrs(n);
    n.childNodes.forEach(walk);
  };
  new MutationObserver(list => {
    for (const m of list) {
      if (m.type === 'characterData') walk(m.target);
      else if (m.type === 'attributes') attrs(m.target);
      else m.addedNodes.forEach(walk);
    }
  }).observe(document, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ATTRS });
  document.addEventListener('DOMContentLoaded', () => walk(document.documentElement));

  // Native dialogs are not part of the page.
  for (const f of ['alert', 'confirm', 'prompt']) { const o = window[f].bind(window); window[f] = (m, ...r) => o(tr(m), ...r); }
  return api;
})();
