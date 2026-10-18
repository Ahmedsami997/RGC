// POST /api/ingest — where an /ai-discuss session in Claude Code sends what is being said.
//
//   Authorization: Bearer <AI_DISCUSS_TOKEN>
//   { "sid": "s-20261018-101500",
//     "session": { "idea": "...", "status": "running", "cast": {"members": [...]}, "castMeta": {...} },  // optional, merged
//     "events":  [ { "kind": "speech", "who": "analyst", "text": "...", ... }, ... ] }                    // optional, appended in order
//   { "ping": true }  -> { ok: true, pong: true }   (checks the URL and the token, writes nothing)
//
// The server numbers the events itself (seq), so the sender never has to.
import { timingSafeEqual } from 'node:crypto';
import { pipeline } from './_redis.js';

const SID = /^s-\d{8}-\d{6}[A-Za-z0-9-]{0,24}$/;
const KEY = /^[a-z0-9_-]{1,30}$/;
const COLOR = /^#[0-9a-fA-F]{6}$/;
const KINDS = new Set(['phase', 'thinking', 'speech', 'score', 'note', 'decision', 'final', 'end']);
const STATUS = new Set(['running', 'approved', 'no-agreement', 'stopped']);
const LIMIT = { batch: 50, eventBytes: 20000, perSession: 400, ttl: 60 * 60 * 24 * 90 };

const fail = (status, message) => Object.assign(new Error(message), { status });

function authorized(req) {
  const expected = process.env.AI_DISCUSS_TOKEN;
  if (!expected || expected.length < 24) return false; // unset or weak token: reject everything
  const got = Buffer.from((req.headers.authorization || '').replace(/^Bearer\s+/i, ''));
  const want = Buffer.from(expected);
  return got.length === want.length && timingSafeEqual(got, want);
}

function cleanSession(s) {
  const out = {};
  const str = (v, n) => String(v).slice(0, n);
  if (s.idea != null) out.idea = str(s.idea, 500);
  if (s.started != null) out.started = str(s.started, 40);
  if (s.status != null) {
    if (!STATUS.has(s.status)) throw fail(400, 'bad status');
    out.status = s.status;
  }
  if (s.cast && Array.isArray(s.cast.members)) {
    out.cast = { members: s.cast.members.slice(0, 12).map((k) => { if (!KEY.test(k)) throw fail(400, 'bad member key'); return k; }) };
  }
  if (s.castMeta && typeof s.castMeta === 'object') {
    out.castMeta = {};
    for (const [k, v] of Object.entries(s.castMeta).slice(0, 12)) {
      if (!KEY.test(k) || !v || typeof v !== 'object') throw fail(400, 'bad castMeta');
      out.castMeta[k] = { name: str(v.name || k, 40), emoji: str(v.emoji || '', 8), color: COLOR.test(v.color) ? v.color : '#9aa3ad' };
    }
  }
  return out;
}

function cleanEvent(e) {
  if (!e || typeof e !== 'object' || Array.isArray(e)) throw fail(400, 'event must be an object');
  if (!KINDS.has(e.kind)) throw fail(400, 'bad event kind');
  const who = e.who;
  const okWho = who == null || (typeof who === 'string' && (KEY.test(who) || who === 'members' || who === 'judges')) || (Array.isArray(who) && who.length <= 12 && who.every((k) => typeof k === 'string' && KEY.test(k)));
  if (!okWho) throw fail(400, 'bad who');
  const { sid, seq, ...rest } = e; // the server owns sid and seq
  if (JSON.stringify(rest).length > LIMIT.eventBytes) throw fail(413, 'event too large');
  return rest;
}

export default async function handler(req, res) {
  res.setHeader('Cache-Control', 'no-store');
  if (req.method !== 'POST') return res.status(405).json({ error: 'method not allowed' });
  if (!authorized(req)) return res.status(401).json({ error: 'unauthorized' });
  try {
    const body = typeof req.body === 'string' ? JSON.parse(req.body) : req.body;
    if (!body || typeof body !== 'object') throw fail(400, 'JSON body required');
    if (body.ping) return res.status(200).json({ ok: true, pong: true });
    const sid = body.sid;
    if (typeof sid !== 'string' || !SID.test(sid)) throw fail(400, 'bad sid');
    const events = body.events == null ? [] : body.events;
    if (!Array.isArray(events)) throw fail(400, 'events must be an array');
    if (events.length > LIMIT.batch) throw fail(413, 'too many events in one call');
    const cleaned = events.map(cleanEvent);
    const patch = cleanSession(body.session || {});

    const sessKey = 'aid:sess:' + sid, evKey = 'aid:ev:' + sid;
    const [rawSession, len] = await pipeline([['GET', sessKey], ['LLEN', evKey]]);
    if (len + cleaned.length > LIMIT.perSession) throw fail(413, 'session is full');
    const prev = rawSession ? JSON.parse(rawSession) : { idea: '', status: 'running', started: new Date().toISOString().slice(0, 10) };
    const session = Object.assign({}, prev, patch);
    const now = Date.now();

    const cmds = [];
    if (cleaned.length) {
      cmds.push(['RPUSH', evKey, ...cleaned.map((e, i) => JSON.stringify({ ...e, seq: len + i + 1 }))]);
      cmds.push(['EXPIRE', evKey, LIMIT.ttl]);
    }
    cmds.push(['SET', sessKey, JSON.stringify(session), 'EX', LIMIT.ttl]);
    cmds.push(['ZADD', 'aid:sessions', 'NX', now, sid]);
    cmds.push(['ZREMRANGEBYRANK', 'aid:sessions', 0, -201]);
    cmds.push(['SET', 'aid:head', JSON.stringify({ sid, status: session.status, updated: now, total: len + cleaned.length }), 'EX', LIMIT.ttl]);
    await pipeline(cmds);
    return res.status(200).json({ ok: true, total: len + cleaned.length });
  } catch (e) {
    if (e && e.status) return res.status(e.status).json({ error: e.message });
    console.error('ingest failed:', e && e.message);
    return res.status(500).json({ error: 'server error' });
  }
}
