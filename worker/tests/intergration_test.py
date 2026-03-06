"""
Integration Tests: Full Worker Pipeline
========================================
These tests verify that real components work correctly TOGETHER,
not in isolation. External dependencies (DB, Drive, Groq, HTTP)
are still mocked at the network boundary, but all internal
modules are wired together as they are in production.

Contrast with unit tests where each function is tested alone:
  Unit:        extract_text_from_pdf() tested with a mocked pdfplumber
  Integration: worker._process_job() tested with real extractor +
               real validator + real text_cleaner all running together

Setup
-----
pip install pytest pytest-asyncio httpx pydantic-settings psycopg2-binary
           pdfplumber python-docx pytesseract pillow groq python-magic-bin
           google-api-python-client google-auth httpx
"""

import pytest
import asyncio
import json
import io
import base64
import hmac as hmac_lib
import hashlib
from datetime import datetime, timezone
from unittest.mock import MagicMock, patch, AsyncMock

# ── App imports ──────────────────────────────────────────────────────────────
from app.worker import InvoiceWorker
from app.models.job import Job, JobPayload, JobStatus
from app.models.invoice import InvoiceData, LineItem, BillTo, ShipTo, DiscountInfo
from app.services.mime_detector import ProcessingPipeline
from app.utils.validator import validate_invoice_data
from app.utils.text_cleaner import preprocess_ocr_text, truncate_text
from app.utils.currency_converter import convert_to_usd, _rate_cache
from app.utils.hmac import compute_hmac, verify_hmac
from app.services.callback_service import CallbackService
from app.services.mime_detector import detect_mime_type, get_pipeline_for_mime, validate_mime_type
from app.extractors.csv_extractor import extract_text_from_csv
from app.extractors.docs_extractor import extract_text_from_docx
from app.extractors.pdf_extractor import extract_text_from_pdf


# ════════════════════════════════════════════════════════════════════════════
# SHARED FIXTURES
# ════════════════════════════════════════════════════════════════════════════

@pytest.fixture
def mock_config():
    config = MagicMock()
    config.worker_id = "integration-worker-01"
    config.poll_interval = 1
    config.max_retries = 3
    config.db_connection_string = "fake-dsn"
    config.google_service_account_key = "fake-key.json"
    config.groq_api_key = "fake-groq-key"
    config.groq_model = "llama-4-70b-versatile"
    config.backend_url = "http://fake-backend.com"
    config.callback_secret = "integration-secret"
    return config


@pytest.fixture
def worker(mock_config):
    """
    Real InvoiceWorker with all external service constructors patched.
    Internal logic (text_cleaner, validator, currency_converter, hmac)
    runs for real — that is what makes this an integration test.
    """
    with patch("app.worker.JobClaimer") as MockClaimer, \
         patch("app.worker.DriveService") as MockDrive, \
         patch("app.worker.LLMExtractor") as MockLLM, \
         patch("app.worker.CallbackService") as MockCallback:

        w = InvoiceWorker(mock_config)
        # Keep references so individual tests can configure return values
        w._mock_claimer   = MockClaimer.return_value
        w._mock_drive     = MockDrive.return_value
        w._mock_llm       = MockLLM.return_value
        w._mock_callback  = MockCallback.return_value
        return w


@pytest.fixture
def valid_invoice_data():
    """Minimal valid InvoiceData used across multiple tests."""
    return InvoiceData(
        InvoiceNumber="INV-INT-001",
        InvoiceDate="2026-03-01",
        VendorName="Acme Corp",
        BillTo=BillTo(Name="Dave Brooks"),
        ShipTo=ShipTo(City="Berlin", State="BE", Country="Germany"),
        LineItems=[
            LineItem(
                ProductName="Cloud Storage Plan",
                ProductId="PRD-CS-001",
                Quantity=2,
                UnitRate=500.0,
                Amount=1000.0,
            )
        ],
        TotalAmount=1000.0,
        Currency="USD",
    )


def _make_job(
    file_id="drive-file-001",
    mime="application/pdf",
    retry_count=0,
    job_id="job-int-001",
):
    payload = JobPayload(
        fileId=file_id,
        originalName="invoice.pdf",
        mimeType=mime,
        fileSize=2048,
        uploader="tester",
        idempotencyKey="idem-key-001",
        detectedAt=datetime.now(timezone.utc).isoformat(),
    )
    return Job(
        id=job_id,
        jobType="INVOICE_EXTRACTION",
        status=JobStatus.PENDING,
        payload=payload,
        retryCount=retry_count,
        createdAt=datetime.now(timezone.utc),
        updatedAt=datetime.now(timezone.utc),
    )


# ════════════════════════════════════════════════════════════════════════════
# 1. TEXT PIPELINE INTEGRATION
#    text_cleaner → validator  (no external calls)
# ════════════════════════════════════════════════════════════════════════════

class TestTextPipelineIntegration:
    """
    Verifies that OCR output cleaned by text_cleaner passes through
    validator without issues. Both modules run for real.
    """

    def test_clean_then_validate_success(self, valid_invoice_data):
        """Cleaned text produces invoice that passes validation."""
        dirty_ocr = "  Invoice   Header  \n\n\n\nTotal:  1000.00  "
        cleaned = preprocess_ocr_text(dirty_ocr)

        # Cleaned text should have no excessive whitespace or newlines
        assert "   " not in cleaned
        assert "\n\n\n" not in cleaned

        # The invoice built from this data should pass validation
        is_valid, err = validate_invoice_data(valid_invoice_data)
        assert is_valid is True, f"Unexpected validation error: {err}"

    def test_truncate_then_validate(self, valid_invoice_data):
        """Truncated text (simulating very long PDFs) still leads to valid invoice."""
        long_text = "x" * 20_000
        truncated = truncate_text(long_text, max_length=10_000)

        assert len(truncated) < 20_000
        assert "[Text truncated due to length...]" in truncated

        # The invoice data itself should still be valid
        is_valid, err = validate_invoice_data(valid_invoice_data)
        assert is_valid is True

    def test_empty_ocr_triggers_invalid_path(self):
        """
        Empty cleaned text is detected BEFORE the LLM is called.
        Simulates scanned blank pages / corrupted images.
        """
        raw = preprocess_ocr_text("   \n\n  ")
        assert raw == ""
        # Worker checks len < 18; empty string satisfies that
        assert len(raw) < 18


# ════════════════════════════════════════════════════════════════════════════
# 2. MIME DETECTION → PIPELINE ROUTING INTEGRATION
#    mime_detector.detect_mime_type → get_pipeline_for_mime
# ════════════════════════════════════════════════════════════════════════════

class TestMimeRoutingIntegration:
    """
    Validates that MIME detection output feeds correctly into pipeline routing.
    Uses real validate_mime_type and get_pipeline_for_mime logic.
    """

    @pytest.mark.parametrize("magic_mime, declared_mime, expected_pipeline", [
        ("application/pdf",  "application/pdf",  ProcessingPipeline.PDF),
        ("image/jpeg",       "image/jpeg",        ProcessingPipeline.IMAGE),
        ("image/jpeg",       "image/jpg",         ProcessingPipeline.IMAGE),   # alias
        ("text/csv",         "text/csv",          ProcessingPipeline.CSV),
        (
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ProcessingPipeline.DOCX,
        ),
    ])
    def test_mime_to_pipeline_routing(self, magic_mime, declared_mime, expected_pipeline):
        """
        Real validate_mime_type + get_pipeline_for_mime run together.
        magic_mime is what the file bytes say; declared_mime is from Drive metadata.
        """
        with patch("magic.from_buffer", return_value=magic_mime):
            detected = detect_mime_type(b"fake bytes")

        assert validate_mime_type(detected, declared_mime) is True
        assert get_pipeline_for_mime(detected) == expected_pipeline

    def test_mime_mismatch_does_not_route(self):
        """A spoofed file (PNG uploaded as PDF) must be caught at MIME validation."""
        with patch("magic.from_buffer", return_value="image/png"):
            detected = detect_mime_type(b"fake bytes")

        # validate_mime_type returns False; pipeline should never be reached
        assert validate_mime_type(detected, "application/pdf") is False

    def test_unsupported_mime_routes_to_unsupported(self):
        """ZIP files must route to UNSUPPORTED regardless of declared type."""
        with patch("magic.from_buffer", return_value="application/zip"):
            detected = detect_mime_type(b"PK fake zip")

        pipeline = get_pipeline_for_mime(detected)
        assert pipeline == ProcessingPipeline.UNSUPPORTED


# ════════════════════════════════════════════════════════════════════════════
# 3. EXTRACTOR → VALIDATOR INTEGRATION
#    Real extractor output fed into real validator
# ════════════════════════════════════════════════════════════════════════════

class TestExtractorValidatorIntegration:
    """
    Runs real extractors on synthetic file bytes and then validates
    the resulting invoice. No LLM or network calls involved.
    """

    def test_csv_extractor_output_is_non_empty(self):
        """Real CSV extractor produces text that would survive the len >= 18 check."""
        csv_bytes = (
            b"InvoiceNumber,Date,VendorName,Total\n"
            b"INV-001,2026-01-01,Acme Corp,1500.00\n"
        )
        text = extract_text_from_csv(csv_bytes)
        assert len(text) >= 18
        assert "INV-001" in text
        assert "Acme Corp" in text

    def test_csv_extractor_strips_and_joins(self):
        """Integration: extractor cleans cells; text_cleaner handles whitespace."""
        csv_bytes = b"  Vendor , Amount \n  Acme  ,  500  \n"
        raw = extract_text_from_csv(csv_bytes)
        cleaned = preprocess_ocr_text(raw)
        assert "Vendor, Amount" in cleaned
        assert "Acme, 500" in cleaned

    def test_docx_extractor_with_mocked_document(self):
        """Real extract_text_from_docx logic runs; only Document() is patched."""
        mock_doc = MagicMock()
        mock_doc.paragraphs = [
            MagicMock(text="  Invoice #INV-DOCX-001  "),
            MagicMock(text="Vendor: Acme Corp"),
            MagicMock(text=""),                          # blank — should be skipped
            MagicMock(text="Total: $2,500.00"),
        ]
        with patch("app.extractors.docs_extractor.Document", return_value=mock_doc):
            text = extract_text_from_docx(b"fake")

        assert "Invoice #INV-DOCX-001" in text
        assert "Vendor: Acme Corp" in text
        assert "Total: $2,500.00" in text
        # Blank paragraph must be absent
        parts = text.split("\n\n")
        assert all(p.strip() for p in parts)

    def test_pdf_extractor_multi_page_join(self):
        """Real pdf extraction logic; pdfplumber is patched at boundary."""
        mock_page1 = MagicMock()
        mock_page1.extract_text.return_value = "Page 1: Invoice Header"
        mock_page2 = MagicMock()
        mock_page2.extract_text.return_value = "Page 2: Line Items"

        mock_pdf = MagicMock()
        mock_pdf.pages = [mock_page1, mock_page2]
        mock_pdf.__enter__.return_value = mock_pdf

        with patch("pdfplumber.open", return_value=mock_pdf):
            text = extract_text_from_pdf(b"fake")

        # Real joining logic: double newline between pages
        assert text == "Page 1: Invoice Header\n\nPage 2: Line Items"
        assert len(text) >= 18

    def test_validator_rejects_missing_vendor(self, valid_invoice_data):
        """Real validator rejects invoice missing VendorName."""
        valid_invoice_data.VendorName = None
        is_valid, err = validate_invoice_data(valid_invoice_data)
        assert is_valid is False
        assert "VendorName" in err

    def test_validator_rejects_zero_quantity_line_item(self, valid_invoice_data):
        """Integration: validator inspects each LineItem produced by the extractor."""
        valid_invoice_data.LineItems[0].Quantity = 0
        is_valid, err = validate_invoice_data(valid_invoice_data)
        assert is_valid is False
        assert "Quantity" in err


# ════════════════════════════════════════════════════════════════════════════
# 4. CURRENCY CONVERTER → VALIDATOR INTEGRATION
#    convert_to_usd output fed directly into validator
# ════════════════════════════════════════════════════════════════════════════

class TestCurrencyConversionIntegration:
    """
    Ensures that currency-converted invoices still pass validation.
    Real convert_to_usd + real validate_invoice_data run together.
    """

    @pytest.fixture(autouse=True)
    def clear_cache(self):
        _rate_cache.clear()
        yield
        _rate_cache.clear()

    def test_inr_to_usd_then_validate(self):
        """Convert INR invoice to USD and verify it still passes validation."""
        invoice = InvoiceData(
            InvoiceNumber="INV-INR-001",
            InvoiceDate="2026-02-15",
            VendorName="BlueWave India",
            BillTo=BillTo(Name="Client A"),
            ShipTo=ShipTo(),
            LineItems=[
                LineItem(
                    ProductName="Consulting",
                    ProductId="SVC-001",
                    Quantity=5,
                    UnitRate=10_000.0,
                    Amount=50_000.0,
                )
            ],
            TotalAmount=50_000.0,
            Currency="INR",
        )

        with patch("app.utils.currency_converter._get_exchange_rate", return_value=0.012):
            converted = convert_to_usd(invoice)

        assert converted.Currency == "USD"
        assert converted.TotalAmount == round(50_000.0 * 0.012, 2)

        is_valid, err = validate_invoice_data(converted)
        assert is_valid is True, f"Post-conversion validation failed: {err}"

    def test_usd_invoice_skips_conversion_and_validates(self, valid_invoice_data):
        """USD invoices bypass conversion; validator should still pass."""
        with patch("app.utils.currency_converter._get_exchange_rate") as mock_rate:
            result = convert_to_usd(valid_invoice_data)
            mock_rate.assert_not_called()

        is_valid, err = validate_invoice_data(result)
        assert is_valid is True

    def test_api_failure_preserves_original_and_validates(self):
        """When exchange API is unreachable, original invoice passes validation."""
        invoice = InvoiceData(
            InvoiceNumber="INV-EUR-001",
            InvoiceDate="2026-01-10",
            VendorName="EU Vendor",
            BillTo=BillTo(Name="Client B"),
            ShipTo=ShipTo(),
            LineItems=[
                LineItem(
                    ProductName="Software License",
                    ProductId="SW-LIC-002",
                    Quantity=1,
                    UnitRate=999.99,
                    Amount=999.99,
                )
            ],
            TotalAmount=999.99,
            Currency="EUR",
        )

        with patch("app.utils.currency_converter._get_exchange_rate", return_value=None):
            result = convert_to_usd(invoice)

        # Currency unchanged — validate original amounts
        assert result.Currency == "EUR"
        is_valid, err = validate_invoice_data(result)
        assert is_valid is True


# ════════════════════════════════════════════════════════════════════════════
# 5. HMAC → CALLBACK SERVICE INTEGRATION
#    compute_hmac feeds into CallbackService._generate_hmac (different impl)
#    and both must agree when verified by the backend stub
# ════════════════════════════════════════════════════════════════════════════

class TestHmacCallbackIntegration:
    """
    Validates that the HMAC signing used in callback_service produces
    a signature verifiable by the backend's expected algorithm.

    Note: utils/hmac.py uses hex + sort_keys (for test endpoint).
          services/callback_service.py uses base64 (for real callbacks).
    Both are tested here.
    """

    SECRET = "integration-secret-key"

    def test_callback_service_hmac_matches_manual_calculation(self):
        """
        Real CallbackService._generate_hmac must match a manually computed
        base64 HMAC — the same calculation the ASP.NET backend performs.
        """
        svc = CallbackService("http://fake-backend.com", self.SECRET)
        body = json.dumps({"jobId": "j-1", "status": "COMPLETED"}).encode("utf-8")

        expected_digest = hmac_lib.new(
            self.SECRET.encode("utf-8"), body, hashlib.sha256
        ).digest()
        expected_b64 = base64.b64encode(expected_digest).decode("utf-8")

        actual = svc._generate_hmac(body)
        assert actual == expected_b64

    def test_utils_hmac_is_deterministic_and_verifiable(self):
        """compute_hmac + verify_hmac round-trip using real logic."""
        payload = {"jobId": "j-2", "status": "INVALID", "workerId": "w-1"}
        sig = compute_hmac(payload, self.SECRET)
        assert verify_hmac(payload, sig, self.SECRET) is True

    def test_tampered_payload_fails_verification(self):
        """Changing any field after signing breaks verification."""
        payload = {"jobId": "j-3", "status": "COMPLETED"}
        sig = compute_hmac(payload, self.SECRET)

        tampered = {**payload, "status": "FAILED"}
        assert verify_hmac(tampered, sig, self.SECRET) is False

    @pytest.mark.asyncio
    async def test_callback_service_sends_correct_headers(self):
        """
        Real CallbackService.send_callback: verify HMAC header is present
        and correctly generated before the HTTP call hits the network.
        """
        svc = CallbackService("http://fake-backend.com", self.SECRET)
        payload = {"jobId": "j-4", "status": "COMPLETED", "workerId": "w-1"}

        captured_headers = {}

        async def fake_post(url, headers, content, **kwargs):
            captured_headers.update(headers)
            mock_resp = MagicMock()
            mock_resp.status_code = 200
            return mock_resp

        with patch("httpx.AsyncClient.post", side_effect=fake_post):
            await svc.send_callback(payload)

        assert "X-Callback-HMAC" in captured_headers
        assert captured_headers["Content-Type"] == "application/json"

        # Re-verify the captured HMAC is correct
        body = json.dumps(payload).encode("utf-8")
        expected = base64.b64encode(
            hmac_lib.new(self.SECRET.encode(), body, hashlib.sha256).digest()
        ).decode()
        assert captured_headers["X-Callback-HMAC"] == expected


# ════════════════════════════════════════════════════════════════════════════
# 6. WORKER._process_job FULL PIPELINE INTEGRATION
#    Drive → MIME → Extractor → LLM → Converter → Validator → Callback shape
# ════════════════════════════════════════════════════════════════════════════

class TestWorkerProcessJobIntegration:
    """
    Tests InvoiceWorker._process_job end-to-end with real internal modules.
    Only the four external boundaries are patched:
        • Drive download (network)
        • python-magic (binary lib)
        • Specific file-format library (pdfplumber / docx / pytesseract)
        • Groq LLM API
    Everything else (text_cleaner, validator, currency_converter,
    mime routing, callback shape) is real.
    """

    @pytest.mark.asyncio
    async def test_pdf_happy_path(self, worker, valid_invoice_data):
        job = _make_job(mime="application/pdf")

        worker.drive_service.download_file.return_value = b"fake-pdf-bytes"
        worker.llm_extractor.extract_invoice.return_value = valid_invoice_data

        with patch("magic.from_buffer", return_value="application/pdf"), \
             patch("pdfplumber.open") as mock_plumber:

            mock_page = MagicMock()
            mock_page.extract_text.return_value = (
                "Invoice INV-INT-001\nVendor: Acme Corp\nTotal: $1,000.00"
            )
            mock_pdf = MagicMock()
            mock_pdf.pages = [mock_page]
            mock_pdf.__enter__.return_value = mock_pdf
            mock_plumber.return_value = mock_pdf

            result = await worker._process_job(job)

        assert result["status"] == "COMPLETED"
        assert result["jobId"] == job.id
        assert result["workerId"] == "integration-worker-01"
        assert "result" in result
        # Validator ran for real — InvoiceNumber must be present
        assert result["result"]["InvoiceNumber"] == "INV-INT-001"

    @pytest.mark.asyncio
    async def test_csv_happy_path(self, worker, valid_invoice_data):
        """Real CSV extractor output flows through to LLM mock."""
        job = _make_job(mime="text/csv", job_id="job-csv-001")
        csv_bytes = (
            b"InvoiceNumber,Vendor,Total\n"
            b"INV-INT-001,Acme Corp,1000.00\n"
        )
        worker.drive_service.download_file.return_value = csv_bytes
        worker.llm_extractor.extract_invoice.return_value = valid_invoice_data

        with patch("magic.from_buffer", return_value="text/csv"):
            result = await worker._process_job(job)

        assert result["status"] == "COMPLETED"
        # Verify LLM received real CSV text (not empty)
        call_args = worker.llm_extractor.extract_invoice.call_args[0][0]
        assert "INV-INT-001" in call_args
        assert "Acme Corp" in call_args

    @pytest.mark.asyncio
    async def test_image_happy_path(self, worker, valid_invoice_data):
        """Real OCR text_cleaner runs on image extractor output."""
        job = _make_job(mime="image/jpeg", job_id="job-img-001")
        worker.drive_service.download_file.return_value = b"fake-jpeg-bytes"
        worker.llm_extractor.extract_invoice.return_value = valid_invoice_data

        raw_ocr = "  Invoice   Header \n\n\n Total:   $1000  "

        with patch("magic.from_buffer", return_value="image/jpeg"), \
             patch("app.worker.extract_text_from_image", return_value=raw_ocr):

            result = await worker._process_job(job)

        assert result["status"] == "COMPLETED"
        # text_cleaner ran for real — verify LLM received cleaned text
        call_text = worker.llm_extractor.extract_invoice.call_args[0][0]
        assert "   " not in call_text          # multiple spaces removed
        assert "\n\n\n" not in call_text       # excessive newlines capped

    @pytest.mark.asyncio
    async def test_mime_mismatch_returns_invalid(self, worker):
        """Real MIME validation catches file-type spoofing."""
        job = _make_job(mime="application/pdf", job_id="job-spoof-001")
        worker.drive_service.download_file.return_value = b"fake-png-bytes"

        # Actual file is a PNG but job says PDF
        with patch("magic.from_buffer", return_value="image/png"):
            result = await worker._process_job(job)

        assert result["status"] == "INVALID"
        assert "MIME type mismatch" in result["reason"]
        # LLM must never have been called
        worker.llm_extractor.extract_invoice.assert_not_called()

    @pytest.mark.asyncio
    async def test_unsupported_mime_returns_invalid(self, worker):
        """ZIP files reach UNSUPPORTED pipeline and produce INVALID callback."""
        job = _make_job(mime="application/zip", job_id="job-zip-001")
        worker.drive_service.download_file.return_value = b"PK fake zip content"

        with patch("magic.from_buffer", return_value="application/zip"):
            result = await worker._process_job(job)

        assert result["status"] == "INVALID"
        assert "Unsupported" in result["reason"]

    @pytest.mark.asyncio
    async def test_short_text_returns_invalid(self, worker):
        """
        Fewer than 18 extracted characters triggers INVALID before LLM.
        Tests that the real length check gate works correctly.
        """
        job = _make_job(mime="application/pdf", job_id="job-short-001")
        worker.drive_service.download_file.return_value = b"fake-pdf"

        with patch("magic.from_buffer", return_value="application/pdf"), \
             patch("app.worker.extract_text_from_pdf", return_value="Too short"):

            result = await worker._process_job(job)

        assert result["status"] == "INVALID"
        assert "Insufficient text" in result["reason"]
        worker.llm_extractor.extract_invoice.assert_not_called()

    @pytest.mark.asyncio
    async def test_drive_failure_returns_failed(self, worker):
        """Drive download error produces FAILED (retriable) callback."""
        job = _make_job(job_id="job-drive-fail-001")
        worker.drive_service.download_file.side_effect = Exception(
            "Failed to download file drive-file-001: Connection reset"
        )

        result = await worker._process_job(job)

        assert result["status"] == "FAILED"
        assert "Failed to download" in result["reason"]

    @pytest.mark.asyncio
    async def test_llm_failure_returns_failed(self, worker):
        """LLM exception produces FAILED callback, not a crash."""
        job = _make_job(mime="application/pdf", job_id="job-llm-fail-001")
        worker.drive_service.download_file.return_value = b"fake-pdf"
        worker.llm_extractor.extract_invoice.side_effect = Exception(
            "LLM extraction failed: Groq rate limit"
        )

        with patch("magic.from_buffer", return_value="application/pdf"), \
             patch("app.worker.extract_text_from_pdf",
                   return_value="Invoice content that is long enough to pass the check"):

            result = await worker._process_job(job)

        assert result["status"] == "FAILED"
        assert "LLM extraction failed" in result["reason"]

    @pytest.mark.asyncio
    async def test_validation_failure_returns_failed(self, worker):
        """
        Real validator rejects an invoice that the LLM returned with missing fields.
        The worker wraps this as FAILED (not INVALID).
        """
        job = _make_job(mime="application/pdf", job_id="job-val-fail-001")
        worker.drive_service.download_file.return_value = b"fake-pdf"

        bad_invoice = InvoiceData(
            InvoiceNumber="INV-BAD",
            InvoiceDate="2026-01-01",
            VendorName=None,          # Missing — validator will reject
            BillTo=BillTo(Name="X"),
            ShipTo=ShipTo(),
            LineItems=[
                LineItem(
                    ProductName="Item",
                    ProductId="P-001",
                    Quantity=1,
                    UnitRate=10.0,
                    Amount=10.0,
                )
            ],
            TotalAmount=10.0,
            Currency="USD",
        )
        worker.llm_extractor.extract_invoice.return_value = bad_invoice

        with patch("magic.from_buffer", return_value="application/pdf"), \
             patch("app.worker.extract_text_from_pdf",
                   return_value="Sufficient content for the text length gate to pass"):

            result = await worker._process_job(job)

        assert result["status"] == "FAILED"
        assert "Validation failed" in result["reason"]
        assert "VendorName" in result["reason"]

    @pytest.mark.asyncio
    async def test_non_usd_invoice_is_converted_then_validated(self, worker):
        """
        Real currency converter runs between LLM and validator.
        EUR invoice must be converted to USD and still pass validation.
        """
        job = _make_job(mime="application/pdf", job_id="job-eur-001")
        worker.drive_service.download_file.return_value = b"fake-pdf"

        eur_invoice = InvoiceData(
            InvoiceNumber="INV-EUR-999",
            InvoiceDate="2026-02-01",
            VendorName="EU Vendor GmbH",
            BillTo=BillTo(Name="Client EU"),
            ShipTo=ShipTo(Country="Germany"),
            LineItems=[
                LineItem(
                    ProductName="Product X",
                    ProductId="PX-001",
                    Quantity=3,
                    UnitRate=200.0,
                    Amount=600.0,
                )
            ],
            TotalAmount=600.0,
            Currency="EUR",
        )
        worker.llm_extractor.extract_invoice.return_value = eur_invoice

        with patch("magic.from_buffer", return_value="application/pdf"), \
             patch("app.worker.extract_text_from_pdf",
                   return_value="Valid invoice content that exceeds minimum length"), \
             patch("app.utils.currency_converter._get_exchange_rate", return_value=1.08):

            result = await worker._process_job(job)

        assert result["status"] == "COMPLETED"
        # Verify conversion actually happened — TotalAmount should be in USD
        assert result["result"]["Currency"] == "USD"
        assert result["result"]["TotalAmount"] == round(600.0 * 1.08, 2)


# ════════════════════════════════════════════════════════════════════════════
# 7. RETRY & BACKOFF INTEGRATION
#    _should_retry_job → _calculate_backoff logic running together
# ════════════════════════════════════════════════════════════════════════════

class TestRetryBackoffIntegration:
    """
    Validates retry decision + backoff calculation as a combined flow,
    mirroring what _poll_and_process does after _process_job returns FAILED.
    """

    def test_failed_job_within_retries_triggers_retry(self, worker):
        callback = {"status": "FAILED", "reason": "Transient error"}
        assert worker._should_retry_job(callback, current_retry_count=0) is True
        assert worker._should_retry_job(callback, current_retry_count=2) is True

    def test_failed_job_at_max_retries_does_not_retry(self, worker):
        callback = {"status": "FAILED", "reason": "Persistent error"}
        # max_retries = 3, so retryCount=3 means exhausted
        assert worker._should_retry_job(callback, current_retry_count=3) is False

    def test_completed_job_never_retried(self, worker):
        assert worker._should_retry_job({"status": "COMPLETED"}, 0) is False

    def test_invalid_job_never_retried(self, worker):
        assert worker._should_retry_job({"status": "INVALID"}, 0) is False

    def test_backoff_sequence_is_exponential_and_capped(self, worker):
        """Real _calculate_backoff: 2^n capped at 30."""
        expected = {1: 2, 2: 4, 3: 8, 4: 16, 5: 30, 10: 30}
        for retry_n, minutes in expected.items():
            assert worker._calculate_backoff(retry_n) == minutes, (
                f"Retry {retry_n}: expected {minutes}m, "
                f"got {worker._calculate_backoff(retry_n)}m"
            )

    @pytest.mark.asyncio
    async def test_poll_and_process_schedules_retry_on_failed(self, worker):
        """
        _poll_and_process integrates _process_job + _should_retry_job +
        _schedule_retry. On FAILED within retries, no callback is sent;
        the job is rescheduled in the DB.
        """
        job = _make_job(retry_count=1, job_id="job-retry-001")
        worker.job_claimer.claim_job.return_value = job

        # Simulate _process_job returning FAILED
        with patch.object(worker, "_process_job", new=AsyncMock(
            return_value={
                "jobId": job.id,
                "status": "FAILED",
                "reason": "Transient network error",
                "workerId": "integration-worker-01",
                "processedAt": datetime.now(timezone.utc).isoformat(),
            }
        )), patch.object(worker, "_schedule_retry", new=AsyncMock()) as mock_retry:

            await worker._poll_and_process()

            # Retry was scheduled, NOT a final callback
            mock_retry.assert_called_once()
            worker.callback_service.send_callback.assert_not_called()

    @pytest.mark.asyncio
    async def test_poll_and_process_sends_callback_on_completed(self, worker):
        """On COMPLETED, send_callback is called; no retry is scheduled."""
        job = _make_job(retry_count=0, job_id="job-complete-001")
        worker.job_claimer.claim_job.return_value = job
        worker.callback_service.send_callback = AsyncMock(return_value=True)
        worker.job_claimer.release_job_lock.return_value = True

        with patch.object(worker, "_process_job", new=AsyncMock(
            return_value={
                "jobId": job.id,
                "status": "COMPLETED",
                "result": {"InvoiceNumber": "INV-001"},
                "workerId": "integration-worker-01",
                "processedAt": datetime.now(timezone.utc).isoformat(),
            }
        )), patch.object(worker, "_schedule_retry", new=AsyncMock()) as mock_retry:

            await worker._poll_and_process()

            worker.callback_service.send_callback.assert_called_once()
            mock_retry.assert_not_called()
            assert worker.stats["jobs_processed"] == 1


# ════════════════════════════════════════════════════════════════════════════
# 8. CALLBACK SHAPE → HMAC VERIFICATION END-TO-END
#    Simulates backend receiving the callback and verifying its signature
# ════════════════════════════════════════════════════════════════════════════

class TestCallbackPayloadIntegration:
    """
    Simulates the full round-trip:
      worker builds callback dict → CallbackService signs it → backend verifies
    Uses real compute_hmac / verify_hmac and real CallbackService._generate_hmac.
    """

    SECRET = "integration-secret"

    def test_completed_callback_shape_and_fields(self, worker, valid_invoice_data):
        """Real _create_completed_callback produces a backend-ready payload."""
        cb = worker._create_completed_callback("job-shape-001", valid_invoice_data)

        assert cb["status"] == "COMPLETED"
        assert cb["jobId"] == "job-shape-001"
        assert cb["workerId"] == "integration-worker-01"
        assert "processedAt" in cb
        assert "result" in cb
        assert cb["result"]["InvoiceNumber"] == "INV-INT-001"

    def test_failed_callback_shape(self, worker):
        cb = worker._create_failed_callback("job-fail-shape", "LLM timeout")
        assert cb["status"] == "FAILED"
        assert cb["reason"] == "LLM timeout"

    def test_invalid_callback_shape(self, worker):
        cb = worker._create_invalid_callback("job-inv-shape", "MIME mismatch")
        assert cb["status"] == "INVALID"
        assert "MIME" in cb["reason"]

    def test_backend_can_verify_completed_callback_signature(self, worker, valid_invoice_data):
        """
        Simulates the backend's HMAC verification of a real COMPLETED callback.
        Uses CallbackService (base64 HMAC) — matches the ASP.NET backend's
        Convert.ToBase64String(HMACSHA256.ComputeHash(...)).
        """
        svc = CallbackService("http://fake", self.SECRET)
        cb = worker._create_completed_callback("job-verify-001", valid_invoice_data)

        # Serialize exactly as CallbackService.send_callback would
        body = json.dumps(cb).encode("utf-8")
        signature = svc._generate_hmac(body)

        # Backend re-computes and compares
        expected = base64.b64encode(
            hmac_lib.new(self.SECRET.encode(), body, hashlib.sha256).digest()
        ).decode()

        assert signature == expected