/* Meeting from voice: audio (or a pasted transcript) -> local speech server + local model -> a draft that opens in the normal
   meeting form. Nothing is saved until the user reviews and presses "Save". */
let voiceRecorder = null, voiceChunks = [], voiceBlob = null;

function openVoiceDraft() {
  voiceBlob = null; voiceRecorder = null;
  const canRecord = !!(navigator.mediaDevices && window.isSecureContext && window.MediaRecorder);
  modal(mh('جلسه از روی صدا', `
    <p class="muted">صدای جلسه را بدهید تا پیش‌نویس صورت‌جلسه (خلاصه، مصوبات و اقدام‌ها) ساخته شود. صدا فقط روی سامانه‌های خود سازمان پردازش می‌شود و ذخیره نمی‌شود. پیش‌نویس را قبل از ذخیره بازبینی کنید.</p>
    <div class="grid">
      <div class="field full"><label for="voiceFile">فایل صوتی جلسه</label><input id="voiceFile" type="file" accept="audio/*,video/*,.m4a,.mp3,.wav,.ogg,.webm,.aac"></div>
      <div class="field full"><div class="toolbar">
        ${canRecord ? '<button type="button" class="btn secondary" id="voiceRec" onclick="toggleVoiceRecording()">ضبط با میکروفون</button>' : '<span class="muted">ضبط مستقیم فقط روی https یا همین رایانه کار می‌کند؛ فایل ضبط‌شده را انتخاب کنید.</span>'}
        <span id="voiceRecInfo" class="muted"></span></div></div>
      <div class="field full"><label for="voiceText">یا متن پیاده‌شده‌ی جلسه را اینجا بچسبانید</label><textarea id="voiceText" rows="5"></textarea></div>
    </div>
    <p id="voiceStatus" class="muted" role="status"></p>
    <div class="actionsbar"><button class="btn" id="voiceGo" onclick="runVoiceDraft()">ساخت پیش‌نویس</button><button class="btn secondary" onclick="closeModal()">انصراف</button></div>
    ${state.user?.role === 'admin' ? `<details class="section"><summary>تنظیم سرور تبدیل گفتار به متن (مدیر)</summary>
      <div class="grid"><div class="field"><label for="sttUrl">آدرس سرور</label><input id="sttUrl" placeholder="http://127.0.0.1:8000" dir="ltr"></div>
      <div class="field"><label for="sttModel">مدل (اختیاری)</label><input id="sttModel" dir="ltr"></div></div>
      <button type="button" class="btn secondary" onclick="saveSttConfig()">ذخیره تنظیم</button></details>` : ''}`));
  if (state.user?.role === 'admin') api('/api/voice/config').then(c => { $('sttUrl').value = c.url || ''; $('sttModel').value = c.model || ''; }).catch(() => {});
}

async function saveSttConfig() {
  try { await api('/api/voice/config', { method: 'PUT', body: JSON.stringify({ url: $('sttUrl').value, model: $('sttModel').value, timeout: '' }) }); toast('تنظیم ذخیره شد.'); }
  catch (e) { toast(e.message); }
}

async function toggleVoiceRecording() {
  const btn = $('voiceRec');
  if (voiceRecorder) { voiceRecorder.stop(); return; }
  try {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    voiceChunks = [];
    voiceRecorder = new MediaRecorder(stream);
    voiceRecorder.ondataavailable = e => { if (e.data.size) voiceChunks.push(e.data); };
    voiceRecorder.onstop = () => {
      stream.getTracks().forEach(t => t.stop());
      voiceBlob = new Blob(voiceChunks, { type: voiceRecorder.mimeType || 'audio/webm' });
      voiceRecorder = null; btn.textContent = 'ضبط دوباره';
      $('voiceRecInfo').textContent = 'ضبط آماده است (' + fa(Math.max(1, Math.round(voiceBlob.size / 1024))) + ' کیلوبایت).';
    };
    voiceRecorder.start();
    btn.textContent = 'پایان ضبط'; $('voiceRecInfo').textContent = 'در حال ضبط…';
  } catch (e) { toast('دسترسی به میکروفون ممکن نشد.'); }
}

async function runVoiceDraft() {
  const status = $('voiceStatus'), go = $('voiceGo');
  const file = $('voiceFile').files[0], text = $('voiceText').value.trim();
  const blob = voiceBlob || file;
  if (!blob && !text) { toast('فایل صوتی را انتخاب کنید یا متن جلسه را بچسبانید.'); return; }
  go.disabled = true;
  status.textContent = blob ? 'در حال تبدیل صدا به متن و ساخت پیش‌نویس… برای جلسه‌های طولانی چند دقیقه طول می‌کشد، این پنجره را باز نگه دارید.' : 'در حال ساخت پیش‌نویس…';
  try {
    const draft = blob
      ? await api('/api/meetings/voice-draft', { method: 'POST', headers: { 'Content-Type': blob.type || 'application/octet-stream', 'X-Filename': encodeURIComponent(file?.name || 'meeting.webm') }, body: blob })
      : await api('/api/meetings/text-draft', { method: 'POST', body: JSON.stringify({ transcript: text }) });
    closeModal();
    await openMeeting(0, draft);
    toast('پیش‌نویس ساخته شد؛ آن را بازبینی و سپس ذخیره کنید.');
  } catch (e) { status.textContent = e.message; go.disabled = false; }
}
