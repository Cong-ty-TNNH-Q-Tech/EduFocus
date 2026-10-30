/**
 * Pure URL/path matcher shared by the MV3 service worker and the Node tests.
 * It mirrors EduFocus.Domain.BlockRule.MatchesUrl and BlockRuleSet.EvaluateUrl:
 * query strings, fragments and credentials are stripped before matching, paths
 * are compared with a virtual trailing slash, and allow rules win.
 */

export const MAX_URL_LENGTH = 2048;
export const MAX_RULE_VALUE_LENGTH = 2048;

const MATCH_TYPES = new Set(['exact', 'suffix', 'contains']);
const RULE_TYPES = new Set(['domain', 'url']);
const EFFECTS = new Set(['block', 'allow']);

function decodeUnreservedPercentEncoding(value) {
  return value.replace(/%([0-9A-Fa-f]{2})/g, (match, hex) => {
    const code = Number.parseInt(hex, 16);
    if (
      (code >= 0x41 && code <= 0x5A) ||
      (code >= 0x61 && code <= 0x7A) ||
      (code >= 0x30 && code <= 0x39) ||
      code === 0x2D ||
      code === 0x2E ||
      code === 0x5F ||
      code === 0x7E
    ) {
      return String.fromCharCode(code);
    }
    return match.toUpperCase();
  });
}

export function sanitizeUrl(rawUrl) {
  if (typeof rawUrl !== 'string') return null;

  const trimmed = rawUrl.trim();
  if (trimmed.length === 0) return null;

  let parsed;
  try {
    parsed = new URL(trimmed);
  } catch {
    return null;
  }

  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return null;
  if (!parsed.hostname) return null;

  const rawPath = parsed.pathname && parsed.pathname.length > 0 ? parsed.pathname : '/';
  const path = decodeUnreservedPercentEncoding(rawPath);
  const sanitized = parsed.origin + path;

  return sanitized.length > MAX_URL_LENGTH ? null : sanitized;
}

export function ensureTrailingSlash(url) {
  return url.endsWith('/') ? url : url + '/';
}

export function deriveHost(rawUrl) {
  const sanitized = sanitizeUrl(rawUrl);
  if (sanitized === null) return null;

  return new URL(sanitized).hostname.toLowerCase();
}

function matchesDomain(value, matchType, host) {
  const domain = value.toLowerCase();

  switch (matchType) {
    case 'exact':
      return host === domain;
    case 'suffix':
      return host === domain || host.endsWith('.' + domain);
    case 'contains':
      return host === domain
        || host.endsWith('.' + domain)
        || host.includes('.' + domain + '.');
    default:
      return false;
  }
}

export function matchesRule(rule, sanitizedUrl, host) {
  if (!rule || !RULE_TYPES.has(rule.type) || !MATCH_TYPES.has(rule.match)) return false;
  if (typeof rule.value !== 'string' || rule.value.length === 0) return false;
  if (rule.value.length > MAX_RULE_VALUE_LENGTH) return false;

  if (rule.type === 'domain') {
    return matchesDomain(rule.value, rule.match, host);
  }

  const candidate = ensureTrailingSlash(sanitizedUrl).toLowerCase();
  const normalizedRuleValue = sanitizeUrl(rule.value) ?? rule.value;
  const value = ensureTrailingSlash(normalizedRuleValue).toLowerCase();

  switch (rule.match) {
    case 'exact':
      return candidate === value;
    case 'suffix':
      return candidate.endsWith(value);
    case 'contains':
      return candidate.includes(value);
    default:
      return false;
  }
}

export function isProtectedHost(snapshot, host) {
  const exact = Array.isArray(snapshot?.protectedExact) ? snapshot.protectedExact : [];
  if (exact.some((value) => typeof value === 'string'
    && host === value.toLowerCase())) {
    return true;
  }

  const services = Array.isArray(snapshot?.protectedServices)
    ? snapshot.protectedServices
    : [];

  return services.some((value) => {
    if (typeof value !== 'string') return false;
    const service = value.toLowerCase();
    return host === service || host.endsWith('.' + service);
  });
}

export function isExpired(rule, nowMs = Date.now()) {
  if (!rule.expiresAtUtc) return false;

  const expiresAt = Date.parse(rule.expiresAtUtc);
  return !Number.isFinite(expiresAt) || expiresAt <= nowMs;
}

export function countActiveBlockingRules(snapshot, nowMs = Date.now()) {
  if (!snapshot || !Array.isArray(snapshot.rules)) return 0;

  let count = 0;
  for (const rule of snapshot.rules) {
    if (!rule || rule.effect !== 'block') continue;
    if (isExpired(rule, nowMs)) continue;
    count++;
  }
  return count;
}

/**
 * @returns {{decision: 'none'|'allow'|'block', rule?: object, reason?: string}}
 */
export function evaluateSnapshot(snapshot, rawUrl, nowMs = Date.now()) {
  const sanitized = sanitizeUrl(rawUrl);
  if (sanitized === null) return { decision: 'none' };

  const host = new URL(sanitized).hostname.toLowerCase();
  if (isProtectedHost(snapshot, host)) {
    return { decision: 'allow', reason: 'protected' };
  }

  let allowed = false;
  let blockedRule = null;

  for (const rule of Array.isArray(snapshot?.rules) ? snapshot.rules : []) {
    if (!rule || !EFFECTS.has(rule.effect)) continue;
    if (isExpired(rule, nowMs)) continue;
    if (!matchesRule(rule, sanitized, host)) continue;

    if (rule.effect === 'allow') {
      allowed = true;
    } else if (blockedRule === null) {
      blockedRule = rule;
    }
  }

  if (allowed) return { decision: 'allow' };
  if (blockedRule !== null) return { decision: 'block', rule: blockedRule };
  return { decision: 'none' };
}

function sanitizeList(values) {
  if (!Array.isArray(values)) return [];

  return values
    .filter((value) => typeof value === 'string'
      && value.length > 0
      && value.length <= MAX_RULE_VALUE_LENGTH)
    .map((value) => value.toLowerCase());
}

/** Validates a bridge snapshot so malformed data can never reach evaluation. */
export function normalizeSnapshot(message) {
  if (!message
    || message.type !== 'snapshot'
    || message.protocol !== 1
    || !Array.isArray(message.rules)) {
    return null;
  }

  const rules = [];

  for (const rule of message.rules) {
    if (!rule || typeof rule !== 'object') continue;
    if (!RULE_TYPES.has(rule.type) || !MATCH_TYPES.has(rule.match)) continue;
    if (!EFFECTS.has(rule.effect)) continue;
    if (typeof rule.value !== 'string'
      || rule.value.length === 0
      || rule.value.length > MAX_RULE_VALUE_LENGTH) {
      continue;
    }

    rules.push({
      id: typeof rule.id === 'string' ? rule.id.slice(0, 64) : '',
      type: rule.type,
      match: rule.match,
      value: rule.value,
      effect: rule.effect,
      expiresAtUtc: typeof rule.expiresAtUtc === 'string' ? rule.expiresAtUtc : null
    });
  }

  return {
    rules,
    protectedExact: sanitizeList(message.protectedExact),
    protectedServices: sanitizeList(message.protectedServices)
  };
}
