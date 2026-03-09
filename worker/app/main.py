import asyncio
import threading
import logging
from contextlib import asynccontextmanager
from datetime import datetime, timezone
from fastapi import FastAPI, HTTPException
from typing import Dict

from app.config import load_config
from app.worker import InvoiceWorker
from app.utils.hmac import compute_hmac
from app.utils.file_logger import setup_file_logging

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)

# Initialize file-based logging (logs/logs_worker/success|warn|fail/)
setup_file_logging()

logger = logging.getLogger(__name__)

# Global worker instance
worker: InvoiceWorker = None


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Startup and shutdown lifecycle."""
    global worker

    # Startup: Initialize and start worker in background thread
    try:
        config = load_config()
        worker = InvoiceWorker(config)

        # Run worker in separate thread
        worker_thread = threading.Thread(
            target=lambda: asyncio.run(worker.start()),
            daemon=True
        )
        worker_thread.start()

        logger.info("FastAPI app started with background worker")

        yield

    finally:
        # Shutdown: Stop worker gracefully
        if worker:
            worker.is_running = False
        logger.info("FastAPI app stopped")


app = FastAPI(
    title="Invoice Processing Worker",
    description="AI-powered invoice extraction worker with llama oss integration",
    version="1.0.0",
    lifespan=lifespan
)


@app.get("/")
def root():
    """Root endpoint."""
    return {
        "service": "Invoice Processing Worker",
        "version": "1.0.0",
        "status": "running",
        "llm_model": "llama-3.3-70b-versatile"
    }


@app.get("/health")
def health():
    """Health check endpoint for monitoring."""
    if not worker:
        raise HTTPException(status_code=503, detail="Worker not initialized")

    uptime = (datetime.now(timezone.utc) - worker.stats["start_time"]).total_seconds()

    return {
        "status": "healthy",
        "worker_id": worker.config.worker_id,
        "uptime_seconds": round(uptime, 2),
        "is_running": worker.is_running
    }


@app.get("/metrics")
def metrics():
    """Worker metrics and statistics."""
    if not worker:
        raise HTTPException(status_code=503, detail="Worker not initialized")

    uptime = (datetime.now(timezone.utc) - worker.stats["start_time"]).total_seconds()
    total_jobs = (
        worker.stats["jobs_processed"] +
        worker.stats["jobs_failed"] +
        worker.stats["jobs_invalid"]
    )

    success_rate = (
        worker.stats["jobs_processed"] / total_jobs
        if total_jobs > 0 else 0
    )

    return {
        "jobs_completed": worker.stats["jobs_processed"],
        "jobs_failed": worker.stats["jobs_failed"],
        "jobs_invalid": worker.stats["jobs_invalid"],
        "total_jobs": total_jobs,
        "success_rate": round(success_rate, 4),
        "uptime_seconds": round(uptime, 2),
        "worker_id": worker.config.worker_id
    }


@app.post("/test/callback")
async def test_callback(payload: Dict):
    """
    Test endpoint for callback HMAC generation.
    """
    if not worker:
        raise HTTPException(status_code=503, detail="Worker not initialized")

    hmac_sig = compute_hmac(payload, worker.config.callback_secret)

    return {
        "payload": payload,
        "hmac": hmac_sig,
        "callback_url": f"{worker.config.backend_url}/api/callback"
    }
