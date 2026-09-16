// Vercel Edge Middleware — HTTP Basic Auth gate in front of every route
// (the static dashboard AND /api/pnl), so the whole site requires a login
// before anyone can see live revenue/cost/budget data.
//
// Multiple named logins are set via ONE Vercel Environment Variable,
// DASHBOARD_USERS, formatted as comma-separated "username:password" pairs:
//
//   DASHBOARD_USERS=Alice:changeme1,Bob:changeme2
//
// (The legacy single-user DASHBOARD_USERNAME/DASHBOARD_PASSWORD pair still
// works too, and is merged in alongside DASHBOARD_USERS if both are set.)
//
// If no users are configured at all, every request is rejected (fails
// closed) rather than left open.

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
  const users = parseUsers(process.env.DASHBOARD_USERS);
  if (process.env.DASHBOARD_USERNAME && process.env.DASHBOARD_PASSWORD) {
    users[process.env.DASHBOARD_USERNAME] = process.env.DASHBOARD_PASSWORD;
  }

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
    headers: { 'WWW-Authenticate': 'Basic realm="Outlet P&L Command Center"' },
  });
}
