(() => {
  const startForm = document.querySelector('[data-start-form]');
  const completeForm = document.querySelector('[data-complete-form]');
  const startStatus = document.querySelector('[data-start-status]');
  const completeStatus = document.querySelector('[data-complete-status]');

  async function readResponse(response) {
    const text = await response.text();
    let body = null;
    try { body = text ? JSON.parse(text) : null; } catch { body = text; }
    if (!response.ok) throw new Error(body?.error || body?.title || (typeof body === 'string' ? body : `Request failed (${response.status})`));
    return body;
  }

  startForm.addEventListener('submit', async event => {
    event.preventDefault();
    startStatus.textContent = 'Sending verification code…';
    completeStatus.textContent = '';
    try {
      const professionalId = startForm.professionalId.value.trim();
      const result = await readResponse(await fetch('/api/professional-auth/start', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ professionalId })
      }));
      localStorage.setItem('ebolito.professionalId', professionalId);
      completeForm.challengeId.value = result.challengeId;
      completeForm.hidden = false;
      startStatus.textContent = `Verification code sent to ${result.destinationHint}.`;
      completeForm.code.focus();
    } catch (error) { startStatus.textContent = error.message; }
  });

  completeForm.addEventListener('submit', async event => {
    event.preventDefault();
    completeStatus.textContent = 'Signing in…';
    try {
      const result = await readResponse(await fetch('/api/professional-auth/complete', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ challengeId: completeForm.challengeId.value, code: completeForm.code.value.trim() })
      }));
      localStorage.setItem('ebolito.professionalId', result.professionalId);
      localStorage.setItem('ebolito.professionalSession', result.token);
      localStorage.setItem('ebolito.professionalSessionExpiresAt', result.expiresAt);
      completeStatus.textContent = 'Signed in. Opening your inbox…';
      window.location.href = '/inbox.html';
    } catch (error) { completeStatus.textContent = error.message; }
  });

  const remembered = localStorage.getItem('ebolito.professionalId');
  if (remembered) startForm.professionalId.value = remembered;
})();
