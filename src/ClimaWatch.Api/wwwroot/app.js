'use strict';

/* ── Elementos do DOM ───────────────────────────────────────────────────── */
const ufSelect         = document.getElementById('uf-select');
const citySelect       = document.getElementById('city-select');
const btnQuery         = document.getElementById('btn-query');
const btnNotifications = document.getElementById('btn-notifications');
const inputError       = document.getElementById('input-error');
const statusBadge      = document.getElementById('status-badge');
const dashboardGrid    = document.getElementById('dashboard-grid');

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
const FIXED_CITIES   = ['Blumenau', 'Manaus', 'Cuiabá', 'São Paulo'];

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
  btnQuery.disabled = active || !citySelect.value;
  if (active) {
    pollingIndicator.classList.remove('hidden');
    setGlobalBadge('Processando…', 'badge-loading');
    ufSelect.disabled = true;
    citySelect.disabled = true;
  } else {
    pollingIndicator.classList.add('hidden');
    ufSelect.disabled = false;
    citySelect.disabled = false;
  }
}

/* ── Fetch helpers ───────────────────────────────────────────────────────── */
async function apiFetch(url, options = {}) {
  const headers = options.headers || {};
  if (!(options.body instanceof FormData)) {
    headers['Content-Type'] = 'application/json';
  }
  const res = await fetch(url, {
    ...options,
    headers,
  });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(`HTTP ${res.status}${text ? ': ' + text : ''}`);
  }
  return res.json();
}

/* ── Cálculo de Risco ────────────────────────────────────────────────────── */
function calcRisk(alerts) {
  if (!Array.isArray(alerts) || alerts.length === 0) {
    return { level: 'normal', label: 'Normal', text: 'Nenhuma regra de alerta foi atingida.' };
  }
  
  let highestSeverity = 'info';
  
  for (const alert of alerts) {
    const sev = (alert.severity || '').toLowerCase();
    if (sev === 'critical') {
      highestSeverity = 'critical';
      break;
    } else if (sev === 'warning') {
      highestSeverity = 'warning';
    } else if (sev === 'info' && highestSeverity !== 'warning') {
      highestSeverity = 'info';
    }
  }
  
  if (highestSeverity === 'critical') {
    return { level: 'critical', label: 'Crítico', text: 'Vento forte detectado.' };
  }
  if (highestSeverity === 'warning') {
    return { level: 'warning', label: 'Alerta', text: 'Temperatura elevada detectada.' };
  }
  
  return { level: 'attention', label: 'Atenção', text: 'Precipitação detectada.' };
}

/* ── Fluxo IBGE Selects ─────────────────────────────────────────────────── */
async function loadUFs() {
  try {
    const ufs = await apiFetch('https://servicodados.ibge.gov.br/api/v1/localidades/estados?orderBy=nome');
    ufSelect.innerHTML = '<option value="">Selecione o estado</option>';
    ufs.forEach(uf => {
      const option = document.createElement('option');
      option.value = uf.sigla;
      option.textContent = `${uf.sigla} — ${uf.nome}`;
      ufSelect.appendChild(option);
    });
  } catch (err) {
    ufSelect.innerHTML = '<option value="">Erro ao carregar estados</option>';
    showError('Não foi possível carregar as UFs (IBGE). Tente recarregar a página.');
  }
}

ufSelect.addEventListener('change', async () => {
  const uf = ufSelect.value;
  citySelect.innerHTML = '<option value="">Selecione a cidade</option>';
  citySelect.disabled = true;
  btnQuery.disabled = true;
  clearError();
  
  if (!uf) {
    citySelect.innerHTML = '<option value="">Selecione o estado primeiro</option>';
    return;
  }
  
  citySelect.innerHTML = '<option value="">Carregando cidades...</option>';
  
  try {
    const cities = await apiFetch(`https://servicodados.ibge.gov.br/api/v1/localidades/estados/${uf}/municipios?orderBy=nome`);
    citySelect.innerHTML = '<option value="">Selecione a cidade</option>';
    cities.forEach(city => {
      const option = document.createElement('option');
      option.value = city.nome;
      option.textContent = city.nome;
      citySelect.appendChild(option);
    });
    citySelect.disabled = false;
  } catch (err) {
    citySelect.innerHTML = '<option value="">Erro ao carregar cidades</option>';
    showError('Não foi possível carregar as cidades do estado selecionado.');
  }
});

citySelect.addEventListener('change', () => {
  if (citySelect.value) {
    btnQuery.disabled = false;
  } else {
    btnQuery.disabled = true;
  }
});

/* ── Fluxo Principal ─────────────────────────────────────────────────────── */
btnQuery.addEventListener('click', () => startQuery());

async function startQuery() {
  clearError();
  stopPolling();

  const city = citySelect.value;
  if (!city) {
    showError('Por favor, selecione uma cidade.');
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
    alertsList.innerHTML = `<p class="error-message">Erro ao carregar alerts: ${err.message}</p>`;
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

/* ── Dashboard "Monitoramento Rápido" ───────────────────────────────────── */
function initDashboard() {
  dashboardGrid.innerHTML = '';
  const cards = [];

  FIXED_CITIES.forEach(city => {
    const card = document.createElement('div');
    card.className = 'db-card';
    card.dataset.city = city;
    card.innerHTML = `
      <div class="db-card-header">
        <span class="db-card-city">🏙 ${city}</span>
      </div>
      <div class="db-card-divider"></div>
      <div class="db-card-body-loading">
        <div class="spinner"></div>
        <span>Carregando…</span>
      </div>
    `;
    dashboardGrid.appendChild(card);
    cards.push({ city, element: card });
  });

  // Disparar consultas com 500ms de atraso entre si
  cards.forEach((card, index) => {
    setTimeout(() => {
      queryAndPollCityDashboard(card.city, card.element);
    }, index * 500);
  });
}

async function queryAndPollCityDashboard(city, element) {
  try {
    const data = await apiFetch('/api/weather-checks', {
      method: 'POST',
      body: JSON.stringify({ city }),
    });

    const checkId = data.weatherCheckId;
    let elapsed = 0;
    
    const interval = setInterval(async () => {
      elapsed += POLL_MS;
      if (elapsed >= POLL_TIMEOUT) {
        clearInterval(interval);
        setDashboardCardError(element, city);
        return;
      }
      
      try {
        const checkData = await apiFetch(`/api/weather-checks/${checkId}`);
        if (checkData.status === 'processed') {
          clearInterval(interval);
          const snapshot = await apiFetch(`/api/weather-checks/${checkId}/snapshot`);
          const alerts = await apiFetch(`/api/weather-checks/${checkId}/alerts`);
          updateDashboardCard(element, city, snapshot, alerts);
        } else if (checkData.status === 'failed') {
          clearInterval(interval);
          setDashboardCardError(element, city);
        }
      } catch (err) {
        clearInterval(interval);
        setDashboardCardError(element, city);
      }
    }, POLL_MS);

  } catch (err) {
    setDashboardCardError(element, city);
  }
}

function updateDashboardCard(element, city, snapshot, alerts) {
  const risk = calcRisk(alerts);
  
  const tempText = snapshot.temperatureC != null ? `${snapshot.temperatureC.toFixed(1)} °C` : '—';
  const rainText = snapshot.precipitationMm != null ? `${snapshot.precipitationMm.toFixed(1)} mm` : '—';
  const windText = snapshot.windSpeedKmh != null ? `Vento: ${snapshot.windSpeedKmh.toFixed(1)} km/h` : 'Vento: —';
  
  let emoji = '🟢';
  if (risk.level === 'attention') emoji = '🔵';
  else if (risk.level === 'warning') emoji = '🟡';
  else if (risk.level === 'critical') emoji = '🔴';

  element.innerHTML = `
    <div class="db-card-header">
      <span class="db-card-city">🏙 ${city}</span>
    </div>
    <div class="db-card-divider"></div>
    <div class="db-card-body">
      <div class="db-temp-rain">
        <span class="db-temp">${tempText}</span>
        <span class="db-dot">·</span>
        <span class="db-rain">${rainText}</span>
      </div>
      <div class="db-wind">${windText}</div>
    </div>
    <div class="db-card-divider"></div>
    <div class="db-card-footer">
      <span class="risk-badge risk-${risk.level}">${emoji} ${risk.label}</span>
      <span class="risk-text">${risk.text}</span>
    </div>
  `;
}

function setDashboardCardError(element, city) {
  element.innerHTML = `
    <div class="db-card-header">
      <span class="db-card-city">🏙 ${city}</span>
    </div>
    <div class="db-card-divider"></div>
    <div class="db-card-body-error">
      ❌ Falhou
    </div>
  `;
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

/* ── Inicialização ──────────────────────────────────────────────────────── */
document.addEventListener('DOMContentLoaded', () => {
  loadUFs();
  initDashboard();
});
