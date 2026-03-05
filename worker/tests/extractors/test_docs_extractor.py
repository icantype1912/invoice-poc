import pytest
from unittest.mock import MagicMock, patch
from app.extractors.docs_extractor import extract_text_from_docx

def test_extract_text_from_docx_success():
    # Mock the Document object and its paragraphs
    mock_doc = MagicMock()
    
    # Create mock paragraphs with text
    para1 = MagicMock()
    para1.text = "  Invoice Header  "
    
    para2 = MagicMock()
    para2.text = "Line Item 1: $100"
    
    para3 = MagicMock()
    para3.text = "" # Should be skipped by the .strip() and if text logic
    
    mock_doc.paragraphs = [para1, para2, para3]
    
    # Patch the Document constructor to return our mock
    with patch("app.extractors.docs_extractor.Document", return_value=mock_doc):
        # Pass dummy bytes as docx_data
        result = extract_text_from_docx(b"fake_docx_bytes")
        
        # Verify paragraph joining and stripping
        assert "Invoice Header" in result
        assert "Line Item 1: $100" in result
        # Check for the double newline separator used in the code
        assert result == "Invoice Header\n\nLine Item 1: $100"

def test_extract_text_from_docx_empty():
    mock_doc = MagicMock()
    mock_doc.paragraphs = []
    
    with patch("app.extractors.docs_extractor.Document", return_value=mock_doc):
        result = extract_text_from_docx(b"empty_bytes")
        assert result == ""

def test_extract_text_from_docx_failure():
    # Simulate an invalid file causing Document() to raise an exception
    with patch("app.extractors.docs_extractor.Document", side_effect=Exception("Invalid format")):
        with pytest.raises(Exception) as excinfo:
            extract_text_from_docx(b"corrupt_data")
        
        assert "DOCX extraction failed" in str(excinfo.value)