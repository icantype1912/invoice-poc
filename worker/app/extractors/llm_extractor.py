import json
import logging
from groq import Groq
from app.models.invoice import InvoiceData

logger = logging.getLogger(__name__)

class LLMExtractor:
    """Groq llama-3 based invoice data extractor."""

    def __init__(self, api_key: str, model: str = "llama-3.3-70b-versatile"):
        self.client = Groq(api_key=api_key)
        self.model = model
        logger.info(f"Initialized LLM extractor with model: {model}")

        self.system_prompt = """You are an expert invoice data extraction system. Extract structured data from the provided text and return ONLY valid JSON.

### 1. SYNONYM & LABEL MAPPING
- InvoiceNumber: Look for "Invoice #", "Bill Number", "Bill No", "Receipt #", or "Doc ID".
- VendorName: The SELLER/COMPANY (usually at the top).
- BillTo.Name: The CUSTOMER/BUYER (who is paying).
- ProductId (SKU): Look for "Item #", "Part No", or "SKU". 
  *CRITICAL*: If no SKU/ID exists, generate a "Slug" based on the ProductName (e.g., "Premium Coffee" -> "PREMIUM-COFFEE").

### 2. REQUIRED JSON STRUCTURE
{
  "InvoiceNumber": "string (REQUIRED - map 'Bill Number' here if applicable)",
  "InvoiceDate": "string (Normalize to YYYY-MM-DD)",
  "OrderId": "string or null",
  "VendorName": "string (REQUIRED - The Seller)",
  "BillTo": {
    "Name": "string (REQUIRED - The Customer)"
  },
  "ShipTo": {
    "City": "string or null",
    "State": "string or null",
    "Country": "string or null"
  },
  "ShipMode": "string or null",
  "LineItems": [
    {
      "ProductName": "string (REQUIRED)",
      "Category": "string or null (e.g., Office, Electronics)",
      "ProductId": "string (REQUIRED - Extract SKU or generate slug from name)",
      "Quantity": number (REQUIRED),
      "UnitRate": number (REQUIRED - Extract Rate or Unit Price or Unit Rate or Price Per Unit),
      "Amount": number (REQUIRED)
    }
  ],
  "Subtotal": number or null,
  "Discount": {
    "Percentage": number or null,
    "Amount": number or null
  } or null,
  "ShippingCost": number or null,
  "TotalAmount": number (REQUIRED),
  "BalanceDue": number or null,
  "Currency": "string (3-letter ISO, default: USD)",
  "Notes": "string or null",
  "Terms": "string or null"
}

### 3. EXTRACTION RULES
1. RETURN ONLY JSON: No markdown code blocks (```), no explanations.
2. NUMERIC CLEANING: Remove all currency symbols ($) and commas (1,500.00 -> 1500.00). Must be numbers.
3. MULTI-LINE ITEMS: If a product description spans multiple lines, merge them into one 'ProductName'.
4. SKU GENERATION: Every line item MUST have a ProductId. If the text doesn't provide a SKU, create one by capitalizing the product name and replacing spaces with hyphens.
5. VALIDATION: If 'TotalAmount' is missing or unreadable, return: {"error": "Missing TotalAmount"}."""

    def extract_invoice(self, raw_text: str) -> InvoiceData:
        """
        Extract structured invoice data from raw text using Groq llama.
        Args:
            raw_text: Extracted text from OCR or PDF
        Returns:
            Validated InvoiceData object
        Raises:
            Exception: If LLM fails or returns invalid data
        """
        user_prompt = f"""Extract invoice data from this text:

{raw_text}

Return only valid JSON matching the required structure.
IMPORTANT: VendorName is the SELLER/COMPANY issuing the invoice (like "SuperStore", "Amazon", etc.)"""

        try:
            logger.info(f"Calling Groq llama API with {len(raw_text)} characters")

            # Call Groq API
            chat_completion = self.client.chat.completions.create(
                messages=[
                    {"role": "system", "content": self.system_prompt},
                    {"role": "user", "content": user_prompt}
                ],
                model=self.model,
                temperature=0.1,  # Low temperature for consistent extraction
                max_tokens=4096,
                response_format={"type": "json_object"}  # Force JSON response
            )

            # Extract response
            response_text = chat_completion.choices[0].message.content
            logger.debug(f"llama response: {len(response_text)} characters")

            # Parse JSON
            invoice_dict = json.loads(response_text)

            # Validate with Pydantic
            invoice_data = InvoiceData(**invoice_dict)

            logger.info(f"Successfully extracted invoice {invoice_data.InvoiceNumber}")

            return invoice_data

        except json.JSONDecodeError as e:
            logger.error(f"llama returned invalid JSON: {e}")
            raise Exception(f"LLM returned invalid JSON: {str(e)}")
        except Exception as e:
            logger.error(f"llama extraction failed: {e}", exc_info=True)
            raise Exception(f"LLM extraction failed: {str(e)}")
