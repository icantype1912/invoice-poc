import logging
import time
import httpx
from app.models.invoice import InvoiceData

logger = logging.getLogger(__name__)

# In-memory cache: { "EUR": { "rate": 1.08, "fetched_at": timestamp } }
_rate_cache: dict[str, dict] = {}
_CACHE_TTL_SECONDS = 3600  # 1 hour


def _get_exchange_rate(from_currency: str) -> float | None:
    """
    Fetch the exchange rate from `from_currency` to USD using the Frankfurter API.
    Results are cached in memory for 1 hour.

    Returns:
        The exchange rate (multiply by this to convert to USD), or None on failure.
    """
    from_currency = from_currency.upper()

    if from_currency == "USD":
        return 1.0

    # Check cache
    cached = _rate_cache.get(from_currency)
    if cached and (time.time() - cached["fetched_at"]) < _CACHE_TTL_SECONDS:
        logger.debug(f"Using cached rate for {from_currency}: {cached['rate']}")
        return cached["rate"]

    # Fetch from Frankfurter API
    try:
        url = f"https://api.frankfurter.app/latest?from={from_currency}&to=USD"
        with httpx.Client(timeout=10.0) as client:
            response = client.get(url)
            response.raise_for_status()

        data = response.json()
        rate = data.get("rates", {}).get("USD")

        if rate is None:
            logger.warning(f"No USD rate returned for {from_currency}")
            return None

        # Cache the result
        _rate_cache[from_currency] = {
            "rate": float(rate),
            "fetched_at": time.time()
        }

        logger.info(f"Fetched exchange rate: 1 {from_currency} = {rate} USD")
        return float(rate)

    except Exception as e:
        logger.warning(
            f"Failed to fetch exchange rate for {from_currency}: {e}. "
            f"Keeping original currency."
        )
        return None


def _convert_amount(amount: float | None, rate: float) -> float | None:
    """Convert a single amount using the given rate, rounding to 2 decimal places."""
    if amount is None:
        return None
    return round(amount * rate, 2)


def convert_to_usd(invoice: InvoiceData) -> InvoiceData:
    """
    Convert all monetary values in an InvoiceData object to USD.

    If the currency is already USD or the exchange rate cannot be fetched,
    returns the original invoice unchanged.

    Args:
        invoice: The InvoiceData with potentially non-USD currency.

    Returns:
        A new InvoiceData with all amounts converted to USD.
    """
    original_currency = (invoice.Currency or "USD").upper()

    if original_currency == "USD":
        return invoice

    rate = _get_exchange_rate(original_currency)

    if rate is None:
        logger.warning(
            f"Could not convert {original_currency} to USD. "
            f"Keeping original currency for invoice {invoice.InvoiceNumber}."
        )
        return invoice

    logger.info(
        f"Converting invoice {invoice.InvoiceNumber} from {original_currency} "
        f"to USD (rate: {rate})"
    )

    # Convert line items
    converted_line_items = []
    for item in invoice.LineItems:
        converted_item = item.model_copy(update={
            "UnitRate": _convert_amount(item.UnitRate, rate),
            "Amount": _convert_amount(item.Amount, rate),
        })
        converted_line_items.append(converted_item)

    # Convert discount if present
    converted_discount = None
    if invoice.Discount:
        converted_discount = invoice.Discount.model_copy(update={
            "Amount": _convert_amount(invoice.Discount.Amount, rate),
            # Percentage stays the same
        })

    # Build updated invoice
    updated_invoice = invoice.model_copy(update={
        "TotalAmount": _convert_amount(invoice.TotalAmount, rate),
        "Subtotal": _convert_amount(invoice.Subtotal, rate),
        "ShippingCost": _convert_amount(invoice.ShippingCost, rate),
        "BalanceDue": _convert_amount(invoice.BalanceDue, rate),
        "LineItems": converted_line_items,
        "Discount": converted_discount,
        "Currency": "USD",
    })

    logger.info(
        f"Invoice {invoice.InvoiceNumber}: "
        f"TotalAmount {original_currency} {invoice.TotalAmount} → USD {updated_invoice.TotalAmount}"
    )

    return updated_invoice
