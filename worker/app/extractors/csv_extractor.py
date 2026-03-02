import io
import csv
import logging

logger = logging.getLogger(__name__)


def extract_text_from_csv(csv_data: bytes, encoding: str = "utf-8") -> str:
    """
    Extract text from CSV file.
    Args:
        csv_data: Raw CSV bytes
        encoding: File encoding (default utf-8)
    Returns:
        Extracted text string
    Raises:
        Exception: If extraction fails
    """
    try:
        logger.debug(f"Opening CSV ({len(csv_data)} bytes)")

        text_parts = []

        decoded = csv_data.decode(encoding, errors="replace")
        reader = csv.reader(io.StringIO(decoded))

        for row_num, row in enumerate(reader, 1):
            line = ", ".join(cell.strip() for cell in row if cell.strip())
            if line:
                text_parts.append(line)
                logger.debug(f"Row {row_num}: {len(line)} characters")

        full_text = "\n".join(text_parts)
        logger.debug(f"Total extracted: {len(full_text)} characters")

        return full_text

    except Exception as e:
        logger.error(f"CSV extraction failed: {e}", exc_info=True)
        raise Exception(f"CSV extraction failed: {str(e)}")
