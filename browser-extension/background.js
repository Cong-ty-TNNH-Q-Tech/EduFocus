import {
  countActiveBlockingRules,
  evaluateSnapshot,
  normalizeSnapshot,
  sanitizeUrl
} from './matcher.js';

const HOST_NAME = 'com.edufocus.native_host';
const RECONNECT_ALARM = 'edufocus-bridge';
const SESSION_SNAPSHOT_KEY = 'edufocusSnapshot';
const RETRY_WINDOW_MS = 2_000;
const INITIAL_SNAPSHOT_WAIT_MS = 600;
const DUPLICATE_WINDOW_MS = 5_000;
const REDIRECT_WINDOW_MS = 1_000;

let snapshot = null;
let port = null;
let lastConnectAttempt = 0;
let initialSnapshotPromise = null;
let resolveInitialSnapshot = null;
const blockedTabs = new Map();
const reportedUrls = new Map();

const restoreSnapshotPromise = (async () => {
  try {
    if (!chrome.storage?.session) return;
    const stored = await chrome.storage.session.get(SESSION_SNAPSHOT_KEY);
    if (snapshot === null && stored && stored[SESSION_SNAPSHOT_KEY]) {
      const restored = normalizeSnapshot(stored[SESSION_SNAPSHOT_KEY]);
      if (restored !== null) {
        snapshot = restored;
        updateBadge();
      }
    }
  } catch {
    // Session storage is best-effort.
  }
})();

function ensureAlarm() {
  chrome.alarms.create(RECONNECT_ALARM, { periodInMinutes: 1 });
}

function settleInitialSnapshot() {
  if (resolveInitialSnapshot) {
    const resolve = resolveInitialSnapshot;
    resolveInitialSnapshot = null;
    initialSnapshotPromise = null;
    resolve();
  }
}

function connectBridge() {
  if (port) return;

  const now = Date.now();
  if (now - lastConnectAttempt < RETRY_WINDOW_MS) return;
  lastConnectAttempt = now;

  try {
    port = chrome.runtime.connectNative(HOST_NAME);
  } catch {
    port = null;
    settleInitialSnapshot();
    return;
  }

  initialSnapshotPromise = new Promise((resolve) => {
    resolveInitialSnapshot = resolve;
  });

  port.onMessage.addListener(handleNativeMessage);
  port.onDisconnect.addListener(() => {
    port = null;
    settleInitialSnapshot();
    setSnapshot(null);
  });

  updateBadge();
}

function handleNativeMessage(message) {
  if (!message || typeof message !== 'object') return;

  lastConnectAttempt = 0;

  if (message.type === 'unavailable') {
    setSnapshot(null);
    settleInitialSnapshot();
    return;
  }

  const next = normalizeSnapshot(message);
  if (next !== null) {
    setSnapshot(next);
    settleInitialSnapshot();
  }
}

function setSnapshot(next) {
  snapshot = next;
  try {
    if (chrome.storage?.session) {
      if (next === null) {
        chrome.storage.session.remove(SESSION_SNAPSHOT_KEY).catch(() => {});
      } else {
        chrome.storage.session
          .set({ [SESSION_SNAPSHOT_KEY]: { type: 'snapshot', protocol: 1, ...next } })
          .catch(() => {});
      }
    }
  } catch {
    // Ignore storage errors.
  }
  updateBadge();
  if (next !== null && countActiveBlockingRules(next) > 0) {
    enforceOpenTabs(next);
  }
}

function enforceOpenTabs(currentSnapshot) {
  if (!chrome.tabs?.query) return;
  try {
    chrome.tabs.query({}, (tabs) => {
      if (!Array.isArray(tabs)) return;
      for (const tab of tabs) {
        if (!tab || typeof tab.id !== 'number' || tab.id < 0 || typeof tab.url !== 'string') {
          continue;
        }
        const verdict = evaluateSnapshot(currentSnapshot, tab.url);
        if (verdict.decision !== 'block') continue;

        const now = Date.now();
        const lastRedirect = blockedTabs.get(tab.id);
        if (lastRedirect !== undefined && now - lastRedirect < REDIRECT_WINDOW_MS) continue;
        blockedTabs.set(tab.id, now);

        chrome.tabs
          .update(tab.id, { url: chrome.runtime.getURL('blocked.html') })
          .catch(() => {});
        reportBlocked(tab.url);
      }
    });
  } catch {
    // Querying open tabs is best-effort.
  }
}

function hasBlockingRules() {
  return countActiveBlockingRules(snapshot) > 0;
}

function updateBadge() {
  const active = hasBlockingRules();
  const text = snapshot === null ? '' : active ? 'ON' : '—';

  chrome.action.setBadgeText({ text }).catch(() => {});
  chrome.action
    .setBadgeBackgroundColor({ color: active ? '#2e7d32' : '#757575' })
    .catch(() => {});
}

async function handleNavigation(details) {
  if (!details || details.tabId < 0) return;

  if (snapshot === null) {
    connectBridge();
    await restoreSnapshotPromise;
    if (snapshot === null && initialSnapshotPromise !== null) {
      await Promise.race([
        initialSnapshotPromise,
        new Promise((resolve) => setTimeout(resolve, INITIAL_SNAPSHOT_WAIT_MS))
      ]);
    }
    if (snapshot === null) return;
  }

  const verdict = evaluateSnapshot(snapshot, details.url);
  if (verdict.decision !== 'block') return;

  const now = Date.now();
  const lastRedirect = blockedTabs.get(details.tabId);
  if (lastRedirect !== undefined && now - lastRedirect < REDIRECT_WINDOW_MS) return;

  blockedTabs.set(details.tabId, now);
  if (blockedTabs.size > 100) blockedTabs.clear();

  chrome.tabs
    .update(details.tabId, { url: chrome.runtime.getURL('blocked.html') })
    .catch(() => {});
  reportBlocked(details.url);
}

function reportBlocked(rawUrl) {
  if (!port) return;

  const url = sanitizeUrl(rawUrl);
  if (url === null) return;

  const now = Date.now();
  const lastReport = reportedUrls.get(url);
  if (lastReport !== undefined && now - lastReport < DUPLICATE_WINDOW_MS) return;

  reportedUrls.set(url, now);
  if (reportedUrls.size > 100) reportedUrls.clear();

  try {
    port.postMessage({ type: 'blocked', url });
  } catch {
    // The port closed between the check and the post; the next navigation retries.
  }
}

chrome.webNavigation.onBeforeNavigate.addListener(handleNavigation);
chrome.webNavigation.onHistoryStateUpdated.addListener(handleNavigation);

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === RECONNECT_ALARM) connectBridge();
});

chrome.runtime.onStartup.addListener(() => {
  ensureAlarm();
  connectBridge();
});

chrome.runtime.onInstalled.addListener(() => {
  ensureAlarm();
  connectBridge();
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message && message.type === 'status') {
    connectBridge();
    const ruleCount = countActiveBlockingRules(snapshot);
    sendResponse({
      connected: snapshot !== null,
      active: ruleCount > 0,
      ruleCount
    });
  }

  return false;
});

ensureAlarm();
connectBridge();
