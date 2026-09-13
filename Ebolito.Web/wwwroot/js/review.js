const params = new URLSearchParams(window.location.search);
const engagementId = params.get('engagement');
const customerId = localStorage.getItem('ebolito.customerId');
const sessionToken = localStorage.getItem('ebolito.customerSessionToken');

function setSummary(html) {
  const host = document.querySelector('[data-engagement-summary]');
  if (host) host.innerHTML = html;
}

async function loadEngagement() {
  const form = document.querySelector('[data-review-form]');
  if (!engagementId) {
    setSummary('This review link is missing an engagement ID.');
    return;
  }
  if (!customerId || !sessionToken) {
    setSummary('This browser does not have a current verified Ebolito customer session. Return to Ebolito and verify the mobile number used for the engagement before reviewing it.');
    return;
  }

  const response = await fetch(`/api/engagements/${encodeURIComponent(engagementId)}`, {
    headers: { 'X-Ebolito-Customer-Session': sessionToken }
  });
  const engagement = await readJson(response);
  if (!response.ok) {
    setSummary(response.status === 401 ? 'Your verified customer session is not authorized for this engagement.' : 'The engagement could not be found.');
    return;
  }

  const statusName = ['Requested','Delivered','Accepted','Declined','Contacted','Hired','Completed','Reviewed'][engagement.status] || String(engagement.status);
  setSummary(`<b>Status:</b> ${escapeHtml(statusName)}<br><b>Request:</b> ${escapeHtml(engagement.requestText)}<br><b>Location:</b> ${escapeHtml(engagement.location)}`);

  if (engagement.customerId?.toLowerCase() !== customerId.toLowerCase()) {
    setSummary('This engagement belongs to a different verified customer identity.');
    return;
  }
  if (statusName === 'Reviewed') {
    setSummary('This completed engagement has already been reviewed.');
    return;
  }
  if (statusName !== 'Completed') {
    setSummary(`This engagement is currently ${escapeHtml(statusName)}. Reviews become available after the work is marked completed.`);
    return;
  }

  form.engagementId.value = engagementId;
  form.hidden = false;
}

async function submitReview(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const status = form.querySelector('[data-review-status]');
  status.textContent = 'Submitting review…';

  const response = await fetch('/api/reviews', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Ebolito-Customer-Session': sessionToken
    },
    body: JSON.stringify({
      engagementId: form.engagementId.value,
      customerId,
      rating: Number(form.rating.value),
      comment: form.comment.value
    })
  });
  const result = await readJson(response);
  if (!response.ok) {
    status.textContent = response.status === 401
      ? 'Your verified customer session is no longer valid. Verify your mobile number again from Ebolito.'
      : (result.error || result.detail || 'Unable to submit review.');
    return;
  }

  status.textContent = 'Thank you. Your verified review has been published.';
  form.querySelectorAll('input,select,textarea,button').forEach(x => x.disabled = true);
  setSummary('Review submitted successfully. This engagement is now marked Reviewed.');
}

async function readJson(response) {
  try { return await response.json(); } catch { return {}; }
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>\"]/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;'}[ch]));
}

document.addEventListener('DOMContentLoaded', () => {
  document.querySelector('[data-review-form]')?.addEventListener('submit', submitReview);
  loadEngagement();
});
