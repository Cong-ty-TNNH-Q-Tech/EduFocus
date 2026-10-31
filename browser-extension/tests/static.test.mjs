import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = join(root, '..');

const read = (name) => readFileSync(join(root, name), 'utf8');
const manifest = JSON.parse(read('manifest.json'));

const expectedExtensionId = 'lcanbfnejggilcaeinmahlcfimecmjmp';
const nativeHostName = 'com.edufocus.native_host';

function extensionIdFromKey(base64Key) {
  const der = Buffer.from(base64Key, 'base64');
  const hex = createHash('sha256').update(der).digest('hex').slice(0, 32);
  return [...hex]
    .map((nibble) => String.fromCharCode(97 + parseInt(nibble, 16)))
    .join('');
}

test('manifest is MV3 and requests only the permissions the feature needs', () => {
  assert.equal(manifest.manifest_version, 3);
  assert.deepEqual(manifest.permissions, ['alarms', 'nativeMessaging', 'storage', 'webNavigation']);
  assert.deepEqual(manifest.host_permissions, ['http://*/*', 'https://*/*']);
  assert.equal(manifest.background.service_worker, 'background.js');
  assert.equal(manifest.background.type, 'module');
  assert.equal(manifest.action.default_popup, 'popup.html');
  assert.equal(manifest.content_scripts, undefined);
  assert.equal(manifest.web_accessible_resources, undefined);
});

test('manifest key derives the fixed extension ID used by the native host', () => {
  assert.equal(extensionIdFromKey(manifest.key), expectedExtensionId);

  const nativeHostManifest = read('native-host/com.edufocus.native_host.json.template');
  assert.ok(nativeHostManifest.includes(`chrome-extension://${expectedExtensionId}/`));
  assert.ok(nativeHostManifest.includes(nativeHostName));
});

test('registration scripts and background agree on the native host name', () => {
  const background = read('background.js');
  assert.ok(background.includes(`'${nativeHostName}'`));

  for (const script of [
    'scripts/register-browser-extension.ps1',
    'scripts/unregister-browser-extension.ps1'
  ]) {
    const source = readFileSync(join(repositoryRoot, script), 'utf8');
    assert.ok(source.includes(nativeHostName), script);
  }
});

test('extension code has no remote assets or unsafe DOM sinks', () => {
  const files = ['blocked.js', 'popup.js', 'background.js', 'matcher.js'];
  const unsafe = [
    'innerHTML',
    'outerHTML',
    'insertAdjacentHTML',
    'document.write',
    'eval(',
    'new Function',
    'javascript:',
    'localStorage',
    'sessionStorage',
    'chrome.storage.local',
    'chrome.storage.sync',
    'chrome.storage.managed',
    'XMLHttpRequest',
    'fetch('
  ];

  for (const file of files) {
    const source = read(file);
    for (const sink of unsafe) {
      assert.ok(!source.includes(sink), `${file} must not use ${sink}`);
    }
  }

  const background = read('background.js');
  assert.ok(
    !/chrome\.storage(?!\??\.session\b)/.test(background),
    'background.js must only use session-scoped storage'
  );

  for (const file of ['blocked.html', 'popup.html', 'blocked.css', 'popup.css']) {
    const source = read(file);
    assert.ok(!/https?:\/\//i.test(source), `${file} must not reference remote assets`);
  }
});

test('HTML pages load scripts only from packaged files and use no inline handlers', () => {
  for (const file of ['blocked.html', 'popup.html']) {
    const source = read(file);
    assert.ok(!/\son[a-z]+\s*=/i.test(source), `${file} must not use inline event handlers`);
    assert.ok(!/<script>(?!\s*<\/script>)/i.test(source), `${file} must not inline scripts`);
    assert.match(source, /<script src="[a-z.]+"><\/script>/);
  }
});

test('the block page never reads or renders the blocked address', () => {
  const source = read('blocked.js');
  assert.ok(!source.includes('location'));
  assert.ok(!source.includes('URLSearchParams'));
  assert.ok(!source.includes('search'));

  const html = read('blocked.html');
  assert.ok(!html.includes('location.search'));
  assert.ok(!html.includes('blockedUrl'));
});

test('the service worker reports only blocked URLs to the native bridge', () => {
  const source = read('background.js');
  const posts = source.match(/postMessage\(/g) ?? [];

  assert.equal(posts.length, 1);
  assert.match(source, /port\.postMessage\(\{ type: 'blocked', url \}\)/);
  assert.match(source, /const url = sanitizeUrl\(rawUrl\);/);
  assert.ok(!source.includes("type: 'navigate'"));
  assert.ok(!source.includes('history'));
});
