import pytest
from app.utils.text_cleaner import preprocess_ocr_text, truncate_text

# --------------------------------------------------------------------------
# TEST: OCR Text Preprocessing
# --------------------------------------------------------------------------
def test_preprocess_ocr_text_whitespace():
    """
    Verifies that multiple spaces are reduced to single spaces 
    and leading/trailing line whitespace is removed.
    """
    raw_text = "  Vendor    Name: SuperStore   \n  Item: Paper  "
    result = preprocess_ocr_text(raw_text)
    
    assert result == "Vendor Name: SuperStore\nItem: Paper"

def test_preprocess_ocr_text_newlines():
    """
    Verifies that excessive newlines (3 or more) are capped at 2.
    """
    raw_text = "Section 1\n\n\n\nSection 2"
    result = preprocess_ocr_text(raw_text)
    
    assert result == "Section 1\n\nSection 2"

def test_preprocess_ocr_text_empty_input():
    """Ensures the function handles None or empty strings gracefully."""
    assert preprocess_ocr_text("") == ""
    assert preprocess_ocr_text(None) == ""

# --------------------------------------------------------------------------
# TEST: Text Truncation
# --------------------------------------------------------------------------
def test_truncate_text_under_limit():
    """Verifies that text under the limit is returned unchanged."""
    short_text = "Short invoice text."
    result = truncate_text(short_text, max_length=50)
    
    assert result == short_text

def test_truncate_text_over_limit():
    """
    Verifies that text exceeding the limit is cut off and 
    appended with the truncation indicator.
    """
    long_text = "This is a very long string of text."
    limit = 10
    
    result = truncate_text(long_text, max_length=limit)
    
    # Check that it starts with the first 10 characters
    assert result.startswith("This is a ")
    # Check for the truncation suffix
    assert "[Text truncated due to length...]" in result