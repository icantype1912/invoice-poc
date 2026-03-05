import pytest
from app.utils.validator import validate_invoice_data
from app.models.invoice import InvoiceData, LineItem, BillTo, ShipTo

@pytest.fixture
def valid_invoice_base():
    """Returns a dictionary representing a valid invoice to be modified by tests."""
    return {
        "InvoiceNumber": "INV-100",
        "InvoiceDate": "2026-03-04",
        "VendorName": "Test Vendor",
        "BillTo": BillTo(Name="John Doe"),
        "ShipTo": ShipTo(),
        "LineItems": [
            LineItem(
                ProductName="Consulting",
                ProductId="SVC-001",
                Quantity=10.0,
                UnitRate=100.0,
                Amount=1000.0
            )
        ],
        "TotalAmount": 1000.0,
        "Currency": "USD"
    }

def test_validator_success(valid_invoice_base):
    invoice = InvoiceData(**valid_invoice_base)
    is_valid, error = validate_invoice_data(invoice)
    assert is_valid is True
    assert error == ""

def test_validator_missing_vendor(valid_invoice_base):
    valid_invoice_base["VendorName"] = None
    invoice = InvoiceData(**valid_invoice_base)
    is_valid, error = validate_invoice_data(invoice)
    assert is_valid is False
    assert "Missing VendorName" in error

def test_validator_invalid_total(valid_invoice_base):
    valid_invoice_base["TotalAmount"] = -50.0
    invoice = InvoiceData(**valid_invoice_base)
    is_valid, error = validate_invoice_data(invoice)
    assert is_valid is False
    assert "Invalid TotalAmount" in error

def test_validator_missing_invoice_number(valid_invoice_base):
    valid_invoice_base["InvoiceNumber"] = ""
    invoice = InvoiceData(**valid_invoice_base)
    is_valid, error = validate_invoice_data(invoice)
    assert is_valid is False
    assert "Missing InvoiceNumber" in error

def test_validator_line_item_invalid_quantity(valid_invoice_base):
    # Modify the line item to have an invalid quantity
    valid_invoice_base["LineItems"][0].Quantity = 0
    invoice = InvoiceData(**valid_invoice_base)
    is_valid, error = validate_invoice_data(invoice)
    assert is_valid is False
    assert "invalid Quantity" in error