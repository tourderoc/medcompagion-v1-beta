#!/usr/bin/env python3
"""
MedCompanion VPS Monitor
Expose les métriques système via une API REST légère.

Installation sur le VPS :
    pip install flask psutil
    python3 monitor.py

Lancer en service systemd (recommandé) :
    sudo nano /etc/systemd/system/medcompanion-monitor.service
    sudo systemctl enable --now medcompanion-monitor
"""

import subprocess
import os
from flask import Flask, jsonify, request, abort
import psutil

app = Flask(__name__)

# ─── Configuration ────────────────────────────────────────────────
# Changer ce token et le copier dans MedCompanion > Paramètres > API > Serveur VPS
API_TOKEN = os.environ.get("MONITOR_TOKEN", "CHANGER_CE_TOKEN_SECRET")

# Services systemd à surveiller
SERVICES = ["livekit", "coturn", "nginx", "postgresql"]

PORT = 5050
# ─────────────────────────────────────────────────────────────────


def check_token():
    """Vérifie le token dans le header X-Token."""
    token = request.headers.get("X-Token", "")
    if token != API_TOKEN:
        abort(401, description="Token invalide")


@app.route("/metrics")
def metrics():
    check_token()

    # CPU (non bloquant — intervalle 0.1s)
    cpu = psutil.cpu_percent(interval=0.1)

    # RAM
    ram = psutil.virtual_memory()

    # Disque racine
    disk = psutil.disk_usage("/")

    # Réseau
    net = psutil.net_io_counters()

    # Uptime
    import time
    uptime_seconds = time.time() - psutil.boot_time()
    hours = int(uptime_seconds // 3600)
    minutes = int((uptime_seconds % 3600) // 60)
    uptime_str = f"{hours}h {minutes}m"

    # État des services
    services = {}
    for svc in SERVICES:
        try:
            result = subprocess.run(
                ["systemctl", "is-active", svc],
                capture_output=True, text=True, timeout=2
            )
            services[svc] = result.stdout.strip()
        except Exception:
            services[svc] = "unknown"

    return jsonify({
        "cpu": round(cpu, 1),
        "ram": round(ram.percent, 1),
        "disk": round(disk.percent, 1),
        "network_sent_mb": round(net.bytes_sent / 1_048_576, 1),
        "network_recv_mb": round(net.bytes_recv / 1_048_576, 1),
        "uptime": uptime_str,
        "services": services
    })


@app.route("/logs/<service_name>")
def logs(service_name):
    check_token()

    # Sécurité : autoriser uniquement les services connus
    allowed = SERVICES + ["nginx", "sshd", "fail2ban"]
    if service_name not in allowed:
        abort(400, description=f"Service non autorisé : {service_name}")

    lines = request.args.get("lines", 50, type=int)
    lines = min(lines, 200)  # Limiter à 200 lignes max

    try:
        result = subprocess.run(
            ["journalctl", "-u", service_name, "-n", str(lines), "--no-pager", "--output=short"],
            capture_output=True, text=True, timeout=5
        )
        return jsonify({"service": service_name, "logs": result.stdout})
    except Exception as e:
        return jsonify({"service": service_name, "logs": "", "error": str(e)}), 500


@app.route("/health")
def health():
    """Endpoint public pour vérifier que le script tourne (sans token)."""
    return jsonify({"status": "ok"})


if __name__ == "__main__":
    print(f"MedCompanion Monitor démarré sur le port {PORT}")
    print(f"Token configuré : {API_TOKEN[:4]}{'*' * (len(API_TOKEN) - 4)}")
    app.run(host="0.0.0.0", port=PORT)
