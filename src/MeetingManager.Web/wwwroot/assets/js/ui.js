/* Shared UI helpers: state, API client, toast, modal, dialog, small renderers. */
const APP_VERSION = '4.2.0'; // must match the server version (see checkServerVersion)
let token = localStorage.getItem('mm_token') || '';
let state = {};

const $ = id => document.getElementById(id);
const esc = x => String(x ?? '').replace(/[&<>"']/g, m => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m]));
const fd = Jalali.display; // stored date -> Persian digits
const fa = Jalali.toFa;
const ROLE_LABEL = { admin: 'مدیر سامانه', manager: 'مدیر', user: 'کاربر' };
const STATUSES = ['جدید', 'در حال انجام', 'انجام شده', 'عقب‌افتاده'];

async function api(url, opt = {}) {
  opt.headers = { ...(opt.headers || {}), 'Content-Type': 'application/json', 'X-Session-Token': token };
  const r = await fetch(url, opt);
  if (r.status === 401 && token) { logout(); throw Error('نشست شما منقضی شده است.'); }
  if (!r.ok) {
    const j = await r.json().catch(() => ({}));
    throw Error(typeof j.detail === 'string' && j.detail ? j.detail : 'خطایی رخ داد. دوباره تلاش کنید.');
  }
  return r.headers.get('content-type')?.includes('application/json') ? r.json() : r.blob();
}

const has = p => !!state.permissions?.includes(p) || state.user?.role === 'admin';

let toastTimer;
function toast(message) {
  const el = $('toast');
  el.textContent = message;
  el.classList.remove('hide');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.add('hide'), 3200);
}

/* ───────── Modal & input dialog ───────── */
let lastFocus = null;
function modal(html) {
  if ($('modal').classList.contains('hide')) lastFocus = document.activeElement;
  $('modalbox').innerHTML = html;
  $('modal').classList.remove('hide');
  $('modalbox').scrollTop = 0;
}
function closeModal() {
  DatePicker.close();
  TimePicker.close();
  $('modal').classList.add('hide');
  lastFocus?.focus?.();
}
const mh = (title, body) => `<div class="modalhead"><h2>${title}</h2><button class="close" aria-label="بستن" onclick="closeModal()">×</button></div>${body}`;

/** Small text prompt that sits above the current modal. Resolves to the text, or null if cancelled. */
function ask({ title, label, value = '', multiline = false, okText = 'ثبت' }) {
  return new Promise(resolve => {
    const box = $('dialogbox');
    const input = multiline ? `<textarea id="askInput" rows="4">${esc(value)}</textarea>` : `<input id="askInput" value="${esc(value)}">`;
    box.innerHTML = `<div class="modalhead"><h2>${esc(title)}</h2><button class="close" id="askClose" aria-label="بستن">×</button></div>
      <div class="field"><label for="askInput">${esc(label)}</label>${input}</div>
      <div class="actionsbar"><button class="btn" id="askOk">${esc(okText)}</button><button class="btn secondary" id="askCancel">انصراف</button></div>`;
    $('dialog').classList.remove('hide');
    const done = v => { $('dialog').classList.add('hide'); $('dialog').onkeydown = null; resolve(v); };
    const submit = () => { const t = $('askInput').value.trim(); if (!t) { $('askInput').focus(); return; } done(t); };
    $('askOk').onclick = submit;
    $('askCancel').onclick = $('askClose').onclick = () => done(null);
    $('dialog').onclick = e => { if (e.target === $('dialog')) done(null); };
    $('dialog').onkeydown = e => {
      if (e.key === 'Escape') { e.stopPropagation(); done(null); }
      if (e.key === 'Enter' && !multiline) { e.preventDefault(); submit(); }
    };
    $('askInput').focus();
  });
}

document.addEventListener('keydown', e => {
  if (e.key !== 'Escape' || DatePicker.isOpen() || TimePicker.isOpen()) return;
  if (!$('dialog').classList.contains('hide')) return;
  if (!$('modal').classList.contains('hide')) closeModal();
});
document.addEventListener('mousedown', e => { if (e.target === $('modal')) closeModal(); });

/* ───────── Small renderers ───────── */
const personOpts = (id, multi = false, ids = []) => {
  const selected = multi ? ids : (id ? [id] : []);
  return state.people.map(p => `<option value="${p.id}" ${selected.includes(p.id) ? 'selected' : ''}>${esc(p.name)}${p.department ? ' — ' + esc(p.department) : ''}</option>`).join('');
};
const projectOpts = id => '<option value="">— بدون پروژه —</option>' + state.projects.map(p => `<option value="${p.id}" ${p.id == id ? 'selected' : ''}>${esc(p.name)}</option>`).join('');
const badge = s => `<span class="badge ${s === 'انجام شده' ? 'done' : s === 'در حال انجام' ? 'doing' : s === 'عقب‌افتاده' ? 'late' : 'new'}">${esc(s)}</span>`;
const progressBar = pct => `<div class="progressbar" role="progressbar" aria-valuenow="${pct}" aria-valuemin="0" aria-valuemax="100"><i style="width:${pct}%"></i></div>`;

/* ───────── File download (Excel / Word / PDF / backup) ───────── */
async function download(url, name) {
  try {
    const full = url + (url.includes('?') ? '&' : '?') + '_mm=' + Date.now();
    const r = await fetch(full, { headers: { 'X-Session-Token': token }, cache: 'no-store' });
    if (!r.ok) {
      const j = await r.json().catch(() => null);
      throw Error(typeof j?.detail === 'string' ? j.detail : `خطا در دریافت فایل (${r.status})`);
    }
    const blob = await r.blob();
    if (!blob.size) {
      throw Error(`سرور فایل خالی برگرداند (HTTP ${r.status}، ${r.headers.get('content-type') || 'بدون نوع'}). برنامه را کامل ببندید و دوباره اجرا کنید؛ اگر ادامه داشت، آدرس /api/health را در مرورگر باز کنید و نتیجه را بفرستید.`);
    }
    if (name.toLowerCase().endsWith('.pdf')) {
      const head = new TextDecoder().decode(new Uint8Array(await blob.slice(0, 5).arrayBuffer()));
      if (head !== '%PDF-') throw Error('فایل PDF معتبر از سرور دریافت نشد.');
    }
    const href = URL.createObjectURL(blob);
    const a = Object.assign(document.createElement('a'), { href, download: name });
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(href), 10000);
    toast(`فایل ${name} آماده شد.`);
  } catch (e) { toast(e.message); }
}

/* Hidden iframe reused by printPdf() so the PDF can be handed to the browser's print dialog. */
let printFrame = null;
function getPrintFrame() {
  if (printFrame) return printFrame;
  printFrame = document.createElement('iframe');
  printFrame.setAttribute('aria-hidden', 'true');
  printFrame.style.cssText = 'position:fixed;left:-10000px;top:-10000px;width:1px;height:1px;border:0';
  document.body.appendChild(printFrame);
  return printFrame;
}

/** Fetch a PDF and send it straight to the browser's print dialog, with no file ever saved to disk. */
async function printPdf(url) {
  try {
    const full = url + (url.includes('?') ? '&' : '?') + '_mm=' + Date.now();
    const r = await fetch(full, { headers: { 'X-Session-Token': token }, cache: 'no-store' });
    if (!r.ok) {
      const j = await r.json().catch(() => null);
      throw Error(typeof j?.detail === 'string' ? j.detail : `خطا در دریافت فایل (${r.status})`);
    }
    const blob = await r.blob();
    if (!blob.size) throw Error('سرور فایل خالی برگرداند.');
    const head = new TextDecoder().decode(new Uint8Array(await blob.slice(0, 5).arrayBuffer()));
    if (head !== '%PDF-') throw Error('فایل PDF معتبر از سرور دریافت نشد.');
    const href = URL.createObjectURL(blob);
    const frame = getPrintFrame();
    frame.onload = () => {
      try { frame.contentWindow.focus(); frame.contentWindow.print(); }
      catch (e) { toast('مرورگر اجازه چاپ مستقیم را نداد؛ فایل را دانلود کنید.'); }
    };
    frame.src = href;
    setTimeout(() => URL.revokeObjectURL(href), 60000);
  } catch (e) { toast(e.message); }
}
