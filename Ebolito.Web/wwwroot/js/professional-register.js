(() => {
  const startSection = document.querySelector('[data-start-section]');
  const completeSection = document.querySelector('[data-complete-section]');
  const startForm = document.querySelector('[data-start-form]');
  const completeForm = document.querySelector('[data-complete-form]');
  const destination = document.querySelector('[data-destination]');

  async function read(response) {
    const text = await response.text();
    let body = null;
    try { body = text ? JSON.parse(text) : null; } catch { body = text; }
    if (!response.ok) throw new Error(body?.error || body?.title || (typeof body === 'string' ? body : `Request failed (${response.status})`));
    return body;
  }

  startForm.addEventListener('submit', async event => {
    event.preventDefault();
    const status = startForm.querySelector('[data-start-status]');
    status.textContent = 'Sending verification code…';
    try {
      const result = await read(await fetch('/api/professional-registration/start', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          displayName: startForm.displayName.value.trim(),
          businessName: startForm.businessName.value.trim() || null,
          mobileNumber: startForm.mobileNumber.value.trim()
        })
      }));
      completeForm.challengeId.value = result.challengeId;
      destination.textContent = result.destinationHint || 'your mobile number';
      startSection.hidden = true;
      completeSection.hidden = false;
      completeForm.code.focus();
      status.textContent = '';
    } catch (error) { status.textContent = error.message; }
  });

  completeForm.addEventListener('submit', async event => {
    event.preventDefault();
    const status = completeForm.querySelector('[data-complete-status]');
    status.textContent = 'Verifying…';
    try {
      const result = await read(await fetch('/api/professional-registration/complete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ challengeId: completeForm.challengeId.value, code: completeForm.code.value.trim() })
      }));
      localStorage.setItem('ebolito.professionalId', result.professionalId);
      localStorage.setItem('ebolito.professionalSession', result.sessionToken);
      localStorage.setItem('ebolito.professionalSessionExpiresAt', result.sessionExpiresAt);
      status.textContent = 'Account created. Opening your profile setup…';
      window.location.assign('/manage.html?onboarding=1');
    } catch (error) { status.textContent = error.message; }
  });
})();
