import pytest
from unittest.mock import MagicMock, patch
from app.extractors.pdf_extractor import extract_text_from_pdf, extract_text_from_pdf_pymupdf

# --------------------------------------------------------------------------
# TEST: pdfplumber extraction
# --------------------------------------------------------------------------
def test_extract_text_from_pdf_success():
    """
    Tests that pdfplumber correctly iterates through pages and joins 
    extracted text using double newlines.
    """
    # 1. Create mock pages with fake text
    mock_page1 = MagicMock()
    mock_page1.extract_text.return_value = "Page 1 Content"
    
    mock_page2 = MagicMock()
    mock_page2.extract_text.return_value = "Page 2 Content"

    # 2. Mock the PDF object returned by pdfplumber.open()
    mock_pdf = MagicMock()
    mock_pdf.pages = [mock_page1, mock_page2]
    # This mocks the context manager: 'with pdfplumber.open(...) as pdf'
    mock_pdf.__enter__.return_value = mock_pdf

    # 3. Patch pdfplumber.open to return our mock instead of opening a file
    with patch("pdfplumber.open", return_value=mock_pdf):
        # Pass dummy bytes; the mock ensures no real file is needed
        result = extract_text_from_pdf(b"fake_pdf_bytes")
        
        # Verify text joining logic
        assert result == "Page 1 Content\n\nPage 2 Content"
        assert mock_page1.extract_text.called
        assert mock_page2.extract_text.called

def test_extract_text_from_pdf_empty_page():
    """Verifies that pages with no text are skipped without crashing."""
    mock_page = MagicMock()
    mock_page.extract_text.return_value = None # Simulate a scanned page/image
    
    mock_pdf = MagicMock()
    mock_pdf.pages = [mock_page]
    mock_pdf.__enter__.return_value = mock_pdf

    with patch("pdfplumber.open", return_value=mock_pdf):
        result = extract_text_from_pdf(b"fake_pdf_bytes")
        assert result == ""

# --------------------------------------------------------------------------
# TEST: PyMuPDF (fitz) extraction
# --------------------------------------------------------------------------
def test_extract_text_from_pdf_pymupdf_success():
    """
    Tests the alternative PyMuPDF pipeline to ensure it correctly 
    calls get_text() on the document pages.
    """
    # 1. Mock the individual page
    mock_page = MagicMock()
    mock_page.get_text.return_value = "PyMuPDF Extracted Text"

    # 2. Mock the document object returned by fitz.open()
    mock_doc = MagicMock()
    # In PyMuPDF, the doc itself is iterable (for page in doc)
    mock_doc.__iter__.return_value = [mock_page]
    mock_doc.__len__.return_value = 1

    # 3. Patch fitz.open
    with patch("fitz.open", return_value=mock_doc):
        result = extract_text_from_pdf_pymupdf(b"fake_pdf_bytes")
        
        assert result == "PyMuPDF Extracted Text"
        mock_doc.close.assert_called_once()

# --------------------------------------------------------------------------
# TEST: Error Handling
# --------------------------------------------------------------------------
def test_extract_text_from_pdf_exception():
    """Verifies that the function raises a custom Exception if pdfplumber fails."""
    with patch("pdfplumber.open", side_effect=Exception("Corrupt PDF")):
        with pytest.raises(Exception) as excinfo:
            extract_text_from_pdf(b"bad_data")
        
        assert "PDF extraction failed" in str(excinfo.value)