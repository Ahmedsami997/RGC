// Vercel Edge Middleware — HTTP Basic Auth gate in front of every route
// (the static dashboard AND /api/pnl), so the whole site requires a login
// before anyone can see live revenue/cost/budget data.
//
// Set DASHBOARD_USERNAME and DASHBOARD_PASSWORD as Vercel Environment
// Variables for this project. If either is missing, every request is
// rejected (fails closed) rather than left open.

export const config = {
  matcher: '/((?!_vercel/).*)',
};

export default function middleware(request) {
  const expectedUser = process.env.DASHBOARD_USERNAME;
  const expectedPass = process.env.DASHBOARD_PASSWORD;

  const authHeader = request.headers.get('authorization');
  if (authHeader && expectedUser && expectedPass) {
    const [scheme, encoded] = authHeader.split(' ');
    if (scheme === 'Basic' && encoded) {
      try {
        const decoded = atob(encoded);
        const sep = decoded.indexOf(':');
        const user = decoded.slice(0, sep);
        const pass = decoded.slice(sep + 1);
        if (user === expectedUser && pass === expectedPass) {
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
