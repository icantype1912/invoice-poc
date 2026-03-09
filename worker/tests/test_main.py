import pytest
from fastapi.testclient import TestClient
from unittest.mock import MagicMock, patch
from datetime import datetime, timezone
from app.main import app

# Create a test client for the FastAPI app
client = TestClient(app)

# --------------------------------------------------------------------------
# TEST: Root Endpoint
# --------------------------------------------------------------------------
def test_read_root():
    """Verifies basic service info and Llama-4 versioning."""
    response = client.get("/")
    assert response.status_code == 200
    data = response.json()
    assert data["service"] == "Invoice Processing Worker"
    assert data["llm_model"] == "llama-oss-120b"

# --------------------------------------------------------------------------
# TEST: Health Check & Metrics (with Worker Mock)
# --------------------------------------------------------------------------
def test_health_check_success():
    """
    Simulates an initialized worker and checks health status.
    We patch the global 'worker' object in app.main.
    """
    mock_worker = MagicMock()
    mock_worker.is_running = True
    mock_worker.config.worker_id = "test-worker-01"
    mock_worker.stats = {"start_time": datetime.now(timezone.utc)}

    with patch("app.main.worker", mock_worker):
        response = client.get("/health")
        assert response.status_code == 200
        assert response.json()["status"] == "healthy"
        assert response.json()["worker_id"] == "test-worker-01"

def test_metrics_calculation():
    """Verifies that success rate and job counts are calculated correctly."""
    mock_worker = MagicMock()
    mock_worker.stats = {
        "start_time": datetime.now(timezone.utc),
        "jobs_processed": 8,
        "jobs_failed": 1,
        "jobs_invalid": 1
    }
    mock_worker.config.worker_id = "test-worker-01"

    with patch("app.main.worker", mock_worker):
        response = client.get("/metrics")
        assert response.status_code == 200
        data = response.json()
        assert data["total_jobs"] == 10
        assert data["success_rate"] == 0.8  # 8/10
        assert data["jobs_completed"] == 8

# --------------------------------------------------------------------------
# TEST: Error States
# --------------------------------------------------------------------------
def test_health_worker_not_initialized():
    """Ensures 503 is returned if the background worker hasn't started."""
    with patch("app.main.worker", None):
        response = client.get("/health")
        assert response.status_code == 503
        assert "not initialized" in response.json()["detail"]

# --------------------------------------------------------------------------
# TEST: Callback Helper
# --------------------------------------------------------------------------
def test_test_callback_hmac():
    """Verifies the HMAC generation helper endpoint."""
    mock_worker = MagicMock()
    mock_worker.config.callback_secret = "test-secret"
    mock_worker.config.backend_url = "http://localhost:5000"
    
    test_payload = {"jobId": "123", "status": "COMPLETED"}

    with patch("app.main.worker", mock_worker), \
         patch("app.main.compute_hmac", return_value="fake-hmac-sig"):
        
        response = client.post("/test/callback", json=test_payload)
        assert response.status_code == 200
        assert response.json()["hmac"] == "fake-hmac-sig"
        assert "/api/callback" in response.json()["callback_url"]