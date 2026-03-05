import pytest
from datetime import datetime, timezone
from app.models.job import Job, JobPayload, JobStatus 

@pytest.fixture
def sample_job_payload():
    return {
        "fileId": "drive_123",
        "originalName": "invoice.pdf",
        "mimeType": "application/pdf",
        "fileSize": 1024,
        "uploader": "test_user",
        "idempotencyKey": "unique_key_1",
        "detectedAt": datetime.now(timezone.utc).isoformat()
    }

@pytest.fixture
def sample_job(sample_job_payload):
    return Job(
        id="job_uuid_123",
        jobType="INVOICE_EXTRACTION",
        status=JobStatus.PENDING,
        payload=JobPayload(**sample_job_payload),
        retryCount=0,
        createdAt=datetime.now(timezone.utc),
        updatedAt=datetime.now(timezone.utc)
    )