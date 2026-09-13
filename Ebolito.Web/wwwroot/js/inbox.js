(() => {
  const professionalId = document.querySelector('[data-professional-id]');
  const adminKey = document.querySelector('[data-admin-key]');
  const statusFilter = document.querySelector('[data-status-filter]');
  const globalStatus = document.querySelector('[data-global-status]');
  const inboxTitle = document.querySelector('[data-inbox-title]');
  const inboxList = document.querySelector('[data-inbox-list]');
  const statusNames = ['Requested','Delivered','Accepted','Declined','Contacted','Hired','Completed','Reviewed'];
  const channelNames = ['WhatsApp','SMS','Ebolito','Email','Slack','Microsoft Teams','Push','Webhook','Messenger','Instagram'];
  let items = [];

  function headers(json = false) {
    const value = {};
    const session = localStorage.getItem('ebolito.professionalSession');
    if (session) value['X-Ebolito-Professional-Session'] = session;
    else if (adminKey.value.trim()) value['X-Ebolito-Profile-Admin-Key'] = adminKey.value.trim();
    if (json) value['Content-Type'] = 'application/json';
    return value;
  }

  async function readResponse(response) {
    const text = await response.text();
    let body = null;
    try { body = text ? JSON.parse(text) : null; } catch { body = text; }
    if (!response.ok) {
      if (response.status === 401 && localStorage.getItem('ebolito.professionalSession')) throw new Error('Your professional session is missing, expired, or does not match this professional. Sign in again.');
      throw new Error(body?.error || body?.title || (typeof body === 'string' ? body : `Request failed (${response.status})`));
    }
    return body;
  }

  function statusName(value) { return statusNames[value] || String(value); }
  function channelName(value) { return channelNames[value] || String(value ?? 'Unknown'); }
  function formatDate(value) { try { return new Date(value).toLocaleString(); } catch { return value || ''; } }
  function escapeHtml(value) { return String(value ?? '').replace(/[&<>\"]/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;'}[ch])); }

  function render() {
    const filter = statusFilter.value;
    const visible = items.filter(x => !filter || statusName(x.engagement.status) === filter);
    if (!visible.length) { inboxList.innerHTML = '<div class="empty">No engagements match this filter.</div>'; return; }
    inboxList.innerHTML = visible.map(item => {
      const e = item.engagement; const customer = item.customer || {}; const name = statusName(e.status);
      const deliveries = (item.deliveries || []).map(d => `<li>${escapeHtml(channelName(d.channel))} · ${escapeHtml(formatDate(d.attemptedAt))}${d.succeeded ? '' : ' · failed'}</li>`).join('');
      const responseActions = name === 'Delivered' ? `<button data-response="0" data-id="${e.id}">Accept</button><button data-response="1" data-id="${e.id}">Decline</button>` : '';
      const progressActions = name === 'Accepted' ? `<button data-progress="0" data-id="${e.id}">Mark contacted</button><button data-progress="1" data-id="${e.id}">Mark hired</button>` : name === 'Contacted' ? `<button data-progress="1" data-id="${e.id}">Mark hired</button>` : name === 'Hired' ? `<button data-progress="2" data-id="${e.id}">Mark completed</button>` : '';
      const review = name === 'Completed' ? `<a href="/review.html?engagement=${encodeURIComponent(e.id)}">Customer review link</a>` : '';
      return `<article class="inbox-card"><h2>${escapeHtml(customer.displayName || 'Verified customer')} <span class="status-pill">${escapeHtml(name)}</span></h2><div class="inbox-meta"><span>${escapeHtml(e.location)}</span><span>Updated ${escapeHtml(formatDate(e.updatedAt))}</span><span>Delivered via ${escapeHtml(channelName(e.deliveredChannel))}</span></div><p>${escapeHtml(e.requestText)}</p><div class="inbox-meta"><span>${escapeHtml(customer.verifiedMobileNumber || '')}</span><span>${escapeHtml(customer.email || '')}</span></div>${deliveries ? `<details class="delivery-list"><summary>Delivery history</summary><ul>${deliveries}</ul></details>` : ''}<div class="inbox-actions">${responseActions}${progressActions}${review}</div></article>`;
    }).join('');
    inboxList.querySelectorAll('[data-response]').forEach(button => button.addEventListener('click', () => respond(button.dataset.id, Number(button.dataset.response))));
    inboxList.querySelectorAll('[data-progress]').forEach(button => button.addEventListener('click', () => progress(button.dataset.id, Number(button.dataset.progress))));
  }

  async function load() {
    globalStatus.textContent = 'Loading inbox…';
    try {
      const id = professionalId.value.trim();
      if (!id) throw new Error('Professional ID is required.');
      if (!localStorage.getItem('ebolito.professionalSession') && !adminKey.value.trim()) throw new Error('Sign in as the professional or provide the administration key.');
      const result = await readResponse(await fetch(`/api/admin/professionals/${encodeURIComponent(id)}/engagements`, { headers: headers() }));
      items = result.engagements || [];
      inboxTitle.textContent = `${result.professional.displayName} — Engagements`;
      globalStatus.textContent = `${items.length} engagement${items.length === 1 ? '' : 's'} loaded.`;
      render();
    } catch (error) { globalStatus.textContent = error.message; inboxList.innerHTML = '<div class="empty">Unable to load inbox.</div>'; }
  }

  async function respond(id, responseValue) {
    globalStatus.textContent = responseValue === 0 ? 'Accepting request…' : 'Declining request…';
    try { await readResponse(await fetch(`/api/engagements/${encodeURIComponent(id)}/response`, { method: 'POST', headers: headers(true), body: JSON.stringify(responseValue) })); await load(); }
    catch (error) { globalStatus.textContent = error.message; }
  }
  async function progress(id, action) {
    globalStatus.textContent = 'Updating engagement…';
    try { await readResponse(await fetch(`/api/admin/engagements/${encodeURIComponent(id)}/progress`, { method: 'POST', headers: headers(true), body: JSON.stringify({ action }) })); await load(); }
    catch (error) { globalStatus.textContent = error.message; }
  }

  const remembered = localStorage.getItem('ebolito.professionalId');
  if (remembered) professionalId.value = remembered;
  document.querySelector('[data-load]').addEventListener('click', load);
  statusFilter.addEventListener('change', render);
  if (remembered && localStorage.getItem('ebolito.professionalSession')) load();
})();
