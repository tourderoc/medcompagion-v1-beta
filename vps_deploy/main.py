"""
Avatar AI Service - VPS (parentaile-avatar)
FastAPI + paprika.onnx + SQLite quota management (Étape 1 migration)
Endpoints: /avatar/{userId}/...
Auth: X-Api-Key header
"""

import os
import sqlite3
import uuid
from datetime import date, datetime
from contextlib import contextmanager
from pathlib import Path

import numpy as np
import onnxruntime as ort
from fastapi import FastAPI, File, UploadFile, HTTPException, Header, Depends
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from fastapi.responses import JSONResponse
from PIL import Image
import uvicorn

# === Config ===
BASE_DIR = Path(__file__).parent
MODEL_PATH = BASE_DIR / "models" / "paprika.onnx"
STATIC_DIR = BASE_DIR / "static" / "avatars"
DB_PATH = BASE_DIR / "quota.db"
VPS_URL = "https://avatar.parentaile.fr"

DAILY_LIMIT = 2
ADMIN_DAILY_LIMIT = 999
API_KEY = os.getenv("AVATAR_API_KEY", "default-test-key")
ADMIN_EMAILS = [
    "tourderoc@gmail.com",
    "admin@parentaile.fr",
    "nairmedcin@gmail.com",
]

# === SQLite ===
def init_db():
    """Initialise la base SQLite pour les quotas."""
    conn = sqlite3.connect(str(DB_PATH))
    conn.execute("""
        CREATE TABLE IF NOT EXISTS quotas (
            userId TEXT NOT NULL,
            date TEXT NOT NULL,
            count INTEGER DEFAULT 0,
            PRIMARY KEY (userId, date)
        )
    """)
    conn.commit()
    conn.close()

@contextmanager
def get_db():
    """Context manager pour les connexions SQLite."""
    conn = sqlite3.connect(str(DB_PATH))
    conn.row_factory = sqlite3.Row
    try:
        yield conn
        conn.commit()
    finally:
        conn.close()

def get_quota(user_id: str, is_admin: bool = False) -> dict:
    """Retourne le quota restant pour un utilisateur."""
    today = date.today().isoformat()
    limit = ADMIN_DAILY_LIMIT if is_admin else DAILY_LIMIT

    with get_db() as conn:
        row = conn.execute(
            "SELECT count FROM quotas WHERE userId = ? AND date = ?",
            (user_id, today)
        ).fetchone()

    count = row["count"] if row else 0
    remaining = max(0, limit - count)

    return {
        "canGenerate": remaining > 0,
        "remaining": remaining,
        "used": count,
        "limit": limit,
        "reason": None if remaining > 0 else "Quota quotidien atteint (2/jour)"
    }

def increment_quota(user_id: str):
    """Incrémente le compteur de génération pour aujourd'hui."""
    today = date.today().isoformat()

    with get_db() as conn:
        row = conn.execute(
            "SELECT count FROM quotas WHERE userId = ? AND date = ?",
            (user_id, today)
        ).fetchone()

        if row:
            conn.execute(
                "UPDATE quotas SET count = count + 1 WHERE userId = ? AND date = ?",
                (user_id, today)
            )
        else:
            conn.execute(
                "INSERT INTO quotas (userId, date, count) VALUES (?, ?, 1)",
                (user_id, today)
            )

# === ONNX Model ===
STATIC_DIR.mkdir(parents=True, exist_ok=True)

print(f"Loading model from {MODEL_PATH}...")
session = ort.InferenceSession(str(MODEL_PATH))
input_name = session.get_inputs()[0].name
print("Model loaded OK")

def transform_image(img: Image.Image) -> np.ndarray:
    """Prépare l'image pour le modèle ONNX paprika [1, 3, 512, 512]."""
    img = img.convert("RGB").resize((512, 512))
    arr = np.array(img).astype(np.float32) / 127.5 - 1.0  # [-1, 1] (512, 512, 3)
    arr = np.transpose(arr, (2, 0, 1))  # Channels first: (3, 512, 512)
    return np.expand_dims(arr, axis=0)  # (1, 3, 512, 512)

def postprocess(output: np.ndarray) -> Image.Image:
    """Convertit la sortie du modèle [1, 3, 512, 512] en image PIL."""
    img = output.squeeze()  # (3, 512, 512)
    img = (img + 1.0) * 127.5
    img = np.clip(img, 0, 255).astype(np.uint8)
    img = np.transpose(img, (1, 2, 0))  # (512, 512, 3) pour PIL
    return Image.fromarray(img)

# === Auth ===
def verify_api_key(x_api_key: str = Header(...)) -> str:
    """Vérifie la clé API X-Api-Key."""
    if x_api_key != API_KEY:
        raise HTTPException(status_code=403, detail="Invalid API Key")
    return x_api_key

# === FastAPI ===
app = FastAPI(title="Parent'aile Avatar Service")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.mount("/static", StaticFiles(directory=str(BASE_DIR / "static")), name="static")

@app.get("/")
async def root():
    return {"status": "ok", "service": "parentaile-avatar", "model": "paprika.onnx", "version": "v1-quota"}

@app.get("/avatar/{user_id}/quota")
async def check_quota_endpoint(user_id: str, email: str = "", _: str = Depends(verify_api_key)):
    """Vérifie le quota d'un utilisateur (authentification requise)."""
    is_admin = email.lower() in [e.lower() for e in ADMIN_EMAILS] if email else False
    quota = get_quota(user_id, is_admin=is_admin)
    return quota

@app.post("/avatar/{user_id}/generate")
async def generate_avatar(user_id: str, file: UploadFile = File(...), email: str = "", _: str = Depends(verify_api_key)):
    """
    Génère un avatar IA. Vérifie et incrémente le quota automatiquement.

    Authentification: Header X-Api-Key requise
    Query param: email (pour vérifier si admin)
    Quota: Géré en SQLite (2/jour, illimité pour admins)
    """
    try:
        is_admin = email.lower() in [e.lower() for e in ADMIN_EMAILS] if email else False
        quota = get_quota(user_id, is_admin=is_admin)

        if not quota["canGenerate"]:
            return JSONResponse(
                status_code=429,
                content={"status": "error", "message": quota["reason"]}
            )

        # Read and transform image
        contents = await file.read()
        from io import BytesIO
        img = Image.open(BytesIO(contents))

        input_data = transform_image(img)
        result = session.run(None, {input_name: input_data})
        output_img = postprocess(result[0])

        # Save with unique filename to avoid cache issues
        filename = f"{user_id}.jpg"
        output_path = STATIC_DIR / filename
        output_img.save(str(output_path), "JPEG", quality=90)

        # Increment quota AFTER successful generation
        increment_quota(user_id)

        url = f"{VPS_URL}/static/avatars/{filename}"
        return {"status": "success", "url": url}

    except Exception as e:
        print(f"Error generating avatar for {user_id}: {e}")
        return JSONResponse(
            status_code=500,
            content={"status": "error", "message": str(e)}
        )

@app.get("/health")
async def health():
    """Health check endpoint."""
    return {
        "status": "ok",
        "model_loaded": session is not None,
        "db_path": str(DB_PATH),
        "timestamp": datetime.now().isoformat()
    }

# === Init ===
init_db()

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)
