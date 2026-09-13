const state = { professionals: [] };

async function loadSkills() {
  const select = document.querySelector('[data-service]');
  if (!select) return;
  const response = await fetch('/api/skills');
  if (!response.ok) return;
  const skills = await response.json();
  select.innerHTML = '<option value="">-- Select a Service --</option>' +
    skills.map(x => `<option value="${escapeHtml(x.name)}">${escapeHtml(x.name)}</option>`).join('');
}

async function searchProfessionals(event) {
  event?.preventDefault();
  const service = document.querySelector('[data-service]')?.value || '';
  const location = document.querySelector('[data-location]')?.value || '';
  const params = new URLSearchParams();
  if (service) params.set('service', service);
  if (location) params.set('location', location);
  const response = await fetch(`/api/professionals?${params}`);
  if (!response.ok) return;
  state.professionals = await response.json();
  renderResults();
}

function renderResults() {
  const host = document.querySelector('[data-results]');
  if (!host) return;
  if (!state.professionals.length) {
    host.innerHTML = '<p>No professionals matched that search yet.</p>';
    return;
  }

  host.innerHTML = state.professionals.map(p => `
    <article class="result-card">
      <div>
        <h3>${escapeHtml(p.displayName)} ${p.isScreened ? '<span title="Screened">✓</span>' : ''}</h3>
        <p>${escapeHtml(p.headline)}</p>
        <small>${escapeHtml(p.location)} · ${p.reviewCount ? `${p.rating} ★ (${p.reviewCount})` : 'New professional'}</small>
      </div>
      <div class="result-actions">
        <button type="button" onclick="showProfile('${encodeURIComponent(p.slug)}')">View work</button>
        <button type="button" onclick="openEngagement('${p.id}', '${escapeAttribute(p.displayName)}')">Request</button>
      </div>
    </article>`).join('');
}

async function showProfile(slug) {
  const response = await fetch(`/api/professionals/${slug}`);
  if (!response.ok) return;
  const profile = await response.json();
  const host = document.querySelector('[data-profile]');
  if (!host) return;
  host.hidden = false;
  host.innerHTML = `
    <button class="profile-close" type="button" onclick="this.parentElement.hidden=true">×</button>
    <h2>${escapeHtml(profile.professional.displayName)}</h2>
    <h3>${escapeHtml(profile.professional.headline)}</h3>
    <p>${escapeHtml(profile.professional.about)}</p>
    <p><b>Services:</b> ${profile.skills.map(x => escapeHtml(x.name)).join(', ')}</p>
    <p><b>Areas:</b> ${profile.professional.serviceAreas.map(x => escapeHtml(x.community ? `${x.community}, ${x.parish}` : x.parish)).join(' · ')}</p>
    <h3>Selected work</h3>
    <div class="project-grid">${profile.projects.map(project => `<article><h4>${escapeHtml(project.title)}</h4><p>${escapeHtml(project.description)}</p><small>${escapeHtml(project.location)}</small></article>`).join('') || '<p>No portfolio projects yet.</p>'}</div>
    <h3>Reviews ${profile.rating ? `· ${profile.rating} ★` : ''}</h3>
    ${profile.reviews.map(review => `<blockquote><b>${'★'.repeat(review.rating)}</b> ${escapeHtml(review.comment)}<footer>${escapeHtml(review.customerDisplayName)}${review.verifiedEngagement ? ' · Verified engagement' : ''}</footer></blockquote>`).join('') || '<p>No reviews yet.</p>'}
    <button type="button" onclick="openEngagement('${profile.professional.id}', '${escapeAttribute(profile.professional.displayName)}')">Request this professional</button>`;
  host.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

function openEngagement(professionalId, professionalName) {
  const dialog = document.querySelector('[data-engagement-dialog]');
  if (!dialog) return;
  dialog.querySelector('[name=professionalId]').value = professionalId;
  dialog.querySelector('[data-professional-name]').textContent = professionalName;
  dialog.showModal();
}

async function submitEngagement(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const status = form.querySelector('[data-engagement-status]');
  status.textContent = 'Sending…';

  const body = {
    professionalId: form.professionalId.value,
    customerId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    skillId: null,
    requestText: form.requestText.value,
    location: form.location.value,
    preferredChannel: Number(form.preferredChannel.value)
  };

  const response = await fetch('/api/engagements', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  const result = await response.json();
  if (!response.ok) {
    status.textContent = result.error || 'Unable to send request.';
    return;
  }
  status.textContent = `Request sent. Delivery: ${['WhatsApp','SMS','Web'][result.deliveredChannel ?? result.requestedChannel]}.`;
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"]/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[ch]));
}
function escapeAttribute(value) { return escapeHtml(value).replace(/'/g, '&#39;'); }

document.addEventListener('DOMContentLoaded', () => {
  loadSkills();
  document.querySelector('[data-search-form]')?.addEventListener('submit', searchProfessionals);
  document.querySelector('[data-engagement-form]')?.addEventListener('submit', submitEngagement);
  searchProfessionals();
});
