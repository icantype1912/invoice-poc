import pytest
from unittest.mock import MagicMock, patch, AsyncMock
from datetime import datetime, timezone
from app.worker import InvoiceWorker
from app.models.invoice import InvoiceData
from app.services.mime_detector import ProcessingPipeline

# --------------------------------------------------------------------------
# FIXTURE: Mock Config & Worker
# --------------------------------------------------------------------------
@pytest.fixture
def mock_config():
    """Provides a fake configuration for worker initialization."""
    config = MagicMock()
    config.worker_id = "test-worker-001"
    config.poll_interval = 1
    config.max_retries = 3
    config.db_connection_string = "fake-connection-string"
    config.google_service_account_key = "fake-key.json"
    config.groq_api_key = "fake-api-key"
    # Updated to Llama-4
    config.groq_model = "llama-4-70b-versatile"
    config.backend_url = "http://fake-backend.com"
    config.callback_secret = "test-secret"
    return config

@pytest.fixture
def worker(mock_config):
    """
    Initializes InvoiceWorker while patching all external service classes.
    This prevents real network/DB connections.
    """
    with patch("app.worker.JobClaimer"), \
         patch("app.worker.DriveService"), \
         patch("app.worker.LLMExtractor"), \
         patch("app.worker.CallbackService"):
        # We pass _env_file=None if your Config class is a BaseSettings object
        return InvoiceWorker(mock_config)

# --------------------------------------------------------------------------
# TEST: Successful Processing Flow (PDF)
# --------------------------------------------------------------------------
@pytest.mark.asyncio
async def test_process_job_success(worker):
    """
    Tests the full _process_job pipeline from download to success callback.
    """
    # 1. Setup a fake job
    mock_job = MagicMock()
    mock_job.id = "job-123"
    mock_job.payload.fileId = "file-abc"
    mock_job.payload.mimeType = "application/pdf"

    # 2. Mock individual pipeline steps
    worker.drive_service.download_file.return_value = b"fake pdf content"
    
    with patch("app.worker.detect_mime_type", return_value="application/pdf"), \
         patch("app.worker.validate_mime_type", return_value=True), \
         patch("app.worker.get_pipeline_for_mime", return_value=ProcessingPipeline.PDF), \
         patch("app.worker.extract_text_from_pdf", return_value="Valid Invoice Text Content for Processing"), \
         patch("app.worker.validate_invoice_data", return_value=(True, "")), \
         patch("app.worker.convert_to_usd", side_effect=lambda x: x): 

        # 3. FIX: Mock the LLM return value with explicit attributes
        mock_invoice = MagicMock(spec=InvoiceData)
        # These attributes must exist so the logger and logic don't throw AttributeErrors
        mock_invoice.InvoiceNumber = "INV-001" 
        mock_invoice.Currency = "USD"
        mock_invoice.TotalAmount = 100.0
        mock_invoice.model_dump.return_value = {"InvoiceNumber": "INV-001", "TotalAmount": 100.0}
        
        worker.llm_extractor.extract_invoice.return_value = mock_invoice

        # 4. Execute
        result = await worker._process_job(mock_job)

        # 5. Assertions
        assert result["status"] == "COMPLETED"
        assert result["jobId"] == "job-123"
        assert result["result"]["InvoiceNumber"] == "INV-001"
        
        worker.drive_service.download_file.assert_called_once_with("file-abc")
        worker.llm_extractor.extract_invoice.assert_called_once()

# --------------------------------------------------------------------------
# TEST: Backoff Logic Calculation
# --------------------------------------------------------------------------
def test_calculate_backoff(worker):
    """Verifies exponential backoff logic: 2^n capped at 30 minutes."""
    assert worker._calculate_backoff(1) == 2   
    assert worker._calculate_backoff(3) == 8   
    assert worker._calculate_backoff(10) == 30 

# --------------------------------------------------------------------------
# TEST: Retry Decision Logic
# --------------------------------------------------------------------------
def test_should_retry_job(worker):
    """Ensures COMPLETED/INVALID are never retried, and FAILED is retried up to max."""
    assert worker._should_retry_job({"status": "COMPLETED"}, 0) is False
    assert worker._should_retry_job({"status": "INVALID"}, 0) is False
    assert worker._should_retry_job({"status": "FAILED"}, 0) is True
    # Max retries is 3, so at 3 it should return False
    assert worker._should_retry_job({"status": "FAILED"}, 3) is False

# --------------------------------------------------------------------------
# TEST: MIME Validation Failure
# --------------------------------------------------------------------------
@pytest.mark.asyncio
async def test_process_job_invalid_mime(worker):
    """Tests that a MIME mismatch returns an INVALID status immediately."""
    mock_job = MagicMock()
    mock_job.id = "job-456"
    mock_job.payload.mimeType = "application/pdf"
    
    worker.drive_service.download_file.return_value = b"fake content"
    
    with patch("app.worker.detect_mime_type", return_value="image/png"), \
         patch("app.worker.validate_mime_type", return_value=False):
        
        result = await worker._process_job(mock_job)
        
        assert result["status"] == "INVALID"
        assert "MIME type mismatch" in result["reason"]