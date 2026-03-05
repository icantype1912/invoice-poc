import pytest
from unittest.mock import patch
from pydantic import ValidationError
from app.config import Config

# --------------------------------------------------------------------------
# TEST: Loading from Environment
# --------------------------------------------------------------------------
def test_config_loading_success():
    """
    Verifies that Config correctly reads environment variables and 
    computes the connection string.
    """
    fake_env = {
        "DB_HOST": "localhost",
        "DB_NAME": "invoice_db",
        "DB_USER": "admin",
        "DB_PASSWORD": "password123",
        "BACKEND_URL": "https://api.test.com",
        "CALLBACK_SECRET": "secret_key",
        "GOOGLE_SERVICE_ACCOUNT_KEY": "path/to/key.json",
        "GROQ_API_KEY": "gsk_test_key",
        "GROQ_MODEL": "llama-4-70b-versatile"  # Specifically testing the upgrade here
    }

    with patch.dict("os.environ", fake_env):
        # Ignore .env file to ensure we are testing the fake_env above
        config = Config(_env_file=None)
        
        # Verify required fields
        assert config.db_host == "localhost"
        assert config.groq_api_key == "gsk_test_key"
        
        # Verify the model was successfully overridden by the env var
        assert config.groq_model == "llama-4-70b-versatile" 

        # Verify computed property remains valid
        conn_str = config.db_connection_string
        assert "host=localhost" in conn_str
        assert "user=admin" in conn_str

# --------------------------------------------------------------------------
# TEST: Validation Errors
# --------------------------------------------------------------------------
def test_config_missing_required_fields():
    """
    Ensures validation fails if required fields are missing and no .env is present.
    """
    with patch.dict("os.environ", {}, clear=True):
        with pytest.raises(ValidationError):
            Config(_env_file=None)

# --------------------------------------------------------------------------
# TEST: Data Type Enforcement
# --------------------------------------------------------------------------
def test_config_type_coercion():
    """Verifies string-to-int conversion for port numbers."""
    fake_env = {
        "DB_HOST": "db", "DB_NAME": "db", "DB_USER": "u", "DB_PASSWORD": "p",
        "BACKEND_URL": "url", "CALLBACK_SECRET": "s",
        "GOOGLE_SERVICE_ACCOUNT_KEY": "k", "GROQ_API_KEY": "key",
        "DB_PORT": "9999"
    }

    with patch.dict("os.environ", fake_env):
        config = Config(_env_file=None)
        assert config.db_port == 9999