/* Calendar features: event index, sidebar mini calendar and the full calendar page.
   Events come from the data already loaded in `state` (meetings, actions, projects). */
const CalEvents = (() => {
  const TYPES = {
    meeting: { label: 'جلسه', color: '#2563eb' },
    event: { label: 'رویداد', color: '#059669' },
    action: { label: 'مهلت اقدام', color: '#d97706' },
    project: { label: 'پروژه', color: '#7c3aed' },
  };
  let index = null;

  const invalidate = () => { index = null; };

  /** Same rule as the server: an unfinished action past its due date is late. */
  function effectiveStatus(a) {
    return a.status !== 'انجام شده' && a.due && Jalali.canonical(a.due) < Jalali.format(Jalali.today()) ? 'عقب‌افتاده' : a.status;
  }

  function build() {
    const idx = {};
    const add = (date, ev) => {
      const key = Jalali.canonical(date);
      if (key) (idx[key] ||= []).push(ev);
    };
    (state.meetings || []).forEach(m => add(m.date, { type: 'meeting', id: m.id, title: m.title, time: m.time, place: m.place, project: m.project }));
    (state.events || []).forEach(e => add(e.date, { type: 'event', id: e.id, title: e.title, time: e.start_time, place: e.place, project: e.project, kind: e.kind }));
    (state.actions || []).forEach(a => add(a.due, { type: 'action', id: a.id, title: a.subject, status: effectiveStatus(a), owner: a.owner || a.assignee, project: a.project }));
    (state.projects || []).forEach(p => {
      add(p.start_date, { type: 'project', id: p.id, title: `شروع پروژه: ${p.name}` });
      add(p.end_date, { type: 'project', id: p.id, title: `پایان پروژه: ${p.name}` });
    });
    return idx;
  }

  const all = () => (index ||= build());
  const on = date => all()[Jalali.format(date)] || [];
  return { TYPES, invalidate, on, effectiveStatus };
})();

/* ───────── Sidebar mini calendar ───────── */
const MiniCal = (() => {
  const chev = d => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${d}"/></svg>`;
  let view = null;
  let collapsed = false;
  try { collapsed = localStorage.getItem('mm_minical') === '0'; } catch (e) { /* storage unavailable */ }

  function render() {
    const el = $('miniCal');
    if (!el) return;
    const today = Jalali.today();
    view ||= { y: today.y, m: today.m };
    const { y, m } = view, lead = Jalali.weekday(y, m, 1), total = Jalali.daysInMonth(y, m);
    const selected = Calendar.selected();
    let cells = Jalali.WEEKDAYS_SHORT.map((w, i) => `<div class="mc-wd ${i === 6 ? 'fri' : ''}">${w}</div>`).join('') + '<span></span>'.repeat(lead);
    for (let d = 1; d <= total; d++) {
      const date = { y, m, d }, evs = CalEvents.on(date), types = [...new Set(evs.map(e => e.type))];
      const cls = ['mc-day', (lead + d - 1) % 7 === 6 && 'fri', Jalali.same(date, today) && 'today', Jalali.same(date, selected) && 'selected'].filter(Boolean).join(' ');
      const label = `${Jalali.longDate(date)}${evs.length ? `، ${fa(evs.length)} رویداد` : ''}`;
      cells += `<button type="button" class="${cls}" onclick="MiniCal.pick(${y},${m},${d})" aria-label="${label}">${fa(d)}${
        types.length ? `<span class="mc-dots">${types.map(t => `<i style="background:${CalEvents.TYPES[t].color}"></i>`).join('')}</span>` : ''}</button>`;
    }
    el.className = 'minical' + (collapsed ? ' collapsed' : '');
    el.innerHTML = `
      <button type="button" class="mc-top" onclick="MiniCal.toggle()" aria-expanded="${!collapsed}" aria-controls="miniCalBody">
        <span><b>تقویم شمسی</b><br><span class="mc-today">امروز: ${Jalali.longDate(today)}</span></span>${chev('M6 9l6 6 6-6').replace('<svg', '<svg class="mc-chevron"')}
      </button>
      <div class="mc-body" id="miniCalBody">
        <div class="mc-nav">
          <button type="button" onclick="MiniCal.move(-1)" aria-label="ماه قبل">${chev('M9 6l6 6-6 6')}</button>
          <span class="mc-title">${Jalali.monthTitle(y, m)}</span>
          <button type="button" onclick="MiniCal.move(1)" aria-label="ماه بعد">${chev('M15 6l-6 6 6 6')}</button>
        </div>
        <div class="mc-grid">${cells}</div>
        <div class="mc-foot"><button type="button" onclick="MiniCal.today()">امروز</button></div>
      </div>`;
  }

  function move(delta) {
    let { y, m } = view;
    m += delta;
    if (m > 12) { m = 1; y++; } else if (m < 1) { m = 12; y--; }
    view = { y, m };
    render();
  }

  function today() { const t = Jalali.today(); view = { y: t.y, m: t.m }; render(); }

  function toggle() {
    collapsed = !collapsed;
    try { localStorage.setItem('mm_minical', collapsed ? '0' : '1'); } catch (e) { /* storage unavailable */ }
    render();
  }

  /** Choosing a day opens the full calendar on that day. */
  function pick(y, m, d) {
    Calendar.select({ y, m, d });
    section('calendar');
  }

  return { render, move, today, toggle, pick };
})();

/* ───────── Full calendar page ───────── */
const Calendar = (() => {
  const t0 = Jalali.today();
  let view = { y: t0.y, m: t0.m };
  let sel = null;
  const filters = { meeting: true, event: true, action: true, project: true };

  const selected = () => sel;

  function select(date) {
    sel = date;
    view = { y: date.y, m: date.m };
    MiniCal.render();
  }

  function render() {
    const { y, m } = view, today = Jalali.today(), lead = Jalali.weekday(y, m, 1), total = Jalali.daysInMonth(y, m);
    const counts = { meeting: 0, event: 0, action: 0, project: 0 };
    let cells = Jalali.WEEKDAYS.map((w, i) => `<div class="cal-head ${i === 6 ? 'fri' : ''}">${w}</div>`).join('') + '<div class="cal-day blank"></div>'.repeat(lead);
    for (let d = 1; d <= total; d++) {
      const date = { y, m, d }, all = CalEvents.on(date);
      all.forEach(e => counts[e.type]++);
      const evs = all.filter(e => filters[e.type]);
      const cls = ['cal-day', (lead + d - 1) % 7 === 6 && 'fri', Jalali.same(date, today) && 'today', Jalali.same(date, sel) && 'selected'].filter(Boolean).join(' ');
      const chip = e => `<span class="cal-chip ${e.type === 'action' && e.status === 'عقب‌افتاده' ? 'late' : e.type}" title="${esc(e.title)}">${esc(e.title)}</span>`;
      cells += `<button type="button" class="${cls}" onclick="Calendar.pick(${d})" aria-label="${Jalali.longDate(date)}${evs.length ? `، ${fa(evs.length)} رویداد` : ''}">
        <span class="calnum">${fa(d)}</span>${evs.slice(0, 3).map(chip).join('')}${evs.length > 3 ? `<span class="cal-more">${fa(evs.length - 3)} مورد دیگر</span>` : ''}</button>`;
    }
    cells += '<div class="cal-day blank"></div>'.repeat((7 - (lead + total) % 7) % 7);
    const years = [];
    for (let v = Math.min(t0.y, y) - 8; v <= Math.max(t0.y, y) + 8; v++) years.push(v);

    $('calendar').innerHTML = `<div class="cal-layout">
      <div class="section">
        <div class="cal-toolbar">
          <div class="toolbar"><button type="button" class="btn secondary" onclick="Calendar.move(-1)">‹ ماه قبل</button><button type="button" class="btn secondary" onclick="Calendar.move(1)">ماه بعد ›</button></div>
          <div class="cal-title">
            <select aria-label="ماه" onchange="Calendar.jump(Number(this.value),${y})">${Jalali.MONTHS.map((n, i) => `<option value="${i + 1}" ${i + 1 === m ? 'selected' : ''}>${n}</option>`).join('')}</select>
            <select aria-label="سال" onchange="Calendar.jump(${m},Number(this.value))">${years.map(v => `<option value="${v}" ${v === y ? 'selected' : ''}>${fa(v)}</option>`).join('')}</select>
          </div>
          <button type="button" class="btn secondary" onclick="Calendar.goToday()">امروز</button>
        </div>
        <div class="cal-filters" role="group" aria-label="نوع رویدادها">${Object.entries(CalEvents.TYPES).map(([k, t]) =>
          `<button type="button" class="cal-filter" aria-pressed="${filters[k]}" onclick="Calendar.toggleFilter('${k}')"><i style="background:${t.color}"></i>${t.label} (${fa(counts[k])})</button>`).join('')}</div>
        <div class="calendar-wrap"><div class="cal-grid">${cells}</div></div>
      </div>
      <div class="section agenda">${agenda()}</div>
    </div>`;
  }

  function agenda() {
    if (!sel) return `<h3>برنامه‌ی روز</h3><div class="empty">برای دیدن رویدادها و ثبت جلسه یا اقدام، یک روز را از تقویم انتخاب کنید.</div>`;
    const key = Jalali.format(sel), evs = CalEvents.on(sel).filter(e => filters[e.type]);
    const item = e => {
      const open = e.type === 'meeting' ? `showMeeting(${e.id})` : e.type === 'event' ? `openEvent(${e.id})` : e.type === 'action' ? `openAction(${e.id})` : `section('projects')`;
      const meta = e.type === 'meeting' || e.type === 'event' ? [e.kind, e.time && fa(e.time), e.place, e.project].filter(Boolean).join(' · ')
        : e.type === 'action' ? [e.status, e.owner, e.project].filter(Boolean).join(' · ') : '';
      return `<button type="button" class="agenda-item ${e.type}" onclick="${open}"><b>${esc(e.title)}</b>${meta ? `<small>${esc(meta)}</small>` : ''}</button>`;
    };
    return `<h3>${Jalali.longDate(sel)}</h3>
      <p class="muted mini">${evs.length ? `${fa(evs.length)} رویداد` : 'رویدادی برای این روز ثبت نشده است.'}</p>
      ${evs.map(item).join('')}
      <div class="agenda-actions">
        ${has('meetings.manage') ? `<button type="button" class="btn" onclick="openMeeting(null,{date:'${key}'})">+ جلسه در این روز</button>` : ''}
        ${has('meetings.manage') ? `<button type="button" class="btn secondary" onclick="openEvent(null,{date:'${key}'})">+ رویداد در این روز</button>` : ''}
        ${has('actions.manage') ? `<button type="button" class="btn secondary" onclick="openAction(null,{due:'${key}'})">+ اقدام با مهلت این روز</button>` : ''}
      </div>`;
  }

  function move(delta) {
    let { y, m } = view;
    m += delta;
    if (m > 12) { m = 1; y++; } else if (m < 1) { m = 12; y--; }
    view = { y, m };
    render();
  }
  const jump = (m, y) => { view = { y, m }; render(); };
  const goToday = () => { select(Jalali.today()); render(); };
  const pick = d => { select({ ...view, d }); render(); };
  const toggleFilter = type => { filters[type] = !filters[type]; render(); };

  return { selected, select, render, move, jump, goToday, pick, toggleFilter };
})();
