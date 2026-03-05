import pytest
import io
from unittest.mock import MagicMock, patch
from app.extractors.image_extractor import extract_text_from_image

def test_extract_text_from_image_success():
    # Mock the Image object
    mock_image = MagicMock()
    mock_image.mode = 'RGB'
    mock_image.size = (100, 100)
    
    # Patch Image.open and pytesseract.image_to_string
    with patch("PIL.Image.open", return_value=mock_image), \
         patch("pytesseract.image_to_string", return_value=" Extracted OCR Text ") as mock_ocr:
        
        result = extract_text_from_image(b"fake_image_bytes")
        
        # Verify result is stripped
        assert result == "Extracted OCR Text"
        # Verify OCR was called with the image and correct language
        mock_ocr.assert_called_once_with(mock_image, lang='eng')

def test_extract_text_from_image_conversion():
    # Simulate an image in a mode that needs conversion (e.g., RGBA)
    mock_image = MagicMock()
    mock_image.mode = 'RGBA'
    
    mock_converted_image = MagicMock()
    mock_converted_image.mode = 'RGB'
    mock_image.convert.return_value = mock_converted_image

    with patch("PIL.Image.open", return_value=mock_image), \
         patch("pytesseract.image_to_string", return_value="Converted"):
        
        extract_text_from_image(b"fake_image_bytes")
        
        # Verify convert('RGB') was called because mode was 'RGBA'
        mock_image.convert.assert_called_once_with('RGB')

def test_extract_text_from_image_failure():
    # Simulate a failure in opening the image
    with patch("PIL.Image.open", side_effect=Exception("Invalid image data")):
        with pytest.raises(Exception) as excinfo:
            extract_text_from_image(b"corrupt_data")
        
        assert "OCR failed" in str(excinfo.value)