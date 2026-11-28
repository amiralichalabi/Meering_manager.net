/* Meeting Manager Pro — application screens.
   Load order: jalali.js → ui.js → datepicker.js → charts.js → calendar.js → app.js */
let currentSection = 'dashboard';
const SECTION_TITLES = { dashboard: 'صفحه اصلی', meetings: 'جلسات', actions: 'اقدامات', projects: 'پروژه‌ها', calendar: 'تقویم', events: 'رویدادها', people: 'افراد و تیم‌ها', reports: 'خروجی و پشتیبان‌گیری', imports: 'ورودی', assistant: 'دستیار هوشمند', notes: 'یادداشت‌های من', access: 'دسترسی‌ها و مجوزها' };
const STATUS_COLORS = { 'انجام شده': '#16a34a', 'در حال انجام': '#2563eb', 'جدید': '#64748b', 'عقب‌افتاده': '#dc2626' };
const ids = text => (text || '').split(',').filter(Boolean).map(Number);

/* ───────── Session ───────── */
async function login() {
  try {
    const r = await api('/api/auth/login', { method: 'POST', body: JSON.stringify({ username: $('lu').value, password: $('lp').value }) });
    token = r.token;
    localStorage.setItem('mm_token', token);
    await start();
    if (r.default_password) setTimeout(() => toast('هشدار: رمز عبور پیش‌فرض مدیر هنوز عوض نشده است؛ از منوی پروفایل آن را تغییر دهید.'), 600);
  } catch (e) { toast(e.message); }
}

function logout() {
  const old = token;
  token = '';
  state = {};
  localStorage.removeItem('mm_token');
  if (old) fetch('/api/auth/logout', { method: 'POST', headers: { 'X-Session-Token': old } }).catch(() => {});
  $('app').classList.add('hide');
  $('login').classList.remove('hide');
}

async function refresh() {
  state = await api('/api/bootstrap');
  try { state.actions = await api('/api/actions'); } catch (e) { state.actions = []; }
  try { state.locations = await api('/api/locations'); } catch (e) { state.locations = []; }
  try { state.sprints = await api('/api/sprints'); } catch (e) { state.sprints = []; }
  try { state.events = await api('/api/events'); } catch (e) { state.events = []; }
  CalEvents.invalidate();
  MiniCal.render();
}

/** The server code is loaded once at start-up while the page files are read from disk on every request, so an
    old server left running shows the new UI with old behaviour. Warn when the two versions differ. */
async function checkServerVersion() {
  try {
    const h = await (await fetch('/api/health', { cache: 'no-store' })).json();
    $('appVersion').textContent = ' · نسخه ' + fa(h.version);
    const banner = $('versionBanner');
    banner.classList.toggle('hide', h.version === APP_VERSION);
    if (h.version !== APP_VERSION) banner.textContent = `نسخه‌ی در حال اجرای سرور (${fa(h.version)}) با فایل‌های برنامه (${fa(APP_VERSION)}) یکی نیست؛ احتمالاً نسخه‌ی قبلی هنوز باز است. برنامه را کامل ببندید (در ویندوز: Task Manager ← MeetingManager) و دوباره اجرا کنید.`;
    if (h.pdf && !h.pdf.ok) toast('خروجی PDF روی این سیستم کار نمی‌کند: ' + h.pdf.error);
  } catch (e) { /* server unreachable: api() reports it */ }
}

async function start() {
  await refresh();
  $('login').classList.add('hide');
  $('app').classList.remove('hide');
  $('userName').textContent = state.user.full_name;
  $('userRole').textContent = (ROLE_LABEL[state.user.role] || state.user.role) + (state.user.birth_date ? ' · تولد ' + Jalali.toFa(state.user.birth_date) : '');
  setAvatar($('profileAvatar'), state.user);
  $('importNav').classList.toggle('hide', !has('actions.manage'));
  $('accessNav').classList.toggle('hide', !has('users.manage') && !has('permissions.manage'));
  MiniCal.render();
  checkServerVersion();
  await section('dashboard');
}

const LOOKS = {
  accents: [['آبی', '#2563eb', '#1d4ed8', '#e8f0ff'], ['سبز', '#16a34a', '#15803d', '#dcfce7'], ['بنفش', '#7c3aed', '#6d28d9', '#ede9fe'], ['نارنجی', '#ea580c', '#c2410c', '#ffedd5'], ['قرمز', '#dc2626', '#b91c1c', '#fee2e2'], ['فیروزه‌ای', '#0891b2', '#0e7490', '#cffafe']],
  backgrounds: [['ساده', ''], ['آسمان', 'linear-gradient(160deg,#e0ecff,#f3f6fa 55%)'], ['نعناع', 'linear-gradient(160deg,#dcf5ea,#f3f6fa 55%)'], ['شرجی', 'linear-gradient(160deg,#fde8d7,#f7f3ee 55%)'], ['یاسی', 'linear-gradient(160deg,#ece3fb,#f6f4fb 55%)']],
};
function applyLook() {
  let look = {};
  try { look = JSON.parse(localStorage.getItem('mm_look') || '{}'); } catch (e) { /* default look */ }
  const s = document.body.style, a = LOOKS.accents[look.accent ?? 0] || LOOKS.accents[0], bg = LOOKS.backgrounds[look.bg ?? 0] || LOOKS.backgrounds[0];
  s.setProperty('--brand', a[1]); s.setProperty('--brand-strong', a[2]); s.setProperty('--brand-soft', a[3]);
  s.setProperty('--ring', `0 0 0 3px ${a[1]}33`);
  document.body.classList.toggle('has-bg', !!bg[1]);
  s.setProperty('--bg-image', bg[1] || 'none');
}
function setLook(key, i) {
  let look = {};
  try { look = JSON.parse(localStorage.getItem('mm_look') || '{}'); } catch (e) { /* ignore */ }
  look[key] = i;
  try { localStorage.setItem('mm_look', JSON.stringify(look)); } catch (e) { /* private mode */ }
  applyLook(); openLook();
}
function openLook() {
  let look = {};
  try { look = JSON.parse(localStorage.getItem('mm_look') || '{}'); } catch (e) { /* ignore */ }
  modal(mh('ظاهر برنامه', `
    <h4>رنگ اصلی</h4><div class="swatches">${LOOKS.accents.map((a, i) => `<button class="swatch ${(look.accent ?? 0) === i ? 'on' : ''}" style="background:${a[1]}" title="${a[0]}" aria-label="${a[0]}" onclick="setLook('accent',${i})"></button>`).join('')}</div>
    <h4>پس‌زمینه</h4><div class="bgs">${LOOKS.backgrounds.map((b, i) => `<button class="bgchip ${(look.bg ?? 0) === i ? 'on' : ''}" style="background:${b[1] || 'var(--bg)'}" onclick="setLook('bg',${i})"><span>${b[0]}</span></button>`).join('')}</div>
    <p class="muted">برای حالت تیره از دکمه‌ی ماه در کنار نام خود استفاده کنید. این تنظیمات فقط روی همین مرورگر ذخیره می‌شود.</p>
    <div class="actionsbar"><button class="btn secondary" onclick="closeModal()">بستن</button></div>`));
}

function toggleTheme() {
  document.body.classList.toggle('dark');
  localStorage.setItem('mm_theme', document.body.classList.contains('dark') ? 'dark' : 'light');
}

/* ───────── Navigation ───────── */
async function section(name) {
  currentSection = name;
  document.querySelectorAll('main section').forEach(x => x.classList.add('hide'));
  $(name).classList.remove('hide');
  document.querySelectorAll('.nav button').forEach(b => {
    b.classList.toggle('active', b.dataset.sec === name);
    b.toggleAttribute('aria-current', b.dataset.sec === name);
  });
  $('title').textContent = SECTION_TITLES[name];
  const quick = {
    dashboard: `<button class="btn" onclick="openMeeting()">+ جلسه جدید</button><button class="btn secondary" onclick="openAction()">+ اقدام جدید</button>`,
    meetings: has('meetings.manage') ? `<button class="btn" onclick="openMeeting()">+ جلسه جدید</button><button class="btn secondary" onclick="openVoiceDraft()">جلسه از روی صدا</button>` : '',
    events: has('meetings.manage') ? `<button class="btn" onclick="openEvent()">+ رویداد جدید</button>` : '',
    actions: has('actions.manage') ? `<button class="btn" onclick="openAction()">+ اقدام جدید</button>` : '',
    projects: has('projects.manage') ? `<button class="btn" onclick="openProject()">+ پروژه جدید</button>` : '',
  };
  $('quick').innerHTML = quick[name] || '';
  const render = { dashboard: renderDashboard, meetings: renderMeetings, actions: renderActions, projects: renderProjects, calendar: () => Calendar.render(), events: renderEvents, people: renderPeople, reports: renderReports, imports: renderImports, assistant: renderAssistant, notes: renderNotes, access: renderAccess }[name];
  await render();
}

/** After a successful save: stay where the user is, except on the dashboard where we show the list. */
const afterSave = entitySection => section(currentSection === 'dashboard' ? entitySection : currentSection);

/* ───────── Dashboard ───────── */
async function renderDashboard() {
  const d = await api('/api/dashboard'), bs = d.by_status;
  const segments = ['انجام شده', 'در حال انجام', 'جدید', 'عقب‌افتاده'].map(s => ({ key: s, label: s, value: bs[s] || 0, color: STATUS_COLORS[s] }));
  const donePct = d.actions ? Math.round((bs['انجام شده'] || 0) / d.actions * 100) : 0;
  const donut = d.actions
    ? Donut.html({ segments, total: d.actions, centerLabel: 'اقدام', subLabel: `${fa(donePct)}٪ تکمیل‌شده` })
    : '<div class="empty">هنوز اقدامی ثبت نشده است.</div>';

  let mine = [], follow = [], todayCount = 0;
  if (has('actions.view') && state.user.person_id) {
    try {
      const all = await api('/api/actions?archived=0'), me = state.user.person_id, today = Jalali.format(Jalali.today());
      const open = a => CalEvents.effectiveStatus(a) !== 'انجام شده';
      mine = all.filter(a => open(a) && (a.assignee_id === me || (!a.assignee_id && a.owner_id === me)));
      follow = all.filter(a => open(a) && a.owner_id === me && a.assignee_id && a.assignee_id !== me);
      todayCount = mine.filter(a => a.due === today).length;
    } catch (e) { /* the rest of the dashboard still works */ }
  }
  const lateOf = list => list.filter(a => CalEvents.effectiveStatus(a) === 'عقب‌افتاده');
  const workRow = a => `<div class="workrow" onclick="openAction(${a.id})"><div><b>${esc(a.subject)}</b><br><span class="muted">${esc(a.project || '')}${a.due ? ' · مهلت: ' + fd(a.due) : ''}${a.sprint ? ' · ' + esc(a.sprint) : ''}</span></div>${badge(CalEvents.effectiveStatus(a))}</div>`;
  const mywork = state.user.person_id && has('actions.view') ? `
    <div class="sumcards">
      <button class="sumcard ok" onclick="goActions({})"><span class="dot"></span><b>${fa(todayCount)}</b><span>کارهای امروز من</span></button>
      <button class="sumcard bad" onclick="goActions({status:'عقب‌افتاده'})"><span class="dot"></span><b>${fa(lateOf(mine).length)}</b><span>کارهای دارای تأخیر</span></button>
      <button class="sumcard info" onclick="goActions({})"><span class="dot"></span><b>${fa(follow.length)}</b><span>کارهای قابل پیگیری</span></button>
    </div>
    <div class="mywork">
      <div class="workcol"><div class="workhead green">کارهای من</div><div class="workbody">${mine.slice(0, 60).map(workRow).join('') || '<div class="empty">کاری برای انجام ندارید.</div>'}</div></div>
      <div class="workcol"><div class="workhead red">پیگیری از دیگران</div><div class="workbody">${follow.slice(0, 60).map(workRow).join('') || '<div class="empty">کاری برای پیگیری ندارید.</div>'}</div></div>
    </div>` : '';

  $('dashboard').innerHTML = mywork + `
    <div class="cards">
      <div class="card"><div class="k">کل جلسات</div><div class="v">${fa(d.meetings)}</div></div>
      <div class="card purple"><div class="k">کل اقدامات</div><div class="v">${fa(d.actions)}</div></div>
      <div class="card green"><div class="k">انجام‌شده</div><div class="v">${fa(bs['انجام شده'] || 0)}</div></div>
      <div class="card orange"><div class="k">در حال انجام</div><div class="v">${fa(bs['در حال انجام'] || 0)}</div></div>
      <div class="card red"><div class="k">عقب‌افتاده</div><div class="v">${fa(d.overdue)}</div></div>
    </div>
    <div class="dash-grid">
      <div class="section donut-card">
        <div class="sectionhead"><h3>وضعیت اقدامات</h3><span class="muted">${fa(d.actions)} اقدام</span></div>
        <div id="statusDonut">${donut}</div>
        <p class="muted mini" style="margin-bottom:0">با رفتن روی هر بخش جزئیات آن در مرکز نمودار نمایش داده می‌شود؛ با کلیک، فهرست همان وضعیت باز می‌شود.</p>
      </div>
      <div class="dash-col">
      <div class="section">
        <div class="sectionhead"><h3>پیشرفت پروژه‌ها</h3><button class="btn secondary" onclick="section('projects')">مشاهده همه</button></div>
        <div class="scrollbox">${d.project_stats.map(p => `<div class="project-row"><b>${esc(p.name)}</b><div>${progressBar(p.progress)}<small class="muted">${fa(p.actions)} اقدام · ${fa(p.done)} انجام‌شده · مدیر: ${esc(p.manager || '—')} · پایان: ${fd(p.end_date) || '—'}</small></div><b>${fa(p.progress)}٪</b></div>`).join('') || '<div class="empty">پروژه‌ای ثبت نشده است.</div>'}</div>
      </div>
      <div class="section">
      ${(d.upcoming_events || []).length ? `<div class="sectionhead"><h3>رویدادهای پیش‌رو</h3><button class="btn secondary" onclick="section('events')">همه رویدادها</button></div>${d.upcoming_events.map(e => `<div class="event"><div><b>${esc(e.title)}</b><br><span class="muted">${esc(e.kind)} · ${fd(e.date)}${e.start_time ? ' · ' + fa(e.start_time) : ''}${e.place ? ' · ' + esc(e.place) : ''}</span></div></div>`).join('')}` : ''}
      <div class="sectionhead"><h3>جلسات اخیر</h3><button class="btn secondary" onclick="section('meetings')">همه جلسات</button></div>
      <div class="scrollbox">${d.upcoming_meetings.map(m => `<div class="event"><div><b>${esc(m.title)}</b><br><span class="muted">${fd(m.date)}${m.time ? ' · ' + fa(m.time) : ''}${m.place ? ' · ' + esc(m.place) : ''}</span></div><button class="btn secondary" onclick="showMeeting(${m.id})">مشاهده</button></div>`).join('') || '<div class="empty">جلسه‌ای ثبت نشده است.</div>'}</div>
      </div>
      </div>
    </div>`;
  Donut.bind($('statusDonut'), status => goActions({ status }));
}

/* ───────── Meetings ───────── */
function renderMeetings() {
  const can = has('meetings.export');
  $('meetings').innerHTML = `<div class="section">
    <div class="searchbar"><input class="search" id="meetingSearch" placeholder="جستجوی جلسه، تاریخ، محل، خلاصه یا پروژه" oninput="filterMeetings()"><button class="btn secondary" onclick="clearSearch('meetingSearch');filterMeetings()">پاک کردن</button></div>
    <div class="sectionhead"><div><h3>جلسات</h3><span class="muted">برای دریافت خروجی، جلسه‌ها را انتخاب کنید.</span></div>
      ${can ? exportToolbar('meeting', 'meetings') : ''}</div>
    <div class="tablewrap"><table class="table"><thead><tr><th><input type="checkbox" aria-label="انتخاب همه" onclick="toggleAll('meeting',this)"></th><th>ردیف</th><th>جلسه</th><th>تاریخ</th><th>ساعت</th><th>پروژه</th><th>خلاصه</th><th>اقدامات</th><th></th></tr></thead>
      <tbody id="meetingRows">${meetingRows(state.meetings || [])}</tbody></table></div></div>`;
}

function exportToolbar(type, base) {
  const item = (label, js) => `<button type="button" class="menu-item" onclick="closeMenus();${js}">${label}</button>`;
  const fm = { xlsx: 'Excel', docx: 'Word', pdf: 'PDF' };
  const group = (title, items) => `<div class="menu-group"><div class="menu-title">${title}</div>${items}</div>`;
  return `<div class="toolbar"><details class="menu"><summary class="btn"><svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v11M7 11l5 5 5-5M5 20h14"/></svg>خروجی</summary>
    <div class="menu-pop">
      ${group('موارد انتخاب‌شده', Object.keys(fm).map(f => item(fm[f], `exportSelected('${type}','${f}')`)).join('') + item('چاپ PDF', `printSelected('${type}')`))}
      ${group('همه‌ی موارد', Object.keys(fm).map(f => item(fm[f], `exportAll('${base}','${f}')`)).join('') + item('چاپ PDF', `printPdf('/api/export/${base}.pdf')`))}
      ${group('با فیلتر', item('بازه‌ی تاریخ یا اسپرینت…', `openExportFilter('${type}')`))}
      ${type === 'action' ? group('Azure DevOps', taskSheetButtons()) : ''}
    </div></details></div>`;
}
function closeMenus() { document.querySelectorAll('details.menu[open]').forEach(d => d.removeAttribute('open')); }
document.addEventListener('click', e => { if (!e.target.closest('details.menu')) closeMenus(); });

function meetingRows(xs) {
  return xs.map((m, i) => `<tr><td><input class="meeting-check" type="checkbox" value="${m.id}" aria-label="انتخاب جلسه"></td><td>${fa(i + 1)}</td>
    <td><b>${esc(m.title)}</b><br><span class="muted">${esc(m.place)}</span></td><td>${fd(m.date)}</td><td>${fa(m.time)}</td><td>${esc(m.project)}</td>
    <td>${esc(m.summary || m.desc || '')}</td><td>${fa(m.current.length)}</td>
    <td><button class="btn secondary" onclick="showMeeting(${m.id})">مشاهده</button> ${has('meetings.manage') ? `<button class="btn secondary" onclick="openMeeting(${m.id})">ویرایش</button>` : ''}</td></tr>`).join('')
    || '<tr><td colspan="9" class="empty">جلسه‌ای پیدا نشد.</td></tr>';
}

function filterMeetings() {
  const q = $('meetingSearch').value.trim().toLowerCase();
  $('meetingRows').innerHTML = meetingRows(state.meetings.filter(m => [m.title, m.date, fd(m.date), m.place, m.summary, m.desc, m.project].join(' ').toLowerCase().includes(q)));
}

function attendeeEditor(list = []) {
  return `<div class="addline"><select id="attendeePick" aria-label="انتخاب فرد"><option value="">انتخاب فرد...</option>${personOpts()}</select><button type="button" class="btn iconbtn" aria-label="افزودن" onclick="addAttendee()">+</button></div><div id="attendeeList" class="collab-list"></div><input type="hidden" id="attendees" value="${list.join(',')}">`;
}
function addAttendee() {
  const id = Number($('attendeePick').value);
  if (!id) return;
  const list = ids($('attendees').value);
  if (!list.includes(id)) list.push(id);
  $('attendees').value = list.join(',');
  renderAttendeeList(list);
}
function removeAttendee(id) {
  const list = ids($('attendees').value).filter(x => x !== id);
  $('attendees').value = list.join(',');
  renderAttendeeList(list);
}
function renderAttendeeList(list) {
  $('attendeeList').innerHTML = list.map(id => {
    const p = state.people.find(x => x.id === id);
    return p ? `<span class="collab-chip">${esc(p.name)}<button type="button" aria-label="حذف" onclick="removeAttendee(${id})">×</button></span>` : '';
  }).join('') || '<span class="muted">هنوز فردی اضافه نشده است.</span>';
}

async function openMeeting(id, prefill = {}) {
  const m = id ? state.meetings.find(x => x.id === id) : null;
  let avail = [];
  try { avail = await api('/api/meetings/available-actions'); } catch (e) { /* not allowed to link actions */ }
  const linkedIds = new Set((m?.current || []).filter(a => a.meeting_id !== id).map(a => a.id));
  modal(mh(id ? 'ویرایش جلسه' : 'جلسه جدید', `
    <div class="grid">
      <div class="field"><label for="mt">عنوان جلسه</label><input id="mt" value="${esc(m?.title ?? prefill.title ?? '')}"></div>
      <div class="field"><label for="md">تاریخ جلسه</label>${datePicker('md', m?.date || prefill.date || Jalali.format(Jalali.today()))}</div>
      <div class="field"><label for="mti">ساعت (۲۴ ساعته)</label>${timePicker('mti', m?.time || '')}</div>
      <div class="field"><label for="mp">محل</label><input id="mp" list="placeList" value="${esc(m?.place || '')}" placeholder="از لیست مکان‌ها انتخاب کنید یا بنویسید">
        <datalist id="placeList">${(state.locations || []).map(l => `<option value="${esc(l.name)}">`).join('')}</datalist></div>
      <div class="field"><label for="mproj">پروژه</label><select id="mproj">${projectOpts(m?.project_id)}</select></div>
      <div class="field full"><label for="msum">خلاصه جلسه</label><textarea id="msum">${esc(m?.summary ?? prefill.summary ?? '')}</textarea></div>
      <div class="field full"><label for="mdesc">توضیحات</label><textarea id="mdesc">${esc(m?.desc || '')}</textarea></div>
      <div class="field full"><label>افراد حاضر در جلسه</label>${attendeeEditor(m?.attendee_ids || prefill.attendee_ids || [])}</div>
    </div>
    <div class="section"><h3>اقدامات جلسه قبل</h3><p class="muted">اقدام‌ها مستقل می‌مانند و فقط به این جلسه لینک می‌شوند.</p>
      <div style="max-height:210px;overflow:auto">${avail.map(a => `<label class="check"><input class="prev-action" type="checkbox" value="${a.id}" ${linkedIds.has(a.id) ? 'checked' : ''}> <span><b>${esc(a.subject)}</b> · ${esc(a.project)} · ${fa(a.progress)}٪</span></label>`).join('') || '<div class="empty">اقدام قابل انتخابی وجود ندارد.</div>'}</div></div>
    <div class="section"><div class="sectionhead"><h3>اقدامات جدید جلسه</h3><div class="toolbar"><button class="btn secondary" onclick="addSprintInline()">+ اسپرینت جدید</button><button class="btn secondary" onclick="addMeetingActionRow()">+ افزودن اقدام</button></div></div>${sprintDatalist()}
      <div id="meetingActionsForm">${(m ? (m.current || []).filter(a => a.meeting_id === id) : (prefill.actions || [])).map(a => meetingActionRow(a)).join('')}</div></div>
    <div class="actionsbar"><button class="btn" onclick="saveMeeting(${id || 0})">ذخیره جلسه</button><button class="btn secondary" onclick="closeModal()">انصراف</button></div>`));
  renderAttendeeList(m?.attendee_ids || prefill.attendee_ids || []);
}

let meetingRowSeq = 0;
function meetingActionRow(a = {}) {
  const k = ++meetingRowSeq;
  return `<div class="section" data-marow="${k}"><div class="grid3">
    <div class="field"><label>موضوع</label><input class="ma-sub" value="${esc(a.subject || '')}"></div>
    <div class="field"><label>مسئول اصلی</label><select class="ma-owner"><option value="">—</option>${personOpts(a.owner_id)}</select></div>
    <div class="field"><label>انجام‌دهنده</label><select class="ma-assignee"><option value="">—</option>${personOpts(a.assignee_id)}</select></div>
    <div class="field"><label>تاریخ شروع</label>${datePicker('ma-start-' + k, a.start_date || '')}</div>
    <div class="field"><label>مهلت</label>${datePicker('ma-due-' + k, a.due || '', { minFrom: 'ma-start-' + k })}</div>
    <div class="field"><label>پروژه</label><select class="ma-project">${projectOpts(a.project_id)}</select></div>
    <div class="field"><label>پیشرفت (٪)</label><input class="ma-progress" type="number" min="0" max="100" value="${a.progress || 0}"></div>
    <div class="field"><label>اسپرینت</label><input class="ma-sprint" list="sprintList" value="${esc(a.sprint || '')}" placeholder="مثلاً اسپرینت ۱"></div>
    <div class="field full"><label>تصمیم / شرح</label><textarea class="ma-decision">${esc(a.decision || '')}</textarea></div>
  </div><button type="button" class="btn danger small" onclick="this.closest('[data-marow]').remove()">حذف این اقدام</button><input type="hidden" class="ma-id" value="${a.id || ''}"></div>`;
}
function addMeetingActionRow() { $('meetingActionsForm').insertAdjacentHTML('beforeend', meetingActionRow()); }

/** Throws if due is before start so the user gets a clear message before the request is sent. */
function checkRange(start, end, message) {
  if (start && end && end < start) throw Error(message);
}

async function saveMeeting(id) {
  try {
    const actions = [...document.querySelectorAll('#meetingActionsForm [data-marow]')].map(row => {
      const k = row.dataset.marow;
      const start = readDate('ma-start-' + k, 'تاریخ شروع اقدام'), due = readDate('ma-due-' + k, 'مهلت اقدام');
      checkRange(start, due, 'مهلت اقدام نمی‌تواند قبل از تاریخ شروع باشد.');
      return {
        id: Number(row.querySelector('.ma-id').value) || null, subject: row.querySelector('.ma-sub').value, decision: row.querySelector('.ma-decision').value,
        owner_id: Number(row.querySelector('.ma-owner').value) || null, assignee_id: Number(row.querySelector('.ma-assignee').value) || null,
        start_date: start, due, project_id: Number(row.querySelector('.ma-project').value) || null, status: 'جدید', priority: 'متوسط',
        progress: Number(row.querySelector('.ma-progress').value || 0), sprint: row.querySelector('.ma-sprint').value.trim(),
      };
    });
    const body = {
      title: $('mt').value, meeting_date: readDate('md', 'تاریخ جلسه'), time: readTime('mti', 'ساعت جلسه'), place: $('mp').value, summary: $('msum').value, description: $('mdesc').value,
      project_id: Number($('mproj').value) || null, previous_items: [], actions, linked_action_ids: [...document.querySelectorAll('.prev-action:checked')].map(x => Number(x.value)),
      attendee_ids: ids($('attendees').value),
    };
    await api(id ? `/api/meetings/${id}` : '/api/meetings', { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) });
    await refresh();
    closeModal();
    toast('جلسه ذخیره شد.');
    await afterSave('meetings');
  } catch (e) { toast(e.message); }
}

function meetingActionsBySprint(list) {
  if (!list.length) return '<div class="empty">اقدامی ثبت نشده است.</div>';
  const card = a => `<div class="section"><b>${esc(a.subject)}</b> ${badge(CalEvents.effectiveStatus(a))}<p>${esc(a.decision)}</p>${progressBar(a.progress)}<small>${fa(a.progress)}٪ · ${esc(a.assignee || a.owner || '')}${a.due ? ' · مهلت: ' + fd(a.due) : ''}</small></div>`;
  const groups = new Map();
  list.forEach(a => { const k = a.sprint || ''; if (!groups.has(k)) groups.set(k, []); groups.get(k).push(a); });
  if (groups.size === 1 && groups.has('')) return list.map(card).join('');
  const order = [...groups.keys()].sort((x, y) => (x === '') - (y === '') || x.localeCompare(y, 'fa'));
  return order.map(k => `<h4>${k ? 'اسپرینت: ' + esc(k) : 'بدون اسپرینت'} <span class="muted">(${fa(groups.get(k).length)} اقدام)</span></h4>${groups.get(k).map(card).join('')}`).join('');
}

function showMeeting(id) {
  const m = state.meetings.find(x => x.id === id);
  if (!m) return;
  modal(mh(esc(m.title), `
    <p class="muted">${fd(m.date)}${m.time ? ' · ساعت ' + fa(m.time) : ''}${m.place ? ' · ' + esc(m.place) : ''}${m.project ? ' · پروژه: ' + esc(m.project) : ''}</p>
    ${m.attendees.length ? `<div class="chips">${m.attendees.map(n => `<span class="chip">${esc(n)}</span>`).join('')}</div>` : ''}
    <div class="section"><h3>خلاصه جلسه</h3><p>${esc(m.summary || 'خلاصه‌ای ثبت نشده است.')}</p></div>
    <div class="section"><h3>اقدامات مرتبط</h3>${meetingActionsBySprint(m.current)}</div>
    <div class="actionsbar">${has('meetings.manage') ? `<button class="btn" onclick="openMeeting(${id})">ویرایش جلسه</button>` : ''}<button class="btn secondary" onclick="closeModal()">بستن</button></div>`));
}

/* ───────── Actions ───────── */
const actionFilter = { q: '', status: '', sprint: '', archived: false };

async function renderActions() {
  try { state.actions = await api('/api/actions?archived=' + (actionFilter.archived ? '1' : '0')); CalEvents.invalidate(); } catch (e) { toast(e.message); return; }
  const can = has('actions.export');
  $('actions').innerHTML = `<div class="section">
    <div class="searchbar"><input class="search" id="actionSearch" value="${esc(actionFilter.q)}" placeholder="جستجوی اقدام، مسئول، پروژه، وضعیت یا جلسه" oninput="filterActions()">
      <select id="actionStatus" class="status-filter" aria-label="فیلتر وضعیت" onchange="filterActions()"><option value="">همه وضعیت‌ها</option>${STATUSES.map(s => `<option ${s === actionFilter.status ? 'selected' : ''}>${s}</option>`).join('')}</select>
      <select id="actionSprint" class="status-filter" aria-label="فیلتر اسپرینت" onchange="filterActions()"><option value="">همه اسپرینت‌ها</option>${sprintNames().map(n => `<option ${n === actionFilter.sprint ? 'selected' : ''}>${esc(n)}</option>`).join('')}</select>
      <button class="btn secondary" onclick="clearSearch('actionSearch');$('actionStatus').value='';$('actionSprint').value='';filterActions()">پاک کردن</button>
      <button class="btn secondary" onclick="openSprints()">اسپرینت‌ها</button>
      <button class="btn secondary" onclick="toggleActionBoard()">${actionBoard ? 'نمای جدول' : 'نمای بُرد'}</button>
      ${archiveSwitch(actionFilter.archived, 'toggleArchivedActions')}</div>
    <div class="sectionhead"><div><h3>اقدامات</h3><span class="muted">جستجو و فیلتر وضعیت روی همه‌ی اقدامات قابل‌دسترس اعمال می‌شود.</span></div>${can ? exportToolbar('action', 'actions') : ''}</div>
    ${actionBoard ? `<div id="actionBoard">${kanbanHtml(filteredActions())}</div>` : `<div class="tablewrap"><table class="table"><thead><tr><th><input type="checkbox" aria-label="انتخاب همه" onclick="toggleAll('action',this)"></th><th>ردیف</th><th>موضوع</th><th>پروژه</th><th>مسئول</th><th>همکاران</th><th>مهلت</th><th>اسپرینت</th><th>پیشرفت</th><th>وضعیت</th><th></th></tr></thead>
      <tbody id="actionRows">${actionRows(filteredActions())}</tbody></table></div>`}</div>`;
}

function filteredActions() {
  const q = actionFilter.q.toLowerCase();
  return (state.actions || []).filter(a => {
    const status = CalEvents.effectiveStatus(a);
    return (!actionFilter.status || status === actionFilter.status) && (!actionFilter.sprint || a.sprint === actionFilter.sprint)
      && [a.subject, a.decision, a.owner, a.assignee, a.project, a.meeting, status, a.due, fd(a.due), (a.collaborators || []).join(' ')].join(' ').toLowerCase().includes(q);
  });
}

function actionRows(xs) {
  return xs.map((a, i) => `<tr><td><input class="action-check" type="checkbox" value="${a.id}" aria-label="انتخاب اقدام"></td><td>${fa(i + 1)}</td>
    <td><b>${esc(a.subject)}</b><br><span class="muted">${esc(a.decision)}</span></td><td>${esc(a.project)}</td><td>${esc(a.owner || a.assignee)}</td>
    <td><div class="chips">${(a.collaborators || []).map(x => `<span class="chip">${esc(x)}</span>`).join('')}</div></td><td>${fd(a.due)}</td><td>${esc(a.sprint || '')}</td>
    <td>${progressBar(a.progress)}<small>${fa(a.progress)}٪</small></td><td>${badge(CalEvents.effectiveStatus(a))}</td>
    <td>${has('actions.manage') ? `<div class="rowactions"><button class="btn secondary small" onclick="openAction(${a.id})">ویرایش</button><button class="btn secondary small" onclick="archiveAction(${a.id},${a.archived ? 'false' : 'true'})">${a.archived ? 'بازگردانی' : 'بایگانی'}</button><button class="btn danger small" onclick="deleteAction(${a.id})">حذف</button></div>` : ''}</td></tr>`).join('')
    || '<tr><td colspan="11" class="empty">اقدامی پیدا نشد.</td></tr>';
}

/** Two-state switch "active | archived" used by actions and projects (same look as the other view buttons). */
const archiveSwitch = (archived, fn) => `<div class="seg" role="group" aria-label="نمایش">
  <button type="button" class="${archived ? '' : 'on'}" aria-pressed="${!archived}" onclick="${fn}(false)">فعال</button>
  <button type="button" class="${archived ? 'on' : ''}" aria-pressed="${archived}" onclick="${fn}(true)">بایگانی‌شده</button></div>`;
async function toggleArchivedActions(on) { actionFilter.archived = on; await renderActions(); }
async function archiveAction(id, archive) {
  try { await api(`/api/actions/${id}/${archive ? 'archive' : 'unarchive'}`, { method: 'POST' }); toast(archive ? 'اقدام بایگانی شد.' : 'اقدام بازگردانی شد.'); await refresh(); await renderActions(); } catch (e) { toast(e.message); }
}
async function deleteAction(id) {
  if (!confirm('این اقدام برای همیشه حذف شود؟ (برای نگه‌داشتن آن، «بایگانی» را بزنید.)')) return;
  try { await api(`/api/actions/${id}`, { method: 'DELETE' }); toast('اقدام حذف شد.'); await refresh(); await renderActions(); } catch (e) { toast(e.message); }
}

function filterActions() {
  actionFilter.q = ($('actionSearch')?.value || '').trim();
  actionFilter.status = $('actionStatus')?.value || '';
  actionFilter.sprint = $('actionSprint')?.value || '';
  if (actionBoard) $('actionBoard').innerHTML = kanbanHtml(filteredActions()); else $('actionRows').innerHTML = actionRows(filteredActions());
}

/** Open the actions list pre-filtered (from the donut, a project card or a person card). */
async function goActions({ q = '', status = '', sprint = '' } = {}) {
  actionFilter.q = q;
  actionFilter.status = status;
  actionFilter.sprint = sprint;
  await section('actions');
}
const projectActions = id => goActions({ q: state.projects.find(p => p.id === id)?.name || '' });
const personActions = id => goActions({ q: state.people.find(p => p.id === id)?.name || '' });

function clearSearch(id) { $(id).value = ''; }
function toggleAll(type, el) { document.querySelectorAll('.' + type + '-check').forEach(x => { x.checked = el.checked; }); }
const selected = type => [...document.querySelectorAll('.' + type + '-check:checked')].map(x => Number(x.value));

async function exportSelected(type, format) {
  const list = selected(type);
  if (!list.length) { toast('حداقل یک مورد را انتخاب کنید.'); return; }
  const base = type === 'meeting' ? 'meetings' : 'actions';
  if (!has(type === 'meeting' ? 'meetings.export' : 'actions.export')) { toast('مجوز این خروجی را ندارید.'); return; }
  await download(`/api/export/${base}.${format}?ids=${list.join(',')}`, `${base}-selected.${format}`);
}
function taskSheetButtons() {
  const b = (label, js) => `<button type="button" class="menu-item" onclick="closeMenus();${js}">${label}</button>`;
  return b('Excel تسک‌ها (همه)', "exportTaskSheet(false)") + b('Excel تسک‌ها (منتخب)', "exportTaskSheet(true)");
}
async function exportTaskSheet(onlySelected) {
  if (!has('actions.export')) { toast('مجوز این خروجی را ندارید.'); return; }
  const list = onlySelected ? selected('action') : [];
  if (onlySelected && !list.length) { toast('حداقل یک مورد را انتخاب کنید.'); return; }
  await download('/api/export/tasks.xlsx' + (list.length ? `?ids=${list.join(',')}` : ''), 'Full_Tasks.xlsx');
}
async function importTaskSheet(input) {
  const f = input.files[0]; input.value = '';
  if (!f) return;
  try {
    const r = await fetch('/api/import/tasks', { method: 'POST', headers: { 'X-Session-Token': token }, body: await f.arrayBuffer() });
    const j = await r.json().catch(() => ({}));
    if (!r.ok) throw Error(typeof j.detail === 'string' ? j.detail : `خطا (${r.status})`);
    toast(`${fa(j.created)} اقدام جدید، ${fa(j.updated)} به‌روزرسانی${j.problems.length ? `، ${fa(j.problems.length)} هشدار` : ''}`);
    const box = $('importResult');
    if (box) {
      box.innerHTML = `<div class="import-ok"><b>${fa(j.created)}</b> اقدام جدید · <b>${fa(j.updated)}</b> به‌روزرسانی${j.skipped ? ` · <b>${fa(j.skipped)}</b> ردشده` : ''}</div>`
        + (j.problems.length ? `<ul class="import-warn">${j.problems.map(x => `<li>${esc(x)}</li>`).join('')}</ul>` : '');
      await refresh();
    } else { if (j.problems.length) alert(j.problems.join('\n')); await refresh(); goActions(); }
  } catch (e) { toast(e.message || 'ورود فایل ناموفق بود.'); }
}
const exportAll = (base, format) => download(`/api/export/${base}.${format}`, `${base}.${format}`);
async function printSelected(type) {
  const list = selected(type);
  if (!list.length) { toast('حداقل یک مورد را انتخاب کنید.'); return; }
  const base = type === 'meeting' ? 'meetings' : 'actions';
  if (!has(type === 'meeting' ? 'meetings.export' : 'actions.export')) { toast('مجوز این خروجی را ندارید.'); return; }
  await printPdf(`/api/export/${base}.pdf?ids=${list.join(',')}`);
}

/* ───────── Kanban board ───────── */
const BOARD_COLS = ['جدید', 'در حال انجام', 'انجام شده'];
const boardColOf = a => { const s = CalEvents.effectiveStatus(a); return s === 'انجام شده' ? 'انجام شده' : (a.progress > 0 || s === 'در حال انجام' || s === 'عقب‌افتاده') ? 'در حال انجام' : 'جدید'; };
function kanbanHtml(list) {
  const can = has('actions.manage');
  return `<div class="kanban">${BOARD_COLS.map(col => {
    const items = list.filter(a => boardColOf(a) === col);
    return `<div class="kcol" ${can ? `ondragover="event.preventDefault();this.classList.add('over')" ondragleave="this.classList.remove('over')" ondrop="dropAction(event,'${col}')"` : ''}>
      <div class="khead k-${BOARD_COLS.indexOf(col)}">${col} <span>${fa(items.length)}</span></div>
      <div class="kbody">${items.map(a => `<div class="kcard" ${can ? 'draggable="true"' : ''} ondragstart="event.dataTransfer.setData('text/plain','${a.id}')" onclick="openAction(${a.id})" style="${a.project_color ? `border-right:4px solid ${a.project_color}` : ''}">
        <b>${esc(a.subject)}</b><div class="mini muted">${esc(a.owner || '')}${a.assignee ? ' ← ' + esc(a.assignee) : ''}</div>
        <div class="mini">${a.due ? 'مهلت: ' + fd(a.due) : ''} ${CalEvents.effectiveStatus(a) === 'عقب‌افتاده' ? '<span class="badge red">عقب‌افتاده</span>' : ''} ${a.sprint ? '<span class="muted">· ' + esc(a.sprint) + '</span>' : ''}</div>${progressBar(a.progress)}</div>`).join('') || '<div class="empty mini">خالی</div>'}</div></div>`;
  }).join('')}</div>`;
}
async function dropAction(ev, col) {
  ev.preventDefault(); ev.currentTarget.classList.remove('over');
  const a = (state.actions || []).find(x => x.id === Number(ev.dataTransfer.getData('text/plain')));
  if (!a || boardColOf(a) === col) return;
  const target = col === 'انجام شده' ? 100 : col === 'جدید' ? 0 : (a.progress > 0 && a.progress < 100 ? a.progress : 10);
  try {
    await api(`/api/actions/${a.id}/progress`, { method: 'POST', body: JSON.stringify({ new_progress: target, update_date: Jalali.format(Jalali.today()), note: `جابه‌جایی در بُرد به «${col}»` }) });
    await refresh();
    if (currentSection === 'projects' && projectPageId) await openProjectPage(projectPageId); else await renderActions();
  } catch (e) { toast(e.message); }
}
let actionBoard = false;
async function toggleActionBoard() { actionBoard = !actionBoard; await renderActions(); }

/* ───────── Project page ───────── */
let projectPageId = null, projectTab = 'actions';
async function openProjectPage(id, tab) {
  projectPageId = id; if (tab) projectTab = tab;
  try { state.actions = await api('/api/actions?archived=0'); } catch (e) { toast(e.message); return; }
  const p = state.projects.find(x => x.id === id); if (!p) { projectPageId = null; renderProjects(); return; }
  const acts = state.actions.filter(a => a.project_id === id), ms = (state.meetings || []).filter(m => m.project_id === id);
  const today = Jalali.format(Jalali.today());
  const done = acts.filter(a => CalEvents.effectiveStatus(a) === 'انجام شده'), late = acts.filter(a => CalEvents.effectiveStatus(a) === 'عقب‌افتاده');
  const open = acts.filter(a => !done.includes(a) && !late.includes(a));
  const timed = open.filter(a => a.due), untimed = open.filter(a => !a.due);
  const seg = [['انجام شده', done.length, 'var(--ok)'], ['دارای تأخیر', late.length, 'var(--danger)'], ['دارای زمان', timed.length, 'var(--warn)'], ['بدون زمان', untimed.length, 'var(--slate)']];
  const total = acts.length || 1;
  const rows = list => list.map(a => `<tr style="cursor:pointer" onclick="openAction(${a.id})"><td><b>${esc(a.subject)}</b></td><td>${esc(a.assignee || a.owner || '')}</td><td>${fd(a.due)}</td><td>${esc(a.sprint || '')}</td><td>${progressBar(a.progress)}</td><td>${badge(CalEvents.effectiveStatus(a))}</td></tr>`).join('') || '<tr><td colspan="6" class="empty">اقدامی ندارد.</td></tr>';
  const pane = {
    actions: `<div class="tablewrap"><table class="table"><thead><tr><th>اقدام</th><th>مسئول</th><th>مهلت</th><th>اسپرینت</th><th>پیشرفت</th><th>وضعیت</th></tr></thead><tbody>${rows(acts)}</tbody></table></div>`,
    board: kanbanHtml(acts),
    meetings: ms.map(m => `<div class="event"><div><b>${esc(m.title)}</b><br><span class="muted">${fd(m.date)}${m.place ? ' · ' + esc(m.place) : ''}</span></div><button class="btn secondary" onclick="showMeeting(${m.id})">جزئیات</button></div>`).join('') || '<div class="empty">جلسه‌ای برای این پروژه ثبت نشده است.</div>',
  }[projectTab];
  $('projects').innerHTML = `<div class="section" style="${p.color ? `border-top:5px solid ${p.color}` : ''}">
    <div class="sectionhead"><div><h3 style="margin:0">${esc(p.name)} ${badge(p.status)}</h3><span class="muted">${esc(p.description || '')}</span><br>
      <span class="muted mini">مدیر: ${esc(p.manager || '—')} · ${p.start_date || p.end_date ? `${fd(p.start_date) || '—'} تا ${fd(p.end_date) || '—'}` : 'بازه‌ی زمانی تعیین نشده'}</span></div>
      <button class="btn secondary" onclick="projectPageId=null;renderProjects()">بازگشت به پروژه‌ها</button></div>
    <h4>وضعیت کلی پروژه <span class="muted">(${fa(acts.length - done.length)} کار باقی‌مانده)</span></h4>
    <div class="statusbar">${seg.map(([l, n, c]) => n ? `<span style="width:${n / total * 100}%;background:${c}" title="${l}: ${n}"></span>` : '').join('') || '<span style="width:100%"></span>'}</div>
    <div class="statuslegend">${seg.map(([l, n, c]) => `<span><i style="background:${c}"></i>${l} · ${fa(n)} مورد</span>`).join('')}</div>
    <div class="tabs">${[['actions', 'کارها'], ['board', 'بُرد'], ['meetings', 'جلسات']].map(([k, l]) => `<button class="tab ${projectTab === k ? 'on' : ''}" onclick="openProjectPage(${id},'${k}')">${l}</button>`).join('')}
      ${has('actions.manage') ? `<button class="btn" style="margin-inline-start:auto" onclick="openAction(null,{project_id:${id}})">+ اقدام جدید</button>` : ''}</div>
    ${pane}</div>`;
}

/* ───────── Personal notes ───────── */
const NOTE_CATS = ['امروز', 'برنامه‌ریزی', 'شخصی'];
let notesCache = [];
async function renderNotes() {
  try { notesCache = await api('/api/notes'); } catch (e) { toast(e.message); return; }
  const cats = [...new Set([...NOTE_CATS, ...notesCache.map(n => n.category)])];
  $('notes').innerHTML = `<div class="section">
    <div class="sectionhead"><div><h3 style="margin:0">یادداشت‌های من</h3><span class="muted">فقط خودتان می‌بینید؛ با دیگران یا گوشی همگام نمی‌شود.</span></div></div>
    <div class="searchbar"><input class="search" id="noteText" placeholder="یادداشت خود را قرار دهید…" onkeydown="if(event.key==='Enter')addNote()">
      <select id="noteCat" class="status-filter" aria-label="دسته">${cats.map(c => `<option>${esc(c)}</option>`).join('')}</select><button class="btn" onclick="addNote()">افزودن</button><button class="btn secondary" onclick="newNoteCategory()">+ دسته‌ی جدید</button></div>
    <div class="notegrid">${cats.map(c => { const items = notesCache.filter(n => n.category === c); return `<div class="notecard"><h4>${esc(c)}</h4>${items.map(n => `<div class="noteitem ${n.done ? 'done' : ''}"><input type="checkbox" ${n.done ? 'checked' : ''} onchange="toggleNote(${n.id},this.checked)"><span>${esc(n.text)}</span><button class="iconbtn" title="حذف" aria-label="حذف" onclick="deleteNote(${n.id})">×</button></div>`).join('') || '<div class="muted mini">خالی</div>'}</div>`; }).join('')}</div></div>`;
}
async function addNote() {
  const text = $('noteText').value.trim(); if (!text) return;
  try { await api('/api/notes', { method: 'POST', body: JSON.stringify({ text, category: $('noteCat').value }) }); await renderNotes(); } catch (e) { toast(e.message); }
}
async function newNoteCategory() {
  const name = await ask({ title: 'دسته‌ی جدید', label: 'نام دسته' }); if (!name) return;
  const text = await ask({ title: 'اولین یادداشت', label: 'یادداشت اول این دسته' }); if (!text) return;
  try { await api('/api/notes', { method: 'POST', body: JSON.stringify({ text, category: name }) }); await renderNotes(); } catch (e) { toast(e.message); }
}
async function toggleNote(id, done) {
  const n = notesCache.find(x => x.id === id);
  try { await api(`/api/notes/${id}`, { method: 'PUT', body: JSON.stringify({ text: n.text, category: n.category, done }) }); await renderNotes(); } catch (e) { toast(e.message); }
}
async function deleteNote(id) { try { await api(`/api/notes/${id}`, { method: 'DELETE' }); await renderNotes(); } catch (e) { toast(e.message); } }

/* ───────── Local assistant ───────── */
let assistantHistory = [];
async function openAssistantConfig() {
  let c; try { c = await api('/api/assistant/config'); } catch (e) { toast(e.message); return; }
  modal(mh('اتصال به مدل محلی', `<p class="muted">یک بار ذخیره کنید؛ بعد از بستن و باز کردن دوباره‌ی برنامه هم می‌ماند.</p>
    <div class="grid">
      <div class="field full"><label for="llmUrl">آدرس سرور (بدون /v1)</label><input id="llmUrl" dir="ltr" placeholder="http://192.168.0.116:1234" value="${esc(c.url)}"></div>
      <div class="field"><label for="llmModel">نام مدل</label><input id="llmModel" dir="ltr" placeholder="qwen3-next-80b-a3b-instruct" value="${esc(c.model)}"></div>
      <div class="field"><label for="llmTimeout">مهلت پاسخ (ثانیه)</label><input id="llmTimeout" dir="ltr" placeholder="400" value="${esc(c.timeout)}"></div></div>
    <div class="actionsbar"><button class="btn" onclick="saveAssistantConfig()">ذخیره</button><button class="btn secondary" onclick="closeModal()">انصراف</button></div>`));
}
async function saveAssistantConfig() {
  try {
    await api('/api/assistant/config', { method: 'PUT', body: JSON.stringify({ url: $('llmUrl').value, model: $('llmModel').value, timeout: $('llmTimeout').value }) });
    closeModal(); toast('ذخیره شد.'); renderAssistant();
  } catch (e) { toast(e.message); }
}
async function renderAssistant() {
  let st = { ok: false };
  try { st = await api('/api/assistant/status'); } catch (e) { /* shown below */ }
  $('assistant').innerHTML = `<div class="section">
    <div class="sectionhead"><div><h3>دستیار هوشمند (مدل محلی)</h3><span class="muted">فقط از داده‌هایی که خودتان اجازه‌ی دیدنشان را دارید جواب می‌دهد و چیزی را تغییر نمی‌دهد.</span></div>
      <span class="muted" id="chatStatus">${assistantStatusText(st)}</span></div>
    <div id="chatLog" class="chatlog">${assistantHistory.map(chatBubble).join('') || '<div class="empty">مثلاً: «اقدام‌های عقب‌افتاده‌ی اسپرینت ۲ کدامند؟» یا «خلاصه‌ی جلسات این ماه»</div>'}</div>
    <div class="searchbar"><input class="search" id="chatInput" placeholder="سؤال خود را بنویسید" onkeydown="if(event.key==='Enter')sendAssistant()"><button class="btn" id="chatSend" onclick="sendAssistant()">ارسال</button>
      <button class="btn secondary" onclick="assistantHistory=[];renderAssistant()">پاک کردن گفتگو</button>
      ${state.user.role === 'admin' ? '<button class="btn secondary" onclick="openAssistantConfig()">تنظیم اتصال مدل</button>' : ''}</div></div>`;
  scrollChat();
  watchAssistant(st);
}
const assistantStatusText = st => st.ok ? '● مدل متصل است: ' + esc(st.model) : '○ مدل در دسترس نیست — ' + esc(st.error || '') + ' (هر چند ثانیه خودکار دوباره تلاش می‌شود)';
/** While the assistant page is open, keeps checking the model server and connects by itself as soon as it is reachable. */
let assistantWatch = null, assistantWasOk = null;
function watchAssistant(first) {
  assistantWasOk = first.ok;
  clearInterval(assistantWatch);
  assistantWatch = setInterval(async () => {
    if ($('assistant').classList.contains('hide') || !token) { clearInterval(assistantWatch); assistantWatch = null; return; }
    let st = { ok: false };
    try { st = await api('/api/assistant/status'); } catch (e) { /* offline */ }
    const el = $('chatStatus'); if (el) el.innerHTML = assistantStatusText(st);
    if (st.ok && assistantWasOk === false) toast('مدل محلی متصل شد.');
    assistantWasOk = st.ok;
  }, 8000);
}
const chatBubble = m => `<div class="bubble ${m.role === 'user' ? 'me' : 'bot'}"><b>${m.role === 'user' ? 'شما' : 'دستیار'}</b><div>${esc(m.content)}</div></div>`;
const scrollChat = () => { const l = $('chatLog'); if (l) l.scrollTop = l.scrollHeight; };
async function sendAssistant() {
  const input = $('chatInput'), q = input.value.trim();
  if (!q) return;
  const history = assistantHistory.slice();
  assistantHistory.push({ role: 'user', content: q });
  input.value = ''; $('chatSend').disabled = true;
  const log = $('chatLog'); log.querySelector('.empty')?.remove();
  log.insertAdjacentHTML('beforeend', chatBubble({ role: 'user', content: q }) + '<div id="chatWait" class="muted chatwait">در حال فکر کردن… (مدل‌های بزرگ ممکن است چند دقیقه طول بکشند)</div>');
  scrollChat();
  let reply;
  try {
    const r = await api('/api/assistant', { method: 'POST', body: JSON.stringify({ question: q, history }) });
    reply = { role: 'assistant', content: r.answer || '(پاسخی دریافت نشد)' };
  } catch (e) { reply = { role: 'assistant', content: '⚠ ' + (e.message || 'خطا') }; }
  assistantHistory.push(reply);
  $('chatWait')?.remove(); log.insertAdjacentHTML('beforeend', chatBubble(reply));
  $('chatSend').disabled = false; scrollChat(); input.focus();
}

/* ───────── Sprints ───────── */
const sprintNames = () => (state.sprints || []).map(s => s.name);
const sprintDatalist = () => `<datalist id="sprintList">${sprintNames().map(n => `<option value="${esc(n)}">`).join('')}</datalist>`;
const refreshSprintDatalist = () => { const d = $('sprintList'); if (d) d.innerHTML = sprintNames().map(n => `<option value="${esc(n)}">`).join(''); };

/** Quick add from a form (meeting / action): only a name; dates can be set later in the sprint manager. */
async function addSprintInline() {
  const name = await ask({ title: 'اسپرینت جدید', label: 'نام اسپرینت (مثلاً اسپرینت ۱)' });
  if (!name) return;
  try {
    await api('/api/sprints', { method: 'POST', body: JSON.stringify({ name, start_date: '', end_date: '', project_id: null }) });
    state.sprints = await api('/api/sprints');
    refreshSprintDatalist();
    toast('اسپرینت اضافه شد؛ در فهرست اسپرینتِ اقدام‌ها قابل انتخاب است.');
  } catch (e) { toast(e.message); }
}

let sprintEditing = null;
async function openSprints() {
  try { state.sprints = await api('/api/sprints'); } catch (e) { toast(e.message); return; }
  const can = has('actions.manage');
  const rows = state.sprints.map((s, i) => `<tr><td>${fa(i + 1)}</td><td><b>${esc(s.name)}</b></td><td>${s.start_date || s.end_date ? `${fd(s.start_date) || '—'} تا ${fd(s.end_date) || '—'}` : '—'}</td>
    <td>${esc((state.projects.find(p => p.id === s.project_id) || {}).name || '—')}</td><td>${fa(s.actions)}</td>
    <td>${can ? `<button class="btn secondary small" onclick="editSprint(${i})">ویرایش</button> ${s.id ? `<button class="btn danger small" onclick="deleteSprint(${s.id})">حذف</button>` : ''}` : ''}
      <button class="btn secondary small" onclick="closeModal();goActions({sprint:state.sprints[${i}].name})">اقدام‌ها</button></td></tr>`).join('');
  modal(mh('اسپرینت‌ها', `
    <div class="tablewrap"><table class="table"><thead><tr><th>ردیف</th><th>نام</th><th>بازه</th><th>پروژه</th><th>اقدام</th><th></th></tr></thead><tbody>${rows || '<tr><td colspan="6" class="empty">هنوز اسپرینتی ثبت نشده است.</td></tr>'}</tbody></table></div>
    ${can ? `<div class="section"><h3 id="sprintFormTitle">اسپرینت جدید</h3><div class="grid">
      <div class="field"><label for="spn">نام</label><input id="spn"></div>
      <div class="field"><label for="spp">پروژه</label><select id="spp">${projectOpts(null)}</select></div>
      <div class="field"><label for="sps">شروع</label>${datePicker('sps', '')}</div>
      <div class="field"><label for="spe">پایان</label>${datePicker('spe', '', { minFrom: 'sps' })}</div></div>
      <div class="actionsbar"><button class="btn" onclick="saveSprint()">ذخیره اسپرینت</button></div></div>` : ''}
    <div class="actionsbar"><button class="btn secondary" onclick="closeModal()">بستن</button></div>`));
  sprintEditing = null;
}
function editSprint(i) {
  const s = state.sprints[i];
  sprintEditing = s;
  $('spn').value = s.name; $('spp').value = s.project_id || '';
  DatePicker.setValue('sps', s.start_date || ''); DatePicker.setValue('spe', s.end_date || '');
  $('sprintFormTitle').textContent = 'ویرایش اسپرینت';
  $('spn').focus();
}
async function saveSprint() {
  try {
    const start = readDate('sps', 'شروع اسپرینت'), end = readDate('spe', 'پایان اسپرینت');
    checkRange(start, end, 'پایان اسپرینت نمی‌تواند قبل از شروع آن باشد.');
    const body = JSON.stringify({ name: $('spn').value, start_date: start, end_date: end, project_id: Number($('spp').value) || null });
    if (sprintEditing && sprintEditing.id) await api(`/api/sprints/${sprintEditing.id}`, { method: 'PUT', body });
    else if (sprintEditing && !sprintEditing.id && $('spn').value.trim() !== sprintEditing.name) await api('/api/sprints', { method: 'POST', body });
    else await api('/api/sprints', { method: 'POST', body });
    await refresh(); toast('اسپرینت ذخیره شد.');
    if (currentSection === 'actions') await renderActions();
    await openSprints();
  } catch (e) { toast(e.message); }
}
async function deleteSprint(id) {
  if (!confirm('این اسپرینت حذف شود؟ (اقدام‌ها حذف نمی‌شوند؛ فقط اگر اقدامی در آن نباشد حذف می‌شود.)')) return;
  try { await api(`/api/sprints/${id}`, { method: 'DELETE' }); await refresh(); toast('اسپرینت حذف شد.'); await openSprints(); } catch (e) { toast(e.message); }
}

/** Export with a calendar range and (for actions) sprints. */
function openExportFilter(type) {
  const isAction = type === 'action', base = isAction ? 'actions' : 'meetings';
  if (!has(isAction ? 'actions.export' : 'meetings.export')) { toast('مجوز این خروجی را ندارید.'); return; }
  const fmts = isAction ? [['xlsx', 'Excel'], ['docx', 'Word'], ['pdf', 'PDF'], ['tasks', 'Excel تسک‌ها (Azure)']] : [['xlsx', 'Excel'], ['docx', 'Word'], ['pdf', 'PDF']];
  modal(mh(isAction ? 'خروجی اقدامات با فیلتر' : 'خروجی جلسات با بازه', `
    <p class="muted">${isAction ? 'بازه‌ی تقویمی و/یا اسپرینت را انتخاب کنید؛ فیلتر خالی یعنی همه.' : 'فقط جلساتی که تاریخشان در این بازه است.'}</p>
    <div class="grid">
      ${isAction ? `<div class="field full"><label for="efBy">بازه بر اساس</label><select id="efBy"><option value="due">مهلت</option><option value="start">تاریخ شروع</option><option value="created">تاریخ ثبت</option><option value="done">تاریخ انجام</option></select></div>` : ''}
      <div class="field"><label for="efFrom">از تاریخ</label>${datePicker('efFrom', '')}</div>
      <div class="field"><label for="efTo">تا تاریخ</label>${datePicker('efTo', '', { minFrom: 'efFrom' })}</div>
      ${isAction ? `<div class="field full"><label>اسپرینت‌ها</label><div>${sprintNames().map(n => `<label class="check"><input class="ef-sprint" type="checkbox" value="${esc(n)}"> <span>${esc(n)}</span></label>`).join('') || '<span class="muted">اسپرینتی ثبت نشده است.</span>'}</div></div>` : ''}
    </div>
    <div class="actionsbar">${fmts.map(([f, l]) => `<button class="btn" onclick="runFilteredExport('${type}','${f}')">${l}</button>`).join('')}<button class="btn secondary" onclick="closeModal()">انصراف</button></div>`));
}
async function runFilteredExport(type, format) {
  try {
    const from = readDate('efFrom', 'از تاریخ'), to = readDate('efTo', 'تا تاریخ');
    checkRange(from, to, 'پایان بازه نمی‌تواند قبل از شروع آن باشد.');
    const qs = new URLSearchParams();
    if (from) qs.set('date_from', from);
    if (to) qs.set('date_to', to);
    if (type === 'action') {
      qs.set('date_by', $('efBy').value);
      const sp = [...document.querySelectorAll('.ef-sprint:checked')].map(x => x.value);
      if (sp.length) qs.set('sprints', sp.join(','));
    }
    if (![...qs.keys()].some(k => k !== 'date_by')) throw Error('حداقل یک بازه‌ی تاریخ یا یک اسپرینت را انتخاب کنید.');
    const base = type === 'action' ? 'actions' : 'meetings';
    const file = format === 'tasks' ? 'tasks.xlsx' : `${base}.${format}`;
    await download(`/api/export/${file}?${qs}`, format === 'tasks' ? 'Full_Tasks.xlsx' : file);
  } catch (e) { toast(e.message); }
}

/* Action form */
let currentActionId = null;
let actionDraft = null;

function collectActionDraft() {
  if (!$('as')) return null;
  return {
    subject: $('as').value, decision: $('ad').value, owner_id: Number($('ao').value) || null, assignee_id: Number($('aa').value) || null,
    collaborator_ids: getCollaboratorIds(), project_id: Number($('ap').value) || null, start_date: $('astart').value, due: $('au').value,
    priority: $('ai').value, progress: Number($('ag').value || 0), status: $('ast').value, sprint: ($('asp')?.value || '').trim(),
  };
}
const getCollaboratorIds = () => [...$('ac').selectedOptions].map(x => Number(x.value));

function openAction(id, prefill = {}) {
  currentActionId = id || null;
  const base = id ? (state.actions || []).find(x => x.id === id) : null;
  if (id && !base) { toast('اقدام پیدا نشد.'); return; }
  const a = { ...(base || {}), ...prefill };
  const opts = (list, current) => list.map(v => `<option ${v === current ? 'selected' : ''}>${v}</option>`).join('');
  const extra = id ? `
    <div class="section"><div class="sectionhead"><h3>چک‌لیست</h3><button class="btn secondary" onclick="addChecklistPrompt(${id})">+ مورد</button></div>
      ${(base.checklist || []).map(c => `<div class="check"><input type="checkbox" ${c.done ? 'checked' : ''} aria-label="انجام شد" onchange="toggleChecklist(${c.id},this.checked)"><span>${esc(c.title)}</span><button class="btn danger small" style="margin-right:auto" aria-label="حذف" onclick="deleteChecklist(${c.id})">×</button></div>`).join('') || '<div class="muted">هنوز موردی اضافه نشده است.</div>'}</div>
    <div class="section"><div class="sectionhead"><h3>زیرکارها</h3><button class="btn secondary" onclick="addSubtaskPrompt(${id})">+ زیرکار</button></div>
      ${(base.subtasks || []).map(x => `<div class="subtask"><b>${esc(x.subject)}</b> · ${fa(x.progress)}٪ · ${esc(x.assignee)}</div>`).join('') || '<div class="muted">زیرکاری ثبت نشده است.</div>'}</div>
    <div class="section"><div class="sectionhead"><h3>یادداشت‌ها</h3><button class="btn secondary" onclick="addNotePrompt(${id})">+ یادداشت</button></div>
      ${(base.notes || []).map(n => `<div class="history"><button class="iconbtn" style="float:left" title="حذف یادداشت" aria-label="حذف یادداشت" onclick="deleteActionNote(${id},${n.id})">×</button><b>${esc(n.user)}</b> · ${esc(new Date(n.created_at + 'Z').toLocaleString(I18N.locale))}<br>${esc(n.body)}</div>`).join('') || '<div class="muted">یادداشتی ثبت نشده است.</div>'}</div>
    <div class="section"><div class="sectionhead"><h3>تاریخچه پیشرفت</h3><button class="btn secondary" onclick="openProgress(${id})">ثبت پیشرفت</button></div>
      ${(base.history || []).slice().reverse().map(h => `<div class="history"><b>${fa(h.from)}٪ ← ${fa(h.to)}٪</b> · ${fd(h.date)}<br>${esc(h.note)}</div>`).join('') || '<div class="muted">سابقه‌ای وجود ندارد.</div>'}</div>` : '';
  modal(mh(id ? 'ویرایش اقدام' : 'اقدام جدید', `
    <div class="grid">
      <div class="field full"><label for="as">موضوع اقدام</label><input id="as" value="${esc(a.subject || '')}"></div>
      <div class="field full"><label for="ad">تصمیم / شرح اقدام</label><textarea id="ad">${esc(a.decision || '')}</textarea></div>
      <div class="field"><label for="ao">مسئول اصلی</label><select id="ao"><option value="">—</option>${personOpts(a.owner_id)}</select></div>
      <div class="field"><label for="aa">انجام‌دهنده</label><select id="aa"><option value="">—</option>${personOpts(a.assignee_id)}</select></div>
      <div class="field"><label for="ac">همکاران</label><select id="ac" multiple size="4">${personOpts(null, true, a.collaborator_ids || [])}</select><span class="hint">برای انتخاب چند نفر، کلید Ctrl را نگه دارید.</span></div>
      <div class="field"><label for="ap">پروژه</label><select id="ap">${projectOpts(a.project_id)}</select></div>
      <div class="field"><label for="astart">تاریخ شروع</label>${datePicker('astart', a.start_date || '')}</div>
      <div class="field"><label for="au">مهلت</label>${datePicker('au', a.due || '', { minFrom: 'astart' })}</div>
      <div class="field"><label for="ai">اولویت</label><select id="ai">${opts(['کم', 'متوسط', 'زیاد', 'بحرانی'], a.priority || 'متوسط')}</select></div>
      <div class="field"><label for="ag">پیشرفت (٪)</label><input id="ag" type="number" min="0" max="100" value="${a.progress || 0}"></div>
      <div class="field"><label for="ast">وضعیت</label><select id="ast">${opts(STATUSES, a.status || 'جدید')}</select></div>
      <div class="field"><label for="asp">اسپرینت</label><input id="asp" list="sprintList" value="${esc(a.sprint || '')}" placeholder="مثلاً اسپرینت ۱">${sprintDatalist()}</div>
    </div>${extra}
    <div class="actionsbar"><button class="btn" onclick="saveAction(${id || 0})">ذخیره اقدام</button><button class="btn secondary" onclick="closeModal()">انصراف</button></div>`));
}

/** Re-open the action form after an inline change (checklist, note, …) keeping what was typed. */
async function reopenAction(id) {
  const draft = collectActionDraft() || actionDraft;
  actionDraft = null;
  await refresh();
  openAction(id, draft || {});
}

async function saveAction(id) {
  try {
    const start = readDate('astart', 'تاریخ شروع'), due = readDate('au', 'مهلت');
    checkRange(start, due, 'مهلت اقدام نمی‌تواند قبل از تاریخ شروع باشد.');
    const body = { ...collectActionDraft(), id: id || null, start_date: start, due };
    await api(id ? `/api/actions/${id}` : '/api/actions', { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) });
    await refresh();
    closeModal();
    toast('اقدام ذخیره شد.');
    await afterSave('actions');
  } catch (e) { toast(e.message); }
}

async function inlineChange(id, request, success) {
  try { await request(); if (success) toast(success); await reopenAction(id); } catch (e) { toast(e.message); }
}
async function addChecklistPrompt(id) {
  const title = await ask({ title: 'مورد جدید چک‌لیست', label: 'عنوان مورد' });
  if (title) inlineChange(id, () => api(`/api/actions/${id}/checklist`, { method: 'POST', body: JSON.stringify({ title, is_done: false }) }));
}
async function toggleChecklist(cid, done) {
  const item = (state.actions || []).flatMap(a => a.checklist || []).find(c => c.id === cid);
  try { await api(`/api/checklist/${cid}`, { method: 'PUT', body: JSON.stringify({ title: item?.title || '—', is_done: done }) }); if (item) item.done = done; }
  catch (e) { toast(e.message); }
}
function deleteChecklist(cid) { inlineChange(currentActionId, () => api(`/api/checklist/${cid}`, { method: 'DELETE' })); }
async function addSubtaskPrompt(id) {
  const subject = await ask({ title: 'زیرکار جدید', label: 'موضوع زیرکار' });
  if (subject) inlineChange(id, () => api(`/api/actions/${id}/subtasks`, { method: 'POST', body: JSON.stringify({ subject }) }));
}
async function deleteActionNote(aid, nid) {
  if (!confirm('این یادداشت حذف شود؟')) return;
  inlineChange(aid, () => api(`/api/actions/${aid}/notes/${nid}`, { method: 'DELETE' }));
}
async function addNotePrompt(id) {
  const body = await ask({ title: 'یادداشت جدید', label: 'متن یادداشت', multiline: true });
  if (body) inlineChange(id, () => api(`/api/actions/${id}/notes`, { method: 'POST', body: JSON.stringify({ body }) }));
}

function openProgress(id) {
  actionDraft = collectActionDraft();
  const a = state.actions.find(x => x.id === id);
  modal(mh('ثبت پیشرفت اقدام', `
    <p class="muted">${esc(a.subject)} — پیشرفت فعلی: ${fa(a.progress)}٪</p>
    <div class="field"><label for="pgRange">پیشرفت جدید</label><div class="range-row"><input id="pgRange" type="range" min="0" max="100" value="${a.progress}" oninput="$('pgNum').value=this.value"><input id="pgNum" type="number" min="0" max="100" value="${a.progress}" oninput="$('pgRange').value=this.value"></div></div>
    <div class="field"><label for="pgDate">تاریخ ثبت</label>${datePicker('pgDate', Jalali.format(Jalali.today()))}</div>
    <div class="field"><label for="pgNote">توضیح</label><textarea id="pgNote"></textarea></div>
    <div class="actionsbar"><button class="btn" onclick="saveProgress(${id})">ثبت</button><button class="btn secondary" onclick="reopenAction(${id})">بازگشت</button></div>`));
}
async function saveProgress(id) {
  try {
    const body = { new_progress: Number($('pgNum').value || 0), update_date: readDate('pgDate', 'تاریخ ثبت'), note: $('pgNote').value };
    await api(`/api/actions/${id}/progress`, { method: 'POST', body: JSON.stringify(body) });
    if (actionDraft) actionDraft.progress = body.new_progress;
    toast('پیشرفت ثبت شد.');
    await reopenAction(id);
  } catch (e) { toast(e.message); }
}

/* ───────── Projects ───────── */
let projectArchivedView = false, archivedProjects = [];
async function toggleArchivedProjects(on) {
  projectArchivedView = on;
  if (on) { try { archivedProjects = await api('/api/projects?archived=1'); } catch (e) { toast(e.message); projectArchivedView = false; } }
  renderProjects();
}
async function archiveProject(id, archive) {
  if (archive && !confirm('پروژه بایگانی شود؟ اقدام‌ها و جلسات آن حذف نمی‌شوند.')) return;
  try {
    await api(`/api/projects/${id}/${archive ? 'archive' : 'unarchive'}`, { method: 'POST' });
    await refresh();
    if (projectArchivedView) archivedProjects = await api('/api/projects?archived=1');
    toast(archive ? 'پروژه بایگانی شد.' : 'پروژه بازگردانی شد.'); renderProjects();
  } catch (e) { toast(e.message); }
}
async function deleteProject(id) {
  if (!confirm('پروژه برای همیشه حذف شود؟ اقدام‌ها و جلسات آن می‌مانند ولی از پروژه جدا می‌شوند. (برای نگه‌داشتن، «بایگانی» را بزنید.)')) return;
  try {
    await api(`/api/projects/${id}`, { method: 'DELETE' });
    await refresh();
    if (projectArchivedView) archivedProjects = await api('/api/projects?archived=1');
    toast('پروژه حذف شد.'); renderProjects();
  } catch (e) { toast(e.message); }
}

function renderProjects() {
  const can = has('projects.manage');
  const countFor = id => ({ m: (state.meetings || []).filter(m => m.project_id === id).length, a: (state.actions || []).filter(a => a.project_id === id).length });
  $('projects').innerHTML = `<div class="section">
    <div class="searchbar"><input class="search" id="projectSearch" placeholder="جستجوی پروژه"></div>
    <div class="sectionhead"><div><h3 style="margin:0">پروژه‌ها</h3><span class="muted">پروژه، جلسات و اقدامات مرتبط</span></div>
      ${archiveSwitch(projectArchivedView, 'toggleArchivedProjects')}</div>
    <div class="grid3">${(projectArchivedView ? archivedProjects : state.projects).map(p => { const c = countFor(p.id); return `<div class="card pcard" style="${p.color ? `border-top:5px solid ${p.color}` : ''}">
      <div class="sectionhead"><h3 style="margin:0;cursor:pointer" onclick="openProjectPage(${p.id})">${esc(p.name)}</h3>${badge(p.status)}</div><p class="muted">${esc(p.description)}</p>
      ${progressBar(p.progress)}<div class="mini" style="margin-top:7px">پیشرفت ${fa(p.progress)}٪ · مدیر: ${esc(p.manager || '—')}</div>
      <div class="mini muted">${p.start_date || p.end_date ? `${fd(p.start_date) || '—'} تا ${fd(p.end_date) || '—'}` : 'بازه‌ی زمانی تعیین نشده'}</div>
      <div class="mini muted">${fa(c.m)} جلسه · ${fa(c.a)} اقدام</div>
      <div class="actionsbar">${can ? `<button class="btn secondary" onclick="openProject(${p.id})">ویرایش</button><button class="btn secondary" onclick="archiveProject(${p.id},${projectArchivedView ? 'false' : 'true'})">${projectArchivedView ? 'بازگردانی' : 'بایگانی'}</button><button class="btn danger" onclick="deleteProject(${p.id})">حذف</button>` : ''}${projectArchivedView ? '' : `<button class="btn" onclick="openProjectPage(${p.id})">صفحه‌ی پروژه</button>`}</div></div>`; }).join('') || '<div class="empty">پروژه‌ای ثبت نشده است.</div>'}</div></div>
    <div class="section"><div class="sectionhead"><div><h3 style="margin:0">Gantt اقدامات</h3><span class="muted">نمای زمانی اقدامات دارای تاریخ شروع یا مهلت</span></div></div>${ganttHtml()}</div>`;
  $('projectSearch').oninput = () => {
    const q = $('projectSearch').value.toLowerCase();
    document.querySelectorAll('#projects .grid3 .card').forEach(c => { c.style.display = c.innerText.toLowerCase().includes(q) ? '' : 'none'; });
  };
}

function ganttHtml() {
  const acts = (state.actions || []).filter(a => Jalali.parse(a.start_date) || Jalali.parse(a.due));
  if (!acts.length) return '<div class="empty">برای نمایش Gantt، برای اقدامات تاریخ شروع و مهلت تعیین کنید.</div>';
  const span = a => { const s = Jalali.parse(a.start_date) || Jalali.parse(a.due), e = Jalali.parse(a.due) || s; return [s, e, Jalali.ordinal(s.y, s.m, s.d), Jalali.ordinal(e.y, e.m, e.d)]; };
  const rows = acts.slice(0, 40).map(a => ({ a, r: span(a) }));
  const min = Math.min(...rows.map(x => x.r[2])), max = Math.max(...rows.map(x => x.r[3])), total = Math.max(1, max - min + 1);
  const label = o => Jalali.toFa(Jalali.format(Jalali.fromOrdinal(o)));
  return `<div class="gantt"><div class="gantt-head"><b>اقدام</b><span>شروع: ${label(min)}</span><span>پایان: ${label(max)}</span></div>${rows.map(({ a, r }) => {
    const right = (r[2] - min) / total * 100, width = Math.max(2, (r[3] - r[2] + 1) / total * 100);
    return `<div class="gantt-row"><div class="gantt-label"><b>${esc(a.subject)}</b><div class="muted">${esc(a.project || 'بدون پروژه')}</div></div>
      <div class="gantt-track"><div class="gantt-dates"><span>${label(r[2])}</span><span>${label(r[3])}</span></div><div class="gantt-bar" style="right:${right}%;width:${width}%" title="${label(r[2])} تا ${label(r[3])}"></div></div></div>`;
  }).join('')}</div>`;
}

function memberEditor(list = []) {
  return `<div class="addline"><select id="pmemberPick" aria-label="انتخاب فرد"><option value="">انتخاب فرد...</option>${personOpts()}</select><button type="button" class="btn iconbtn" aria-label="افزودن" onclick="addProjectMember()">+</button></div><div id="pmemberList" class="collab-list"></div><input type="hidden" id="pmembers" value="${list.join(',')}">`;
}
function addProjectMember() {
  const id = Number($('pmemberPick').value);
  if (!id) return;
  const list = ids($('pmembers').value);
  if (!list.includes(id)) list.push(id);
  $('pmembers').value = list.join(',');
  renderProjectMemberList(list);
}
function removeProjectMember(id) {
  const list = ids($('pmembers').value).filter(x => x !== id);
  $('pmembers').value = list.join(',');
  renderProjectMemberList(list);
}
function renderProjectMemberList(list) {
  $('pmemberList').innerHTML = list.map(id => {
    const p = state.people.find(x => x.id === id);
    return p ? `<span class="collab-chip">${esc(p.name)}<button type="button" aria-label="حذف" onclick="removeProjectMember(${id})">×</button></span>` : '';
  }).join('') || '<span class="muted">عضوی انتخاب نشده است.</span>';
}

const PROJECT_COLORS = ['#2563eb','#16a34a','#7c3aed','#ea580c','#dc2626','#0891b2','#ca8a04','#64748b'];
const projectSwatches = cur => ['', ...PROJECT_COLORS].map(c => `<button type="button" class="swatch ${c === cur ? 'on' : ''}" style="background:${c || 'var(--slate-soft)'}" title="${c || 'بدون رنگ'}" aria-label="${c || 'بدون رنگ'}" onclick="pickProjectColor('${c}')"></button>`).join('');
function pickProjectColor(c) { $('pcolor').value = c; $('pswatches').innerHTML = projectSwatches(c); }

function openProject(id) {
  const p = id ? state.projects.find(x => x.id === id) : null;
  modal(mh(id ? 'ویرایش پروژه' : 'پروژه جدید', `
    <div class="grid">
      <div class="field"><label for="pnm">نام پروژه</label><input id="pnm" value="${esc(p?.name || '')}"></div>
      <div class="field"><label for="pmgr">مدیر پروژه</label><select id="pmgr"><option value="">—</option>${personOpts(p?.manager_id)}</select></div>
      <div class="field"><label for="psd">تاریخ شروع</label>${datePicker('psd', p?.start_date ?? Jalali.format(Jalali.today()))}</div>
      <div class="field"><label for="ped">تاریخ پایان</label>${datePicker('ped', p?.end_date || '', { minFrom: 'psd' })}</div>
      <div class="field"><label for="pst">وضعیت</label><select id="pst">${['فعال', 'متوقف', 'بسته'].map(v => `<option ${v === (p?.status || 'فعال') ? 'selected' : ''}>${v}</option>`).join('')}</select></div>
      <div class="field"><label for="ppg">پیشرفت (٪)</label><input id="ppg" type="number" min="0" max="100" value="${p?.progress || 0}"></div>
      <div class="field full"><label>اعضای پروژه</label>${memberEditor(p?.member_ids || [])}</div>
      <div class="field full"><label>رنگ پروژه</label><input type="hidden" id="pcolor" value="${esc(p?.color || '')}"><div class="swatches" id="pswatches">${projectSwatches(p?.color || '')}</div></div>
      <div class="field full"><label for="pds">توضیح</label><textarea id="pds">${esc(p?.description || '')}</textarea></div>
    </div>
    <div class="actionsbar"><button class="btn" onclick="saveProject(${id || 0})">ذخیره</button><button class="btn secondary" onclick="closeModal()">انصراف</button></div>`));
  renderProjectMemberList(p?.member_ids || []);
}

async function saveProject(id) {
  try {
    const start = readDate('psd', 'تاریخ شروع پروژه'), end = readDate('ped', 'تاریخ پایان پروژه');
    checkRange(start, end, 'تاریخ پایان پروژه نمی‌تواند قبل از تاریخ شروع باشد.');
    await api(id ? `/api/projects/${id}` : '/api/projects', { method: id ? 'PUT' : 'POST', body: JSON.stringify({
      name: $('pnm').value, status: $('pst').value, description: $('pds').value, manager_id: Number($('pmgr').value) || null,
      start_date: start, end_date: end, color: $('pcolor').value, progress: Number($('ppg').value || 0), member_ids: ids($('pmembers').value) }) });
    await refresh();
    closeModal();
    toast('پروژه ذخیره شد.');
    await afterSave('projects');
  } catch (e) { toast(e.message); }
}

/* ───────── People, teams, categories ───────── */
let peopleData = { teams: [], cats: [] };

async function renderPeople() {
  peopleData = { teams: await api('/api/teams'), cats: await api('/api/categories'), locations: await api('/api/locations') };
  state.locations = peopleData.locations;
  $('people').innerHTML = `<div class="switch" role="tablist">${[['people', 'افراد'], ['teams', 'تیم‌ها'], ['locations', 'مکان‌ها'], ['categories', 'دسته‌بندی‌ها']].map(([k, t], i) => `<button role="tab" class="${i ? '' : 'active'}" data-tab="${k}" onclick="peopleTab('${k}')">${t}</button>`).join('')}</div><div id="peopleTab" class="section"></div>`;
  peopleTab('people');
}
function peopleTab(tab) {
  document.querySelectorAll('#people .switch button').forEach(b => b.classList.toggle('active', b.dataset.tab === tab));
  ({ people: renderPeopleTab, teams: renderTeams, locations: renderLocations, categories: renderCategories })[tab]();
}
function renderPeopleTab() {
  $('peopleTab').innerHTML = `<div class="sectionhead"><h3>افراد</h3>${has('people.manage') ? '<button class="btn" onclick="openPerson()">+ فرد جدید</button>' : ''}</div>
    <div class="grid3">${state.people.map(p => `<div class="card"><h3>${esc(p.name)}</h3><div class="muted">${esc(p.role)}${p.role && p.department ? ' · ' : ''}${esc(p.department)}</div>
      <p><span class="chip">${esc(p.category || 'بدون دسته')}</span></p><button class="btn secondary" onclick="personActions(${p.id})">اقدامات فرد</button>${has('people.manage') ? ` <button class="btn secondary" onclick="openPerson(${p.id})">ویرایش</button>` : ''}</div>`).join('') || '<div class="empty">فردی ثبت نشده است.</div>'}</div>`;
}
function renderTeams() {
  $('peopleTab').innerHTML = `<div class="sectionhead"><h3>تیم‌ها</h3>${has('teams.manage') ? '<button class="btn" onclick="openTeam()">+ تیم جدید</button>' : ''}</div>
    ${peopleData.teams.map(t => `<div class="card" style="margin:10px 0"><h3>${esc(t.name)}</h3><p>${esc(t.description)}</p><div class="muted">مدیر: ${esc(t.manager || '—')}</div><div class="chips">${t.members.map(x => `<span class="chip">${esc(x)}</span>`).join('')}</div>${has('teams.manage') ? `<button class="btn secondary" style="margin-top:8px" onclick="openTeam(${t.id})">ویرایش</button>` : ''}</div>`).join('') || '<div class="empty">تیمی ثبت نشده است.</div>'}`;
}
function renderCategories() {
  $('peopleTab').innerHTML = `<div class="sectionhead"><h3>دسته‌بندی افراد</h3>${has('people.manage') ? '<button class="btn" onclick="addCategory()">+ دسته‌بندی</button>' : ''}</div>
    <div class="grid3">${peopleData.cats.map(c => `<div class="card"><h3>${esc(c.name)}</h3><div class="muted">${fa(state.people.filter(p => p.category_id === c.id || p.category === c.name).length)} نفر</div></div>`).join('') || '<div class="empty">دسته‌بندی‌ای ثبت نشده است.</div>'}</div>`;
}
function openPerson(id) {
  const p = id ? state.people.find(x => x.id === id) : null;
  modal(mh(id ? 'ویرایش فرد' : 'فرد جدید', `<div class="grid">
    <div class="field"><label for="pn">نام</label><input id="pn" value="${esc(p?.name || '')}"></div><div class="field"><label for="pdpt">واحد</label><input id="pdpt" value="${esc(p?.department || '')}"></div>
    <div class="field"><label for="prl">سمت</label><input id="prl" value="${esc(p?.role || '')}"></div><div class="field"><label for="pcat">دسته‌بندی</label><input id="pcat" value="${esc(p?.category || '')}"></div></div>
    <div class="actionsbar"><button class="btn" onclick="savePerson(${id || 0})">ذخیره</button><button class="btn secondary" onclick="closeModal()">انصراف</button></div>`));
}
async function savePerson(id) {
  try {
    const body = { name: $('pn').value, department: $('pdpt').value, role: $('prl').value, category: $('pcat').value, is_active: true };
    await api(id ? `/api/people/${id}` : '/api/people', { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) });
    await refresh(); closeModal(); toast('فرد ذخیره شد.'); await section('people');
  } catch (e) { toast(e.message); }
}
function openTeam(id) {
  const t = peopleData.teams.find(x => x.id === id);
  modal(mh(id ? 'ویرایش تیم' : 'تیم جدید', `<div class="grid">
    <div class="field"><label for="tn">نام تیم</label><input id="tn" value="${esc(t?.name || '')}"></div><div class="field"><label for="tm">مدیر تیم</label><select id="tm"><option value="">—</option>${personOpts(t?.manager_id)}</select></div>
    <div class="field full"><label for="tmem">اعضای تیم</label><select id="tmem" multiple size="6">${personOpts(null, true, t?.member_ids || [])}</select></div>
    <div class="field full"><label for="td">توضیح</label><textarea id="td">${esc(t?.description || '')}</textarea></div></div>
    <div class="actionsbar"><button class="btn" onclick="saveTeam(${id || 0})">ذخیره</button><button class="btn secondary" onclick="closeModal()">انصراف</button></div>`));
}
async function saveTeam(id) {
  try {
    await api(id ? `/api/teams/${id}` : '/api/teams', { method: id ? 'PUT' : 'POST', body: JSON.stringify({ name: $('tn').value, description: $('td').value, manager_id: Number($('tm').value) || null, member_ids: [...$('tmem').selectedOptions].map(x => Number(x.value)), is_active: true }) });
    await refresh(); closeModal(); toast('تیم ذخیره شد.'); await section('people');
  } catch (e) { toast(e.message); }
}
function renderLocations() {
  $('peopleTab').innerHTML = `<div class="sectionhead"><h3>مکان‌ها</h3>${has('locations.manage') ? '<button class="btn" onclick="openLocation()">+ مکان جدید</button>' : ''}</div>
    <div class="grid3">${peopleData.locations.map(l => `<div class="card"><h3>${esc(l.name)}${l.is_active ? '' : ' <span class="badge new">غیرفعال</span>'}</h3><div class="muted">${esc(l.address || '—')}</div>
      ${l.description ? `<p>${esc(l.description)}</p>` : ''}${has('locations.manage') ? `<button class="btn secondary" onclick="openLocation(${l.id})">ویرایش</button>` : ''}</div>`).join('') || '<div class="empty">مکانی ثبت نشده است.</div>'}</div>`;
}
function openLocation(id) {
  const l = peopleData.locations.find(x => x.id === id);
  modal(mh(id ? 'ویرایش مکان' : 'مکان جدید', `<div class="grid">
    <div class="field"><label for="ln">نام مکان</label><input id="ln" value="${esc(l?.name || '')}"></div>
    <div class="field"><label for="la">آدرس</label><input id="la" value="${esc(l?.address || '')}"></div>
    <div class="field full"><label for="ld">توضیحات</label><textarea id="ld">${esc(l?.description || '')}</textarea></div>
    <div class="field"><label class="check"><input id="lact" type="checkbox" ${l?.is_active !== false ? 'checked' : ''}> فعال</label></div></div>
    <div class="actionsbar"><button class="btn" onclick="saveLocation(${id || 0})">ذخیره</button><button class="btn secondary" onclick="closeModal()">انصراف</button></div>`));
}
async function saveLocation(id) {
  try {
    const body = { name: $('ln').value, address: $('la').value, description: $('ld').value, is_active: $('lact').checked };
    await api(id ? `/api/locations/${id}` : '/api/locations', { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) });
    await refresh(); closeModal(); toast('مکان ذخیره شد.'); await section('people'); peopleTab('locations');
  } catch (e) { toast(e.message); }
}
async function addCategory() {
  const name = await ask({ title: 'دسته‌بندی جدید', label: 'نام دسته‌بندی', okText: 'ایجاد' });
  if (!name) return;
  try { await api('/api/categories', { method: 'POST', body: JSON.stringify({ name }) }); toast('دسته‌بندی ساخته شد.'); await renderPeople(); peopleTab('categories'); }
  catch (e) { toast(e.message); }
}

/* ───────── Export & backup ───────── */
const IC = {
  print: '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M7 9V4h10v5M7 17H5a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-2"/><rect x="7" y="14" width="10" height="6" rx="1"/></svg>',
  range: '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3.5" y="5" width="17" height="15.5" rx="2.5"/><path d="M8 3v4M16 3v4M3.5 10h17"/></svg>',
  down: '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v11M7 11l5 5 5-5M5 20h14"/></svg>',
  up: '<svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 16V5M7 9l5-5 5 5M5 20h14"/></svg>',
};
function renderReports() {
  const canM = has('meetings.export'), canA = has('actions.export'), canB = has('backup.manage');
  const no = label => `<span class="muted">مجوز ${label} ندارید.</span>`;
  /* Gestalt: one card = one subject. Top row: the three file formats as one joined group (proximity + similarity);
     bottom row: two secondary actions of equal size, separated by a divider (common region). */
  const card = (title, text, base, type, can, label) => `<div class="section io-card"><h3>${title}</h3><p class="muted">${text}</p>${can ? `
    <div class="btngroup" role="group" aria-label="قالب فایل">${['xlsx', 'docx', 'pdf'].map(f => `<button class="btn" onclick="download('/api/export/${base}.${f}','${base}.${f}')">${{ xlsx: 'Excel', docx: 'Word', pdf: 'PDF' }[f]}</button>`).join('')}</div>
    <div class="io-sub"><button class="btn ghost" onclick="printPdf('/api/export/${base}.pdf')">${IC.print}چاپ PDF</button><button class="btn ghost" onclick="openExportFilter('${type}')">${IC.range}خروجی بازه‌ای</button></div>` : no('خروجی ' + label)}</div>`;
  $('reports').innerHTML = `<div class="grid3 export-grid">
    ${card('جلسات', 'Word، PDF و Excel با ساختار راست‌به‌چپ؛ اقدام‌ها به تفکیک اسپرینت.', 'meetings', 'meeting', canM, 'جلسات')}
    ${card('اقدامات', 'فقط اقداماتی که اجازه‌ی دیدنشان را دارید.', 'actions', 'action', canA, 'اقدامات')}
    <div class="section io-card"><h3>Excel تسک‌ها</h3><p class="muted">همان قالب Full_Tasks (شیت Items) که می‌توان دوباره از بخش «ورودی» وارد برنامه کرد.</p>${canA ? `
      <div class="io-sub single"><button class="btn" onclick="exportTaskSheet(false)">${IC.down}دریافت Full_Tasks.xlsx</button></div>` : no('خروجی اقدامات')}</div>
    <div class="section io-card"><h3>پشتیبان‌گیری</h3><p class="muted">یک نسخه از پایگاه داده دریافت می‌شود.</p>${canB ? `<div class="io-sub single"><button class="btn success" onclick="download('/api/backup','meeting_manager_backup.db')">${IC.down}دریافت نسخه‌ی پشتیبان</button></div>` : no('پشتیبان‌گیری')}</div></div>`;
}

/* ───────── Import ───────── */
function renderImports() {
  $('imports').innerHTML = `<div class="section io-card">
    <div class="sectionhead"><div><h3>ورود از Excel تسک‌ها</h3><span class="muted">فایل Full_Tasks (شیت Items)؛ تسک‌های موجود به‌روزرسانی و بقیه اضافه می‌شوند.</span></div></div>
    ${has('actions.manage') ? `<label class="dropzone" ondragover="event.preventDefault();this.classList.add('over')" ondragleave="this.classList.remove('over')" ondrop="event.preventDefault();this.classList.remove('over');importTaskSheet({files:event.dataTransfer.files,value:''})">
      <input type="file" id="taskSheetFile" accept=".xlsx" hidden onchange="importTaskSheet(this)">${IC.up}
      <b>فایل .xlsx را اینجا رها کنید</b><span class="muted">یا برای انتخاب فایل کلیک کنید</span></label>
    <div id="importResult" class="import-result"></div>` : '<span class="muted">مجوز ورود اقدامات را ندارید.</span>'}</div>`;
}

/* ───────── Users & permissions ───────── */
async function renderAccess() {
  if (!has('users.manage') && !has('permissions.manage')) { $('access').innerHTML = '<div class="empty">دسترسی به این بخش ندارید.</div>'; return; }
  $('access').innerHTML = `<div class="section"><div class="sectionhead"><h3>کاربران</h3>${has('users.manage') ? '<button class="btn" onclick="openUser()">+ کاربر جدید</button>' : ''}</div><div id="usersList">در حال بارگذاری...</div></div>
    <div class="section"><h3>نقش‌ها و مجوزها</h3><div id="permArea">در حال بارگذاری...</div></div>`;
  if (has('users.manage')) {
    const xs = await api('/api/users');
    $('usersList').innerHTML = `<div class="tablewrap"><table class="table"><tr><th>کاربر</th><th>نام</th><th>نقش</th><th>فرد مرتبط</th><th>فعال</th><th></th></tr>${xs.map(x => `<tr><td>${esc(x.username)}</td><td>${esc(x.full_name)}</td><td>${esc(ROLE_LABEL[x.role] || x.role)}</td><td>${esc(state.people.find(p => p.id === x.person_id)?.name || '—')}</td><td>${x.is_active ? 'بله' : 'خیر'}</td><td><button class="btn secondary" onclick="openUser(${x.id})">ویرایش</button></td></tr>`).join('')}</table></div>`;
  } else $('usersList').innerHTML = '';
  if (has('permissions.manage')) {
    const d = await api('/api/permissions');
    $('permArea').innerHTML = Object.keys(d.roles).map(role => `<div class="rolebox"><h3>${esc(ROLE_LABEL[role] || role)}</h3><div class="permgrid">${d.permissions.map(p => `<label class="perm"><input type="checkbox" data-role="${role}" data-code="${p.code}" ${d.roles[role].includes(p.code) ? 'checked' : ''}>${esc(p.title)}</label>`).join('')}</div><button class="btn" style="margin-top:10px" onclick="saveRolePerm('${role}')">ذخیره مجوزهای این نقش</button></div>`).join('');
  } else $('permArea').innerHTML = '';
}
async function openUser(id) {
  try {
    const u = id ? (await api('/api/users')).find(x => x.id === id) : null;
    modal(mh(id ? 'ویرایش کاربر' : 'کاربر جدید', `<div class="grid">
      <div class="field"><label for="un">نام کاربری</label><input id="un" value="${esc(u?.username || '')}"></div><div class="field"><label for="uf">نام</label><input id="uf" value="${esc(u?.full_name || '')}"></div>
      <div class="field"><label for="up">رمز عبور ${id ? '(فقط در صورت تغییر)' : ''}</label><input id="up" type="password" autocomplete="new-password"></div>
      <div class="field"><label for="ur">نقش</label><select id="ur">${['user', 'manager', 'admin'].map(r => `<option value="${r}" ${(u?.role || 'user') === r ? 'selected' : ''}>${ROLE_LABEL[r]}</option>`).join('')}</select></div>
      <div class="field"><label for="uperson">فرد مرتبط برای دسترسی به اقدامات</label><select id="uperson"><option value="">—</option>${personOpts(u?.person_id)}</select></div>
      <div class="field"><label>وضعیت</label><label class="check"><input id="uactive" type="checkbox" ${u?.is_active === false ? '' : 'checked'}> کاربر فعال است</label></div></div>
      <div class="actionsbar"><button class="btn" onclick="saveUser(${id || 0})">ذخیره</button><button class="btn secondary" onclick="closeModal()">انصراف</button></div>`));
  } catch (e) { toast(e.message); }
}
async function saveUser(id) {
  try {
    const body = { username: $('un').value, full_name: $('uf').value, role: $('ur').value, person_id: Number($('uperson').value) || null, is_active: $('uactive').checked };
    const pw = $('up').value;
    if (pw) body.password = pw; else if (!id) throw Error('رمز عبور کاربر جدید الزامی است.');
    await api(id ? `/api/users/${id}` : '/api/users', { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) });
    closeModal(); toast('کاربر ذخیره شد.'); await renderAccess();
  } catch (e) { toast(e.message); }
}
async function saveRolePerm(role) {
  const codes = [...document.querySelectorAll(`[data-role="${role}"]:checked`)].map(x => x.dataset.code);
  try { await api('/api/permissions', { method: 'PUT', body: JSON.stringify({ role, permission_codes: codes }) }); toast('مجوزها ذخیره شد.'); }
  catch (e) { toast(e.message); }
}

/* ───────── Profile & password ───────── */
function setAvatar(el, u) {
  if (u.avatar && /^data:image\/(png|jpeg|gif|webp);base64,[A-Za-z0-9+\/=]+$/.test(u.avatar)) { el.textContent = ''; el.style.backgroundImage = `url("${u.avatar}")`; el.style.backgroundSize = 'cover'; el.style.backgroundPosition = 'center'; }
  else { el.style.backgroundImage = ''; el.textContent = (u.full_name || u.username || 'م').slice(0, 1); }
}
/** Shrinks a chosen photo to a small square JPEG (data URL) so it stays light in the database. */
function pickAvatar(file) {
  return new Promise((resolve, reject) => {
    if (!file || !file.type.startsWith('image/')) return reject(Error('یک فایل تصویر انتخاب کنید.'));
    const img = new Image(), url = URL.createObjectURL(file);
    img.onload = () => {
      const n = 160, c = document.createElement('canvas'); c.width = c.height = n;
      const s = Math.min(img.width, img.height), ctx = c.getContext('2d');
      ctx.drawImage(img, (img.width - s) / 2, (img.height - s) / 2, s, s, 0, 0, n, n);
      URL.revokeObjectURL(url); resolve(c.toDataURL('image/jpeg', 0.85));
    };
    img.onerror = () => reject(Error('تصویر خوانده نشد.'));
    img.src = url;
  });
}
let profileAvatarDraft = '';
async function chooseAvatar(input) {
  try { profileAvatarDraft = await pickAvatar(input.files[0]); setAvatar($('pfAvatar'), { avatar: profileAvatarDraft }); } catch (e) { toast(e.message); }
}
async function saveProfile() {
  try {
    const birth = readDate('pfBirth', 'تاریخ تولد');
    await api('/api/auth/profile', { method: 'PUT', body: JSON.stringify({ birth_date: birth, avatar: profileAvatarDraft }) });
    state.user.avatar = profileAvatarDraft; state.user.birth_date = birth; setAvatar($('profileAvatar'), state.user);
    $('userRole').textContent = (ROLE_LABEL[state.user.role] || state.user.role) + (birth ? ' · تولد ' + Jalali.toFa(birth) : '');
    closeModal(); toast('اطلاعات ذخیره شد.');
  } catch (e) { toast(e.message); }
}
async function openProfile() {
  try {
    const u = await api('/api/auth/me');
    profileAvatarDraft = u.avatar || '';
    modal(mh('اطلاعات کاربری', `<div class="profile-card" style="grid-template-columns:72px 1fr"><div class="profile-avatar" id="pfAvatar" style="width:72px;height:72px;font-size:28px"></div>
      <div><h3 style="margin:0">${esc(u.full_name)}</h3><div class="muted">${esc(u.username)} · ${esc(ROLE_LABEL[u.role] || u.role)}</div>
      <div class="muted" style="margin-top:5px">آخرین ورود: ${u.last_login ? esc(new Date(u.last_login).toLocaleString('fa-IR')) : 'اولین ورود'}</div></div></div>
      <div class="grid"><div class="field"><label for="pfFile">تصویر پروفایل</label><input id="pfFile" type="file" accept="image/*" onchange="chooseAvatar(this)"></div>
        <div class="field"><label for="pfBirth">تاریخ تولد (اختیاری)</label>${datePicker('pfBirth', u.birth_date || '')}</div></div>
      <div class="actionsbar"><button class="btn" onclick="saveProfile()">ذخیره</button><button class="btn secondary" onclick="openPassword()">تغییر رمز عبور</button><button class="btn secondary" onclick="closeModal()">بستن</button></div>`));
    setAvatar($('pfAvatar'), u);
  } catch (e) { toast(e.message); }
}
function openPassword() {
  modal(mh('تغییر رمز عبور', `<div class="field"><label for="oldpw">رمز فعلی</label><input id="oldpw" type="password" autocomplete="current-password"></div>
    <div class="field"><label for="newpw">رمز جدید (حداقل ۶ نویسه)</label><input id="newpw" type="password" autocomplete="new-password"></div>
    <div class="actionsbar"><button class="btn" onclick="changePassword()">ذخیره</button><button class="btn secondary" onclick="closeModal()">انصراف</button></div>`));
}
async function changePassword() {
  try {
    await api('/api/auth/password', { method: 'POST', body: JSON.stringify({ current_password: $('oldpw').value, new_password: $('newpw').value }) });
    closeModal(); toast('رمز عبور تغییر کرد.');
  } catch (e) { toast(e.message); }
}

/* ───────── Boot ───────── */
(async () => {
  if (localStorage.getItem('mm_theme') === 'dark') document.body.classList.add('dark');
  applyLook();
  if (!token) return;
  try { await start(); } catch (e) { logout(); }
})();
