'use strict';

/* ── Elementos do DOM ───────────────────────────────────────────────────── */
const cityInput        = document.getElementById('city-input');
const btnQuery         = document.getElementById('btn-query');
const btnNotifications = document.getElementById('btn-notifications');
const inputError       = document.getElementById('input-error');
const statusBadge      = document.getElementById('status-badge');

// Status card
const cardStatus       = document.getElementById('card-status');
const valCheckId       = document.getElementById('val-check-id');
const valCity          = document.getElementById('val-city');
const valStatus        = document.getElementById('val-status');
const pollingIndicator = document.getElementById('polling-indicator');

// Snapshot card
const cardSnapshot     = document.getElementById('card-snapshot');
const snapLocation     = document.getElementById('snap-location');
const snapTemp         = document.getElementById('snap-temp');
const snapFeels        = document.getElementById('snap-feels');
const snapRain         = document.getElementById('snap-rain');
const snapWind         = document.getElementById('snap-wind');
const snapTime         = document.getElementById('snap-time');

// Alerts card
const cardAlerts       = document.getElementById('card-alerts');
const alertsList       = document.getElementById('alerts-list');

// Notifications card
const notificationsList = document.getElementById('notifications-list');

/* ── Estado ─────────────────────────────────────────────────────────────── */
let pollingInterval  = null;
const POLL_MS        = 2000;   // intervalo de polling
const POLL_TIMEOUT   = 60000;  // timeout máximo (60s)

/* ── Utilitários ─────────────────────────────────────────────────────────── */
function formatDate(isoString) {
  if (!isoString) return '—';
  try {
    return new Date(isoString).toLocaleString('pt-BR', {
      day: '2-digit', month: '2-digit', year: 'numeric',
      hour: '2-digit', minute: '2-digit', second: '2-digit',
    });
  } catch {
    return isoString;
  }
}

function setGlobalBadge(text, cls) {
  statusBadge.textContent = text;
  statusBadge.className = `badge ${cls}`;
}

function setStatusBadge(statusText) {
  const map = {
    queued:    ['queued',    'badge-queued'],
    processed: ['processed', 'badge-processed'],
    failed:    ['failed',    'badge-failed'],
  };
  const [label, cls] = map[statusText] ?? [statusText, 'badge-idle'];
  valStatus.textContent = label;
  valStatus.className = `badge ${cls}`;
}

function showError(message) {
  inputError.textContent = message;
  inputError.classList.remove('hidden');
}

function clearError() {
  inputError.textContent = '';
  inputError.classList.add('hidden');
}

function stopPolling() {
  if (pollingInterval) {
    clearInterval(pollingInterval);
    pollingInterval = null;
  }
}

function setLoading(active) {
  btnQuery.disabled = active;
  if (active) {
    pollingIndicator.classList.remove('hidden');
    setGlobalBadge('Processando…', 'badge-loading');
  } else {
    pollingIndicator.classList.add('hidden');
  }
}

/* ── Fetch helpers ───────────────────────────────────────────────────────── */
async function apiFetch(url, options = {}) {
  const res = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(`HTTP ${res.status}${text ? ': ' + text : ''}`);
  }
  return res.json();
}

/* ── Fluxo Principal ─────────────────────────────────────────────────────── */
btnQuery.addEventListener('click', () => startQuery());
cityInput.addEventListener('keydown', (e) => {
  if (e.key === 'Enter') startQuery();
});

async function startQuery() {
  clearError();
  stopPolling();

  const city = cityInput.value.trim();
  if (!city) {
    showError('Por favor, informe o nome de uma cidade.');
    cityInput.focus();
    return;
  }

  // Limpar cards anteriores
  cardSnapshot.classList.add('hidden');
  cardAlerts.classList.add('hidden');

  // Mostrar card de status
  cardStatus.classList.remove('hidden');
  valCheckId.textContent = '…';
  valCity.textContent = city;
  setStatusBadge('queued');
  setLoading(true);

  try {
    const data = await apiFetch('/api/weather-checks', {
      method: 'POST',
      body: JSON.stringify({ city }),
    });

    const checkId = data.weatherCheckId;
    valCheckId.textContent = checkId;
    setStatusBadge(data.status ?? 'queued');

    // Iniciar polling com timeout
    let elapsed = 0;
    pollingInterval = setInterval(async () => {
      elapsed += POLL_MS;
      if (elapsed >= POLL_TIMEOUT) {
        stopPolling();
        setLoading(false);
        setStatusBadge('failed');
        setGlobalBadge('Timeout', 'badge-failed');
        showError('Tempo limite atingido. O servidor pode estar lento. Tente novamente.');
        return;
      }
      await pollStatus(checkId);
    }, POLL_MS);

  } catch (err) {
    setLoading(false);
    setGlobalBadge('Erro', 'badge-failed');
    showError(`Erro ao enviar consulta: ${err.message}`);
  }
}

async function pollStatus(checkId) {
  try {
    const data = await apiFetch(`/api/weather-checks/${checkId}`);
    setStatusBadge(data.status);

    if (data.status === 'processed') {
      stopPolling();
      setLoading(false);
      setGlobalBadge('Processado ✓', 'badge-processed');
      await loadSnapshot(checkId);
      await loadAlerts(checkId);

    } else if (data.status === 'failed') {
      stopPolling();
      setLoading(false);
      setGlobalBadge('Falha', 'badge-failed');
      showError(`A consulta falhou: ${data.errorMessage ?? 'Erro desconhecido.'}`);
    }
    // queued/processing → continua polling
  } catch (err) {
    stopPolling();
    setLoading(false);
    setGlobalBadge('Erro', 'badge-failed');
    showError(`Erro ao verificar status: ${err.message}`);
  }
}

/* ── Snapshot ────────────────────────────────────────────────────────────── */
async function loadSnapshot(checkId) {
  try {
    const s = await apiFetch(`/api/weather-checks/${checkId}/snapshot`);
    snapLocation.textContent = `${s.locationName ?? '—'}, ${s.countryCode ?? ''}`.trim().replace(/,\s*$/, '');
    snapTemp.textContent     = s.temperatureC != null ? `${s.temperatureC.toFixed(1)} °C` : '—';
    snapFeels.textContent    = s.apparentTemperatureC != null ? `${s.apparentTemperatureC.toFixed(1)} °C` : '—';
    snapRain.textContent     = s.precipitationMm != null ? `${s.precipitationMm.toFixed(1)} mm` : '—';
    snapWind.textContent     = s.windSpeedKmh != null ? `${s.windSpeedKmh.toFixed(1)} km/h` : '—';
    snapTime.textContent     = formatDate(s.observedAtUtc);
    cardSnapshot.classList.remove('hidden');
  } catch (err) {
    cardSnapshot.classList.remove('hidden');
    cardSnapshot.querySelector('.snapshot-grid').innerHTML =
      `<p class="error-message">Erro ao carregar snapshot: ${err.message}</p>`;
  }
}

/* ── Alertas ─────────────────────────────────────────────────────────────── */
async function loadAlerts(checkId) {
  cardAlerts.classList.remove('hidden');
  alertsList.innerHTML = '';

  try {
    const alerts = await apiFetch(`/api/weather-checks/${checkId}/alerts`);

    if (!Array.isArray(alerts) || alerts.length === 0) {
      alertsList.innerHTML = '<p class="empty-message">Nenhum alerta gerado para esta consulta.</p>';
      return;
    }

    alerts.forEach(alert => {
      const div = document.createElement('div');
      div.className = 'alert-item';
      div.innerHTML = `
        <div>
          <p class="alert-message">${escapeHtml(alert.message ?? '—')}</p>
          <p class="alert-meta">Detectado em ${formatDate(alert.detectedAtUtc)}</p>
        </div>
        <span class="badge badge-${escapeHtml(alert.severity ?? 'info')}">${escapeHtml(alert.severity ?? 'info')}</span>
      `;
      alertsList.appendChild(div);
    });
  } catch (err) {
    alertsList.innerHTML = `<p class="error-message">Erro ao carregar alertas: ${err.message}</p>`;
  }
}

/* ── Notificações ────────────────────────────────────────────────────────── */
btnNotifications.addEventListener('click', loadNotifications);

async function loadNotifications() {
  notificationsList.innerHTML = '<p class="empty-message">Carregando…</p>';
  btnNotifications.disabled = true;

  try {
    const notifications = await apiFetch('/api/notifications');
    btnNotifications.disabled = false;

    if (!Array.isArray(notifications) || notifications.length === 0) {
      notificationsList.innerHTML = '<p class="empty-message">Nenhuma notificação registrada ainda.</p>';
      return;
    }

    notificationsList.innerHTML = '';
    notifications.forEach(n => {
      const div = document.createElement('div');
      div.className = 'notification-item';
      div.innerHTML = `
        <span class="notif-channel">${escapeHtml(n.channel ?? '—')}</span>
        <span class="notif-status">Status: <strong>${escapeHtml(n.status ?? '—')}</strong></span>
        <span class="notif-date">${formatDate(n.createdAtUtc)}</span>
      `;
      notificationsList.appendChild(div);
    });
  } catch (err) {
    btnNotifications.disabled = false;
    notificationsList.innerHTML = `<p class="error-message">Erro ao carregar notificações: ${err.message}</p>`;
  }
}

/* ── Segurança: escape HTML ─────────────────────────────────────────────── */
function escapeHtml(str) {
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}
