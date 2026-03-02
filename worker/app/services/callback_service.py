import httpx
import json
import logging
import hmac
import hashlib
import base64

logger = logging.getLogger(__name__)

class CallbackService:
    """Handles HMAC-signed callbacks to ASP.NET backend."""

    def __init__(self, backend_url: str, callback_secret: str):
        self.backend_url = backend_url
        self.callback_secret = callback_secret
        self.logger = logger

    def _generate_hmac(self, body: bytes) -> str:
        """
        Generate HMAC-SHA256 signature for request body.

        Args:
            body: Request body as bytes

        Returns:
            Base64-encoded HMAC signature (to match ASP.NET backend)
        """
        signature = hmac.new(
            self.callback_secret.encode('utf-8'),
            body,
            hashlib.sha256
        ).digest()  # FIX: Use .digest() instead of .hexdigest()

        # FIX: Encode to base64 to match backend's Convert.ToBase64String()
        return base64.b64encode(signature).decode('utf-8')

    async def send_callback(self, callback_data: dict) -> bool:
        """
        Send callback to backend API with HMAC authentication.

        Args:
            callback_data: Dictionary containing job result data

        Returns:
            True if callback was accepted (HTTP 200)

        Raises:
            Exception: If callback fails or times out
        """
        url = f"{self.backend_url}/api/callback"

        # Serialize callback data
        body = json.dumps(callback_data).encode('utf-8')

        # Generate HMAC signature (base64 encoded)
        hmac_signature = self._generate_hmac(body)

        headers = {
            "Content-Type": "application/json",
            "X-Callback-HMAC": hmac_signature
        }

        self.logger.info(f"Sending callback for job {callback_data['jobId']} to {url}")
        self.logger.debug(f"HMAC signature: {hmac_signature[:20]}...")

        try:
            # Increase timeout to 180 seconds (3 minutes)
            async with httpx.AsyncClient(timeout=180.0, verify = False) as client:
                response = await client.post(url, headers=headers, content=body)

                if response.status_code == 200:
                    self.logger.info(f"Callback accepted for job {callback_data['jobId']}")
                    return True
                else:
                    self.logger.error(f"Callback failed: HTTP {response.status_code}")
                    self.logger.error(f"Response: {response.text}")
                    raise Exception(f"Backend returned {response.status_code}")

        except httpx.TimeoutException:
            self.logger.error("Callback request timed out")
            raise Exception("Callback request timed out after 180s")
        except httpx.ReadTimeout:
            self.logger.error("Callback request timed out")
            raise Exception("Callback request timed out after 180s")
        except Exception as e:
            self.logger.error(f"Callback request failed: {e}")
            raise
