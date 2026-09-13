(() => {
  const idInput = document.querySelector('[data-engagement-id]');
  const keyInput = document.querySelector('[data-admin-key]');
  const summary = document.querySelector('[data-summary]');
  const status = document.querySelector('[data-status]');
  const action = document.querySelector('[data-action]');
  const reviewWrap = document.querySelector('[data-review-wrap]');
  const reviewLink = document.querySelector('[data-review-link]');

  const statusNames = ['Requested','Delivered','Accepted','Declined','Contacted','Hired','Completed','Reviewed'];

  async function readJson(response) {
    const text = await response.text();
    let body = null;
    try { body = text ? JSON.parse(text) : null; } catch { body = text; }
    if (!response.ok) {
      const message = body?.error || body?.title || (typeof body === 'string' ? body : `Request failed (${response.status})`);
      throw new Error(message);
    }
    return body;
  }

  function renderEngagement(engagement) {
    const name = statusNames[engagement.status] || String(engagement.status);
    summary.textContent = `Status: ${name} | Request: ${engagement.requestText} | Location: ${engagement.location}`;
  }

  async function load() {
    status.textContent = 'Loading…';
    reviewWrap.hidden = true;
    try {
      const id = idInput.value.trim();
      if (!id) throw new Error('Engagement ID is required.');
      const response = await fetch(`/api/engagements/${encodeURIComponent(id)}`);
      const engagement = await readJson(response);
      renderEngagement(engagement);
      if ((statusNames[engagement.status] || '') === 'Completed') showReview(`/review.html?engagement=${encodeURIComponent(id)}`);
      status.textContent = 'Loaded.';
    } catch (error) {
      status.textContent = error.message;
    }
  }

  function showReview(url) {
    reviewLink.href = url;
    reviewLink.textContent = new URL(url, window.location.origin).href;
    reviewWrap.hidden = false;
  }

  async function advance() {
    status.textContent = 'Updating…';
    reviewWrap.hidden = true;
    try {
      const id = idInput.value.trim();
      const key = keyInput.value.trim();
      if (!id || !key) throw new Error('Engagement ID and administration key are required.');
      const response = await fetch(`/api/admin/engagements/${encodeURIComponent(id)}/progress`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Ebolito-Profile-Admin-Key': key
        },
        body: JSON.stringify({ action: Number(action.value) })
      });
      const result = await readJson(response);
      renderEngagement(result.engagement);
      if (result.reviewUrl) showReview(result.reviewUrl);
      status.textContent = 'Engagement updated.';
    } catch (error) {
      status.textContent = error.message;
    }
  }

  document.querySelector('[data-load]').addEventListener('click', load);
  document.querySelector('[data-advance]').addEventListener('click', advance);
})();
