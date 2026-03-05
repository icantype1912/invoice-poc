import pytest
import json
from unittest.mock import MagicMock, patch
from app.extractors.llm_extractor import LLMExtractor
from app.models.invoice import InvoiceData

# --------------------------------------------------------------------------
# FIXTURE: MOCK GROQ CLIENT
# --------------------------------------------------------------------------
@pytest.fixture
def mock_groq_client():
    """
    This intercepts the Groq library. Instead of making a network call,
    it returns a 'MagicMock' object that acts like the real client.
    """
    with patch("app.extractors.llm_extractor.Groq") as mock_groq:
        client_instance = mock_groq.return_value
        yield client_instance

# --------------------------------------------------------------------------
# FIXTURE: FAKE INVOICE DATA
# --------------------------------------------------------------------------
@pytest.fixture
def valid_llm_json_response():
    """
    This is the 'Expected Output'. In a unit test, we define what the AI 
    SHOULD return so we can test if our code parses it correctly.
    Data is modeled after your sample invoices (e.g., Dave Brooks).
    """
    return {
        "InvoiceNumber": "18898",
        "InvoiceDate": "2012-09-19",
        "OrderId": "ES-2012-DB1306048-41171",
        "VendorName": "SuperStore",
        "BillTo": {"Name": "Dave Brooks"},
        "ShipTo": {"City": "Bochum", "State": "North Rhine-Westphalia", "Country": "Germany"},
        "ShipMode": "Second Class",
        "LineItems": [
            {
                "ProductName": "HP Copy Machine, Laser Copiers",
                "Category": "Technology",
                "ProductId": "TEC-CO-4767",
                "Quantity": 4,
                "UnitRate": 973.32,
                "Amount": 3893.28
            }
        ],
        "Subtotal": 3893.28,
        "TotalAmount": 3967.11,
        "Currency": "USD"
    }

# --------------------------------------------------------------------------
# TEST CASE: SUCCESSFUL EXTRACTION
# --------------------------------------------------------------------------
def test_extract_invoice_success(mock_groq_client, valid_llm_json_response):
    """
    Verifies that when the LLM returns valid JSON text, our code:
    1. Parses the JSON string into a dictionary.
    2. Successfully creates an 'InvoiceData' Pydantic object.
    """
    # 1. SETUP THE MOCK RESPONSE
    # We simulate the deep nesting of the Groq response: 
    # response -> choices[0] -> message -> content
    mock_response = MagicMock()
    mock_response.choices[0].message.content = json.dumps(valid_llm_json_response)
    mock_groq_client.chat.completions.create.return_value = mock_response

    # 2. EXECUTE THE FUNCTION
    # The string "Raw text..." is a dummy input. Since we mocked the API,
    # the function won't actually look at this string; it will just
    # return the 'mock_response' we defined above.
    extractor = LLMExtractor(api_key="fake_key")
    result = extractor.extract_invoice("Raw invoice text from PDF or OCR")

    # 3. VERIFY THE RESULTS (Assertions)
    assert isinstance(result, InvoiceData)
    assert result.InvoiceNumber == "18898"
    assert result.VendorName == "SuperStore"
    
    # Verify that the LLM was called with the safety settings you defined
    _, kwargs = mock_groq_client.chat.completions.create.call_args
    assert kwargs["temperature"] == 0.1  # Ensures low randomness [cite: 84]
    assert kwargs["response_format"] == {"type": "json_object"}  # Forces JSON [cite: 85]

# --------------------------------------------------------------------------
# TEST CASE: BROKEN JSON HANDLING
# --------------------------------------------------------------------------
def test_extract_invoice_invalid_json(mock_groq_client):
    """
    Verifies that if the LLM returns a broken string that isn't valid JSON,
    our code catches the 'json.JSONDecodeError' and raises an exception.
    """
    mock_response = MagicMock()
    mock_response.choices[0].message.content = "This is not JSON code"
    mock_groq_client.chat.completions.create.return_value = mock_response

    extractor = LLMExtractor(api_key="fake_key")
    
    # We expect our code's try/except block to catch this and raise an Exception [cite: 87]
    with pytest.raises(Exception) as excinfo:
        extractor.extract_invoice("dummy text")
    
    assert "LLM returned invalid JSON" in str(excinfo.value)

# --------------------------------------------------------------------------
# TEST CASE: MISSING DATA HANDLING (PYDANTIC)
# --------------------------------------------------------------------------
def test_extract_invoice_pydantic_validation_error(mock_groq_client):
    """
    Verifies that if the LLM returns valid JSON but misses a REQUIRED field
    (like InvoiceNumber), the Pydantic model throws an error.
    """
    # JSON is valid, but missing 'InvoiceNumber' which is REQUIRED 
    incomplete_json = {"VendorName": "SuperStore", "TotalAmount": 100.0}
    
    mock_response = MagicMock()
    mock_response.choices[0].message.content = json.dumps(incomplete_json)
    mock_groq_client.chat.completions.create.return_value = mock_response

    extractor = LLMExtractor(api_key="fake_key")
    
    # This should fail because 'InvoiceData' requires 'InvoiceNumber' 
    with pytest.raises(Exception) as excinfo:
        extractor.extract_invoice("dummy text")
    
    assert "LLM extraction failed" in str(excinfo.value)