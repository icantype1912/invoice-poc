import pytest
from app.services.mime_detector import (
    detect_mime_type, 
    validate_mime_type, 
    get_pipeline_for_mime, 
    ProcessingPipeline
)
from unittest.mock import patch

def test_detect_mime_type():
    # Mocking magic.from_buffer so we don't need the actual lib installed
    with patch("magic.from_buffer") as mock_magic:
        mock_magic.return_value = "application/pdf"
        result = detect_mime_type(b"%PDF-1.4 fake data")
        assert result == "application/pdf"

def test_validate_mime_type_success():
    # Test direct match
    assert validate_mime_type("application/pdf", "application/pdf") is True
    # Test alias match (JPG/JPEG)
    assert validate_mime_type("image/jpeg", "image/jpg") is True

def test_validate_mime_type_mismatch():
    assert validate_mime_type("image/png", "application/pdf") is False

@pytest.mark.parametrize("mime, expected_pipeline", [
    ("application/pdf", ProcessingPipeline.PDF),
    ("image/jpeg", ProcessingPipeline.IMAGE),
    ("text/csv", ProcessingPipeline.CSV),
    ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ProcessingPipeline.DOCX),
    ("application/zip", ProcessingPipeline.UNSUPPORTED)
])
def test_get_pipeline_for_mime(mime, expected_pipeline):
    assert get_pipeline_for_mime(mime) == expected_pipeline