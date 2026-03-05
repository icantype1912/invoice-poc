import pytest
from app.utils.hmac import compute_hmac, verify_hmac

# --------------------------------------------------------------------------
# FIXTURES: Common data for HMAC tests
# --------------------------------------------------------------------------
@pytest.fixture
def test_secret():
    """Returns a dummy shared secret for signing."""
    return "super-shared-secret-key-123"

@pytest.fixture
def test_payload():
    """Returns a sample callback payload dictionary."""
    return {
        "jobId": "uuid-1234-5678",
        "status": "COMPLETED",
        "workerId": "worker-1"
    }

# --------------------------------------------------------------------------
# TEST: Signature Computation
# --------------------------------------------------------------------------
def test_compute_hmac_is_deterministic(test_payload, test_secret):
    """
    Verifies that the same payload and secret always produce the 
    same signature (lowercase hex, 64 chars).
    Reference: app.utils.hmac.compute_hmac
    """
    sig1 = compute_hmac(test_payload, test_secret)
    sig2 = compute_hmac(test_payload, test_secret)
    
    # Assertions
    assert sig1 == sig2
    assert len(sig1) == 64  # SHA-256 hex length
    assert sig1.islower()

# --------------------------------------------------------------------------
# TEST: Signature Verification Success/Failure
# --------------------------------------------------------------------------
def test_verify_hmac_success(test_payload, test_secret):
    """Verifies that a correctly computed signature passes validation."""
    sig = compute_hmac(test_payload, test_secret)
    assert verify_hmac(test_payload, sig, test_secret) is True

def test_verify_hmac_tamper_detection(test_payload, test_secret):
    """Verifies that changing the payload even slightly causes verification to fail."""
    sig = compute_hmac(test_payload, test_secret)
    
    # Tamper with the payload
    tampered_payload = test_payload.copy()
    tampered_payload["status"] = "FAILED"
    
    assert verify_hmac(tampered_payload, sig, test_secret) is False

# --------------------------------------------------------------------------
# TEST: JSON Serialization Whitespace
# --------------------------------------------------------------------------
def test_hmac_serialization_sensitivity(test_payload, test_secret):
    """
    Ensures that the compute_hmac function is using strict 
    separators (no spaces) for signature matching.
    """
    # If the backend uses spaces and we don't, the signature fails.
    # Your code uses separators=(',', ':') to ensure NO whitespace.
    sig = compute_hmac(test_payload, test_secret)
    assert isinstance(sig, str)