import pytest
import json
import base64
import hmac
import hashlib
from unittest.mock import patch, MagicMock
import httpx
from app.services.callback_service import CallbackService

# --------------------------------------------------------------------------
# FIXTURE: Initialize CallbackService with test credentials
# --------------------------------------------------------------------------
@pytest.fixture
def callback_service():
    """
    Creates an instance of CallbackService with dummy backend details.
    """
    return CallbackService(
        backend_url="https://api.testbackend.com",
        callback_secret="test_secret_key_123"
    )

# --------------------------------------------------------------------------
# TEST: HMAC Generation Logic
# --------------------------------------------------------------------------
def test_generate_hmac_logic(callback_service):
    """
    Verifies that the HMAC signature is generated using SHA256 and 
    properly Base64 encoded to match ASP.NET expectations.
    """
    test_body = b'{"test": "data"}'
    secret = "test_secret_key_123"
    
    # Manual calculation to compare against the service's output
    expected_digest = hmac.new(
        secret.encode('utf-8'),
        test_body,
        hashlib.sha256
    ).digest()
    expected_base64 = base64.b64encode(expected_digest).decode('utf-8')
    
    result = callback_service._generate_hmac(test_body)
    
    assert result == expected_base64

# --------------------------------------------------------------------------
# TEST: Successful Callback (HTTP 200)
# --------------------------------------------------------------------------
@pytest.mark.asyncio
async def test_send_callback_success(callback_service):
    """
    Simulates a successful POST request to the backend.
    Uses 'respx' or 'httpx' mocking to simulate a 200 OK response.
    """
    test_data = {"jobId": "job-123", "status": "COMPLETED"}
    
    # Mock the httpx.AsyncClient.post response
    with patch("httpx.AsyncClient.post") as mock_post:
        mock_post.return_value = MagicMock(status_code=200)
        
        success = await callback_service.send_callback(test_data)
        
        assert success is True
        # Verify the correct URL and headers were used
        args, kwargs = mock_post.call_args
        assert args[0] == "https://api.testbackend.com/api/callback"
        assert "X-Callback-HMAC" in kwargs["headers"]
        assert kwargs["headers"]["Content-Type"] == "application/json"

# --------------------------------------------------------------------------
# TEST: Backend Error Handling (HTTP 500)
# --------------------------------------------------------------------------
@pytest.mark.asyncio
async def test_send_callback_backend_error(callback_service):
    """
    Verifies that the service raises an Exception when the backend 
    returns a non-200 status code.
    """
    test_data = {"jobId": "job-123"}
    
    with patch("httpx.AsyncClient.post") as mock_post:
        mock_post.return_value = MagicMock(status_code=500, text="Internal Server Error")
        
        with pytest.raises(Exception) as excinfo:
            await callback_service.send_callback(test_data)
        
        assert "Backend returned 500" in str(excinfo.value)

# --------------------------------------------------------------------------
# TEST: Timeout Handling
# --------------------------------------------------------------------------
@pytest.mark.asyncio
async def test_send_callback_timeout(callback_service):
    """
    Verifies that the service handles httpx.TimeoutException gracefully 
    by raising a descriptive error.
    """
    test_data = {"jobId": "job-123"}
    
    with patch("httpx.AsyncClient.post", side_effect=httpx.TimeoutException("Timeout")):
        with pytest.raises(Exception) as excinfo:
            await callback_service.send_callback(test_data)
        
        assert "Callback request timed out after 180s" in str(excinfo.value)