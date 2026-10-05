import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(new URL(`../public/${path}`, import.meta.url), 'utf8').replaceAll('\r\n', '\n');
const headers = read('_headers');
const index = read('index.html');
const csp = headers.match(/Content-Security-Policy: (.+)/)[1];
const directive = (name) => csp.split(';').map((d) => d.trim().split(/\s+/)).find(([n]) => n === name)?.slice(1) ?? [];

test('every page is served with HSTS', () => {
  assert.match(headers, /^\/\*\n(?:  .+\n)*  Strict-Transport-Security: max-age=\d{7,}; includeSubDomains\n/m);
});

test('the CSP admits Cloudflare Web Analytics and nothing else from outside', () => {
  assert.deepEqual(directive('script-src'), ["'self'", 'https://static.cloudflareinsights.com']);
  assert.deepEqual(directive('connect-src'), ["'self'", 'https://cloudflareinsights.com']);
  assert.deepEqual(directive('style-src'), ["'self'"]);
});

test('phone visitors get a menu with every header link', () => {
  const menu = index.match(/<details class="menu">([\s\S]*?)<\/details>/);
  assert.ok(menu, 'menu present');
  for (const label of ['Levels', 'Download', 'Help', 'Sign in']) assert.match(menu[1], new RegExp(`>${label}</a>`));
});

test('asking for an invitation goes to the portal help page', () => {
  assert.match(index, /<a href="https:\/\/app\.s4ops\.com\/Support">Ask for an invitation<\/a>/);
});

test('pages carry no inline scripts or style attributes', () => {
  for (const page of ['index.html', '404.html']) {
    const html = read(page);
    assert.doesNotMatch(html, /\sstyle="/, page);
    assert.doesNotMatch(html, /<script(?![^>]*\ssrc=)[^>]*>/, page);
  }
});
