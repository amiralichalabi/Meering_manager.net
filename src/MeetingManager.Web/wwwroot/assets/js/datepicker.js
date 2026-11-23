/* Jalali date picker: a popover anchored to the field (it never replaces the form it is used in).
   Markup:  DatePicker.field('id', value, { minFrom: 'otherInputId' })
   Value:   DatePicker.read('id', 'برچسب')  -> 'YYYY/MM/DD' | ''   (throws a readable Error if invalid)
   Typing is allowed too: Persian/Latin digits and / - . separators are normalised on change. */
const DatePicker = (() => {
  const svg = path => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${path}</svg>`;
  const ICON_CAL = svg('<rect x="3.5" y="5" width="17" height="15.5" rx="2.5"/><path d="M8 3v4M16 3v4M3.5 10h17"/>');
  const ICON_PREV = svg('<path d="M9 6l6 6-6 6"/>'); // RTL: "previous" sits on the right and points right
  const ICON_NEXT = svg('<path d="M15 6l-6 6 6 6"/>');

  let pop = null;     // popover element while open
  let input = null;   // input it is attached to
  let view = null;    // {y, m} month on display
  let focusDay = null; // {y, m, d} keyboard focus

  const isOpen = () => !!pop;
  const example = () => Jalali.toFa(Jalali.format(Jalali.today()));

  function field(id, value = '', { minFrom = '' } = {}) {
    return `<div class="date-field"><input id="${id}" class="date-input" type="text" inputmode="numeric" autocomplete="off"
      placeholder="${example()}" value="${esc(fd(value))}" ${minFrom ? `data-min-from="${minFrom}"` : ''} aria-haspopup="dialog">
      <button type="button" class="date-btn" aria-label="انتخاب تاریخ از تقویم">${ICON_CAL}</button></div>`;
  }

  function read(idOrEl, label = 'تاریخ') {
    const el = typeof idOrEl === 'string' ? $(idOrEl) : idOrEl;
    if (!el) return '';
    const value = Jalali.canonical(el.value);
    if (value === null) {
      el.setAttribute('aria-invalid', 'true');
      el.focus();
      throw Error(`${label} معتبر نیست. نمونه‌ی درست: ${example()}`);
    }
    return value;
  }

  /** Programmatic set (value is a canonical date or ''). Fires 'change' so dependants can react. */
  function setValue(idOrEl, value) {
    const el = typeof idOrEl === 'string' ? $(idOrEl) : idOrEl;
    if (!el) return;
    el.value = fd(value);
    el.removeAttribute('aria-invalid');
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }

  const minDate = () => {
    const ref = input?.dataset.minFrom && $(input.dataset.minFrom);
    return ref ? Jalali.parse(ref.value) : null;
  };

  function open(el) {
    if (pop && input === el) return;
    close();
    input = el;
    const selected = Jalali.parse(el.value);
    const base = selected || Jalali.today();
    view = { y: base.y, m: base.m };
    focusDay = selected || { ...base, d: Jalali.same(base, Jalali.today()) ? base.d : 1 };
    pop = document.createElement('div');
    pop.className = 'dp';
    pop.setAttribute('role', 'dialog');
    pop.setAttribute('aria-label', 'انتخاب تاریخ شمسی');
    pop.addEventListener('click', onPopClick);
    pop.addEventListener('change', onPopChange);
    pop.addEventListener('keydown', onPopKey);
    document.body.appendChild(pop);
    render();
    position();
  }

  function close() {
    if (!pop) return;
    pop.remove();
    pop = input = view = focusDay = null;
  }

  function position() {
    if (!pop || !input) return;
    const r = input.getBoundingClientRect(), w = pop.offsetWidth, h = pop.offsetHeight, m = 8;
    let left = Math.min(Math.max(r.right - w, m), innerWidth - w - m);
    let top = r.bottom + 6;
    if (top + h > innerHeight - m) top = r.top - h - 6 >= m ? r.top - h - 6 : Math.max(m, innerHeight - h - m);
    pop.style.left = left + 'px';
    pop.style.top = top + 'px';
  }

  function render(focusSelector) {
    if (!pop) return;
    const { y, m } = view, today = Jalali.today(), selected = Jalali.parse(input.value), min = minDate();
    const lead = Jalali.weekday(y, m, 1), total = Jalali.daysInMonth(y, m);
    let cells = Jalali.WEEKDAYS_SHORT.map((w, i) => `<div class="dp-wd ${i === 6 ? 'fri' : ''}">${w}</div>`).join('');
    cells += '<span></span>'.repeat(lead);
    for (let d = 1; d <= total; d++) {
      const date = { y, m, d }, wd = (lead + d - 1) % 7;
      const cls = ['dp-day', wd === 6 && 'fri', Jalali.same(date, today) && 'today', Jalali.same(date, selected) && 'selected'].filter(Boolean).join(' ');
      const disabled = min && Jalali.compare(date, min) < 0;
      const isFocus = focusDay && Jalali.same(date, focusDay);
      cells += `<button type="button" class="${cls}" data-d="${d}" tabindex="${isFocus ? 0 : -1}" ${disabled ? 'disabled' : ''}
        aria-label="${Jalali.longDate(date)}" aria-pressed="${Jalali.same(date, selected)}">${Jalali.toFa(d)}</button>`;
    }
    const years = [];
    for (let yy = Math.min(today.y, y) - 10; yy <= Math.max(today.y, y) + 10; yy++) years.push(yy);
    pop.innerHTML = `
      <div class="dp-head">
        <button type="button" class="dp-nav" data-act="prev" aria-label="ماه قبل">${ICON_PREV}</button>
        <div class="dp-selects">
          <select data-act="month" aria-label="ماه">${Jalali.MONTHS.map((n, i) => `<option value="${i + 1}" ${i + 1 === m ? 'selected' : ''}>${n}</option>`).join('')}</select>
          <select data-act="year" aria-label="سال">${years.map(v => `<option value="${v}" ${v === y ? 'selected' : ''}>${Jalali.toFa(v)}</option>`).join('')}</select>
        </div>
        <button type="button" class="dp-nav" data-act="next" aria-label="ماه بعد">${ICON_NEXT}</button>
      </div>
      <div class="dp-grid">${cells}</div>
      <div class="dp-foot"><button type="button" data-act="today">امروز</button><button type="button" data-act="clear">پاک کردن</button></div>`;
    if (focusSelector) pop.querySelector(focusSelector)?.focus();
  }

  function choose(date) {
    const el = input;
    close();
    setValue(el, Jalali.format(date));
    el.focus();
  }

  function shiftMonth(delta, focusSelector) {
    let { y, m } = view;
    m += delta;
    if (m > 12) { m = 1; y++; } else if (m < 1) { m = 12; y--; }
    view = { y, m };
    focusDay = { y, m, d: Math.min(focusDay?.d || 1, Jalali.daysInMonth(y, m)) };
    render(focusSelector);
  }

  function onPopClick(e) {
    const btn = e.target.closest('button');
    if (!btn) return;
    if (btn.dataset.d) return choose({ ...view, d: Number(btn.dataset.d) });
    const act = btn.dataset.act;
    if (act === 'prev') shiftMonth(-1, '[data-act=prev]');
    else if (act === 'next') shiftMonth(1, '[data-act=next]');
    else if (act === 'today') {
      const t = Jalali.today(), min = minDate();
      if (min && Jalali.compare(t, min) < 0) { view = { y: min.y, m: min.m }; focusDay = min; render(); return; }
      choose(t);
    } else if (act === 'clear') { const el = input; close(); setValue(el, ''); el.focus(); }
  }

  function onPopChange(e) {
    const act = e.target.dataset.act;
    if (act !== 'month' && act !== 'year') return;
    const y = Number(pop.querySelector('[data-act=year]').value), m = Number(pop.querySelector('[data-act=month]').value);
    view = { y, m };
    focusDay = { y, m, d: Math.min(focusDay?.d || 1, Jalali.daysInMonth(y, m)) };
    render(`[data-act=${act}]`);
  }

  function moveFocus(days) {
    const next = Jalali.addDays(focusDay, days), min = minDate();
    if (min && Jalali.compare(next, min) < 0) return;
    focusDay = next;
    view = { y: next.y, m: next.m };
    render(`[data-d="${next.d}"]`);
  }

  function onPopKey(e) {
    if (!e.target.classList.contains('dp-day')) return;
    // RTL grid: the next day is to the left of the current one.
    const step = { ArrowLeft: 1, ArrowRight: -1, ArrowDown: 7, ArrowUp: -7 }[e.key];
    if (step) { e.preventDefault(); moveFocus(step); }
    else if (e.key === 'PageDown') { e.preventDefault(); shiftMonth(1, `[data-d="${focusDay.d}"]`); }
    else if (e.key === 'PageUp') { e.preventDefault(); shiftMonth(-1, `[data-d="${focusDay.d}"]`); }
  }

  /* ───────── Global wiring (event delegation: works for modals that re-render their HTML) ───────── */
  document.addEventListener('click', e => {
    const btn = e.target.closest('.date-btn');
    if (btn) {
      const el = btn.parentElement.querySelector('.date-input');
      if (pop && input === el) close(); else open(el);
      return;
    }
    const el = e.target.closest('.date-input');
    if (el) { open(el); return; }
    if (pop && !pop.contains(e.target)) close();
  });

  document.addEventListener('keydown', e => {
    if (!pop) return;
    if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation(); // close only the picker, not the modal underneath
      const el = input;
      close();
      el?.focus();
    } else if (e.key === 'ArrowDown' && e.target === input) {
      e.preventDefault();
      pop.querySelector('.dp-day[tabindex="0"]')?.focus();
    }
  }, true);

  document.addEventListener('focusin', e => {
    if (pop && !pop.contains(e.target) && !e.target.closest('.date-field')) close();
  });

  document.addEventListener('input', e => {
    if (!pop || e.target !== input) return;
    const p = Jalali.parse(input.value);
    input.removeAttribute('aria-invalid');
    if (p) { view = { y: p.y, m: p.m }; focusDay = p; render(); }
  });

  document.addEventListener('change', e => {
    const el = e.target;
    if (!el.classList?.contains('date-input')) return;
    const value = Jalali.canonical(el.value);
    if (value === null) { el.setAttribute('aria-invalid', 'true'); el.title = `تاریخ معتبر نیست. نمونه: ${example()}`; }
    else { el.removeAttribute('aria-invalid'); el.removeAttribute('title'); el.value = fd(value); }
  });

  window.addEventListener('resize', position);
  document.addEventListener('scroll', e => { if (pop && !pop.contains(e.target)) position(); }, true);

  return { field, read, setValue, open, close, isOpen };
})();

const datePicker = DatePicker.field;
const readDate = DatePicker.read;
