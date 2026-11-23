/* Donut chart with the summary shown in the centre.
   Hovering or focusing a slice (or its legend row) swaps the centre to that slice; clicking it calls onSelect(key). */
const Donut = (() => {
  const R = 84, C = 2 * Math.PI * R, GAP = 3; // radius, circumference, gap between slices (in SVG units)

  /** segments: [{key,label,value,color}] */
  function html({ segments, total, centerLabel, subLabel }) {
    const sum = segments.reduce((s, x) => s + x.value, 0);
    const pct = v => (sum ? Math.round(v / sum * 100) : 0);
    const visible = segments.filter(s => s.value > 0);
    let offset = 0;
    const arcs = visible.map(s => {
      const len = s.value / sum * C;
      const drawn = visible.length > 1 ? Math.max(len - GAP, 1) : len;
      const arc = `<circle class="seg" data-key="${esc(s.key)}" cx="110" cy="110" r="${R}" stroke="${s.color}"
        stroke-dasharray="0 ${C}" data-dash="${drawn} ${C - drawn}" stroke-dashoffset="${-offset}" tabindex="0"
        role="button" aria-label="${esc(s.label)}: ${fa(s.value)} از ${fa(sum)}"/>`;
      offset += len;
      return arc;
    }).join('');
    const data = JSON.stringify({ total, centerLabel, subLabel, items: Object.fromEntries(segments.map(s => [s.key, { label: s.label, value: s.value, pct: pct(s.value) }])) });
    return `<div class="donut-wrap" data-donut='${esc(data)}'>
        <svg class="donut" viewBox="0 0 220 220" aria-hidden="false" role="group" aria-label="نمودار دونات وضعیت اقدامات">
          <circle class="track" cx="110" cy="110" r="${R}"/>${arcs}
        </svg>
        <div class="donut-center" aria-live="polite">
          <div class="donut-num">${fa(total)}</div><div class="donut-label">${esc(centerLabel)}</div><div class="donut-sub">${esc(subLabel)}</div>
        </div>
      </div>
      <div class="donut-legend">${segments.map(s => `<button type="button" class="legend-item" data-key="${esc(s.key)}">
        <i class="dot" style="background:${s.color}"></i><span>${esc(s.label)}</span><b>${fa(s.value)}</b><span class="pct">${fa(pct(s.value))}٪</span></button>`).join('')}</div>`;
  }

  /** Wire hover/focus/click behaviour and run the draw-in animation. */
  function bind(root, onSelect) {
    const wrap = root.querySelector('.donut-wrap');
    if (!wrap) return;
    const data = JSON.parse(wrap.dataset.donut);
    const svg = wrap.querySelector('.donut'), num = wrap.querySelector('.donut-num'),
          label = wrap.querySelector('.donut-label'), sub = wrap.querySelector('.donut-sub');

    const show = key => {
      const it = data.items[key];
      root.querySelectorAll('[data-key]').forEach(el => el.classList.toggle('active', el.dataset.key === key));
      svg.classList.toggle('has-active', !!it);
      if (it) { num.textContent = fa(it.value); label.textContent = it.label; sub.textContent = `${fa(it.pct)}٪ از کل`; }
      else { num.textContent = fa(data.total); label.textContent = data.centerLabel; sub.textContent = data.subLabel; }
    };
    root.addEventListener('mouseover', e => { const el = e.target.closest('[data-key]'); if (el) show(el.dataset.key); });
    root.addEventListener('focusin', e => { const el = e.target.closest('[data-key]'); if (el) show(el.dataset.key); });
    root.addEventListener('mouseleave', () => show(null));
    root.addEventListener('focusout', e => { if (!root.contains(e.relatedTarget)) show(null); });
    root.addEventListener('click', e => { const el = e.target.closest('[data-key]'); if (el && onSelect) onSelect(el.dataset.key); });
    root.addEventListener('keydown', e => {
      const el = e.target.closest('.seg');
      if (el && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); onSelect?.(el.dataset.key); }
    });

    requestAnimationFrame(() => requestAnimationFrame(() => {
      wrap.querySelectorAll('.seg').forEach(c => c.setAttribute('stroke-dasharray', c.dataset.dash));
    }));
  }

  return { html, bind };
})();
