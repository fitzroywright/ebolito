(() => {
  const statusHost = document.querySelector('[data-status]');
  const listHost = document.querySelector('[data-list]');
  const statusNames = ['Requested','Delivered','Accepted','Declined','Contacted','Hired','Completed','Reviewed'];
  const channelNames = ['WhatsApp','SMS','Ebolito','Email','Slack','Microsoft Teams','Push','Webhook','Messenger','Instagram'];

  function sessionToken() { return localStorage.getItem('ebolito.customerSessionToken'); }
  async function readResponse(response) {
    const text = await response.text();
    let body = null;
    try { body = text ? JSON.parse(text) : null; } catch { body = text; }
    if (!response.ok) throw new Error(response.status === 401 ? 'Your verified customer session has expired. Return to the marketplace and verify your mobile number again.' : (body?.error || body?.title || (typeof body === 'string' ? body : `Request failed (${response.status})`)));
    return body;
  }
  function escapeHtml(value) { return String(value ?? '').replace(/[&<>\"]/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;'}[ch])); }
  function formatDate(value) { try { return new Date(value).toLocaleString(); } catch { return value || ''; } }
  function statusName(value) { return statusNames[value] || String(value); }
  function channelName(value) { return channelNames[value] || String(value ?? 'Unknown'); }

  function render(data) {
    const items = data.engagements || [];
    if (!items.length) { listHost.innerHTML = '<div class="empty">You have not sent any Ebolito engagement requests yet.</div>'; return; }
    listHost.innerHTML = items.map(item => {
      const e = item.engagement;
      const pro = item.professional || {};
      const name = statusName(e.status);
      const profile = pro.slug ? `<a href="/?profile=${encodeURIComponent(pro.slug)}">View professional</a>` : '';
      const review = item.reviewUrl ? `<a href="${item.reviewUrl}">Leave verified review</a>` : (item.reviewSubmitted ? '<span>Review submitted</span>' : '');
      const contact = name === 'Accepted' || name === 'Contacted' || name === 'Hired' || name === 'Completed' || name === 'Reviewed'
        ? [pro.phoneNumber, pro.whatsAppNumber].filter(Boolean).map(x => `<span>${escapeHtml(x)}</span>`).join('') : '';
      return `<article class="card"><h2>${escapeHtml(pro.displayName || 'Professional')} <span class="status-pill">${escapeHtml(name)}</span></h2><div class="meta"><span>${escapeHtml(e.location)}</span><span>Updated ${escapeHtml(formatDate(e.updatedAt))}</span><span>Delivered via ${escapeHtml(channelName(e.deliveredChannel))}</span></div><p>${escapeHtml(e.requestText)}</p><div class="meta">${contact}</div><div class="actions">${profile}${review}</div></article>`;
    }).join('');
  }

  async function load() {
    const token = sessionToken();
    if (!token) {
      statusHost.textContent = 'No verified customer session is stored in this browser. Return to the marketplace and verify your mobile number.';
      listHost.innerHTML = '<div class="empty"><a href="/">Return to marketplace</a></div>';
      return;
    }
    statusHost.textContent = 'Loading…';
    try {
      const data = await readResponse(await fetch('/api/customer/engagements', { headers: { 'X-Ebolito-Customer-Session': token } }));
      statusHost.textContent = `Signed in as ${data.customer.displayName}.`;
      render(data);
    } catch (error) { statusHost.textContent = error.message; listHost.innerHTML = '<div class="empty">Unable to load your engagements.</div>'; }
  }

  document.querySelector('[data-refresh]').addEventListener('click', load);
  load();
})();
