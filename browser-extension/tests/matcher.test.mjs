import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  countActiveBlockingRules,
  ensureTrailingSlash,
  evaluateSnapshot,
  isProtectedHost,
  matchesRule,
  normalizeSnapshot,
  sanitizeUrl
} from '../matcher.js';

const SERVICES = [
  'meet.google.com',
  'teams.microsoft.com',
  'classroom.google.com',
  'accounts.google.com',
  'login.microsoftonline.com',
  'login.live.com',
  'zoom.us'
];

function snapshot(rules, protectedExact = ['google.com', 'microsoft.com']) {
  return { rules, protectedExact, protectedServices: SERVICES };
}

function rule(type, match, value, effect = 'block', expiresAtUtc = null) {
  return { id: 'rule-1', type, match, value, effect, expiresAtUtc };
}

test('sanitizeUrl strips credentials, query and fragment', () => {
  assert.equal(
    sanitizeUrl('https://user:pass@Example.COM:443/Path/To?token=1#section'),
    'https://example.com/Path/To');
  assert.equal(sanitizeUrl('http://example.com'), 'http://example.com/');
  assert.equal(sanitizeUrl(' https://example.com/a '), 'https://example.com/a');
});

test('sanitizeUrl rejects non-http schemes and malformed input', () => {
  for (const value of [
    'javascript:alert(1)',
    'data:text/html,<script>alert(1)</script>',
    'ftp://example.com/file',
    'file:///C:/Windows',
    'chrome-extension://abc/blocked.html',
    '//example.com/path',
    '',
    '   ',
    null,
    undefined,
    42
  ]) {
    assert.equal(sanitizeUrl(value), null, String(value));
  }
});

test('sanitizeUrl enforces the bridge URL limit', () => {
  const tooLong = 'https://example.com/' + 'a'.repeat(2100);
  assert.equal(sanitizeUrl(tooLong), null);
});

test('ensureTrailingSlash only appends when missing', () => {
  assert.equal(ensureTrailingSlash('https://example.com'), 'https://example.com/');
  assert.equal(ensureTrailingSlash('https://example.com/a'), 'https://example.com/a/');
  assert.equal(ensureTrailingSlash('https://example.com/a/'), 'https://example.com/a/');
});

test('exact URL rules ignore query, fragment, host case and a missing trailing slash', () => {
  const rules = [rule('url', 'exact', 'https://example.com/lesson/')];

  assert.equal(
    evaluateSnapshot(snapshot(rules), 'https://example.com/lesson?x=1#top').decision,
    'block');
  assert.equal(
    evaluateSnapshot(snapshot(rules), 'HTTPS://EXAMPLE.COM/LESSON').decision,
    'block');
  assert.equal(
    evaluateSnapshot(snapshot(rules), 'https://example.com/lesson/2').decision,
    'none');
  assert.equal(
    evaluateSnapshot(snapshot(rules), 'https://sub.example.com/lesson/').decision,
    'none');
});

test('contains URL rules block YouTube Shorts paths but not query strings', () => {
  const rules = [
    rule('url', 'contains', 'https://www.youtube.com/shorts/'),
    rule('url', 'contains', 'https://m.youtube.com/shorts/')
  ];
  const ruleset = snapshot(rules);

  assert.equal(
    evaluateSnapshot(ruleset, 'https://www.youtube.com/shorts/abc123?si=track').decision,
    'block');
  assert.equal(
    evaluateSnapshot(ruleset, 'https://m.youtube.com/shorts/xyz').decision,
    'block');
  assert.equal(
    evaluateSnapshot(ruleset, 'https://www.youtube.com/watch?v=1&next=/shorts/').decision,
    'none');
  assert.equal(
    evaluateSnapshot(ruleset, 'https://www.youtube.com/feed/shorts/').decision,
    'none');
  assert.equal(
    evaluateSnapshot(ruleset, 'https://evil.example/?next=https://www.youtube.com/shorts/').decision,
    'none');
});

test('path matching is case-insensitive on both sides', () => {
  const rules = [rule('url', 'contains', 'https://www.facebook.com/reel/')];

  assert.equal(
    evaluateSnapshot(snapshot(rules), 'https://www.facebook.com/REEL/987').decision,
    'block');
});

test('suffix URL rules compare the whole sanitized URL like the domain model', () => {
  const rules = [rule('url', 'suffix', 'https://example.com/files/')];

  assert.equal(
    evaluateSnapshot(snapshot(rules), 'https://example.com/files/').decision,
    'block');
  assert.equal(
    evaluateSnapshot(snapshot(rules), 'https://example.com/files?x=1').decision,
    'block');
  assert.equal(
    evaluateSnapshot(snapshot(rules), 'https://example.com/a/files/').decision,
    'none');
  assert.equal(
    evaluateSnapshot(snapshot(rules), 'https://example.com/files/report.pdf').decision,
    'none');
});

test('domain exact rules match only the exact host', () => {
  const rules = [rule('domain', 'exact', 'chatgpt.com')];

  assert.equal(evaluateSnapshot(snapshot(rules), 'https://chatgpt.com/chat').decision, 'block');
  assert.equal(evaluateSnapshot(snapshot(rules), 'https://chatgpt.com:443/').decision, 'block');
  assert.equal(evaluateSnapshot(snapshot(rules), 'https://chat.openai.com/').decision, 'none');
  assert.equal(evaluateSnapshot(snapshot(rules), 'https://notchatgpt.com/').decision, 'none');
});

test('domain suffix and contains rules cover subdomains and embedded segments', () => {
  const suffix = [rule('domain', 'suffix', 'openai.com')];
  assert.equal(evaluateSnapshot(snapshot(suffix), 'https://chat.openai.com/').decision, 'block');
  assert.equal(evaluateSnapshot(snapshot(suffix), 'https://openai.com/').decision, 'block');
  assert.equal(evaluateSnapshot(snapshot(suffix), 'https://openai.com.evil.test/').decision, 'none');

  const contains = [rule('domain', 'contains', 'tiktok.com')];
  assert.equal(evaluateSnapshot(snapshot(contains), 'https://www.tiktok.com/foryou').decision, 'block');
  assert.equal(evaluateSnapshot(snapshot(contains), 'https://tiktok.com.evil.test/').decision, 'none');
});

test('protected apex hosts stay exact while shared services cover subdomains', () => {
  const rules = [rule('domain', 'exact', 'gemini.google.com')];
  const ruleset = snapshot(rules);

  assert.equal(evaluateSnapshot(ruleset, 'https://google.com/').decision, 'allow');
  assert.equal(evaluateSnapshot(ruleset, 'https://gemini.google.com/').decision, 'block');
  assert.equal(isProtectedHost(ruleset, 'meet.google.com'), true);
  assert.equal(isProtectedHost(ruleset, 'us02web.zoom.us'), true);
  assert.equal(isProtectedHost(ruleset, 'notzoom.us'), false);
});

test('allow rules win over block rules regardless of order', () => {
  const rules = [
    rule('domain', 'exact', 'chatgpt.com'),
    rule('url', 'exact', 'https://chatgpt.com/lesson/', 'allow')
  ];
  const ruleset = snapshot(rules);

  assert.equal(evaluateSnapshot(ruleset, 'https://chatgpt.com/lesson/').decision, 'allow');
  assert.equal(evaluateSnapshot(ruleset, 'https://chatgpt.com/chat').decision, 'block');
});

test('expired rules never block', () => {
  const now = Date.parse('2026-10-30T08:00:00Z');
  const rules = [
    rule('domain', 'exact', 'chatgpt.com', 'block', '2026-10-30T07:59:00Z'),
    rule('domain', 'exact', 'claude.ai', 'block', '2026-10-30T09:00:00Z')
  ];
  const ruleset = snapshot(rules);

  assert.equal(evaluateSnapshot(ruleset, 'https://chatgpt.com/', now).decision, 'none');
  assert.equal(evaluateSnapshot(ruleset, 'https://claude.ai/', now).decision, 'block');
});

test('malformed rules are dropped before evaluation', () => {
  const normalized = normalizeSnapshot({
    type: 'snapshot',
    protocol: 1,
    rules: [
      rule('domain', 'exact', 'chatgpt.com'),
      { id: 'x', type: 'app', match: 'exact', value: 'discord', effect: 'block' },
      { id: 'y', type: 'domain', match: 'regex', value: 'a.com', effect: 'block' },
      { id: 'z', type: 'url', match: 'contains', value: '', effect: 'block' },
      { id: 'w', type: 'url', match: 'contains', value: 'https://a.com/x/', effect: 'deny' },
      null
    ],
    protectedExact: ['google.com'],
    protectedServices: ['meet.google.com']
  });

  assert.equal(normalized.rules.length, 1);
  assert.equal(normalized.rules[0].value, 'chatgpt.com');
  assert.deepEqual(normalized.protectedExact, ['google.com']);
});

test('normalizeSnapshot rejects unknown protocols and shapes', () => {
  assert.equal(normalizeSnapshot(null), null);
  assert.equal(normalizeSnapshot({ type: 'snapshot', protocol: 2, rules: [] }), null);
  assert.equal(normalizeSnapshot({ type: 'blocked', rules: [] }), null);
  assert.equal(normalizeSnapshot({ type: 'snapshot', protocol: 1, rules: 'nope' }), null);
});

test('server redirect chains are evaluated hop by hop', () => {
  const rules = [rule('url', 'contains', 'https://www.youtube.com/shorts/')];
  const ruleset = snapshot(rules);

  assert.equal(evaluateSnapshot(ruleset, 'https://short.link/abc').decision, 'none');
  assert.equal(
    evaluateSnapshot(ruleset, 'https://www.youtube.com/shorts/redirected').decision,
    'block');
});

test('HTML-looking paths are matched as plain text and never evaluated as markup', () => {
  const rules = [rule('url', 'contains', 'https://www.youtube.com/shorts/')];
  const verdict = evaluateSnapshot(
    snapshot(rules),
    'https://www.youtube.com/shorts/%3Cimg%20src=x%20onerror=alert(1)%3E');

  assert.equal(verdict.decision, 'block');
  assert.ok(verdict.rule);
});

test('matchesRule rejects non-domain and non-url rule types', () => {
  assert.equal(matchesRule({ type: 'app', match: 'exact', value: 'discord' },
    'https://example.com/', 'example.com'), false);
});

test('countActiveBlockingRules excludes allow and expired rules', () => {
  const now = Date.parse('2026-10-30T08:00:00Z');
  const ruleset = snapshot([
    rule('domain', 'exact', 'chatgpt.com', 'block', null),
    rule('domain', 'exact', 'expired.test', 'block', '2026-10-30T07:59:00Z'),
    rule('domain', 'exact', 'meet.google.com', 'allow', null)
  ]);

  assert.equal(countActiveBlockingRules(ruleset, now), 1);
});

test('sanitizeUrl decodes unreserved percent-encoded characters', () => {
  assert.equal(sanitizeUrl('https://example.com/%7Euser/lesson'), 'https://example.com/~user/lesson');
});
