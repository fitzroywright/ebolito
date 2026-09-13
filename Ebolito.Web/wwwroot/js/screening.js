(() => {
  const adminKey = document.querySelector('[data-admin-key]');
  const filter = document.querySelector('[data-filter]');
  const status = document.querySelector('[data-status]');
  const list = document.querySelector('[data-list]');

  function headers(json = false) {
    const result = { 'X-Ebolito-Profile-Admin-Key': adminKey.value.trim() };
    if (json) result['Content-Type'] = 'application/json';
    return result;
  }

  async function read(response) {
    const text = await response.text();
    let body = null;
    try { body = text ? JSON.parse(text) : null; } catch { body = text; }
    if (!response.ok) throw new Error(response.status === 401 ? 'Administration key is missing or incorrect.' : (body?.error || body?.title || (typeof body === 'string' ? body : `Request failed (${response.status})`)));
    return body;
  }

  function escapeHtml(value) { return String(value ?? '').replace(/[&<>\"]/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;'}[ch])); }

  function render(items) {
    if (!items.length) { list.innerHTML = '<div class="card">No professionals matched this view.</div>'; return; }
    list.innerHTML = items.map(p => {
      const state = !p.isActive ? 'Inactive' : p.isScreened ? 'Screened' : 'Pending';
      const areas = (p.serviceAreas || []).map(x => x.community ? `${x.community}, ${x.parish}` : x.parish).join(' · ') || 'No service areas yet';
      const approve = !p.isScreened && p.isActive ? `<button type="button" data-action="approve" data-id="${p.id}">Approve</button>` : '';
      const unscreen = p.isScreened ? `<button type="button" data-action="unscreen" data-id="${p.id}">Remove screening</button>` : '';
      const active = p.isActive ? `<button type="button" data-action="deactivate" data-id="${p.id}">Deactivate</button>` : `<button type="button" data-action="reactivate" data-id="${p.id}">Reactivate</button>`;
      return `<article class="card"><h2>${escapeHtml(p.displayName)} <span class="badge">${state}</span></h2><div class="meta"><span>${escapeHtml(p.businessName || 'Independent professional')}</span><span>${escapeHtml(p.phoneNumber || 'No phone')}</span><span>${escapeHtml(areas)}</span></div><p><strong>${escapeHtml(p.headline || 'Profile incomplete')}</strong></p><p>${escapeHtml(p.about || '')}</p><p class="skills">Skill IDs: ${escapeHtml((p.skillIds || []).join(', ') || 'none selected')}</p><div class="actions"><a href="/manage.html?professionalId=${encodeURIComponent(p.id)}">Open profile</a>${approve}${unscreen}${active}</div></article>`;
    }).join('');
    list.querySelectorAll('button[data-action]').forEach(button => button.addEventListener('click', () => apply(button.dataset.id, button.dataset.action)));
  }

  async function load() {
    status.textContent = 'Loading…';
    try {
      if (!adminKey.value.trim()) throw new Error('Administration key is required.');
      const items = await read(await fetch(`/api/admin/screening/professionals?status=${encodeURIComponent(filter.value)}`, { headers: headers() }));
      render(items);
      status.textContent = `${items.length} professional${items.length === 1 ? '' : 's'} loaded.`;
    } catch (error) { status.textContent = error.message; }
  }

  async function apply(id, action) {
    const labels = { approve: 'approve', unscreen: 'remove screening from', deactivate: 'deactivate', reactivate: 'reactivate' };
    if (!confirm(`Are you sure you want to ${labels[action]} this professional?`)) return;
    const payload = action === 'approve' ? { isScreened: true } : action === 'unscreen' ? { isScreened: false } : action === 'deactivate' ? { isActive: false } : { isActive: true };
    status.textContent = 'Saving decision…';
    try {
      await read(await fetch(`/api/admin/screening/professionals/${encodeURIComponent(id)}`, { method: 'POST', headers: headers(true), body: JSON.stringify(payload) }));
      await load();
    } catch (error) { status.textContent = error.message; }
  }

  document.querySelector('[data-load]').addEventListener('click', load);
  filter.addEventListener('change', () => { if (adminKey.value.trim()) load(); });
})();
