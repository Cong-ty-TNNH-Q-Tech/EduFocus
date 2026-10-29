import { evaluateSnapshot, normalizeSnapshot, sanitizeUrl } from './matcher.js';

const HOST_NAME = 'com.edufocus.native_host';
const RECONNECT_ALARM = 'edufocus-bridge';
const RETRY_WINDOW_MS = 15_000;
const DUPLICATE_WINDOW_MS = 5_000;
const REDIRECT_WINDOW_MS = 1_000;

let snapshot = null;
let port = null;
let lastConnectAttempt = 0;
const blockedTabs = new Map();
const reportedUrls = new Map();

function ensureAlarm() {
  chrome.alarms.create(RECONNECT_ALARM, { periodInMinutes: 1 });
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
    return;
  }

  port.onMessage.addListener(handleNativeMessage);
  port.onDisconnect.addListener(() => {
    port = null;
    setSnapshot(null);
  });

  updateBadge();
}

function handleNativeMessage(message) {
  if (!message || typeof message !== 'object') return;

  if (message.type === 'unavailable') {
    setSnapshot(null);
    return;
  }

  const next = normalizeSnapshot(message);
  if (next !== null) setSnapshot(next);
}

function setSnapshot(next) {
  snapshot = next;
  updateBadge();
}

function hasBlockingRules() {
  return snapshot !== null && snapshot.rules.some((rule) => rule.effect === 'block');
}

function updateBadge() {
  const active = hasBlockingRules();
  const text = snapshot === null ? '' : active ? 'ON' : '—';

  chrome.action.setBadgeText({ text }).catch(() => {});
  chrome.action
    .setBadgeBackgroundColor({ color: active ? '#2e7d32' : '#757575' })
    .catch(() => {});
}

function handleNavigation(details) {
  if (!details || details.frameId !== 0 || details.tabId < 0) return;

  if (snapshot === null) {
    connectBridge();
    return;
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
    sendResponse({
      connected: port !== null,
      active: hasBlockingRules(),
      ruleCount: snapshot === null ? 0 : snapshot.rules.length
    });
  }

  return false;
});

ensureAlarm();
connectBridge();
