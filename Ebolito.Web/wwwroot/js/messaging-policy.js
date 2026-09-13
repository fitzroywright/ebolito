(() => {
  const professionalId = document.querySelector('[data-professional-id]');
  const adminKey = document.querySelector('[data-admin-key]');
  const globalStatus = document.querySelector('[data-global-status]');
  const capabilitiesHost = document.querySelector('[data-capabilities]');
  const form = document.querySelector('[data-policy-form]');
  const policyStatus = document.querySelector('[data-policy-status]');

  const channels = [
    ['WhatsApp', 0], ['SMS', 1], ['Ebolito/Web', 2], ['Email', 3], ['Slack', 4],
    ['Microsoft Teams', 5], ['Push', 6], ['Webhook', 7], ['Messenger', 8], ['Instagram', 9]
  ];
  const externalChannels = channels.filter(x => x[1] !== 2);

  function headers(json = false) {
    const result = { 'X-Ebolito-Profile-Admin-Key': adminKey.value.trim() };
    if (json) result['Content-Type'] = 'application/json';
    return result;
  }

  async function readResponse(response) {
    const text = await response.text();
    let body = null;
    try { body = text ? JSON.parse(text) : null; } catch { body = text; }
    if (!response.ok) throw new Error(body?.error || body?.title || (typeof body === 'string' ? body : `Request failed (${response.status})`));
    return body;
  }

  function optionMarkup(includeBlank = false) {
    return (includeBlank ? '<option value="">None</option>' : '') + externalChannels.map(([name, value]) => `<option value="${value}">${name}</option>`).join('');
  }

  function channelLabel(value) {
    const found = channels.find(x => x[1] === Number(value));
    return found ? found[0] : String(value);
  }

  function channelValue(name) {
    const normalized = String(name || '').trim().toLowerCase();
    const found = channels.find(x => x[0].toLowerCase() === normalized || (normalized === 'teams' && x[1] === 5) || (normalized === 'sms' && x[1] === 1) || (normalized === 'web' && x[1] === 2));
    if (!found) throw new Error(`Unknown channel: ${name}`);
    return found[1];
  }

  function renderCapabilities(data) {
    const configured = data.configured || {};
    capabilitiesHost.innerHTML = Object.entries(configured).map(([name, enabled]) => `<div class="cap ${enabled ? 'on' : 'off'}"><strong>${name}</strong><br>${enabled ? 'Enabled' : 'Not enabled'}</div>`).join('');
    if (!data.commonMessagingCompiled) capabilitiesHost.innerHTML += '<div class="cap off"><strong>Common.Messaging</strong><br>Fallback adapter build</div>';
  }

  function setPolicy(policy) {
    form.primaryChannel.value = String(policy.primaryChannel);
    form.businessChannel.value = policy.businessChannel == null ? '' : String(policy.businessChannel);
    form.fallbackChannel.value = String(policy.fallbackChannel);
    form.escalationAfterMinutes.value = Math.max(1, Math.round((policy.escalationAfter?.totalMinutes ?? policy.escalationAfterMinutes ?? 10)));
    form.escalationOrder.value = (policy.escalationOrder || []).map(channelLabel).join(', ');
    form.endpoints.value = (policy.endpoints || []).map(x => `${channelLabel(x.channel)}|${x.address}|${x.label || ''}|${x.enabled !== false}`).join('\n');
  }

  function parseOrder(value) {
    return value.split(',').map(x => x.trim()).filter(Boolean).map(channelValue).filter(x => x !== 2);
  }

  function parseEndpoints(value) {
    return value.split(/\r?\n/).map(x => x.trim()).filter(Boolean).map(line => {
      const [channel, address, label, enabled] = line.split('|').map(x => x.trim());
      if (!channel || !address) throw new Error(`Invalid endpoint line: ${line}`);
      return {
        channel: channelValue(channel),
        address,
        label: label || null,
        enabled: enabled === '' || enabled == null ? true : !['false', '0', 'no', 'off'].includes(enabled.toLowerCase())
      };
    });
  }

  async function load() {
    globalStatus.textContent = 'Loading messaging policy…';
    policyStatus.textContent = '';
    try {
      const id = professionalId.value.trim();
      if (!id || !adminKey.value.trim()) throw new Error('Professional ID and administration key are required.');
      const [capabilities, policy] = await Promise.all([
        readResponse(await fetch('/api/admin/messaging/capabilities', { headers: headers() })),
        readResponse(await fetch(`/api/admin/professionals/${encodeURIComponent(id)}/notification-policy`, { headers: headers() }))
      ]);
      renderCapabilities(capabilities);
      setPolicy(policy);
      globalStatus.textContent = 'Messaging policy loaded.';
    } catch (error) {
      globalStatus.textContent = error.message;
    }
  }

  form.addEventListener('submit', async event => {
    event.preventDefault();
    policyStatus.textContent = 'Saving…';
    try {
      const id = professionalId.value.trim();
      if (!id || !adminKey.value.trim()) throw new Error('Professional ID and administration key are required.');
      const payload = {
        primaryChannel: Number(form.primaryChannel.value),
        businessChannel: form.businessChannel.value === '' ? null : Number(form.businessChannel.value),
        fallbackChannel: Number(form.fallbackChannel.value),
        escalationAfterMinutes: Number(form.escalationAfterMinutes.value),
        escalationOrder: parseOrder(form.escalationOrder.value),
        endpoints: parseEndpoints(form.endpoints.value)
      };
      const saved = await readResponse(await fetch(`/api/admin/professionals/${encodeURIComponent(id)}/notification-policy`, {
        method: 'PUT', headers: headers(true), body: JSON.stringify(payload)
      }));
      setPolicy(saved);
      policyStatus.textContent = 'Routing policy saved.';
    } catch (error) {
      policyStatus.textContent = error.message;
    }
  });

  form.primaryChannel.innerHTML = optionMarkup();
  form.businessChannel.innerHTML = optionMarkup(true);
  form.fallbackChannel.innerHTML = optionMarkup();
  document.querySelector('[data-load]').addEventListener('click', load);
})();
