import pytest
import time
from unittest.mock import MagicMock, patch
from app.utils.currency_converter import convert_to_usd, _get_exchange_rate, _rate_cache
from app.models.invoice import InvoiceData, LineItem, DiscountInfo

# --------------------------------------------------------------------------
# FIXTURE: Setup a sample invoice in a non-USD currency (INR)
# --------------------------------------------------------------------------
@pytest.fixture
def sample_invoice_inr():
    """
    Creates an InvoiceData object in INR for conversion testing.
    Modeled after your sample BlueWave Consulting invoice logic.
    """
    return InvoiceData(
        InvoiceNumber="BILL-2026-93969",
        InvoiceDate="2026-01-20",
        VendorName="BlueWave Consulting",
        BillTo={"Name": "Brightfield Education"},
        ShipTo={},
        LineItems=[
            LineItem(
                ProductName="Cloud Storage",
                ProductId="PRD-7212",
                Quantity=7,
                UnitRate=1000.0,
                Amount=7000.0
            )
        ],
        Subtotal=7000.0,
        TotalAmount=7000.0,
        Currency="INR",
        Discount=DiscountInfo(Amount=100.0, Percentage=None)
    )

# --------------------------------------------------------------------------
# TEST: API Rate Fetching and Caching
# --------------------------------------------------------------------------
def test_get_exchange_rate_caching():
    """
    Verifies that _get_exchange_rate fetches from API once and then uses cache.
    Reference: app.utils.currency_converter._get_exchange_rate
    """
    # Clear cache before starting
    _rate_cache.clear()
    
    with patch("httpx.Client") as mock_client:
        # Mock the API response
        mock_response = MagicMock()
        mock_response.json.return_value = {"rates": {"USD": 0.012}}
        mock_response.raise_for_status = MagicMock()
        mock_client.return_value.__enter__.return_value.get.return_value = mock_response

        # Call 1: Should trigger API call
        rate1 = _get_exchange_rate("INR")
        # Call 2: Should use in-memory cache
        rate2 = _get_exchange_rate("INR")

        assert rate1 == 0.012
        assert rate2 == 0.012
        # Ensure the GET request only happened once
        assert mock_client.return_value.__enter__.return_value.get.call_count == 1

# --------------------------------------------------------------------------
# TEST: Full Invoice Conversion
# --------------------------------------------------------------------------
def test_convert_to_usd_success(sample_invoice_inr):
    """
    Tests that all monetary fields (Total, Subtotal, LineItems, Discount)
    are correctly multiplied by the exchange rate and rounded.
    """
    with patch("app.utils.currency_converter._get_exchange_rate") as mock_rate:
        mock_rate.return_value = 0.012  # 1 INR = 0.012 USD
        
        converted = convert_to_usd(sample_invoice_inr)
        
        # Verify Currency changed
        assert converted.Currency == "USD"
        
        # Verify Math (7000 * 0.012 = 84.0)
        assert converted.TotalAmount == 84.0
        assert converted.Subtotal == 84.0
        
        # Verify Line Items
        assert converted.LineItems[0].UnitRate == 12.0
        assert converted.LineItems[0].Amount == 84.0
        
        # Verify Discount (100 * 0.012 = 1.2)
        assert converted.Discount.Amount == 1.2

# --------------------------------------------------------------------------
# TEST: Fallback Logic
# --------------------------------------------------------------------------
def test_convert_to_usd_skips_if_already_usd(sample_invoice_inr):
    """Ensures logic returns original invoice if currency is already USD."""
    sample_invoice_inr.Currency = "USD"
    with patch("app.utils.currency_converter._get_exchange_rate") as mock_rate:
        result = convert_to_usd(sample_invoice_inr)
        mock_rate.assert_not_called()
        assert result.TotalAmount == sample_invoice_inr.TotalAmount

def test_convert_to_usd_handles_api_failure(sample_invoice_inr):
    """Ensures original invoice is returned if the API/rate fetch fails."""
    with patch("app.utils.currency_converter._get_exchange_rate", return_value=None):
        result = convert_to_usd(sample_invoice_inr)
        assert result.Currency == "INR"
        assert result.TotalAmount == 7000.0