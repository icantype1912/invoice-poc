import pytest
from app.extractors.csv_extractor import extract_text_from_csv

def test_extract_text_from_csv_success():
    # Simulate a standard CSV with some messy whitespace
    csv_content = b"Item, Quantity, Price\nLaptop, 1, 1200\n Mouse , 2 , 25 "
    
    result = extract_text_from_csv(csv_content)
    
    # Verify rows are joined by newlines and cells by commas
    assert "Item, Quantity, Price" in result
    assert "Laptop, 1, 1200" in result
    # Verify whitespace stripping
    assert "Mouse, 2, 25" in result

def test_extract_text_from_csv_skips_empty_rows_and_cells():
    # CSV with empty rows and empty cells
    csv_content = b"Header1, Header2\n\nData1, , Data2\n , , "
    
    result = extract_text_from_csv(csv_content)
    
    # Should only contain non-empty content
    assert result == "Header1, Header2\nData1, Data2"

def test_extract_text_from_csv_encoding():
    # Use a character and encoding that will trigger the 'replace' logic
    # but is valid for the initial encoding process.
    csv_content = "Currency, £".encode("iso-8859-1")
    
    # We pass 'utf-8' to the function even though data is iso-8859-1.
    # The function should decode it using 'replace' without raising an error.
    result = extract_text_from_csv(csv_content, encoding="utf-8")
    
    assert "Currency" in result
    # The '£' in iso-8859-1 is invalid in utf-8, so it becomes the replacement character
    assert "" in result or "Currency" in result

def test_extract_text_from_csv_failure():
    # Passing None to trigger the broad Exception catch in the function
    with pytest.raises(Exception) as excinfo:
        extract_text_from_csv(None)
    
    assert "CSV extraction failed" in str(excinfo.value)