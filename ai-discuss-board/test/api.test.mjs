// Run with:  node --test test/api.test.mjs        (no dependencies)
// A tiny in-memory stand-in for the Upstash REST "pipeline" endpoint is enough to exercise both handlers
// and the middleware exactly as Vercel would call them.
import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';

import ingest from '../api/ingest.js';
import board from '../api/board.js';
import middleware from '../middleware.js';

const TOKEN = 'k'.repeat(40);
const SID = 's-20261018-101500';

/* ---------- fake Upstash ---------- */
const kv = new Map(), lists = new Map(), zsets = new Map();
const rng = (arr, a, b) => { const n = arr.length; let s = a < 0 ? n + a : a, e = b < 0 ? n + b : b; s = Math.max(0, s); return [s, e]; };
function run([cmd, ...a]) {
  switch (cmd.toUpperCase()) {
    case 'GET': return kv.has(a[0]) ? kv.get(a[0]) : null;
    case 'SET': kv.set(a[0], a[1]); return 'OK';
    case 'MGET': return a.map((k) => (kv.has(k) ? kv.get(k) : null));
    case 'LLEN': return (lists.get(a[0]) || []).length;
    case 'RPUSH': { const l = lists.get(a[0]) || []; l.push(...a.slice(1)); lists.set(a[0], l); return l.length; }
    case 'LRANGE': { const l = lists.get(a[0]) || []; const [s, e] = rng(l, +a[1], +a[2]); return l.slice(s, e + 1); }
    case 'EXPIRE': return 1;
    case 'ZADD': { const z = zsets.get(a[0]) || new Map(); const nx = a[1] === 'NX'; const [score, member] = nx ? [a[2], a[3]] : [a[1], a[2]]; zsets.set(a[0], z); if (nx && z.has(member)) return 0; z.set(member, +score); return 1; }
    case 'ZREVRANGE': { const z = [...(zsets.get(a[0]) || new Map())].sort((x, y) => y[1] - x[1]).map((x) => x[0]); const [s, e] = rng(z, +a[1], +a[2]); return z.slice(s, e + 1); }
    case 'ZREMRANGEBYRANK': { const z = zsets.get(a[0]); if (!z) return 0; const sorted = [...z].sort((x, y) => x[1] - y[1]).map((x) => x[0]); const [s, e] = rng(sorted, +a[1], +a[2]); const gone = sorted.slice(s, e + 1); gone.forEach((m) => z.delete(m)); return gone.length; }
    default: throw new Error('unsupported ' + cmd);
  }
}
let server;
before(async () => {
  server = http.createServer((req, res) => {
    let raw = ''; req.on('data', (c) => (raw += c));
    req.on('end', () => {
      if (req.headers.authorization !== 'Bearer redis-secret') { res.writeHead(401); return res.end('{}'); }
      const out = JSON.parse(raw).map((c) => { try { return { result: run(c) }; } catch (e) { return { error: e.message }; } });
      res.writeHead(200, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(out));
    });
  });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  process.env.UPSTASH_REDIS_REST_URL = 'http://127.0.0.1:' + server.address().port;
  process.env.UPSTASH_REDIS_REST_TOKEN = 'redis-secret';
  process.env.AI_DISCUSS_TOKEN = TOKEN;
});
after(() => server.close());

const call = (handler, { method = 'GET', headers = {}, body, query = {} } = {}) => new Promise((resolve) => {
  const res = { code: 200, setHeader() {}, status(c) { this.code = c; return this; }, json(o) { resolve({ code: this.code, body: o }); return this; } };
  handler({ method, headers, body, query }, res);
});
const post = (body, token = TOKEN) => call(ingest, { method: 'POST', headers: token ? { authorization: 'Bearer ' + token } : {}, body });

/* ---------- ingest: who may write ---------- */
test('ingest rejects a missing or wrong token, and every request when the server token is unset or weak', async () => {
  assert.equal((await post({ ping: true }, '')).code, 401);
  assert.equal((await post({ ping: true }, 'x'.repeat(40))).code, 401);
  assert.equal((await post({ ping: true }, TOKEN.slice(0, -1))).code, 401);
  assert.equal((await call(ingest, { method: 'GET' })).code, 405);
  const saved = process.env.AI_DISCUSS_TOKEN;
  process.env.AI_DISCUSS_TOKEN = 'short';
  assert.equal((await post({ ping: true }, 'short')).code, 401);
  delete process.env.AI_DISCUSS_TOKEN;
  assert.equal((await post({ ping: true }, '')).code, 401);
  process.env.AI_DISCUSS_TOKEN = saved;
  assert.deepEqual((await post({ ping: true })).body, { ok: true, pong: true });
});

/* ---------- ingest: what it accepts ---------- */
test('ingest validates ids, kinds and sizes', async () => {
  assert.equal((await post({ sid: 'nope', events: [] })).code, 400);
  assert.equal((await post({ sid: SID, events: [{ kind: 'script' }] })).code, 400);
  assert.equal((await post({ sid: SID, events: [{ kind: 'speech', who: '<img src=x>' }] })).code, 400);
  assert.equal((await post({ sid: SID, session: { status: 'weird' } })).code, 400);
  assert.equal((await post({ sid: SID, events: Array.from({ length: 51 }, () => ({ kind: 'note' })) })).code, 413);
  assert.equal((await post({ sid: SID, events: [{ kind: 'note', text: 'x'.repeat(21000) }] })).code, 413);
});

test('ingest numbers events itself, ignores client sid/seq, and sanitises the session', async () => {
  const r1 = await post({ sid: SID, session: { idea: 'فكرة', status: 'running', cast: { members: ['analyst', 'architect'] }, castMeta: { poet: { name: 'الشاعر', emoji: '🪶', color: 'red" onload="x' } } }, events: [{ kind: 'phase', label: 'p1', seq: 99, sid: 'evil' }, { kind: 'thinking', who: 'members' }] });
  assert.equal(r1.code, 200); assert.equal(r1.body.total, 2);
  const r2 = await post({ sid: SID, events: [{ kind: 'speech', who: 'analyst', stance: 'SUPPORT', text: 'مرحبا', extras: [{ label: 'a', text: 'b' }] }] });
  assert.equal(r2.body.total, 3);
  const got = await call(board, { query: { sid: SID } });
  assert.equal(got.code, 200);
  assert.deepEqual(got.body.events.map((e) => e.seq), [1, 2, 3]);
  assert.equal(got.body.events[0].sid, undefined);
  assert.equal(got.body.session.idea, 'فكرة');
  assert.equal(got.body.session.castMeta.poet.color, '#9aa3ad', 'a non-hex colour must never be stored');
  assert.equal(got.body.running, true);
  assert.equal(got.body.sessions[0].id, SID);
});

/* ---------- board: reading ---------- */
test('board returns only new events for `from`, and a cheap "unchanged" answer for `since`', async () => {
  const full = await call(board, { query: { sid: SID } });
  const inc = await call(board, { query: { sid: SID, from: '2' } });
  assert.deepEqual(inc.body.events.map((e) => e.seq), [3]);
  assert.equal(inc.body.total, 3);
  const same = await call(board, { query: { sid: SID, from: '3', since: String(full.body.head.updated) } });
  assert.deepEqual(same.body, { changed: false, running: true });
  await post({ sid: SID, session: { status: 'approved' }, events: [{ kind: 'end', status: 'approved' }] });
  const after = await call(board, { query: { sid: SID, from: '3', since: String(full.body.head.updated) } });
  assert.equal(after.body.changed, true);
  assert.equal(after.body.running, false);
  assert.deepEqual(after.body.events.map((e) => e.kind), ['end']);
});

test('board follows the newest session when none is asked for, and rejects a malformed sid', async () => {
  await post({ sid: 's-20261019-090000', session: { idea: 'newer' }, events: [{ kind: 'note', text: 'hi' }] });
  const latest = await call(board, { query: {} });
  assert.equal(latest.body.sid, 's-20261019-090000');
  assert.deepEqual(latest.body.sessions.map((s) => s.id), ['s-20261019-090000', SID]);
  assert.equal((await call(board, { query: { sid: '../../etc' } })).code, 400);
  assert.equal((await call(board, { method: 'POST' })).code, 405);
});

test('a session is capped at 400 events', async () => {
  const sid = 's-20261020-000000';
  for (let i = 0; i < 8; i++) assert.equal((await post({ sid, events: Array.from({ length: 50 }, () => ({ kind: 'note', text: 'x' })) })).code, 200);
  assert.equal((await post({ sid, events: [{ kind: 'note', text: 'one too many' }] })).code, 413);
});

/* ---------- middleware: who may read ---------- */
test('middleware: login required everywhere, except the token-protected ingest endpoint', () => {
  process.env.DASHBOARD_USERS = 'Alice:pw1,Bob:pw2';
  const req = (path, auth) => new Request('https://board.test' + path, { headers: auth ? { authorization: 'Basic ' + btoa(auth) } : {} });
  assert.equal(middleware(req('/')).status, 401);
  assert.equal(middleware(req('/api/board')).status, 401);
  assert.equal(middleware(req('/', 'Alice:wrong')).status, 401);
  assert.equal(middleware(req('/', 'Mallory:pw1')).status, 401);
  assert.equal(middleware(req('/', 'Alice:pw1')), undefined);
  assert.equal(middleware(req('/api/board', 'Bob:pw2')), undefined);
  assert.equal(middleware(req('/api/ingest')), undefined, 'ingest has its own bearer token');
  assert.equal(middleware(req('/api/ingest/')).status, 401, 'only the exact path is exempt');
  delete process.env.DASHBOARD_USERS;
  assert.equal(middleware(req('/', 'Alice:pw1')).status, 401, 'no users configured: fail closed');
});
