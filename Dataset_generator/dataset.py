import os
import sys
import random
import time
import hashlib
import multiprocessing
from datetime import datetime, timedelta
from decimal import Decimal, ROUND_HALF_UP
from io import BytesIO
from reportlab.lib.pagesizes import A4
from reportlab.lib import colors
from reportlab.lib.units import mm
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, HRFlowable
)
from reportlab.lib.enums import TA_RIGHT, TA_CENTER
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.fonts import addMapping


# ─── FONT SETUP ────────────────────────────────────────────────────────────────
import platform

if platform.system() == "Windows":
    _FONT_DIR = r"C:\Users\anagha.kini\OneDrive - AVEVA Solutions Limited\dev\dejavu-fonts-ttf-2.37\dejavu-fonts-ttf-2.37\ttf" + "\\"
else:
    _FONT_DIR = "/usr/share/fonts/truetype/dejavu/"

F_NORMAL      = "DejaVu"
F_BOLD        = "DejaVu-Bold"
F_ITALIC      = "DejaVu-Italic"
F_BOLD_ITALIC = "DejaVu-BoldItalic"


def _register_fonts():
    try:
        pdfmetrics.registerFont(TTFont("DejaVu",            _FONT_DIR + "DejaVuSans.ttf"))
        pdfmetrics.registerFont(TTFont("DejaVu-Bold",       _FONT_DIR + "DejaVuSans-Bold.ttf"))
        pdfmetrics.registerFont(TTFont("DejaVu-Italic",     _FONT_DIR + "DejaVuSans-Oblique.ttf"))
        pdfmetrics.registerFont(TTFont("DejaVu-BoldItalic", _FONT_DIR + "DejaVuSans-BoldOblique.ttf"))
        addMapping("DejaVu", 0, 0, "DejaVu")
        addMapping("DejaVu", 1, 0, "DejaVu-Bold")
        addMapping("DejaVu", 0, 1, "DejaVu-Italic")
        addMapping("DejaVu", 1, 1, "DejaVu-BoldItalic")
    except Exception as e:
        print(f"[WARN] Font registration failed: {e}")


_register_fonts()

# ─── CONFIG ────────────────────────────────────────────────────────────────────
OUTPUT_DIR = "./invoices"
VAT_RATE   = Decimal("0.20")

CURRENCIES = [
    {"code": "USD", "symbol": "$"},
    {"code": "EUR", "symbol": "€"},
    {"code": "GBP", "symbol": "£"},
    {"code": "INR", "symbol": "₹"},
]

COMPANIES = [
    {"name": "Apex Digital Solutions",  "email": "billing@apexdigital.com",    "phone": "+1 (555) 201-4823",  "address": "300 Tech Park Ave, San Francisco, CA 94105",   "website": "www.apexdigital.com"},
    {"name": "BlueWave Consulting",     "email": "accounts@bluewaveconsult.io", "phone": "+44 20 7946 0321",   "address": "14 Harbour Road, London, EC2V 8RF",            "website": "www.bluewaveconsult.io"},
    {"name": "NovaBridge Labs",         "email": "finance@novabridgelabs.com",  "phone": "+1 (415) 883-2910",  "address": "88 Innovation Drive, Austin, TX 78701",        "website": "www.novabridgelabs.com"},
    {"name": "Greenline Systems",       "email": "invoices@greenlinesys.com",   "phone": "+49 89 12345678",    "address": "Marienplatz 5, 80331 Munich, Germany",         "website": "www.greenlinesys.com"},
    {"name": "Orion Cloud Works",       "email": "billing@orioncloud.net",      "phone": "+91 98765 43210",    "address": "Tower 2, Cyber City, Gurugram 122002, India",  "website": "www.orioncloud.net"},
    {"name": "Meridian Tech Partners",  "email": "finance@meridiantech.com",    "phone": "+1 (212) 555-9823",  "address": "1 World Trade Center, New York, NY 10007",     "website": "www.meridiantech.com"},
    {"name": "Cobalt Software Ltd.",    "email": "billing@cobaltsoft.co.uk",    "phone": "+44 161 820 3390",   "address": "22 Spinningfields, Manchester, M3 3AP",        "website": "www.cobaltsoft.co.uk"},
    {"name": "Pinnacle Data Corp.",     "email": "ar@pinnacledata.io",          "phone": "+61 2 9000 1234",    "address": "100 Market St, Sydney NSW 2000, Australia",    "website": "www.pinnacledata.io"},
    {"name": "Ironclad Ventures",       "email": "billing@ironcladventures.com","phone": "+1 (650) 300-7821",  "address": "500 Silicon Ave, Palo Alto, CA 94301",         "website": "www.ironcladventures.com"},
    {"name": "Horizon Digital Agency",  "email": "finance@horizondigital.io",   "phone": "+44 20 3456 7890",   "address": "78 Oxford Street, London, W1D 1BS",            "website": "www.horizondigital.io"},
]

CLIENTS = [
    {"name": "Stellar Retail Group",    "email": "accounts@stellarretail.com",  "address": "500 Commerce Blvd, New York, NY 10001"},
    {"name": "UrbanNest Interiors",     "email": "finance@urbannest.co",         "address": "22 Design Street, Chicago, IL 60601"},
    {"name": "Pinecrest Financial",     "email": "ap@pinecrestfinancial.com",    "address": "9 Wall Street, New York, NY 10005"},
    {"name": "Horizon Logistics Ltd.",  "email": "billing@horizonlogistics.eu", "address": "Rue de la Loi 56, 1000 Brussels, Belgium"},
    {"name": "Quantum Media Group",     "email": "accounts@quantummedia.io",     "address": "Level 8, 200 George St, Sydney NSW 2000"},
    {"name": "TerraFirm Properties",    "email": "finance@terrafirm.co.uk",     "address": "45 Baker Street, London W1U 7EU"},
    {"name": "ClearPath Analytics",     "email": "ap@clearpathanalytics.com",   "address": "300 Maple Ave, Boston, MA 02101"},
    {"name": "Vertex Manufacturing",    "email": "procurement@vertexmfg.de",    "address": "Industriestraße 10, 40210 Düsseldorf, Germany"},
    {"name": "Summit Energy Group",     "email": "billing@summitenergy.com",    "address": "500 Energy Plaza, Houston, TX 77002"},
    {"name": "Crestwood Healthcare",    "email": "ap@crestwoodhc.com",          "address": "80 Medical Drive, Boston, MA 02115"},
    {"name": "Pacific Rim Exports",     "email": "finance@pacificrimex.com",    "address": "18 Harbour View, Singapore 018960"},
    {"name": "Alpine Solutions AG",     "email": "accounts@alpinesolutions.ch", "address": "Bahnhofstrasse 12, 8001 Zürich, Switzerland"},
    {"name": "RedSky Aerospace",        "email": "procurement@redsky.aero",     "address": "Hangar 4, Cape Canaveral, FL 32920"},
    {"name": "Brightfield Education",   "email": "billing@brightfieldedu.org",  "address": "10 Campus Drive, Cambridge, MA 02139"},
    {"name": "IronBridge Capital",      "email": "ap@ironbridgecap.com",        "address": "1 Financial Square, London EC2V 8RT"},
]

# ─── CATALOG: (name, category, price_min, price_max) ──────────────────────────
# Services: price is an hourly rate ($/hr), qty = hours
# Products: price is per-unit cost, qty = number of units

SERVICES = [
    # Engineering  (hourly rates)
    ("Backend Development",          "Engineering",      110,  300),
    ("Frontend Development",         "Engineering",      100,  280),
    ("Mobile App Development",       "Engineering",      130,  380),
    ("API Integration",              "Engineering",      120,  300),
    ("Microservices Architecture",   "Engineering",      160,  420),
    ("Database Schema Design",       "Engineering",      130,  350),
    ("Code Review & Audit",          "Engineering",      100,  250),
    ("Legacy System Migration",      "Engineering",      175,  450),
    # Infrastructure & DevOps
    ("Cloud Infrastructure Setup",   "Infrastructure",   150,  400),
    ("DevOps Pipeline Setup",        "Infrastructure",   180,  420),
    ("Kubernetes Cluster Setup",     "Infrastructure",   200,  500),
    ("CI/CD Implementation",         "Infrastructure",   150,  380),
    ("Server Monitoring Setup",      "Infrastructure",   120,  300),
    ("Disaster Recovery Planning",   "Infrastructure",   180,  450),
    # Design
    ("UI/UX Design",                 "Design",            95,  250),
    ("Brand Identity Design",        "Design",           120,  350),
    ("Wireframing & Prototyping",    "Design",            90,  220),
    ("Motion Graphics Design",       "Design",           110,  300),
    ("Print & Marketing Design",     "Design",            80,  200),
    # Marketing
    ("SEO Optimization",             "Marketing",         80,  200),
    ("Content Strategy Session",     "Marketing",         75,  180),
    ("PPC Campaign Management",      "Marketing",         90,  250),
    ("Social Media Management",      "Marketing",         70,  180),
    ("Email Marketing Setup",        "Marketing",         85,  200),
    ("Market Research Report",       "Marketing",        120,  300),
    # Analytics & Data
    ("Data Analysis & Reporting",    "Analytics",        100,  250),
    ("Business Intelligence Setup",  "Analytics",        150,  400),
    ("Data Pipeline Development",    "Analytics",        160,  420),
    ("Dashboard Development",        "Analytics",        130,  350),
    ("Predictive Modelling",         "Analytics",        180,  480),
    # Security
    ("Security Audit",               "Security",         200,  500),
    ("Penetration Testing",          "Security",         250,  600),
    ("Compliance Consulting",        "Security",         180,  450),
    ("Vulnerability Assessment",     "Security",         175,  420),
    # Consulting & Management
    ("Technical Consulting",         "Consulting",       150,  350),
    ("Project Management",           "Management",        85,  175),
    ("Agile Coaching",               "Management",       120,  300),
    ("IT Strategy Workshop",         "Consulting",       200,  500),
    ("Vendor Evaluation Service",    "Consulting",       140,  360),
    # Support & Training
    ("Staff IT Training",            "Training",          80,  200),
    ("Software Onboarding Session",  "Training",          70,  180),
    ("24/7 Technical Support",       "Support",          100,  300),
    ("Helpdesk Management",          "Support",           90,  240),
]

PRODUCTS = [
    # Software & Licenses
    ("Annual SaaS License",          "Software",         299, 2999),
    ("Software Support Package",     "Software",         199,  999),
    ("Monitoring Dashboard License", "Software",          99,  499),
    ("Enterprise CRM License",       "Software",         499, 4999),
    ("Project Management Suite",     "Software",         149,  999),
    ("Antivirus Suite (1yr)",        "Software",          49,  199),
    ("ERP Module License",           "Software",         599, 5999),
    ("Data Backup Software (1yr)",   "Software",          79,  399),
    ("Video Conferencing License",   "Software",          89,  599),
    ("E-Signature Platform (1yr)",   "Software",          59,  299),
    # Hardware
    ("Server Rack Unit",             "Hardware",         399, 1200),
    ("Hardware Maintenance Kit",     "Hardware",          45,  180),
    ("UPS Power Backup Unit",        "Hardware",         180,  550),
    ("VoIP Phone System",            "Hardware",         250,  800),
    ("Desktop Workstation",          "Hardware",         600, 2500),
    ("Laptop (Business Class)",      "Hardware",         800, 3000),
    ("Docking Station",              "Hardware",          99,  350),
    ("External Monitor (27in)",      "Hardware",         250,  900),
    ("Barcode Scanner",              "Hardware",          79,  300),
    ("Label Printer",                "Hardware",         120,  450),
    # Networking
    ("Network Switch",               "Networking",       150,  600),
    ("Wireless Access Point",        "Networking",       120,  400),
    ("Firewall Appliance",           "Networking",       350,  950),
    ("Network Router (Enterprise)",  "Networking",       200,  800),
    ("Ethernet Cabling Bundle",      "Networking",        49,  250),
    ("Network Rack Cabinet",         "Networking",       180,  700),
    ("PoE Injector",                 "Networking",        39,  150),
    ("SFP Transceiver Module",       "Networking",        25,  120),
    # Storage
    ("Backup Storage Device (2TB)",  "Storage",           79,  250),
    ("NAS Device (4-Bay)",           "Storage",          350, 1200),
    ("SSD Drive (1TB)",              "Storage",           89,  250),
    ("Tape Backup Drive",            "Storage",          200,  800),
    ("USB Flash Drive 64GB 10pk",    "Storage",           25,   80),
    ("Cloud Storage Subscription",   "Storage",           49,  499),
    # Security
    ("SSL Certificate (1yr)",        "Security",          89,   89),
    ("Hardware Security Key 5pk",    "Security",          50,  200),
    ("IP Security Camera",           "Security",          99,  450),
    ("Access Control Terminal",      "Security",         250,  900),
    ("Biometric Door Lock",          "Security",         180,  700),
    # Office Supplies
    ("Printer Toner Cartridge",      "Office Supplies",   30,  120),
    ("Paper Ream A4 5pk",            "Office Supplies",   20,   60),
    ("Ergonomic Office Chair",       "Office Supplies",  200,  900),
    ("Standing Desk",                "Office Supplies",  350, 1500),
    ("Whiteboard Large",             "Office Supplies",   80,  300),
    ("Cable Management Kit",         "Office Supplies",   20,   80),
    # Power
    ("PDU Power Strip Rack",         "Power",             80,  350),
    ("Industrial Generator",         "Power",            500, 5000),
    ("Solar Panel Kit",              "Power",            800, 8000),
    ("Extension Cord Reel 20m",      "Power",             30,  120),
]

# ─── STABLE ITEM ID LOOKUP ─────────────────────────────────────────────────────
def _make_item_id(name: str, kind: str) -> str:
    h = int(hashlib.md5(name.encode()).hexdigest(), 16)
    prefix = "SVC" if kind == "Service" else "PRD"
    return f"{prefix}-{10000 + (h % 90000)}"

ITEM_IDS: dict[str, str] = {}
for _name, _cat, *_ in SERVICES:
    ITEM_IDS[_name] = _make_item_id(_name, "Service")
for _name, _cat, *_ in PRODUCTS:
    ITEM_IDS[_name] = _make_item_id(_name, "Product")

# ──────────────────────────────────────────────────────────────────────────────

PAYMENT_TERMS = [("Net 15", 15), ("Net 30", 30), ("Net 45", 45), ("Due on Receipt", 0)]

NOTES = [
    "Please include the invoice number as a reference when making payment.",
    "Thank you for your business! We appreciate your continued partnership.",
    "Late payments may be subject to a 1.5% monthly finance charge.",
    "For billing inquiries, please contact our accounts team at the email above.",
    "All amounts are exclusive of VAT unless stated otherwise.",
    "This invoice was generated electronically and is valid without a signature.",
    "Payment via bank transfer is preferred. Please use the details provided.",
    "We value your partnership and look forward to working with you again.",
    "Goods remain the property of the seller until payment is received in full.",
    "Please allow 3-5 business days for payment processing.",
]

BRAND_COLORS = [
    colors.HexColor("#1B4F8A"),
    colors.HexColor("#1A6B3C"),
    colors.HexColor("#7B2D8B"),
    colors.HexColor("#B5451B"),
    colors.HexColor("#1E5E72"),
    colors.HexColor("#2C3E7A"),
    colors.HexColor("#5A3E28"),
    colors.HexColor("#8B1A1A"),
    colors.HexColor("#1A5276"),
    colors.HexColor("#145A32"),
]

# ─── PRE-BUILT SHARED STYLES ───────────────────────────────────────────────────
_base = getSampleStyleSheet()
_base["Normal"].fontName = F_NORMAL
_base["Normal"].fontSize = 9

def _S(name, **kw):
    kw.setdefault("fontName", F_NORMAL)
    return ParagraphStyle(name, parent=_base["Normal"], **kw)

S_SMALL   = _S("Small",   fontSize=8,  textColor=colors.HexColor("#555555"), leading=12)
S_LABEL   = _S("Label",   fontSize=8,  textColor=colors.HexColor("#888888"), fontName=F_BOLD)
S_VALUE   = _S("Value",   fontSize=9,  textColor=colors.HexColor("#111111"), fontName=F_BOLD)
S_FOOTER  = _S("Footer",  fontSize=8,  textColor=colors.HexColor("#888888"), alignment=TA_CENTER)
S_NOTE    = _S("Note",    fontSize=8,  textColor=colors.HexColor("#444444"), leading=13)
S_COMPANY = _S("Company", fontSize=10, textColor=colors.HexColor("#222222"), leading=14, fontName=F_BOLD)
S_TH      = _S("TH",      fontSize=8,  textColor=colors.white, fontName=F_BOLD)
S_TH_R    = _S("THR",     fontSize=8,  textColor=colors.white, fontName=F_BOLD, alignment=TA_RIGHT)
S_TD      = _S("TD",      fontSize=8,  leading=11)
S_TD_DIM  = _S("TDDim",   fontSize=8,  textColor=colors.HexColor("#555555"))
S_TD_R    = _S("TDR",     fontSize=8,  alignment=TA_RIGHT)
S_TD_RB   = _S("TDRB",    fontSize=8,  alignment=TA_RIGHT, fontName=F_BOLD)
S_TOT     = _S("Tot",     fontSize=9,  alignment=TA_RIGHT)
S_TOT_WB  = _S("TotWB",   fontSize=9,  alignment=TA_RIGHT, fontName=F_BOLD, textColor=colors.white)
S_LOGO    = _S("Logo",    fontSize=10, textColor=colors.white, fontName=F_BOLD, alignment=TA_CENTER)
S_PAY_V   = _S("PayV",    fontSize=9,  fontName=F_BOLD)
S_PAY_D   = _S("PayD",    fontSize=8,  leading=12)

# Cached reusable TableStyles
_TS_HEADER = TableStyle([("VALIGN", (0,0), (-1,-1), "TOP")])
_TS_BILL   = TableStyle([
    ("BACKGROUND",     (0,0), (-1,-1), colors.HexColor("#F0F4FF")),
    ("LEFTPADDING",    (0,0), (-1,-1), 8),
    ("TOPPADDING",     (0,0), (-1,-1), 4),
    ("BOTTOMPADDING",  (0,0), (-1,-1), 4),
    ("ROUNDEDCORNERS", [4]),
])
_TS_META   = TableStyle([
    ("FONTNAME",       (0,0), (-1,-1), F_NORMAL),
    ("FONTSIZE",       (0,0), (-1,-1), 8),
    ("ROWBACKGROUNDS", (0,0), (-1,-1), [colors.HexColor("#F7F7F7"), colors.white]),
    ("TOPPADDING",     (0,0), (-1,-1), 3),
    ("BOTTOMPADDING",  (0,0), (-1,-1), 3),
    ("LEFTPADDING",    (0,0), (-1,-1), 5),
])
_TS_PAYMENT = TableStyle([
    ("VALIGN",         (0,0), (-1,-1), "TOP"),
    ("TOPPADDING",     (0,0), (-1,-1), 2),
    ("BOTTOMPADDING",  (0,0), (-1,-1), 2),
])
_TS_NOTES  = TableStyle([
    ("BACKGROUND",     (0,0), (-1,-1), colors.HexColor("#FFFBEE")),
    ("LEFTPADDING",    (0,0), (-1,-1), 8),
    ("TOPPADDING",     (0,0), (-1,-1), 4),
    ("BOTTOMPADDING",  (0,0), (-1,-1), 6),
    ("ROUNDEDCORNERS", [3]),
])

# Column widths: ID | Description | Category | Type | Qty | Unit | Rate | Total
# "Rate" label differs by type: "Rate/hr" for services, "Unit Price" for products
# But since a single table has mixed rows, we use "Rate" as a neutral label.
_CW_ITEMS = [18*mm, 42*mm, 22*mm, 20*mm, 12*mm, 12*mm, 24*mm, 23*mm]


# ─── DECIMAL HELPERS ──────────────────────────────────────────────────────────
def d2(value) -> Decimal:
    """Convert a float to a Decimal rounded to 2 decimal places."""
    return Decimal(str(value)).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)


def fmt(amount: Decimal, symbol: str) -> str:
    """Format a Decimal as currency with thousands separator."""
    return f"{symbol}{amount:,.2f}"


# ─── DATA GENERATION ───────────────────────────────────────────────────────────
def generate_invoice_data(rng):
    company  = rng.choice(COMPANIES)
    client   = rng.choice(CLIENTS)
    currency = rng.choice(CURRENCIES)
    brand    = rng.choice(BRAND_COLORS)
    sym      = currency["symbol"]

    n_items      = rng.randint(3, 9)
    service_pool = rng.sample(SERVICES, min(10, len(SERVICES)))
    product_pool = rng.sample(PRODUCTS, min(10, len(PRODUCTS)))
    combined     = [("Service", *s) for s in service_pool] + [("Product", *p) for p in product_pool]
    rng.shuffle(combined)

    items = []
    for kind, name, category, pmin, pmax in combined[:n_items]:
        # Use Decimal arithmetic throughout so displayed figures always add up.
        unit_price = d2(round(rng.uniform(pmin, pmax), 2))
        qty        = rng.randint(1, 20) if kind == "Service" else rng.randint(1, 10)
        # Unit label: services are billed by the hour, products by the unit
        unit_label = "hrs" if kind == "Service" else "units"
        # line total = unit_price × qty, rounded to 2dp
        line_total = d2(unit_price * qty)
        items.append({
            "id":          ITEM_IDS[name],
            "description": name,
            "category":    category,
            "type":        kind,
            "qty":         qty,
            "unit":        unit_label,
            "unit_price":  unit_price,   # Decimal
            "total":       line_total,   # Decimal  — exactly qty × unit_price
        })

    issue_dt     = datetime.today() - timedelta(days=rng.randint(5, 180))
    terms, days  = rng.choice(PAYMENT_TERMS)
    due_dt       = issue_dt + timedelta(days=days)

    # ── Summary arithmetic (all Decimal, rounded at each step) ──────────────
    # Subtotal = sum of line totals (each already rounded to 2dp)
    subtotal     = sum(i["total"] for i in items)           # exact sum of Decimals

    discount_pct = rng.choice([0, 0, 5, 10, 15])
    # Discount applied on the subtotal
    discount     = d2(subtotal * Decimal(discount_pct) / 100)
    # VAT is calculated on the discounted amount (taxable base)
    taxable      = subtotal - discount
    vat_amount   = d2(taxable * VAT_RATE)
    # Grand total = taxable base + VAT
    total        = taxable + vat_amount                     # exact; both already 2dp

    prefix = rng.choice(["INV", "BILL", "REC"])
    inv_no = f"{prefix}-{datetime.today().year}-{rng.randint(10000,99999)}"

    return {
        "company":         company,
        "client":          client,
        "currency":        currency,
        "brand_color":     brand,
        "invoice_no":      inv_no,
        "issue_date":      issue_dt.strftime("%d %b %Y"),
        "due_date":        due_dt.strftime("%d %b %Y"),
        "payment_terms":   terms,
        "payment_details": (
            f"Bank: First National Bank\nAccount Name: {company['name']}\n"
            f"Account No: {rng.randint(10000000,99999999)}\n"
            f"Sort Code / IBAN: {rng.randint(10,99)}-{rng.randint(10,99)}-{rng.randint(10,99)}"
        ),
        "items":        items,
        "subtotal":     subtotal,       # Decimal
        "discount_pct": discount_pct,
        "discount":     discount,       # Decimal
        "taxable":      taxable,        # Decimal — shown explicitly so reader can verify VAT
        "vat_amount":   vat_amount,     # Decimal — 20% of taxable
        "total":        total,          # Decimal — taxable + vat_amount
        "note":         rng.choice(NOTES),
    }


# ─── PDF BUILDER ───────────────────────────────────────────────────────────────
def build_invoice(filepath, d):
    buf   = BytesIO()
    brand = d["brand_color"]
    sym   = d["currency"]["symbol"]

    doc = SimpleDocTemplate(buf, pagesize=A4,
                            leftMargin=15*mm, rightMargin=15*mm,
                            topMargin=15*mm,  bottomMargin=15*mm)

    s_title = ParagraphStyle("T", parent=_base["Normal"], fontSize=22,
                              textColor=brand, leading=26, fontName=F_BOLD)

    logo = Table([[Paragraph("YOUR LOGO", S_LOGO)]], colWidths=[40*mm], rowHeights=[14*mm])
    logo.setStyle(TableStyle([
        ("BACKGROUND",     (0,0), (-1,-1), brand),
        ("VALIGN",         (0,0), (-1,-1), "MIDDLE"),
        ("ROUNDEDCORNERS", [4]),
    ]))

    meta = Table([
        [Paragraph("Invoice No.", S_LABEL), Paragraph(d["invoice_no"],      S_VALUE)],
        [Paragraph("Issue Date",  S_LABEL), Paragraph(d["issue_date"],       S_VALUE)],
        [Paragraph("Due Date",    S_LABEL), Paragraph(d["due_date"],         S_VALUE)],
        [Paragraph("Currency",    S_LABEL), Paragraph(d["currency"]["code"], S_VALUE)],
    ], colWidths=[25*mm, 40*mm])
    meta.setStyle(_TS_META)

    header = Table([[
        Table([[logo], [Spacer(1, 4*mm)],
               [Paragraph(d["company"]["name"],    S_COMPANY)],
               [Paragraph(d["company"]["address"], S_SMALL)],
               [Paragraph(d["company"]["email"],   S_SMALL)],
               [Paragraph(d["company"]["phone"],   S_SMALL)],
               [Paragraph(d["company"]["website"], S_SMALL)]],
              colWidths=[90*mm]),
        Table([[Paragraph("INVOICE", s_title)], [Spacer(1, 3*mm)], [meta]],
              colWidths=[90*mm]),
    ]], colWidths=[90*mm, 90*mm])
    header.setStyle(_TS_HEADER)

    bill_to = Table([
        [Paragraph("BILL TO",              S_LABEL)],
        [Paragraph(d["client"]["name"],    S_COMPANY)],
        [Paragraph(d["client"]["address"], S_SMALL)],
        [Paragraph(d["client"]["email"],   S_SMALL)],
    ], colWidths=[180*mm])
    bill_to.setStyle(_TS_BILL)

    # ── Items table ────────────────────────────────────────────────────────────
    # Header: "Rate" covers both hourly rates (services) and per-unit prices (products)
    rows = [[
        Paragraph("Item ID",     S_TH),
        Paragraph("Description", S_TH),
        Paragraph("Category",    S_TH),
        Paragraph("Type",        S_TH),
        Paragraph("Qty",         S_TH_R),
        Paragraph("Unit",        S_TH),
        Paragraph("Rate",        S_TH_R),   # was "Unit Price" — now a neutral label
        Paragraph("Amount",      S_TH_R),   # was "Total"     — matches accounting convention
    ]]
    for item in d["items"]:
        rows.append([
            Paragraph(item["id"],                       S_TD_DIM),
            Paragraph(item["description"],              S_TD),
            Paragraph(item["category"],                 S_TD_DIM),
            Paragraph(item["type"],                     S_TD_DIM),
            Paragraph(str(item["qty"]),                 S_TD_R),
            Paragraph(item["unit"],                     S_TD_DIM),
            Paragraph(fmt(item["unit_price"], sym),     S_TD_R),
            Paragraph(fmt(item["total"],      sym),     S_TD_RB),
        ])

    items_table = Table(rows, colWidths=_CW_ITEMS)
    items_table.setStyle(TableStyle([
        ("BACKGROUND",     (0,0), (-1,0),  brand),
        ("ROWBACKGROUNDS", (0,1), (-1,-1), [colors.HexColor("#F9F9F9"), colors.white]),
        ("FONTNAME",       (0,0), (-1,-1), F_NORMAL),
        ("FONTSIZE",       (0,0), (-1,-1), 8),
        ("TOPPADDING",     (0,0), (-1,-1), 5),
        ("BOTTOMPADDING",  (0,0), (-1,-1), 5),
        ("LEFTPADDING",    (0,0), (-1,-1), 5),
        ("RIGHTPADDING",   (0,0), (-1,-1), 5),
        ("LINEBELOW",      (0,0), (-1,0),  0.5, colors.white),
        ("LINEBELOW",      (0,-1),(-1,-1), 1,   brand),
        ("VALIGN",         (0,0), (-1,-1), "MIDDLE"),
    ]))

    # ── Totals block ──────────────────────────────────────────────────────────
    # Layout: left blank area | right-aligned labels + values
    # The math shown here must exactly match the Decimal values computed in
    # generate_invoice_data():
    #
    #   Subtotal              = Σ (qty × rate) for each line
    #   Discount (n%)         = Subtotal × n/100          [only shown when n > 0]
    #   Taxable Amount        = Subtotal − Discount
    #   VAT (20%)             = Taxable Amount × 0.20
    #   TOTAL DUE             = Taxable Amount + VAT
    #
    # We show "Taxable Amount" only when a discount has been applied, so the
    # reader can clearly see what the 20% VAT is based on.

    totals_data = [
        [Paragraph("Subtotal:", S_TOT), Paragraph(fmt(d["subtotal"], sym), S_TOT)]
    ]

    if d["discount"] > 0:
        totals_data.append([
            Paragraph(f"Discount ({d['discount_pct']}%):", S_TOT),
            Paragraph(f"-{fmt(d['discount'], sym)}", S_TOT),
        ])
        # Show the taxable base so the reader can verify VAT
        totals_data.append([
            Paragraph("Taxable Amount:", S_TOT),
            Paragraph(fmt(d["taxable"], sym), S_TOT),
        ])

    totals_data.append([
        Paragraph(f"VAT ({int(VAT_RATE * 100)}%):", S_TOT),
        Paragraph(fmt(d["vat_amount"], sym), S_TOT),
    ])
    totals_data.append([
        Paragraph("TOTAL DUE:", S_TOT_WB),
        Paragraph(fmt(d["total"], sym), S_TOT_WB),
    ])

    totals_table = Table(totals_data, colWidths=[130*mm, 53*mm])
    totals_table.setStyle(TableStyle([
        ("TOPPADDING",    (0,0), (-1,-1), 4),
        ("BOTTOMPADDING", (0,0), (-1,-1), 4),
        ("RIGHTPADDING",  (0,0), (-1,-1), 5),
        ("BACKGROUND",    (0, len(totals_data)-1), (-1,-1), brand),
        ("ROUNDEDCORNERS", [3]),
    ]))

    payment = Table([
        [Paragraph("Payment Terms",    S_LABEL), Paragraph("Bank / Payment Details", S_LABEL)],
        [Paragraph(d["payment_terms"], S_PAY_V), Paragraph(d["payment_details"],     S_PAY_D)],
    ], colWidths=[90*mm, 90*mm])
    payment.setStyle(_TS_PAYMENT)

    notes = Table([
        [Paragraph("Notes",   S_LABEL)],
        [Paragraph(d["note"], S_NOTE)],
    ], colWidths=[180*mm])
    notes.setStyle(_TS_NOTES)

    grey_hr = HRFlowable(width="100%", thickness=0.5, color=colors.HexColor("#CCCCCC"))

    story = [
        header,
        Spacer(1, 6*mm),
        HRFlowable(width="100%", thickness=1.5, color=brand),
        Spacer(1, 5*mm),
        bill_to,
        Spacer(1, 6*mm),
        items_table,
        Spacer(1, 5*mm),
        totals_table,
        Spacer(1, 6*mm),
        grey_hr,
        Spacer(1, 3*mm),
        payment,
        Spacer(1, 5*mm),
        notes,
        Spacer(1, 8*mm),
        grey_hr,
        Spacer(1, 3*mm),
        Paragraph(
            f"{d['company']['name']} · {d['company']['website']} · "
            f"{d['company']['email']} · {d['company']['phone']}",
            S_FOOTER),
        Paragraph("Thank you for your business.", S_FOOTER),
    ]
    doc.build(story)

    with open(filepath, "wb") as f:
        f.write(buf.getvalue())


# ─── SELF-TEST ─────────────────────────────────────────────────────────────────
def _verify_math(d):
    """Assert that all arithmetic in an invoice data dict is consistent."""
    sym = d["currency"]["symbol"]
    errors = []

    # 1. Each line: total == qty × unit_price
    for item in d["items"]:
        expected = d2(item["unit_price"] * item["qty"])
        if item["total"] != expected:
            errors.append(
                f"Line '{item['description']}': "
                f"{item['qty']} × {fmt(item['unit_price'], sym)} = {fmt(expected, sym)} "
                f"but stored as {fmt(item['total'], sym)}"
            )

    # 2. Subtotal == sum of line totals
    expected_sub = sum(i["total"] for i in d["items"])
    if d["subtotal"] != expected_sub:
        errors.append(
            f"Subtotal mismatch: sum of lines = {fmt(expected_sub, sym)}, "
            f"stored = {fmt(d['subtotal'], sym)}"
        )

    # 3. Discount == subtotal × pct / 100
    expected_disc = d2(d["subtotal"] * Decimal(d["discount_pct"]) / 100)
    if d["discount"] != expected_disc:
        errors.append(
            f"Discount mismatch: {d['discount_pct']}% of {fmt(d['subtotal'], sym)} "
            f"= {fmt(expected_disc, sym)}, stored = {fmt(d['discount'], sym)}"
        )

    # 4. Taxable == subtotal − discount
    expected_tax = d["subtotal"] - d["discount"]
    if d["taxable"] != expected_tax:
        errors.append(
            f"Taxable mismatch: {fmt(d['subtotal'], sym)} - {fmt(d['discount'], sym)} "
            f"= {fmt(expected_tax, sym)}, stored = {fmt(d['taxable'], sym)}"
        )

    # 5. VAT == taxable × 0.20
    expected_vat = d2(d["taxable"] * VAT_RATE)
    if d["vat_amount"] != expected_vat:
        errors.append(
            f"VAT mismatch: 20% of {fmt(d['taxable'], sym)} "
            f"= {fmt(expected_vat, sym)}, stored = {fmt(d['vat_amount'], sym)}"
        )

    # 6. Total == taxable + VAT
    expected_total = d["taxable"] + d["vat_amount"]
    if d["total"] != expected_total:
        errors.append(
            f"Total mismatch: {fmt(d['taxable'], sym)} + {fmt(d['vat_amount'], sym)} "
            f"= {fmt(expected_total, sym)}, stored = {fmt(d['total'], sym)}"
        )

    return errors


# ─── WORKER ────────────────────────────────────────────────────────────────────
def _worker(args):
    batch_indices, seed, output_dir = args
    _register_fonts()
    rng   = random.Random(seed)
    count = 0
    for i in batch_indices:
        data = generate_invoice_data(rng)

        # Paranoia check — should never fire with correct Decimal logic
        errs = _verify_math(data)
        if errs:
            print(f"\n[ERROR] Math check failed for invoice {data['invoice_no']}:")
            for e in errs:
                print(f"  • {e}")
            continue  # skip writing a broken invoice

        filename = f"{output_dir}/{data['invoice_no']}_{i:06d}.pdf"
        build_invoice(filename, data)
        count += 1
    return count


# ─── MAIN ──────────────────────────────────────────────────────────────────────
def main():
    n = n_workers = None
    if len(sys.argv) >= 2:
        try:    n = int(sys.argv[1])
        except ValueError: pass
    if len(sys.argv) >= 3:
        try:    n_workers = int(sys.argv[2])
        except ValueError: pass

    if n is None:
        try:    n = int(input("How many invoices would you like to generate? ").strip())
        except ValueError:
            print("Please enter a valid number."); sys.exit(1)

    if n_workers is None:
        n_workers = min(multiprocessing.cpu_count(), 8)

    os.makedirs(OUTPUT_DIR, exist_ok=True)
    print(f"\n{'─'*55}")
    print(f" Invoice Generator | {n:,} invoices | {n_workers} workers")
    print(f"{'─'*55}\n")

    indices    = list(range(n))
    batch_size = max(1, (n + n_workers - 1) // n_workers)
    batches    = [indices[i:i+batch_size] for i in range(0, n, batch_size)]
    base_seed  = int(time.time())
    tasks      = [(batch, base_seed + idx, OUTPUT_DIR) for idx, batch in enumerate(batches)]

    start = time.time()
    done  = 0
    with multiprocessing.Pool(processes=n_workers) as pool:
        for batch_count in pool.imap_unordered(_worker, tasks):
            done   += batch_count
            elapsed = time.time() - start
            rate    = done / elapsed if elapsed > 0 else 0
            eta     = (n - done) / rate if rate > 0 else 0
            pct     = done / n * 100
            bar     = "█" * int(pct / 2) + "░" * (50 - int(pct / 2))
            print(f"\r [{bar}] {pct:5.1f}% {done:,}/{n:,} {rate:,.0f}/s ETA {eta:.0f}s ",
                  end="", flush=True)

    elapsed = time.time() - start
    print(f"\n\n Done! {n:,} invoices in {elapsed:.1f}s ({n/elapsed:,.0f} invoices/sec)")
    print(f" Saved to: {os.path.abspath(OUTPUT_DIR)}\n")


if __name__ == "__main__":
    multiprocessing.freeze_support()
    main()