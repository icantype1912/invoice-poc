import pytest
import io
from unittest.mock import MagicMock, patch
from googleapiclient.errors import HttpError
from app.services.drive_service import DriveService

@pytest.fixture
def mock_drive_service():
    # Patch the service account and build functions to avoid real API calls
    with patch("google.oauth2.service_account.Credentials.from_service_account_file"), \
         patch("app.services.drive_service.build"):
        service = DriveService("fake_key.json")
        service.service = MagicMock() # Mock the actual Google API client
        return service

def test_download_file_success(mock_drive_service):
    # Setup mock for successful chunked download
    mock_request = mock_drive_service.service.files().get_media()
    
    # We mock MediaIoBaseDownload to simulate a successful completion
    with patch("app.services.drive_service.MediaIoBaseDownload") as mock_download_class:
        mock_downloader = mock_download_class.return_value
        mock_downloader.next_chunk.return_value = (None, True) # (status, done)
        
        # Simulate file content in the buffer
        file_content = b"fake invoice data"
        
        # We need to mock how the buffer is handled inside download_file
        with patch("io.BytesIO") as mock_buffer:
            instance = mock_buffer.return_value
            instance.read.return_value = file_content
            
            result = mock_drive_service.download_file("file_123")
            
            assert result == file_content
            assert mock_download_class.called

def test_download_file_retry_on_connection_error(mock_drive_service):
    # Setup mock to fail once then succeed
    mock_request = mock_drive_service.service.files().get_media()
    
    with patch("app.services.drive_service.MediaIoBaseDownload") as mock_download_class:
        mock_downloader = mock_download_class.return_value
        # First call raises ConnectionError, second call succeeds
        mock_downloader.next_chunk.side_effect = [ConnectionError("WinError 10053"), (None, True)]
        
        with patch("time.sleep"): # Skip actual sleeping during tests
            result = mock_drive_service.download_file("file_123")
            
            assert result is not None
            # Check that it was called twice due to the retry logic [cite: 120, 126]
            assert mock_downloader.next_chunk.call_count == 2

def test_download_file_max_retries_exceeded(mock_drive_service):
    with patch("app.services.drive_service.MediaIoBaseDownload") as mock_download_class:
        mock_downloader = mock_download_class.return_value
        # Always fail
        mock_downloader.next_chunk.side_effect = ConnectionError("Persistent Error")
        
        with patch("time.sleep"), pytest.raises(Exception) as excinfo:
            mock_drive_service.download_file("file_123")
        
        assert "Failed to download file" in str(excinfo.value)
        # Verify it tried 3 times [cite: 120, 127]
        assert mock_downloader.next_chunk.call_count == 3