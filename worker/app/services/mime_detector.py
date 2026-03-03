import magic
import logging
from enum import Enum

logger = logging.getLogger(__name__)


class ProcessingPipeline(str, Enum):
    """Available processing pipelines."""
    IMAGE = "image"
    PDF = "pdf"
    CSV = "csv"
    DOCX = "docx"
    UNSUPPORTED = "unsupported"


def detect_mime_type(file_data: bytes) -> str:
    """
    Detect MIME type using magic numbers (file content analysis).
    More reliable than file extension or Drive metadata.
    Args:
        file_data: Raw file bytes
    Returns:
        MIME type string (e.g., "application/pdf", "image/jpeg")
    """
    # Analyze first 4KB (sufficient for magic number detection)
    mime = magic.from_buffer(file_data[:4096], mime=True)
    logger.debug(f"Detected MIME type: {mime}")
    return mime


def validate_mime_type(detected: str, expected: str) -> bool:
    """
    Validate that detected MIME matches expected.
    Handles common variations (image/jpg vs image/jpeg).
    Args:
        detected: MIME type from magic number analysis
        expected: MIME type from Drive metadata
    Returns:
        True if types match (accounting for aliases)
    """
    # Define MIME type aliases
    mime_aliases = {
        'application/pdf': ['application/pdf'],
        'image/jpeg': ['image/jpeg', 'image/jpg'],
        'image/png': ['image/png'],
        'text/csv': ['text/csv', 'application/csv'],
        'application/vnd.openxmlformats-officedocument.wordprocessingml.document': [
            'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
        ],
    }

    # Check if both are in same alias group
    for canonical, aliases in mime_aliases.items():
        if expected in aliases and detected in aliases:
            logger.debug(f"MIME types match: {detected} == {expected}")
            return True

    # Direct match
    matches = detected == expected
    if not matches:
        logger.warning(f"MIME type mismatch: detected={detected}, expected={expected}")

    return matches


def get_pipeline_for_mime(mime_type: str) -> ProcessingPipeline:
    """
    Route to appropriate extraction pipeline based on MIME type.
    Args:
        mime_type: MIME type string
    Returns:
        ProcessingPipeline enum value
    """
    if mime_type in ['image/jpeg', 'image/jpg', 'image/png']:
        return ProcessingPipeline.IMAGE
    elif mime_type == 'application/pdf':
        return ProcessingPipeline.PDF
    elif mime_type in ['text/csv', 'application/csv']:
        return ProcessingPipeline.CSV
    elif mime_type == 'application/vnd.openxmlformats-officedocument.wordprocessingml.document':
        return ProcessingPipeline.DOCX
    else:
        return ProcessingPipeline.UNSUPPORTED
