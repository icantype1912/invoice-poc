import pytest
import json
from unittest.mock import MagicMock, patch
from app.database.job_claimer import JobClaimer

@pytest.fixture
def mock_claimer():
    claimer = JobClaimer("dbname=test user=test")
    claimer.connection = MagicMock()
    return claimer

def test_connect_success():
    with patch("psycopg2.connect") as mock_psycopg:
        claimer = JobClaimer("dsn")
        claimer.connect()
        mock_psycopg.assert_called_once_with("dsn")
        assert claimer.connection is not None

def test_claim_job_no_jobs(mock_claimer):
    # Setup mock cursor to return None (no jobs pending)
    mock_cursor = mock_claimer.connection.cursor.return_value
    mock_cursor.fetchone.return_value = None
    
    result = mock_claimer.claim_job("worker-1")
    
    assert result is None
    mock_cursor.execute.assert_called_once() # Check SELECT was called

def test_claim_job_success(mock_claimer, sample_job_payload):
    mock_cursor = mock_claimer.connection.cursor.return_value
    
    # Simulate a row returned from PostgreSQL
    mock_cursor.fetchone.return_value = {
        "Id": "job_uuid_123",
        "JobType": "INVOICE_EXTRACTION",
        "Status": "PENDING",
        "PayloadJson": json.dumps(sample_job_payload),
        "RetryCount": 0,
        "CreatedAt": "2026-03-04T10:00:00Z",
        "UpdatedAt": "2026-03-04T10:00:00Z"
    }
    
    result = mock_claimer.claim_job("worker-1")
    
    assert result.id == "job_uuid_123"
    assert result.payload.fileId == "drive_123"
    mock_claimer.connection.commit.assert_called_once()

def test_release_job_lock(mock_claimer):
    mock_cursor = mock_claimer.connection.cursor.return_value
    mock_cursor.rowcount = 1
    
    success = mock_claimer.release_job_lock("job_uuid_123")
    
    assert success is True
    mock_claimer.connection.commit.assert_called_once()

def test_release_all_locks(mock_claimer):
    mock_cursor = mock_claimer.connection.cursor.return_value
    mock_cursor.rowcount = 5
    
    count = mock_claimer.release_all_locks("worker-1")
    
    assert count == 5
    mock_claimer.connection.commit.assert_called_once()

def test_claim_job_database_error(mock_claimer):
    mock_cursor = mock_claimer.connection.cursor.return_value
    mock_cursor.execute.side_effect = Exception("DB Error")
    
    with pytest.raises(Exception):
        mock_claimer.claim_job("worker-1")
    
    mock_claimer.connection.rollback.assert_called_once()