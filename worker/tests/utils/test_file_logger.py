import pytest
import os
import logging
import glob
from app.utils.file_logger import setup_file_logging, SuccessFilter, WarnFilter, FailFilter

@pytest.fixture(autouse=True)
def cleanup_loggers():
    """Clears handlers to prevent duplicate logging between tests."""
    root = logging.getLogger()
    for handler in root.handlers[:]:
        root.removeHandler(handler)
    yield
    for handler in root.handlers[:]:
        root.removeHandler(handler)

def test_log_filters():
    """Verifies Success, Warn, and Fail filters correctly identify levels."""
    info_record = logging.LogRecord("test", logging.INFO, "path", 1, "msg", None, None)
    warn_record = logging.LogRecord("test", logging.WARNING, "path", 1, "msg", None, None)
    err_record = logging.LogRecord("test", logging.ERROR, "path", 1, "msg", None, None)

    assert SuccessFilter().filter(info_record) is True
    assert SuccessFilter().filter(warn_record) is False
    assert WarnFilter().filter(warn_record) is True
    assert FailFilter().filter(err_record) is True

def test_setup_file_logging_routes_correctly(tmp_path):
    """Verifies directory creation and log routing with explicit level setting."""
    base_log_dir = str(tmp_path / "logs_worker")
    
    # 1. Initialize logging
    setup_file_logging(base_dir=base_log_dir)
    
    # CRITICAL: Force the logger to DEBUG so it doesn't block INFO/DEBUG logs
    test_logger = logging.getLogger("worker_test")
    test_logger.setLevel(logging.DEBUG)
    test_logger.propagate = True 

    # 2. Generate Logs
    test_logger.info("THIS IS SUCCESS")
    test_logger.warning("THIS IS WARNING")
    test_logger.error("THIS IS FAILURE")

    # 3. Verify File Content
    success_files = glob.glob(os.path.join(base_log_dir, "success", "*.txt"))
    warn_files = glob.glob(os.path.join(base_log_dir, "warn", "*.txt"))
    fail_files = glob.glob(os.path.join(base_log_dir, "fail", "*.txt"))

    with open(success_files[0], "r", encoding="utf-8") as f:
        content = f.read()
        assert "THIS IS SUCCESS" in content
        assert "THIS IS FAILURE" not in content

    with open(warn_files[0], "r", encoding="utf-8") as f:
        assert "THIS IS WARNING" in f.read()

    with open(fail_files[0], "r", encoding="utf-8") as f:
        assert "THIS IS FAILURE" in f.read()