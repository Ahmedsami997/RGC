"""
Vercel serverless function: GET /api/pnl

Same Odoo data model as the self-hosted outlet_pnl_dashboard.py (see that
file for the full commentary on the classification rules) — this version:
  - reads Odoo credentials from environment variables instead of a local
    .env file (set these in the Vercel project's Environment Variables,
    never commit them to git)
  - fetches months in parallel (ThreadPoolExecutor) to fit inside a
    serverless function's execution time limit
  - relies on Vercel's Edge cache (Cache-Control: s-maxage) rather than an
    in-process cache, since serverless instances don't share memory
  - defaults to a shorter trailing window (PNL_MONTHS, default 9) to keep
    a cold-cache request comfortably inside typical plan timeouts; raise
    it via the PNL_MONTHS environment variable if your plan allows longer
    function durations.
"""
import json
import os
from calendar import monthrange
from concurrent.futures import ThreadPoolExecutor
from datetime import date
from http.server import BaseHTTPRequestHandler
import urllib.request

N_MONTHS = int(os.environ.get('PNL_MONTHS', '9'))
CACHE_SECONDS = int(os.environ.get('PNL_CACHE_SECONDS', '600'))


def env(name):
    value = os.environ.get(name)
    if not value:
        raise RuntimeError(f'Missing required environment variable: {name}')
    return value


def rpc(odoo_url, service, method, args):
    payload = json.dumps({
        'jsonrpc': '2.0', 'method': 'call',
        'params': {'service': service, 'method': method, 'args': args},
        'id': 1,
    }).encode('utf-8')
    req = urllib.request.Request(
        odoo_url + '/jsonrpc', data=payload,
        headers={'Content-Type': 'application/json'}
    )
    with urllib.request.urlopen(req, timeout=45) as resp:
        result = json.loads(resp.read().decode('utf-8'))
    if 'error' in result:
        msg = result['error'].get('data', {}).get('message') or result['error'].get('message')
        raise RuntimeError(msg or 'Odoo RPC error')
    return result['result']


class Odoo:
    def __init__(self):
        self.url = env('ODOO_URL').rstrip('/')
        self.db = env('ODOO_DB')
        self.username = env('ODOO_USERNAME')
        self.api_key = env('ODOO_API_KEY')
        self.uid = rpc(self.url, 'common', 'login', [self.db, self.username, self.api_key])
        if not self.uid:
            raise RuntimeError('Odoo login failed — check ODOO_* environment variables')

    def execute_kw(self, model, method, args, kwargs=None):
        return rpc(self.url, 'object', 'execute_kw',
                    [self.db, self.uid, self.api_key, model, method, args, kwargs or {}])


OUTLETS = [
    {'id': 73, 'code': 'SLICE', 'name': 'Slice', 'category': 'Food & Beverage'},
    {'id': 75, 'code': 'REPARTEE', 'name': 'Repartee', 'category': 'Food & Beverage'},
    {'id': 77, 'code': 'LINKS', 'name': 'Links', 'category': 'Food & Beverage'},
    {'id': 59, 'code': 'MEMBER-LOUNGE', 'name': 'Member Lounge', 'category': 'Food & Beverage'},
    {'id': 74, 'code': 'CAFE-T', 'name': 'Cafe T', 'category': 'Food & Beverage'},
    {'id': 132, 'code': 'BCART1', 'name': 'Beverage Cart 1', 'category': 'Food & Beverage'},
    {'id': 193, 'code': 'BANQUET', 'name': 'Banquet', 'category': 'Banqueting & Events'},
    {'id': 195, 'code': 'GOLF-MGR', 'name': 'Golf Manager', 'category': 'Golf Operations'},
    {'id': 53, 'code': 'CRD', 'name': 'CRD (Reception)', 'category': 'Golf Operations'},
    {'id': 68, 'code': 'MEMBERSHIP', 'name': 'Membership', 'category': 'Membership'},
]
OUTLET_IDS = [o['id'] for o in OUTLETS]
REGION = {'id': 'rgn-bh-riffa', 'name': 'Riffa – Main Campus', 'country': 'Bahrain'}

# The 18 departments making up the real "Consolidated Profit & Loss
# Statement" — verified against the finance team's manual Aug-2026 Excel
# report. "Golf Operations" splits POS revenue (analytic "Golf Manager")
# from payroll/opex (analytic "Golf Operations") in Odoo, so both ids are
# summed to match the one department line finance reports.
DEPARTMENTS = [
    {'id': 'dept-gcm', 'name': 'Golf Course Maintenance', 'category': 'Golf Operations', 'analyticIds': [63]},
    {'id': 'dept-ro', 'name': 'RO Plant', 'category': 'Golf Operations', 'analyticIds': [69]},
    {'id': 'dept-landscape', 'name': 'Landscape', 'category': 'Golf Operations', 'analyticIds': [67]},
    {'id': 'dept-ga', 'name': 'G&A (Administration)', 'category': 'Corporate', 'analyticIds': [51]},
    {'id': 'dept-cartfleet', 'name': 'Cart Fleet', 'category': 'Golf Operations', 'analyticIds': [54]},
    {'id': 'dept-facility', 'name': 'Facility', 'category': 'Corporate', 'analyticIds': [60]},
    {'id': 'dept-membership', 'name': 'Membership', 'category': 'Membership', 'analyticIds': [68]},
    {'id': 'dept-otherpartner', 'name': 'Other / Partnership', 'category': 'Corporate', 'analyticIds': [128]},
    {'id': 'dept-fb', 'name': 'F&B (Shared)', 'category': 'Food & Beverage', 'analyticIds': [57]},
    {'id': 'dept-cafet', 'name': 'Cafe T', 'category': 'Food & Beverage', 'analyticIds': [74]},
    {'id': 'dept-links', 'name': 'Links', 'category': 'Food & Beverage', 'analyticIds': [77]},
    {'id': 'dept-banquet', 'name': 'Banquet', 'category': 'Banqueting & Events', 'analyticIds': [193]},
    {'id': 'dept-repartee', 'name': 'Repartee', 'category': 'Food & Beverage', 'analyticIds': [75]},
    {'id': 'dept-memberlounge', 'name': 'Member Lounge', 'category': 'Food & Beverage', 'analyticIds': [59]},
    {'id': 'dept-slice', 'name': 'Slice', 'category': 'Food & Beverage', 'analyticIds': [73]},
    {'id': 'dept-bcart', 'name': 'B Cart', 'category': 'Food & Beverage', 'analyticIds': [132]},
    {'id': 'dept-golfops', 'name': 'Golf Operations', 'category': 'Golf Operations', 'analyticIds': [64, 195]},
    {'id': 'dept-countryclub', 'name': 'Country Club', 'category': 'Golf Operations', 'analyticIds': [4]},
]
DEPARTMENT_ANALYTIC_IDS = sorted({aid for d in DEPARTMENTS for aid in d['analyticIds']})
ALL_ANALYTIC_IDS = sorted(set(OUTLET_IDS) | set(DEPARTMENT_ANALYTIC_IDS))
# Departments whose live Odoo total does not yet fully reconcile with the
# manual report (flagged in the UI rather than silently shown as exact).
UNRECONCILED_DEPARTMENTS = {'dept-countryclub', 'dept-fb', 'dept-golfops'}

PNL_KEYS = [
    'foodSales', 'beverageSales', 'deliverySales', 'otherRevenue',
    'foodCost', 'beverageCost', 'packagingCost',
    'payroll', 'overtime', 'staffBenefits', 'rent', 'utilities', 'marketing',
    'repairsMaintenance', 'cleaning', 'security', 'technology', 'otherExpenses',
    'depreciation', 'financeCost',
]


def empty_line():
    return {k: 0.0 for k in PNL_KEYS}


def classify_revenue(name):
    u = name.upper()
    if 'DELIVERY' in u:
        return 'deliverySales'
    if 'FOOD' in u:
        return 'foodSales'
    if any(k in u for k in ('BEER', 'WINE', 'LIQUOR', 'SOFT DRINK', 'BEVERAGE', 'COCKTAIL')):
        return 'beverageSales'
    return 'otherRevenue'


def classify_cost_of_sales(name):
    u = name.upper()
    if 'PACKAG' in u:
        return 'packagingCost'
    if any(k in u for k in ('BEER', 'WINE', 'LIQUOR', 'SOFT DRINK', 'BEVERAGE', 'COCKTAIL')):
        return 'beverageCost'
    return 'foodCost'


def classify_staff(name):
    u = name.upper()
    if 'OVERTIME' in u:
        return 'overtime'
    if any(k in u for k in ('SALARY', 'PAYROLL / SALARY', 'TEMP/CASUAL', 'CONTRACT LABOR')) and 'TAX' not in u:
        return 'payroll'
    return 'staffBenefits'


def classify_general_opex(name):
    u = name.upper()
    if any(k in u for k in ('ELECTRIC', 'WATER', 'UTILIT', 'GAS, DIESEL')):
        return 'utilities'
    if 'RENT' in u:
        return 'rent'
    if any(k in u for k in ('ADVERTIS', 'PROMOTION', 'MARKETING')):
        return 'marketing'
    if any(k in u for k in ('R&M', 'REPAIR', 'MAINTENANCE')):
        return 'repairsMaintenance'
    if any(k in u for k in ('CLEAN', 'WASTE REMOVAL')):
        return 'cleaning'
    if 'SECURIT' in u:
        return 'security'
    if any(k in u for k in ('IT RELATED', 'TECH SUPPORT', 'IT SOLUTIONS', 'SOFTWARE')):
        return 'technology'
    return 'otherExpenses'


def route_actual_line(account_type, code, name):
    code = code or ''
    if account_type in ('income', 'income_other'):
        return classify_revenue(name)
    if account_type == 'expense_direct_cost' or code.startswith('505'):
        return classify_cost_of_sales(name)
    if code.startswith('808'):
        return 'financeCost' if 'INTEREST' in name.upper() else 'depreciation'
    if account_type in ('expense', 'expense_depreciation'):
        if code.startswith('637'):
            return classify_staff(name)
        return classify_general_opex(name)
    return None


def route_budget_line(name):
    u = name.upper()
    if u.startswith('SALES') or ' REVENUE' in u or u.startswith('REVENUE'):
        return classify_revenue(name)
    if u.startswith('COS') or u.startswith('COST OF'):
        return classify_cost_of_sales(name)
    if 'DEPRECIAT' in u or 'AMORTIZ' in u:
        return 'depreciation'
    if 'INTEREST' in u or 'FINANCE CHARGE' in u or 'BANK CHARGE' in u:
        return 'financeCost'
    if any(k in u for k in (
        'PAYROLL', 'SALARY', 'WAGE', 'OVERTIME', 'GOSI', 'LMRA', 'TAMKEEN', 'MEDICAL', 'DENTAL',
        'HOUSING', 'ALLOWANCE', '401K', 'INDEMNITY', 'UNIFORM', 'TRAINING', 'RECRUIT', 'VACATION',
        'TRANSPORTATION', 'AIRFARE', 'VISA', 'CPR', 'RECOGNITION', 'REWARD', 'LABOR', 'EMPLOYEE',
    )):
        return classify_staff(name)
    return classify_general_opex(name)


def month_add(y, m, n):
    idx = (y * 12 + (m - 1)) + n
    return idx // 12, idx % 12 + 1


def month_bounds(y, m):
    last_day = monthrange(y, m)[1]
    return f'{y:04d}-{m:02d}-01', f'{y:04d}-{m:02d}-{last_day:02d}'


def days_in_month(y, m):
    return monthrange(y, m)[1]


def trailing_periods(n):
    today = date.today()
    y, m = today.year, today.month
    return [month_add(y, m, -i) for i in range(n - 1, -1, -1)]


def fetch_actual_month(odoo, date_from, date_to):
    lines = odoo.execute_kw(
        'account.move.line', 'search_read',
        [[
            ['date', '>=', date_from], ['date', '<=', date_to],
            ['parent_state', '=', 'posted'],
            ['account_id.account_type', 'in', [
                'income', 'income_other', 'expense', 'expense_direct_cost', 'expense_depreciation',
            ]],
            ['analytic_distribution', '!=', False],
        ]],
        {'fields': ['account_id', 'analytic_distribution', 'balance'], 'limit': 50000},
    )
    account_ids = sorted({l['account_id'][0] for l in lines if l['account_id']})
    accounts = odoo.execute_kw('account.account', 'read', [account_ids], {'fields': ['code', 'name', 'account_type']}) if account_ids else []
    acct_by_id = {a['id']: a for a in accounts}

    result = {aid: empty_line() for aid in ALL_ANALYTIC_IDS}
    for line in lines:
        dist = line['analytic_distribution'] or {}
        acc = acct_by_id.get(line['account_id'][0])
        if not acc:
            continue
        bucket = route_actual_line(acc['account_type'], acc['code'], acc['name'])
        if not bucket:
            continue
        is_revenue = acc['account_type'] in ('income', 'income_other')
        raw = -line['balance'] if is_revenue else line['balance']
        for analytic_id_str, pct in dist.items():
            analytic_id = int(analytic_id_str)
            if analytic_id not in result:
                continue
            result[analytic_id][bucket] += raw * (pct / 100.0)
    return result


def fetch_budget_month(odoo, date_from, date_to):
    lines = odoo.execute_kw(
        'crossovered.budget.lines', 'search_read',
        [[
            ['analytic_account_id', 'in', ALL_ANALYTIC_IDS],
            ['date_from', '<=', date_to], ['date_to', '>=', date_from],
        ]],
        {'fields': ['analytic_account_id', 'general_budget_id', 'planned_amount']},
    )
    result = {aid: empty_line() for aid in ALL_ANALYTIC_IDS}
    for line in lines:
        if not line['analytic_account_id'] or not line['general_budget_id']:
            continue
        analytic_id = line['analytic_account_id'][0]
        if analytic_id not in result:
            continue
        bucket = route_budget_line(line['general_budget_id'][1])
        if not bucket:
            continue
        result[analytic_id][bucket] += abs(line['planned_amount'])
    return result


def sum_lines(lines):
    out = empty_line()
    for line in lines:
        for k in PNL_KEYS:
            out[k] += line[k]
    return out


def build_period(odoo, y, m, today):
    df, dt = month_bounds(y, m)
    actual = fetch_actual_month(odoo, df, dt)
    budget = fetch_budget_month(odoo, df, dt)

    py_y, py_m = month_add(y, m, -12)
    is_current = (y == today.year and m == today.month)
    py_df, py_dt = month_bounds(py_y, py_m)
    if is_current:
        py_dt = f'{py_y:04d}-{py_m:02d}-{min(today.day, days_in_month(py_y, py_m)):02d}'
    try:
        prior_year = fetch_actual_month(odoo, py_df, py_dt)
    except Exception:
        prior_year = {aid: empty_line() for aid in ALL_ANALYTIC_IDS}

    if is_current:
        elapsed = min(today.day, days_in_month(y, m))
        run_rate = days_in_month(y, m) / max(elapsed, 1)
    else:
        run_rate = 1.0

    period_key = f'{y:04d}-{m:02d}'
    records = []
    for o in OUTLETS:
        a = actual[o['id']]
        b = budget[o['id']]
        p = prior_year[o['id']]
        forecast = {k: round(v * run_rate, 3) for k, v in a.items()} if is_current else dict(a)
        has_activity = any(abs(v) > 0.5 for v in a.values())
        status = 'Missing'
        if has_activity:
            status = 'Submitted' if is_current else 'Approved'
        records.append({
            'outletId': o['id'], 'period': period_key,
            'actual': a, 'budget': b, 'forecast': forecast, 'priorYear': p,
            'reportingStatus': status, 'hasActivity': has_activity,
        })

    department_records = []
    for d in DEPARTMENTS:
        d_actual = sum_lines([actual[aid] for aid in d['analyticIds']])
        d_budget = sum_lines([budget[aid] for aid in d['analyticIds']])
        d_prior = sum_lines([prior_year[aid] for aid in d['analyticIds']])
        d_forecast = {k: round(v * run_rate, 3) for k, v in d_actual.items()} if is_current else dict(d_actual)
        department_records.append({
            'departmentId': d['id'], 'period': period_key,
            'actual': d_actual, 'budget': d_budget, 'forecast': d_forecast, 'priorYear': d_prior,
            'reconciled': d['id'] not in UNRECONCILED_DEPARTMENTS,
        })

    return records, department_records


def build_dataset():
    odoo = Odoo()
    today = date.today()
    periods = trailing_periods(N_MONTHS)

    records = []
    department_records = []
    with ThreadPoolExecutor(max_workers=min(8, len(periods))) as pool:
        futures = [pool.submit(build_period, odoo, y, m, today) for (y, m) in periods]
        for fut in futures:
            recs, dept_recs = fut.result()
            records.extend(recs)
            department_records.extend(dept_recs)

    return {
        'regions': [REGION],
        'outlets': [
            {'id': o['id'], 'code': o['code'], 'name': o['name'], 'category': o['category'],
             'regionId': REGION['id'], 'city': 'Riffa'}
            for o in OUTLETS
        ],
        'periods': [f'{y:04d}-{m:02d}' for (y, m) in periods],
        'records': records,
        'departments': [
            {'id': d['id'], 'name': d['name'], 'category': d['category'], 'reconciled': d['id'] not in UNRECONCILED_DEPARTMENTS}
            for d in DEPARTMENTS
        ],
        'departmentRecords': department_records,
        'generatedAt': __import__('time').strftime('%Y-%m-%dT%H:%M:%S'),
        'source': 'odoo-live',
        'notes': [
            'Outlets are the real Point of Sale configs (pos.config) under Restaurants-POS, '
            'matched to their Odoo analytic account — same structure as your live setup.',
            'Actuals come from posted account.move.line entries split by each line\'s real '
            'analytic_distribution percentage. Budget comes from the Budget module '
            '(crossovered.budget.lines) for the same analytic accounts.',
            'Forecast = actual for closed months; for the current in-progress month it is a '
            'simple run-rate projection (actual-to-date scaled to a full month).',
            'Reporting status reflects whether Odoo has posted transactions for that outlet '
            'and month — it is not a formal sign-off workflow (Odoo has none configured).',
            'Only costs whose journal entry carries an analytic_distribution tag for that '
            'outlet are included. Shared/overhead costs not analytically allocated to a '
            'specific outlet will not appear here, so outlet-level margins can run higher '
            'than a fully-loaded P&L.',
            'The Consolidated P&L was checked line-by-line against the finance team\'s '
            'manual Aug-2026 report: 15 of 18 departments reconcile to within a small '
            'rounding/audit-adjustment tolerance. "Golf Operations", "Country Club" and '
            '"F&B (Shared)" do not yet fully reconcile (flagged in that page) — their '
            'manual report likely combines analytic tags or GL accounts this live feed '
            'does not yet know how to split the same way.',
        ],
    }


class handler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def do_GET(self):
        try:
            data = build_dataset()
            body = json.dumps(data, ensure_ascii=False).encode('utf-8')
            self.send_response(200)
            self.send_header('Content-Type', 'application/json; charset=utf-8')
            self.send_header('Cache-Control', f'public, s-maxage={CACHE_SECONDS}, stale-while-revalidate=590')
            self.end_headers()
            self.wfile.write(body)
        except Exception as e:
            body = json.dumps({'error': str(e)}, ensure_ascii=False).encode('utf-8')
            self.send_response(500)
            self.send_header('Content-Type', 'application/json; charset=utf-8')
            self.end_headers()
            self.wfile.write(body)
