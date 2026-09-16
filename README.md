# Outlet P&L Command Center — Vercel deployment

Live version of the Royal Golf Club outlet P&L dashboard, deployed on Vercel at
`outlet.theroyalgolfclub.com`. Same real Odoo data (Restaurants-POS outlets,
real actuals/budget) as the self-hosted `outlet_pnl_dashboard.py` version —
see that file's comments for the full explanation of the data model.

## What's here

- `public/index.html` — the dashboard (static, fetches `/api/pnl`)
- `api/pnl.py` — Vercel Python serverless function that queries Odoo and
  returns the same JSON shape the dashboard expects
- `middleware.js` — Edge Middleware that HTTP-Basic-Auth-protects every
  route (dashboard + API) — nothing is visible without a login
- `vercel.json` — gives the Python function up to 60s to run (a cold,
  uncached request fetches several months of Odoo data in parallel)

## One-time setup (do this in the Vercel dashboard — I can't do this part,
it needs your Vercel login)

1. **Import / connect this repo** to the `rgc-revenue-dashboard` Vercel
   project (Settings → Git), if not already connected.

2. **Environment Variables** (Project → Settings → Environment Variables).
   Copy the same values from `.env.it_dashboard` on the office machine:

   | Name | Value |
   |---|---|
   | `ODOO_URL` | `https://mast-it-golf-main-15203359.dev.odoo.com` |
   | `ODOO_DB` | `mast-it-golf-main-15203359` |
   | `ODOO_USERNAME` | `ahmed@theroyalgolfclub.com` |
   | `ODOO_API_KEY` | *(copy from `.env.it_dashboard`)* |
   | `DASHBOARD_USERS` | `username1:password1,username2:password2,...` |

   `DASHBOARD_USERS` holds every login as comma-separated `username:password`
   pairs — add or remove people by editing this one value and redeploying.
   Apply them to Production (and Preview if you want previews to work too).
   **Never commit real values into the repo** — `.gitignore` already
   excludes `.env`, and this file only ever shows placeholder examples.

3. **Custom domain**: Project → Settings → Domains → Add →
   `outlet.theroyalgolfclub.com`. Vercel will show you a DNS record to add
   (a CNAME, the same pattern your `www.theroyalgolfclub.com` already uses
   pointing at `cname.vercel-dns.com`). Add that record with whoever
   manages public DNS for `theroyalgolfclub.com` — the internal AD DNS
   server (`rgcdc03`) is a different, internal-only zone and won't affect
   this.

4. **Redeploy** after adding the environment variables (Vercel doesn't
   apply new env vars to an already-running deployment).

## Verifying it worked

- Visit `https://outlet.theroyalgolfclub.com` — your browser should prompt
  for the username/password you set above before showing anything.
- First load after a deploy (or after the 10-minute cache expires) can take
  up to ~30–60s while `api/pnl.py` pulls fresh data from Odoo; after that
  it's served from Vercel's edge cache and loads instantly for everyone
  until the cache refreshes.

## Adjusting the trailing window

`api/pnl.py` defaults to a 9-month trailing window (`PNL_MONTHS=9`) rather
than the 13 months the self-hosted version uses, to keep a cold-cache
request safely inside serverless time limits. If your Vercel plan allows
longer function durations, raise `PNL_MONTHS` (and `vercel.json`'s
`maxDuration`) back toward 13.

## Managing dashboard logins

All logins live in the single `DASHBOARD_USERS` environment variable
(Project → Settings → Environment Variables), as `username:password` pairs
separated by commas — e.g. `Alice:pass1,Bob:pass2`. To add, remove, or
change someone's password: edit that value and **redeploy** (env var
changes don't apply to an already-running deployment). There's no user
database — passwords are plain values in that one variable, checked by
`middleware.js` on every request.

## Keeping this in sync with the self-hosted version

The dashboard frontend (`public/index.html`) is generated from the same
source as the office machine's `outlet_pnl_dashboard.html`. If you ask for
a change to one, ask for it to be applied to both, or they'll drift apart.
