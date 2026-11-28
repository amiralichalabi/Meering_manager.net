/* Events: interviews, hand-overs, visits... (scheduled, but not a meeting: no minutes, no actions). */
const EVENT_KINDS = ['مصاحبه', 'تحویل', 'بازدید', 'یادآوری', 'سایر'];

function eventRows(xs) {
  const can = has('meetings.manage');
  return xs.map((e, i) => `<tr><td>${fa(i + 1)}</td><td><b>${esc(e.title)}</b><br><span class="muted">${esc(e.place)}</span></td><td>${esc(e.kind)}</td><td>${fd(e.date)}</td>
    <td>${e.start_time ? fa(e.start_time) + (e.end_time ? ' – ' + fa(e.end_time) : '') : 'تمام‌روز'}</td><td>${esc(e.attendees.join('، '))}</td><td>${esc(e.project)}</td>
    <td>${can ? `<button class="btn secondary" onclick="openEvent(${e.id})">ویرایش</button>` : ''}</td></tr>`).join('')
    || '<tr><td colspan="8" class="empty">رویدادی پیدا نشد.</td></tr>';
}

async function renderEvents() {
  try { state.events = await api('/api/events'); } catch (e) { state.events = []; }
  CalEvents.invalidate();
  $('events').innerHTML = `<div class="section">
    <div class="searchbar"><input class="search" id="eventSearch" placeholder="جستجوی رویداد، نوع، فرد یا محل" oninput="filterEvents()"><button class="btn secondary" onclick="clearSearch('eventSearch');filterEvents()">پاک کردن</button></div>
    <div class="sectionhead"><div><h3>رویدادها</h3><span class="muted">مصاحبه، تحویل، بازدید و هر چیزی که جلسه نیست ولی زمان دارد.</span></div></div>
    <div class="tablewrap"><table class="table"><thead><tr><th>ردیف</th><th>رویداد</th><th>نوع</th><th>تاریخ</th><th>ساعت</th><th>افراد</th><th>پروژه</th><th></th></tr></thead>
      <tbody id="eventRows">${eventRows(state.events)}</tbody></table></div></div>`;
}

function filterEvents() {
  const q = $('eventSearch').value.trim().toLowerCase();
  $('eventRows').innerHTML = eventRows(state.events.filter(e => [e.title, e.kind, e.place, e.date, fd(e.date), e.project, e.attendees.join(' ')].join(' ').toLowerCase().includes(q)));
}

function eventAttendeePick() { // reuses the meeting attendee editor widgets
  return attendeeEditor([]);
}

async function openEvent(id, prefill = {}) {
  const e = id ? (state.events || []).find(x => x.id === id) : null;
  const v = e || {};
  modal(mh(id ? 'ویرایش رویداد' : 'رویداد جدید', `
    <div class="grid">
      <div class="field"><label for="evt">عنوان</label><input id="evt" value="${esc(v.title ?? prefill.title ?? '')}"></div>
      <div class="field"><label for="evk">نوع</label><select id="evk">${EVENT_KINDS.map(k => `<option ${((v.kind || prefill.kind || 'مصاحبه') === k) ? 'selected' : ''}>${k}</option>`).join('')}</select></div>
      <div class="field"><label for="evd">تاریخ</label>${datePicker('evd', v.date || prefill.date || Jalali.format(Jalali.today()))}</div>
      <div class="field"><label for="evs">ساعت شروع (خالی = تمام‌روز)</label>${timePicker('evs', v.start_time || '')}</div>
      <div class="field"><label for="eve">ساعت پایان</label>${timePicker('eve', v.end_time || '')}</div>
      <div class="field"><label for="evp">محل یا لینک</label><input id="evp" list="placeList" value="${esc(v.place || '')}"><datalist id="placeList">${(state.locations || []).map(l => `<option value="${esc(l.name)}">`).join('')}</datalist></div>
      <div class="field"><label for="evproj">پروژه</label><select id="evproj">${projectOpts(v.project_id)}</select></div>
      <div class="field full"><label for="evdesc">توضیحات</label><textarea id="evdesc">${esc(v.description || '')}</textarea></div>
      <div class="field full"><label>افراد مرتبط</label>${attendeeEditor(v.attendee_ids || [])}</div>
    </div>
    <div id="evConflicts"></div>
    <div class="actionsbar"><button class="btn" onclick="saveEvent(${id || 0})">ذخیره رویداد</button><button class="btn secondary" onclick="closeModal()">انصراف</button>
      ${id ? `<button class="btn danger" onclick="deleteEvent(${id})">حذف</button>` : ''}</div>`));
  renderAttendeeList(v.attendee_ids || []);
}

function readEventForm() {
  return {
    title: $('evt').value, kind: $('evk').value, event_date: readDate('evd', 'تاریخ رویداد'), start_time: readTime('evs', 'ساعت شروع'), end_time: readTime('eve', 'ساعت پایان'),
    place: $('evp').value, description: $('evdesc').value, project_id: Number($('evproj').value) || null, attendee_ids: ids($('attendees').value),
  };
}

function conflictText(c) {
  return `${c.type === 'meeting' ? 'جلسه' : 'رویداد'} «${c.title}»${c.time ? ' ساعت ' + fa(c.time) : ''} · افراد: ${c.people.join('، ')}`;
}

async function saveEvent(id) {
  try {
    const body = readEventForm();
    const chk = await api('/api/events/check', { method: 'POST', body: JSON.stringify({ ...body, id: id || null }) });
    if (chk.conflicts.length) {
      const msg = 'تداخل زمانی:\n' + chk.conflicts.map(conflictText).join('\n') + '\n\nبا این وجود ذخیره شود؟';
      $('evConflicts').innerHTML = `<div class="banner" role="alert">${esc(msg).replace(/\n/g, '<br>')}</div>`;
      if (!confirm(msg)) return;
    }
    await api(id ? `/api/events/${id}` : '/api/events', { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) });
    await refresh();
    closeModal();
    toast('رویداد ذخیره شد.');
    await afterSave('events');
  } catch (e) { toast(e.message); }
}

async function deleteEvent(id) {
  if (!confirm('این رویداد حذف شود؟')) return;
  try { await api(`/api/events/${id}`, { method: 'DELETE' }); await refresh(); closeModal(); toast('رویداد حذف شد.'); await afterSave('events'); } catch (e) { toast(e.message); }
}
