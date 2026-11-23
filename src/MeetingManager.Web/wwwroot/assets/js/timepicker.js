/* 24-hour time picker (hour + minute columns): a popover anchored to the field.
   Markup:  TimePicker.field('id', value)
   Value:   TimePicker.read('id', 'برچسب')  -> 'HH:MM' | ''   (throws a readable Error if invalid)
   Typing is allowed too: Persian/Latin digits are normalised on change. */
const TimePicker = (() => {
  const svg = path => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${path}</svg>`;
  const ICON_CLOCK = svg('<circle cx="12" cy="12" r="8.5"/><path d="M12 7.3V12l3.2 2"/>');

  let pop = null;   // popover element while open
  let input = null; // input it is attached to

  const isOpen = () => !!pop;
  const pad = n => String(n).padStart(2, '0');
  const TIME_RE = /^([01]\d|2[0-3]):([0-5]\d)$/;
  const example = () => fa('10:30');

  /** Canonical ASCII 'HH:MM' form of typed text, '' for empty, null when invalid. */
  function canonical(text) {
    const t = Jalali.toEn(text).trim();
    if (!t) return '';
    const m = TIME_RE.exec(t);
    return m ? `${m[1]}:${m[2]}` : null;
  }

  function field(id, value = '') {
    return `<div class="time-field"><input id="${id}" class="time-input" type="text" inputmode="numeric" autocomplete="off"
      placeholder="${example()}" value="${esc(fa(value))}" aria-haspopup="dialog">
      <button type="button" class="time-btn" aria-label="انتخاب ساعت">${ICON_CLOCK}</button></div>`;
  }

  function read(idOrEl, label = 'ساعت') {
    const el = typeof idOrEl === 'string' ? $(idOrEl) : idOrEl;
    if (!el) return '';
    const value = canonical(el.value);
    if (value === null) {
      el.setAttribute('aria-invalid', 'true');
      el.focus();
      throw Error(`${label} معتبر نیست. نمونه‌ی درست: ${example()}`);
    }
    return value;
  }

  /** Programmatic set (value is a canonical 'HH:MM' or ''). Fires 'change' so dependants can react. */
  function setValue(idOrEl, value) {
    const el = typeof idOrEl === 'string' ? $(idOrEl) : idOrEl;
    if (!el) return;
    el.value = fa(value);
    el.removeAttribute('aria-invalid');
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }

  function open(el) {
    if (pop && input === el) return;
    close();
    input = el;
    pop = document.createElement('div');
    pop.className = 'tp';
    pop.setAttribute('role', 'dialog');
    pop.setAttribute('aria-label', 'انتخاب ساعت (۲۴ ساعته)');
    pop.addEventListener('click', onPopClick);
    document.body.appendChild(pop);
    render();
    position();
  }

  function close() {
    if (!pop) return;
    pop.remove();
    pop = input = null;
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

  function render() {
    if (!pop) return;
    const c = canonical(input.value);
    const [hh, mm] = c ? c.split(':').map(Number) : [null, null];
    const col = (count, sel, attr) => {
      let out = '';
      for (let v = 0; v < count; v++) {
        out += `<button type="button" class="tp-cell ${v === sel ? 'selected' : ''}" data-${attr}="${v}" tabindex="${v === sel ? 0 : -1}" aria-pressed="${v === sel}">${fa(pad(v))}</button>`;
      }
      return out;
    };
    pop.innerHTML = `
      <div class="tp-head">ساعت (۲۴ ساعته)</div>
      <div class="tp-cols">
        <div class="tp-col" data-col="h" role="listbox" aria-label="ساعت">${col(24, hh, 'h')}</div>
        <div class="tp-sep">:</div>
        <div class="tp-col" data-col="m" role="listbox" aria-label="دقیقه">${col(60, mm, 'm')}</div>
      </div>
      <div class="tp-foot"><button type="button" data-act="now">اکنون</button><button type="button" data-act="clear">پاک کردن</button></div>`;
    pop.querySelector('.tp-col[data-col=h] .selected')?.scrollIntoView({ block: 'center' });
    pop.querySelector('.tp-col[data-col=m] .selected')?.scrollIntoView({ block: 'center' });
  }

  function choose(hh, mm) {
    const c = canonical(input.value);
    const [curH, curM] = c ? c.split(':').map(Number) : [0, 0];
    setValue(input, `${pad(hh ?? curH)}:${pad(mm ?? curM)}`);
    render();
  }

  function onPopClick(e) {
    const btn = e.target.closest('button');
    if (!btn) return;
    if (btn.dataset.h !== undefined) return choose(Number(btn.dataset.h), null);
    if (btn.dataset.m !== undefined) return choose(null, Number(btn.dataset.m));
    const act = btn.dataset.act;
    if (act === 'now') {
      const d = new Date(), el = input;
      choose(d.getHours(), d.getMinutes());
      close();
      el.focus();
    } else if (act === 'clear') { const el = input; close(); setValue(el, ''); el.focus(); }
  }

  /* ───────── Global wiring (event delegation: works for modals that re-render their HTML) ───────── */
  document.addEventListener('click', e => {
    const btn = e.target.closest('.time-btn');
    if (btn) {
      const el = btn.parentElement.querySelector('.time-input');
      if (pop && input === el) close(); else open(el);
      return;
    }
    const el = e.target.closest('.time-input');
    if (el) { open(el); return; }
    // Picking an hour/minute re-renders the popover's innerHTML inside its own click handler
    // (which runs before this one, since it's bubbling from a descendant), detaching e.target
    // from the tree; composedPath() still reflects the path at dispatch time, pop.contains() would not.
    if (pop && !e.composedPath().includes(pop)) close();
  });

  document.addEventListener('keydown', e => {
    if (!pop) return;
    if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation(); // close only the picker, not the modal underneath
      const el = input;
      close();
      el?.focus();
    }
  }, true);

  document.addEventListener('focusin', e => {
    if (pop && !pop.contains(e.target) && !e.target.closest('.time-field')) close();
  });

  document.addEventListener('input', e => {
    if (!pop || e.target !== input) return;
    if (canonical(input.value) !== null) render();
  });

  document.addEventListener('change', e => {
    const el = e.target;
    if (!el.classList?.contains('time-input')) return;
    const value = canonical(el.value);
    if (value === null) { el.setAttribute('aria-invalid', 'true'); el.title = `ساعت معتبر نیست. نمونه: ${example()}`; }
    else { el.removeAttribute('aria-invalid'); el.removeAttribute('title'); el.value = fa(value); }
  });

  window.addEventListener('resize', position);
  document.addEventListener('scroll', e => { if (pop && !pop.contains(e.target)) position(); }, true);

  return { field, read, setValue, open, close, isOpen };
})();

const timePicker = TimePicker.field;
const readTime = TimePicker.read;
