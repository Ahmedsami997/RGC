// Vercel Edge Middleware — HTTP Basic Auth in front of the whole board (page and /api/board),
// same scheme as the outlet P&L dashboard: one env var, DASHBOARD_USERS, holding comma-separated
// "username:password" pairs (e.g. Alice:pass1,Bob:pass2). If no user is configured, every request is
// rejected (fails closed).
//
// One exception: POST /api/ingest is where Claude Code sends the discussion events. It cannot answer a
// browser login prompt, so it is exempt here and protected by its own secret instead (AI_DISCUSS_TOKEN,
// checked inside api/ingest.js, which rejects everything when the token is missing or too short).

export const config = {
  matcher: '/((?!_vercel/).*)',
};

function parseUsers(raw) {
  const users = {};
  if (!raw) return users;
  for (const pair of raw.split(',')) {
    const idx = pair.indexOf(':');
    if (idx === -1) continue;
    const user = pair.slice(0, idx).trim();
    const pass = pair.slice(idx + 1).trim();
    if (user) users[user] = pass;
  }
  return users;
}

export default function middleware(request) {
  if (new URL(request.url).pathname === '/api/ingest') return; // token-protected in the handler

  const users = parseUsers(process.env.DASHBOARD_USERS);
  const authHeader = request.headers.get('authorization');
  if (authHeader && Object.keys(users).length > 0) {
    const [scheme, encoded] = authHeader.split(' ');
    if (scheme === 'Basic' && encoded) {
      try {
        const decoded = atob(encoded);
        const sep = decoded.indexOf(':');
        const user = decoded.slice(0, sep);
        const pass = decoded.slice(sep + 1);
        if (Object.prototype.hasOwnProperty.call(users, user) && users[user] === pass) {
          return; // undefined => allow the request through
        }
      } catch (e) {
        // fall through to 401
      }
    }
  }

  return new Response('Authentication required', {
    status: 401,
    headers: { 'WWW-Authenticate': 'Basic realm="AI Discuss Board"' },
  });
}
