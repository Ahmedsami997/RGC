"""
Vercel serverless function: GET /api/pnl-detail?period=YYYY-MM&department=<id|consolidated>

Full GL-account-level "Consolidated Profit & Loss Statement" — every income,
cost of sales, payroll, operating expense, advertising, owners expense,
depreciation and interest line, exactly matching the finance team's manual
Excel report layout (see GL_TEMPLATE below, extracted directly from that
report). Also returns the "F&B Revenue & Cost of Sales by Outlet" matrix
when department=consolidated.

Companion to api/pnl.py (same Odoo credentials/env vars); kept as a
separate function/file since Vercel routes one file per API path.
"""
import json
import os
import threading
import time
from calendar import monthrange
from datetime import date
from http.server import BaseHTTPRequestHandler
import urllib.parse
import urllib.request

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

GL_TEMPLATE = [
    ('INCOME', '404100', 'SALES - GOLF MEMBER REVENUE'),
    ('INCOME', '404200', 'SALES - 18 HOLE - PRIME'),
    ('INCOME', '404201', 'SALES - 18 HOLE - NON PRIME'),
    ('INCOME', '404202', 'SALES - TOURNAMENT'),
    ('INCOME', '404205', 'SALES - OTHER GREEN FEE'),
    ('INCOME', '404206', 'SALES - HOTEL PRIME'),
    ('INCOME', '404207', 'SALES - HOTEL NON-PRIME'),
    ('INCOME', '404210', 'TROON PROGRAMS'),
    ('INCOME', '404211', 'SALES - JUNIOR'),
    ('INCOME', '404213', 'SALES - 9 HOLES'),
    ('INCOME', '404214', 'SALES- WEE MONTY'),
    ('INCOME', '404215', 'SALES- NIGHT GOLF'),
    ('INCOME', '404216', 'SALES - PGA/CART FEES'),
    ('INCOME', '404220', 'SALES - COMP GOLF'),
    ('INCOME', '404224', 'SALES - MEMBER ROUNDS'),
    ('INCOME', '404225', 'SALES - MEMBER GUEST PRIME'),
    ('INCOME', '404226', 'SALES - MEMBER GUEST NON-PRIME'),
    ('INCOME', '404231', 'SALES - GOLF LESSONS'),
    ('INCOME', '404232', 'SALES - RANGE RENTALS OTHER GOLF REVENUE'),
    ('INCOME', '404234', 'SALES - TRACKMAN'),
    ('INCOME', '404236', 'SALES - MERCHANDISE'),
    ('INCOME', '404240', 'SALES - MEMBER GUEST ROUNDS'),
    ('INCOME', '404300', 'SALES - PERSONAL TRAINING'),
    ('INCOME', '404301', 'SALES - PERSONAL SERVICES - FITNESS'),
    ('INCOME', '404302', 'SALES - PROGRAM FEES - FITNESS'),
    ('INCOME', '404305', 'SALES - FITNESS GUEST FEES'),
    ('INCOME', '404306', 'SALES - OTHER FITNESS REVENUE'),
    ('INCOME', '404307', 'SALES - COUNTRY CLUB MEMBERSHIP REVENUE'),
    ('INCOME', '404309', 'SALES - CLASSES'),
    ('INCOME', '404311', 'SALES - PERSONAL TRAINING'),
    ('INCOME', '404313', 'SALES - SWIM LESSONS'),
    ('INCOME', '404316', 'SALES - BALLET CLASSES'),
    ('INCOME', '404317', 'SALES - PADEL-SERVICES'),
    ('INCOME', '404400', 'SALES - FOOD'),
    ('INCOME', '404403', 'SALES - DISCOUNT FOOD'),
    ('INCOME', '404405', 'SALES - BEER'),
    ('INCOME', '404406', 'SALES - WINE'),
    ('INCOME', '404407', 'SALES - LIQUOR'),
    ('INCOME', '404408', 'SALES - SOFT DRINKS'),
    ('INCOME', '404409', 'SALES - TOBACCO'),
    ('INCOME', '404411', 'SALES - DISCOUNT BEVERAGE'),
    ('INCOME', '404413', 'SALES - OTHER F&B INCOME'),
    ('INCOME', '404416', 'SALES - F&B ELEMENTS MEMBERSHIP'),
    ('INCOME', '404500', 'SALES - RIFFA VIEWS VILLA MAINTENANCE'),
    ('INCOME', '404502', 'SALES - SHAIKH HAMOOD VILLA MAINT'),
    ('INCOME', '404503', 'JAMAL ABULLA SALMAN KAMAL VILL'),
    ('INCOME', '404506', 'SALES - RASHID EQUESTRIAN & HORSE RACI'),
    ('INCOME', '404508', 'SALES - BASEL MOHD ABU ALFATAH V.4165'),
    ('INCOME', '404509', 'SALES - SWIMMING POOL MAINTENACE'),
    ('INCOME', '404511', 'SALES - ROYAL COURT 2 VILLAS WINDOW CL'),
    ('INCOME', '404512', 'SALES - HORSE RACE TRACK RO PLANT MAIN'),
    ('INCOME', '404513', 'SALES - BASEL MOHD AÁLI VILLA MAINT'),
    ('INCOME', '404514', 'SALES - HORSE RACE CLUB FRONT ENTRANCE'),
    ('INCOME', '404515', 'SALES - RV OASIS & LAGOON ESTATE MAINT'),
    ('INCOME', '404521', 'SALES - JASRA GARDEN RENOVATION'),
    ('INCOME', '404526', 'HORSE CLUB RACE DAY & DIVOT RE'),
    ('INCOME', '404529', 'SALES - MAINT OF SAND TRACK @HORSE RAC'),
    ('INCOME', '404549', 'SALES - GENERAL LANDSCAPE REVENUE'),
    ('INCOME', '404548', 'SALES - GENERAL LANDSCAPE REVENUE'),
    ('INCOME', '404550', 'SALES LS OUTSIDE PROJECTS'),
    ('INCOME', '404553', 'SHISHA SALES'),
    ('INCOME', '404600', 'SALES -CART MAINTENANCE REVENUE'),
    ('INCOME', '404601', 'SALES -REPAIR REVENUE'),
    ('INCOME', '404602', 'SALES- CART SPARE PARTS REVENUE'),
    ('INCOME', '404603', 'SALES - CART LEASE REVENUE'),
    ('INCOME', '404604', 'SALES - CART TRANSPORTATION REVENUE'),
    ('INCOME', '404605', 'SALES - CART SALES REVENUE'),
    ('INCOME', '404606', 'SALES - CART BATTERY SALE REVENUE'),
    ('INCOME', '404607', 'SALES - CART TYRE SALE REVENUE'),
    ('INCOME', '404608', 'SALES CART DIV OTHER REVENUE'),
    ('INCOME', '404609', 'SALES - 2ND HAND GOLF CART'),
    ('INCOME', '404702', 'SALES - CART TYRE SALE REVENUE'),
    ('INCOME', '404701', 'SALES - OTHER INCOME'),
    ('INCOME', '404703', 'SALES - PARTNERSHIP'),
    ('INCOME', '404704', 'SALES- OFFICE RENT REVENUE'),
    ('INCOME', '404705', 'SALES- OTHER REVENUE PARTNERSHIP'),
    ('COST OF SALES', '505200', 'COS-GOLF FEES'),
    ('COST OF SALES', '505231', 'COS - GOLF LESSONS'),
    ('COST OF SALES', '505232', 'COS - RANGE, RENTALS , OTH'),
    ('COST OF SALES', '505309', 'COS - COMM COUNTRY CLUB CLASSES'),
    ('COST OF SALES', '505310', 'COS-CLASSES'),
    ('COST OF SALES', '505311', 'COS - PERSONAL TRAINING'),
    ('COST OF SALES', '505350', 'COS - GOLF'),
    ('COST OF SALES', '505400', 'COS - FOOD'),
    ('COST OF SALES', '505401', 'COS - FOOD WASTAGE/SPOILAGE'),
    ('COST OF SALES', '505405', 'COS - BEER'),
    ('COST OF SALES', '505406', 'COS - WINE'),
    ('COST OF SALES', '505407', 'COS - LIQUOR'),
    ('COST OF SALES', '505408', 'COS - SOFT DRINKS'),
    ('COST OF SALES', '505409', 'COS - TOBACCO'),
    ('COST OF SALES', '505410', 'COS - BEVERAGE/SPOILAGE'),
    ('COST OF SALES', '505500', 'COS - RIFFA VIEWS VILLA MAINTENA'),
    ('COST OF SALES', '505506', 'COS - RASHID EQUESTRIAN & HORSE RACI'),
    ('COST OF SALES', '505509', 'COS - SWIMMING POOL MAINTENANCE'),
    ('COST OF SALES', '505514', 'COS - HORSE CLUB RACE DAY & DIVOT RE'),
    ('COST OF SALES', '505515', 'COS - RV OASIS & LAGOON ESTATE MAINTENANCE'),
    ('COST OF SALES', '505516', 'COS - LANDSCAPE MAINT NEW CLIENTS'),
    ('COST OF SALES', '505517', 'COS - GARDEN SHOW'),
    ('COST OF SALES', '505518', 'COS- GENERAL LANDSCAPE'),
    ('COST OF SALES', '505529', 'COS - MAINT OF SAND TRACK @HORSE RAC'),
    ('COST OF SALES', '505530', 'COS - LSC WORK AT NEW GARDEN RV L-15'),
    ('COST OF SALES', '505531', 'COS - CROSSING LSC WORKS AT REHC(CEB'),
    ('COST OF SALES', '505532', 'COS - LSC NEW PROJECTS'),
    ('COST OF SALES', '505839', 'HORSE RACE TRACK FERTILIZER SU'),
    ('COST OF SALES', '505875', 'LANDSCAPE'),
    ('COST OF SALES', '505570', 'COS-F&B OTHERS'),
    ('COST OF SALES', '505600', 'COS- CART MAINTENANCE'),
    ('COST OF SALES', '505601', 'COS- CART REPAIR'),
    ('COST OF SALES', '505602', 'COS- CFD CART SPARE PARTS'),
    ('COST OF SALES', '505604', 'COS- CART TRANSPORATION'),
    ('COST OF SALES', '505605', 'COS- CART SALE'),
    ('COST OF SALES', '505606', 'COS- CART BATTERY SALE'),
    ('COST OF SALES', '505607', 'COS- CART TYRE SALE'),
    ('COST OF SALES', '505608', 'COS- CART DIV OTHER COS'),
    ('COST OF SALES', '505609', 'COS- 2 HAND CART COS'),
    ('COST OF SALES', '505630', 'COS - WET BAR'),
    ('COST OF SALES', '505701', 'COS- OTHERS'),
    ('COST OF SALES', '505815', 'Others COS'),
    ('COST OF SALES', '500000', 'COST OF GOODS SOLD'),
    ('PAYROLL EXPENSES', '637000', 'PAYROLL / SALARY'),
    ('PAYROLL EXPENSES', '637001', 'PAYROLL - SALES COMMISSIONS'),
    ('PAYROLL EXPENSES', '637004', 'PAYROLL - TAMKEEN SUPPORT'),
    ('PAYROLL EXPENSES', '637007', 'PAYROLL OVERTIME'),
    ('PAYROLL EXPENSES', '637012', 'INCENTIVE/BONUS'),
    ('PAYROLL EXPENSES', '637015', 'PAYROLL VACATION'),
    ('PAYROLL EXPENSES', '637020', 'PAYROLL TAXES GOSI'),
    ('PAYROLL EXPENSES', '637029', 'PAYROLL TAXES LMRA'),
    ('PAYROLL EXPENSES', '637030', 'WORKERS COMPENSATION'),
    ('PAYROLL EXPENSES', '637040', 'MEDICAL/DENTAL'),
    ('PAYROLL EXPENSES', '637055', 'STAFF TRANSPORTATION'),
    ('PAYROLL EXPENSES', '637060', 'STAFF ACCOMMODATION'),
    ('PAYROLL EXPENSES', '637065', 'HOUSING ALLOWANCE'),
    ('PAYROLL EXPENSES', '637070', 'FOOD ALLOWANCE'),
    ('PAYROLL EXPENSES', '637075', 'TRANSPORTATION ALLOWANCE'),
    ('PAYROLL EXPENSES', '637080', 'TELEPHONE ALLOWANCE'),
    ('PAYROLL EXPENSES', '637085', 'SCHOOL FEES'),
    ('PAYROLL EXPENSES', '637090', 'AIRFARE'),
    ('PAYROLL EXPENSES', '637095', 'OTHER ALLOWANCE'),
    ('PAYROLL EXPENSES', '637100', 'SEMINARS'),
    ('PAYROLL EXPENSES', '637110', 'EMPLOYEE UNIFORMS'),
    ('PAYROLL EXPENSES', '637115', 'SERVERS UNIFORM'),
    ('PAYROLL EXPENSES', '637130', 'EMPLOYEE RECOGNITION & REWARDS'),
    ('PAYROLL EXPENSES', '637140', 'RECRUITING & RELOCATION'),
    ('PAYROLL EXPENSES', '637145', 'VISA AND CPR'),
    ('PAYROLL EXPENSES', '637150', 'BUSINESS RELATED MEALS'),
    ('PAYROLL EXPENSES', '637170', 'OTHER EMPLOYEE RELATED'),
    ('PAYROLL EXPENSES', '637180', 'EMPLOYEE MEALS'),
    ('PAYROLL EXPENSES', '642000', 'CASH DIFFERENCE LOSS'),
    ('PAYROLL EXPENSES', '637190', 'TEMP/CASUAL/ LABOR'),
    ('PAYROLL EXPENSES', '637031', '401K INDEMINITY'),
    ('PAYROLL EXPENSES', '637181', 'OTHER EMPLOYEE BENEFITS'),
    ('PAYROLL EXPENSES', '637611', 'EMPLOYEE TRAINIG'),
    ('OPERATING EXPENSES', '707205', 'BANK FEES'),
    ('OPERATING EXPENSES', '700000', 'EXPENSES'),
    ('OPERATING EXPENSES', '707200', 'LEGAL FEES'),
    ('OPERATING EXPENSES', '707210', 'ACCOUNTING FEES'),
    ('OPERATING EXPENSES', '707215', 'TRAVEL EXPENSE'),
    ('OPERATING EXPENSES', '707220', 'OTHER PROFESSIONAL FEES'),
    ('OPERATING EXPENSES', '707250', 'FOOD TASTINGS'),
    ('OPERATING EXPENSES', '707265', 'LEVIES - BONUS ISSUED'),
    ('OPERATING EXPENSES', '707330', 'SMALL EQUIPMENT'),
    ('OPERATING EXPENSES', '707340', 'EQUIPMENT RENTAL'),
    ('OPERATING EXPENSES', '707350', 'EQUIPMENT LEASES'),
    ('OPERATING EXPENSES', '707360', 'VEHICLE LEASE'),
    ('OPERATING EXPENSES', '707400', 'GOLF OPERATING SUPPLIES'),
    ('OPERATING EXPENSES', '707405', 'RANGE BALLS/TEACHING AIDS/ACADEMY'),
    ('OPERATING EXPENSES', '707410', 'COURSE SUPPLIES'),
    ('OPERATING EXPENSES', '707430', 'OFFICE SUPPLIES'),
    ('OPERATING EXPENSES', '707435', 'REIMBURSEMENT OF WATER COST(RV)'),
    ('OPERATING EXPENSES', '707440', 'OPERATING SUPPLIES'),
    ('OPERATING EXPENSES', '707444', 'CLEANING SUPPLIES'),
    ('OPERATING EXPENSES', '707445', 'LOCKER ROOM SUPPLIES'),
    ('OPERATING EXPENSES', '707455', 'TOWELS/ROBES/GUEST CLOTHING'),
    ('OPERATING EXPENSES', '707460', 'LAUNDRY'),
    ('OPERATING EXPENSES', '707465', 'LINENS'),
    ('OPERATING EXPENSES', '707470', 'CHINA. GLASS & UTENSILS'),
    ('OPERATING EXPENSES', '707475', 'DP World TOUR 2026 F&B Exp'),
    ('OPERATING EXPENSES', '707480', 'TOURNAMENT EXPENSE'),
    ('OPERATING EXPENSES', '707500', 'POSTAGE'),
    ('OPERATING EXPENSES', '707510', 'FREIGHT & SHIPPING'),
    ('OPERATING EXPENSES', '707520', 'PARKING TKT/TRANSPORT/PETROL'),
    ('OPERATING EXPENSES', '707530', 'COMPUTER RELATED & TECH SUPPORT'),
    ('OPERATING EXPENSES', '707535', 'CONTROLS & AUDIT'),
    ('OPERATING EXPENSES', '707540', 'PRINTING& STATIONARY (OFFICE SUPP.)'),
    ('OPERATING EXPENSES', '707550', 'SAFETY SUPPLIES'),
    ('OPERATING EXPENSES', '707560', 'CREDIT CARD FEES'),
    ('OPERATING EXPENSES', '707570', 'CASH OVER/SHORT'),
    ('OPERATING EXPENSES', '707590', 'DUES, SUBSCRIPTIONS & SURVEYS'),
    ('OPERATING EXPENSES', '707593', 'EVENT EXPENSE'),
    ('OPERATING EXPENSES', '707595', 'ENTERTAINMENT/GIFTS'),
    ('OPERATING EXPENSES', '707610', 'R&M EQUIPMENT'),
    ('OPERATING EXPENSES', '707611', 'EMPLOYEE TRAINING'),
    ('OPERATING EXPENSES', '707615', 'R&M LIFTS (ELEVATORS)'),
    ('OPERATING EXPENSES', '707630', 'R&M IRRIGATION'),
    ('OPERATING EXPENSES', '707635', 'R&M AIRCONDITIONING'),
    ('OPERATING EXPENSES', '707640', 'R&M DRAINAGE'),
    ('OPERATING EXPENSES', '707645', 'R&M POOL'),
    ('OPERATING EXPENSES', '707650', 'CLEANING MAINTENANCE CONTRACTS'),
    ('OPERATING EXPENSES', '707655', 'R&M LAKES'),
    ('OPERATING EXPENSES', '707660', 'R&M BUILDING'),
    ('OPERATING EXPENSES', '707665', 'R&M ELECTRICAL & MECHANICAL'),
    ('OPERATING EXPENSES', '707670', 'LIGHT BULBS'),
    ('OPERATING EXPENSES', '707675', 'R&M PLUMBING'),
    ('OPERATING EXPENSES', '707678', 'R&M VEHICLES'),
    ('OPERATING EXPENSES', '707680', 'R&M CARTS - GOLF'),
    ('OPERATING EXPENSES', '707690', 'R&M BEVERAGE CARTS'),
    ('OPERATING EXPENSES', '707695', 'CONTRACT LABOR'),
    ('OPERATING EXPENSES', '707710', 'SEED & MULCH'),
    ('OPERATING EXPENSES', '707720', 'SAND & GRAVEL'),
    ('OPERATING EXPENSES', '707730', 'LANDSCAPING'),
    ('OPERATING EXPENSES', '707740', 'GAS, DIESEL, OIL & LUBRICANTS'),
    ('OPERATING EXPENSES', '707750', 'SMALL TOOLS'),
    ('OPERATING EXPENSES', '707755', 'MACHINERY SPARE PARTS'),
    ('OPERATING EXPENSES', '707760', 'FERTILIZERS'),
    ('OPERATING EXPENSES', '707770', 'CHEMICALS - HERBICIDES'),
    ('OPERATING EXPENSES', '707775', 'CHEMICALS - INSECTICIDES'),
    ('OPERATING EXPENSES', '707780', 'CHEMICALS - FUNGICIDES'),
    ('OPERATING EXPENSES', '707785', 'CHEMICALS - OTHER'),
    ('OPERATING EXPENSES', '707790', 'GROWTH REGULATOR'),
    ('OPERATING EXPENSES', '707795', 'WETTING AGENT'),
    ('OPERATING EXPENSES', '707800', 'ELECTRIC'),
    ('OPERATING EXPENSES', '707810', 'CABLE TV/MUSIC'),
    ('OPERATING EXPENSES', '707815', 'RO PLANT MAINTENANCE CHARGES'),
    ('OPERATING EXPENSES', '707830', 'WASTE REMOVAL'),
    ('OPERATING EXPENSES', '707835', 'TELEPHONE'),
    ('OPERATING EXPENSES', '707840', 'SECURITY'),
    ('OPERATING EXPENSES', '707850', 'PEST CONTROL'),
    ('OPERATING EXPENSES', '707865', 'MISCELLANEOUS EXPENSES'),
    ('OPERATING EXPENSES', '707472', 'MATERIAL TESTING'),
    ('OPERATING EXPENSES', '707625', 'R&M GENERAL MAINTENANCE'),
    ('OPERATING EXPENSES', '707880', 'FEES, PERMITS & LICENSES'),
    ('OPERATING EXPENSES', '707875', 'OTHER TAXES'),
    ('OPERATING EXPENSES', '707885', 'PROPERTY INSURANCE'),
    ('OPERATING EXPENSES', '707361', 'OFFICE RENTAL'),
    ('ADVERTISING & PROMOTION', '707230', 'ADVERTISING'),
    ('ADVERTISING & PROMOTION', '707240', 'PROMOTION'),
    ('OWNERS EXPENSES', '707895', 'OTHER OWNER RELATED EXP'),
    ('OWNERS EXPENSES', '707910', 'NO MAD RENT'),
    ('OWNERS EXPENSES', '707920', 'BASE MANAGEMENT FEES'),
    ('OWNERS EXPENSES', '808200', 'INTEREST EXPENSE -DEBT'),
    ('OWNERS EXPENSES', '808250', 'INTEREST EXPENSE - CAPITAL LEASE'),
    ('OWNERS EXPENSES', '808300', 'INTEREST INCOME'),
    ('OWNERS EXPENSES', '707960', 'CASH CAPITAL RESERVE'),
    ('DEPRECIATION', '808000', 'DEPRECIATION - BUILDING'),
    ('DEPRECIATION', '808010', 'DEPRECIATION - VEHICLES'),
    ('DEPRECIATION', '808030', 'DEPRECIATION - FURNITURE'),
    ('DEPRECIATION', '808040', 'DEPRECIATION - IT SOLUTIONS'),
    ('DEPRECIATION', '808050', 'DEPRECIATION - EQUIPMENT'),
    ('DEPRECIATION', '808060', 'DEPRECIATION - MACHINERY'),
    ('DEPRECIATION', '808070', 'DEPRECIATION - FRONT 9 DEVELOPMENT'),
    ('DEPRECIATION', '808020', 'DEPRECIATION - GOLF COURSE IMPROVEMENT'),
    ('INTEREST ON SHAREHOLDER LOAN', '808400', 'INTEREST ON MUMTALAKAT & OSOL LOAN'),
]

SECTION_ORDER = [
    'INCOME', 'COST OF SALES', 'PAYROLL EXPENSES', 'OPERATING EXPENSES',
    'ADVERTISING & PROMOTION', 'OWNERS EXPENSES', 'DEPRECIATION', 'INTEREST ON SHAREHOLDER LOAN',
]
CODE_TO_NAME = {code: name for section, code, name in GL_TEMPLATE}
NAME_TO_CODE = {}
for _section, _code, _name in GL_TEMPLATE:
    NAME_TO_CODE.setdefault(_name.strip().upper(), _code)

FB_OUTLETS = [d for d in DEPARTMENTS if d['id'] in (
    'dept-cafet', 'dept-bcart', 'dept-memberlounge', 'dept-links', 'dept-slice', 'dept-repartee', 'dept-banquet',
)]


def classify_fb_revenue_type(name):
    u = name.upper()
    if 'DISCOUNT FOOD' in u:
        return 'SALES - DISCOUNT FOOD'
    if 'FOOD' in u and 'ELEMENT' not in u:
        return 'SALES - FOOD'
    if 'DISCOUNT BEVERAGE' in u:
        return 'SALES - DISCOUNT BEVERAGE'
    if 'BEER' in u:
        return 'SALES - BEER'
    if 'WINE' in u:
        return 'SALES - WINE'
    if 'LIQUOR' in u:
        return 'SALES - LIQUOR'
    if 'SOFT DRINK' in u:
        return 'SALES - SOFT DRINKS'
    if 'TOBACCO' in u or 'SHISHA' in u:
        return 'SALES - TOBACCO'
    if 'ELEMENTS MEMBERSHIP' in u:
        return 'SALES - F&B ELEMENTS MEMBERSHIP'
    return 'SALES - OTHER F&B INCOME'


def classify_fb_cos_type(name):
    u = name.upper()
    if 'WASTAGE' in u or 'SPOILAGE' in u:
        return 'COS - FOOD WASTAGE/SPOILAGE' if 'FOOD' in u else 'COS - BEVERAGE/SPOILAGE'
    if 'FOOD' in u:
        return 'COS - FOOD'
    if 'BEER' in u:
        return 'COS - BEER'
    if 'WINE' in u:
        return 'COS - WINE'
    if 'LIQUOR' in u:
        return 'COS - LIQUOR'
    if 'SOFT DRINK' in u:
        return 'COS - SOFT DRINKS'
    if 'TOBACCO' in u:
        return 'COS - TOBACCO'
    return 'COS - F&B OTHERS'


def month_add(y, m, n):
    idx = (y * 12 + (m - 1)) + n
    return idx // 12, idx % 12 + 1


def month_bounds(y, m):
    last_day = monthrange(y, m)[1]
    return f'{y:04d}-{m:02d}-01', f'{y:04d}-{m:02d}-{last_day:02d}'


def days_in_month(y, m):
    return monthrange(y, m)[1]


def fetch_gl_lines_raw(odoo, date_from, date_to):
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
    enriched = []
    for line in lines:
        acc = acct_by_id.get(line['account_id'][0])
        if not acc:
            continue
        enriched.append({
            'code': acc['code'], 'account_type': acc['account_type'],
            'balance': line['balance'], 'dist': line['analytic_distribution'] or {},
        })
    return enriched


def fetch_gl_budget_raw(odoo, date_from, date_to):
    lines = odoo.execute_kw(
        'crossovered.budget.lines', 'search_read',
        [[
            ['analytic_account_id', 'in', DEPARTMENT_ANALYTIC_IDS],
            ['date_from', '<=', date_to], ['date_to', '>=', date_from],
        ]],
        {'fields': ['analytic_account_id', 'general_budget_id', 'planned_amount']},
    )
    out = []
    for l in lines:
        if not l['analytic_account_id'] or not l['general_budget_id']:
            continue
        code = NAME_TO_CODE.get(l['general_budget_id'][1].strip().upper())
        out.append({'analytic_id': l['analytic_account_id'][0], 'code': code, 'amount': l['planned_amount']})
    return out


def aggregate_gl_by_code(enriched_lines, target_analytic_ids):
    target_set = set(target_analytic_ids)
    by_code = {}
    for line in enriched_lines:
        is_revenue = line['account_type'] in ('income', 'income_other')
        raw = -line['balance'] if is_revenue else line['balance']
        for aid_s, pct in line['dist'].items():
            if int(aid_s) not in target_set:
                continue
            by_code[line['code']] = by_code.get(line['code'], 0) + raw * (pct / 100.0)
    return by_code


def aggregate_budget_by_code(budget_lines, target_analytic_ids):
    target_set = set(target_analytic_ids)
    by_code = {}
    for l in budget_lines:
        if l['analytic_id'] not in target_set or not l['code']:
            continue
        by_code[l['code']] = by_code.get(l['code'], 0) + abs(l['amount'])
    return by_code


def build_statement(actual_by_code, budget_by_code, py_by_code):
    sections = []
    for section_name in SECTION_ORDER:
        section_lines = []
        for section, code, name in GL_TEMPLATE:
            if section != section_name:
                continue
            section_lines.append({
                'code': code, 'name': name,
                'actual': round(actual_by_code.get(code, 0), 3),
                'budget': round(budget_by_code.get(code, 0), 3),
                'priorYear': round(py_by_code.get(code, 0), 3),
            })
        totals = {
            'actual': round(sum(l['actual'] for l in section_lines), 3),
            'budget': round(sum(l['budget'] for l in section_lines), 3),
            'priorYear': round(sum(l['priorYear'] for l in section_lines), 3),
        }
        sections.append({'name': section_name, 'lines': section_lines, 'totals': totals})
    return sections


def totals_for(sections, name):
    for s in sections:
        if s['name'] == name:
            return s['totals']
    return {'actual': 0, 'budget': 0, 'priorYear': 0}


def totals_sub(a, b):
    return {k: round(a[k] - b[k], 3) for k in a}


def build_fb_by_outlet(enriched_lines):
    outlets = FB_OUTLETS
    rev_order = [
        'SALES - FOOD', 'SALES - DISCOUNT FOOD', 'SALES - BEER', 'SALES - WINE', 'SALES - LIQUOR',
        'SALES - SOFT DRINKS', 'SALES - TOBACCO', 'SALES - DISCOUNT BEVERAGE',
        'SALES - OTHER F&B INCOME', 'SALES - F&B ELEMENTS MEMBERSHIP',
    ]
    cos_order = [
        'COS - FOOD', 'COS - FOOD WASTAGE/SPOILAGE', 'COS - BEER', 'COS - WINE', 'COS - LIQUOR',
        'COS - SOFT DRINKS', 'COS - TOBACCO', 'COS - BEVERAGE/SPOILAGE', 'COS - F&B OTHERS',
    ]
    outlet_by_analytic = {aid: o['id'] for o in outlets for aid in o['analyticIds']}
    rev_matrix = {t: {o['id']: 0.0 for o in outlets} for t in rev_order}
    cos_matrix = {t: {o['id']: 0.0 for o in outlets} for t in cos_order}

    for line in enriched_lines:
        is_revenue = line['account_type'] in ('income', 'income_other')
        is_cogs = line['account_type'] == 'expense_direct_cost' or (line['code'] or '').startswith('505')
        if not (is_revenue or is_cogs):
            continue
        raw = -line['balance'] if is_revenue else line['balance']
        name = CODE_TO_NAME.get(line['code'], '')
        for aid_s, pct in line['dist'].items():
            aid = int(aid_s)
            outlet_id = outlet_by_analytic.get(aid)
            if not outlet_id:
                continue
            amt = raw * (pct / 100.0)
            if is_revenue:
                t = classify_fb_revenue_type(name)
                rev_matrix.setdefault(t, {o['id']: 0.0 for o in outlets})
                rev_matrix[t][outlet_id] = rev_matrix[t].get(outlet_id, 0) + amt
            else:
                t = classify_fb_cos_type(name)
                cos_matrix.setdefault(t, {o['id']: 0.0 for o in outlets})
                cos_matrix[t][outlet_id] = cos_matrix[t].get(outlet_id, 0) + amt

    def rows_from(matrix, order):
        rows = []
        for t in order:
            by_outlet = matrix.get(t, {})
            rows.append({
                'type': t,
                'byOutlet': {k: round(v, 3) for k, v in by_outlet.items()},
                'total': round(sum(by_outlet.values()), 3),
            })
        return rows

    revenue_rows = rows_from(rev_matrix, rev_order)
    cos_rows = rows_from(cos_matrix, cos_order)
    revenue_total_by_outlet = {o['id']: round(sum(rev_matrix[t].get(o['id'], 0) for t in rev_order), 3) for o in outlets}
    cos_total_by_outlet = {o['id']: round(sum(cos_matrix[t].get(o['id'], 0) for t in cos_order), 3) for o in outlets}
    return {
        'outlets': [{'id': o['id'], 'name': o['name']} for o in outlets],
        'revenueRows': revenue_rows,
        'revenueTotalByOutlet': revenue_total_by_outlet,
        'revenueGrandTotal': round(sum(revenue_total_by_outlet.values()), 3),
        'cosRows': cos_rows,
        'cosTotalByOutlet': cos_total_by_outlet,
        'cosGrandTotal': round(sum(cos_total_by_outlet.values()), 3),
    }


_detail_cache = {}
_detail_lock = threading.Lock()


def get_detail_data(period, department_id):
    y, m = int(period[:4]), int(period[5:7])
    today = date.today()
    df, dt = month_bounds(y, m)
    is_current = (y == today.year and m == today.month)
    if is_current:
        dt = f'{y:04d}-{m:02d}-{min(today.day, days_in_month(y, m)):02d}'
    py_y, py_m = month_add(y, m, -12)
    py_df, py_dt = month_bounds(py_y, py_m)
    if is_current:
        py_dt = f'{py_y:04d}-{py_m:02d}-{min(today.day, days_in_month(py_y, py_m)):02d}'

    with _detail_lock:
        cached = _detail_cache.get(period)
        if cached and time.time() - cached['ts'] < CACHE_SECONDS:
            odoo = None
            lines, py_lines, budget_lines = cached['lines'], cached['py_lines'], cached['budget_lines']
        else:
            odoo = Odoo()
            lines = fetch_gl_lines_raw(odoo, df, dt)
            py_lines = fetch_gl_lines_raw(odoo, py_df, py_dt)
            budget_lines = fetch_gl_budget_raw(odoo, df, dt)
            _detail_cache[period] = {'ts': time.time(), 'lines': lines, 'py_lines': py_lines, 'budget_lines': budget_lines}

    if department_id == 'consolidated':
        target_ids = DEPARTMENT_ANALYTIC_IDS
        dept_name = 'Consolidated (All Departments)'
    else:
        dept = next((d for d in DEPARTMENTS if d['id'] == department_id), None)
        if not dept:
            raise ValueError('Unknown department: ' + department_id)
        target_ids = dept['analyticIds']
        dept_name = dept['name']

    actual_by_code = aggregate_gl_by_code(lines, target_ids)
    py_by_code = aggregate_gl_by_code(py_lines, target_ids)
    budget_by_code = aggregate_budget_by_code(budget_lines, target_ids)

    sections = build_statement(actual_by_code, budget_by_code, py_by_code)
    income = totals_for(sections, 'INCOME')
    cos = totals_for(sections, 'COST OF SALES')
    payroll = totals_for(sections, 'PAYROLL EXPENSES')
    opex = totals_for(sections, 'OPERATING EXPENSES')
    adv = totals_for(sections, 'ADVERTISING & PROMOTION')
    owners = totals_for(sections, 'OWNERS EXPENSES')
    dep = totals_for(sections, 'DEPRECIATION')
    interest = totals_for(sections, 'INTEREST ON SHAREHOLDER LOAN')

    gross_profit = totals_sub(totals_sub(totals_sub(totals_sub(income, cos), payroll), opex), adv)
    operating_profit = totals_sub(totals_sub(gross_profit, owners), dep)
    net_profit = totals_sub(operating_profit, interest)

    result = {
        'period': period, 'departmentId': department_id, 'departmentName': dept_name,
        'sections': sections,
        'grossProfit': gross_profit, 'operatingProfit': operating_profit, 'netProfit': net_profit,
    }
    if department_id == 'consolidated':
        result['fbByOutlet'] = build_fb_by_outlet(lines)
    return result


class handler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def do_GET(self):
        try:
            parsed = urllib.parse.urlparse(self.path)
            qs = urllib.parse.parse_qs(parsed.query)
            today = date.today()
            period = (qs.get('period') or [None])[0] or f'{today.year:04d}-{today.month:02d}'
            department = (qs.get('department') or ['consolidated'])[0]
            data = get_detail_data(period, department)
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
