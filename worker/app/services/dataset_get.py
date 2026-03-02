import os
import random
from faker import Faker
from reportlab.pdfgen import canvas
from reportlab.lib.pagesizes import A4

fake = Faker()

OUTPUT_FOLDER = r"C:\Users\anagha.kini\OneDrive - AVEVA Solutions Limited\dev\test-data"
os.makedirs(OUTPUT_FOLDER, exist_ok=True)

WIDTH, HEIGHT = A4

BRANDS = ["Amazon", "Walmart", "Target"]

DATES = (
    ["23 Feb 2026"] * 20 +
    ["26 Aug 2025"] * 30 +
    ["26 Feb 2025"] * 40
)

PRODUCT_CATALOG = [
    "Wireless Mouse","USB-C Charger","Bluetooth Headphones","Laptop Sleeve",
    "Office Chair","Desk Lamp","Mechanical Keyboard","Notebook Pack",
    "Gaming Monitor","HD Webcam","External Hard Drive","Portable SSD",
    "Smartphone Stand","Tablet Case","Wireless Earbuds","Smart Watch",
    "Printer Ink Cartridge","Router","Power Bank","LED Strip Lights",
    "Water Bottle","Backpack","Desk Organizer","Whiteboard",
    "Stapler","Paper Shredder","Mouse Pad","HDMI Cable",
    "USB Hub","Laptop Cooling Pad","Graphic Tablet","Noise Cancelling Headphones",
    "Fitness Tracker","Phone Tripod","Portable Speaker","Smart Bulb",
    "Digital Alarm Clock","Office Desk","Monitor Stand","Keyboard Wrist Rest",
    "Flash Drive 64GB","Ethernet Cable","Surge Protector","Scanner",
    "Projector","Tablet","Smartphone","Wireless Keyboard",
    "Desk Fan","Laptop Stand","Conference Speakerphone"
]

def generate_invoice(invoice_no):

    brand = random.choice(BRANDS)
    date = random.choice(DATES)
    customer = fake.name()
    city = fake.city()
    country = fake.country()

    filename = os.path.join(OUTPUT_FOLDER, f"{brand}_{invoice_no}.pdf")
    c = canvas.Canvas(filename, pagesize=A4)

    # ===== HEADER =====
    c.setFont("Helvetica-Bold", 18)
    c.drawString(50, HEIGHT - 50, "INVOICE")

    c.setFont("Helvetica", 12)
    c.drawString(50, HEIGHT - 80, f"# {invoice_no}")

    c.setFont("Helvetica-Bold", 14)
    c.drawString(50, HEIGHT - 105, brand)

    # ===== BILL TO =====
    c.setFont("Helvetica", 11)
    c.drawString(50, HEIGHT - 140, "Bill To:")
    c.drawString(50, HEIGHT - 155, customer)

    # ===== SHIP TO =====
    c.drawString(300, HEIGHT - 140, "Ship To:")
    c.drawString(300, HEIGHT - 155, f"{city}, {country}")

    # ===== DATE / SHIP MODE / BALANCE =====
    c.drawString(50, HEIGHT - 185, f"Date: {date}")
    c.drawString(250, HEIGHT - 185, "Ship Mode: Standard Class")

    # ===== TABLE HEADER =====
    y = HEIGHT - 220
    c.setFont("Helvetica-Bold", 11)
    c.drawString(50, y, "Item")
    c.drawString(300, y, "Qty")
    c.drawString(350, y, "Rate")
    c.drawString(420, y, "Amount")

    c.line(50, y-5, 550, y-5)

    # ===== PRODUCTS =====
    c.setFont("Helvetica", 10)

    num_items = random.randint(4, 8)
    selected = random.sample(PRODUCT_CATALOG, num_items)

    subtotal = 0
    y -= 25

    for item in selected:
        qty = random.randint(1, 6)
        rate = random.randint(500, 15000)
        amount = qty * rate
        subtotal += amount

        c.drawString(50, y, item[:35])
        c.drawString(300, y, str(qty))
        c.drawString(350, y, f"${rate}")
        c.drawString(420, y, f"${amount}")

        y -= 18

    # ===== TOTALS =====
    discount = round(subtotal * random.choice([0.10, 0.15]), 2)
    shipping = random.randint(50, 300)
    total = subtotal - discount + shipping

    y -= 10
    c.line(300, y, 550, y)
    y -= 20

    c.drawString(350, y, "Subtotal:")
    c.drawString(420, y, f"${subtotal}")
    y -= 18

    c.drawString(350, y, "Discount:")
    c.drawString(420, y, f"-${discount}")
    y -= 18

    c.drawString(350, y, "Shipping:")
    c.drawString(420, y, f"${shipping}")
    y -= 18

    c.setFont("Helvetica-Bold", 11)
    c.drawString(350, y, "Total:")
    c.drawString(420, y, f"${total}")

    # ===== FOOTER =====
    c.setFont("Helvetica", 10)
    c.drawString(50, 100, "Notes:")
    c.drawString(50, 85, "Thanks for your business!")

    c.drawString(50, 60, f"Order ID: {fake.uuid4()}")

    c.save()


# Generate 90 invoices
invoice_start = 60000

for i in range(90):
    generate_invoice(invoice_start + i)

print("90 visually similar invoices generated successfully.")