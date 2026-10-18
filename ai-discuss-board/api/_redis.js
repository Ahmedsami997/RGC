// Minimal Upstash Redis REST client (no dependencies). The Upstash integration from the Vercel
// Marketplace injects either the UPSTASH_REDIS_REST_* or the legacy KV_REST_API_* variables; both work.
// Files starting with "_" in api/ are helpers, never routed as functions.

export async function pipeline(commands) {
  const url = process.env.UPSTASH_REDIS_REST_URL || process.env.KV_REST_API_URL;
  const token = process.env.UPSTASH_REDIS_REST_TOKEN || process.env.KV_REST_API_TOKEN;
  if (!url || !token) throw new Error('redis-not-configured');
  const r = await fetch(url.replace(/\/$/, '') + '/pipeline', {
    method: 'POST',
    headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' },
    body: JSON.stringify(commands),
  });
  if (!r.ok) throw new Error('redis-http-' + r.status);
  const out = await r.json();
  return out.map((o) => {
    if (o && o.error) throw new Error('redis: ' + o.error);
    return o.result;
  });
}
