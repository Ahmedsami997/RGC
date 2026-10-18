// GET /api/board?sid=<session>&from=<events already held>&since=<head.updated last seen>
// The board page polls this. When nothing changed since `since`, the answer costs a single Redis read.
import { pipeline } from './_redis.js';

const SID = /^s-\d{8}-\d{6}[A-Za-z0-9-]{0,24}$/;
const RUNNING_WINDOW_MS = 10 * 60 * 1000; // a session nobody has written to for 10 minutes no longer counts as live

const parse = (v) => { try { return v ? JSON.parse(v) : null; } catch { return null; } };

export default async function handler(req, res) {
  res.setHeader('Cache-Control', 'no-store');
  if (req.method !== 'GET') return res.status(405).json({ error: 'method not allowed' });
  const sidParam = String(req.query.sid || '');
  const from = Math.max(0, parseInt(req.query.from, 10) || 0);
  const since = String(req.query.since || '');
  if (sidParam && !SID.test(sidParam)) return res.status(400).json({ error: 'bad sid' });
  try {
    const [rawHead] = await pipeline([['GET', 'aid:head']]);
    const head = parse(rawHead);
    const running = !!(head && head.status === 'running' && Date.now() - head.updated < RUNNING_WINDOW_MS);
    if (since && head && String(head.updated) === since) return res.status(200).json({ changed: false, running });

    const [ids] = await pipeline([['ZREVRANGE', 'aid:sessions', 0, 19]]);
    const sid = sidParam || ids[0] || '';
    const cmds = [];
    if (ids.length) cmds.push(['MGET', ...ids.map((i) => 'aid:sess:' + i)]);
    if (sid) cmds.push(['LRANGE', 'aid:ev:' + sid, from, -1], ['LLEN', 'aid:ev:' + sid], ['GET', 'aid:sess:' + sid]);
    const out = cmds.length ? await pipeline(cmds) : [];
    let n = 0;
    const metas = ids.length ? out[n++] : [];
    const sessions = ids.map((id, i) => ({ id, ...(parse(metas[i]) || {}) })).filter((s) => s.idea !== undefined);
    let events = [], total = 0, session = null;
    if (sid) {
      events = (out[n++] || []).map(parse).filter(Boolean);
      total = out[n++] || 0;
      const doc = parse(out[n++]);
      session = doc ? { id: sid, ...doc } : null;
    }
    return res.status(200).json({ changed: true, head, running, sid, sessions, session, events, total });
  } catch (e) {
    console.error('board failed:', e && e.message);
    return res.status(500).json({ error: e && e.message === 'redis-not-configured' ? 'redis-not-configured' : 'server error' });
  }
}
