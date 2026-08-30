"""
Minimal local backend for testing SensorDataExporter.cs.

Run this, then point Unity's SensorDataExporter.backendUrl at
http://localhost:5000/sensor-data and hit Play with a quake triggered.

Install once:
    pip install flask --break-system-packages

Run:
    python receiver.py

Then open http://localhost:5000/latest in a browser while Unity is running
to see the most recent reading, or watch this terminal for live logs.
"""

from flask import Flask, request, jsonify
from datetime import datetime

app = Flask(__name__)

# Very simple in-memory store - fine for testing, swap for a real DB later
latest_payload = None
history = []
MAX_HISTORY = 200


@app.route("/sensor-data", methods=["POST"])
def receive_sensor_data():
    global latest_payload

    payload = request.get_json()
    if payload is None:
        return jsonify({"error": "no JSON body received"}), 400

    payload["received_at"] = datetime.utcnow().isoformat()
    latest_payload = payload
    history.append(payload)
    if len(history) > MAX_HISTORY:
        history.pop(0)

    # Quick console readout so you can watch it live
    readings = payload.get("readings", [])
    summary = ", ".join(
        f"{r['zone_id']}={r['acceleration_g']:.3f}g"
        + (" [DAMAGED]" if r.get("is_damaged") else "")
        for r in readings
    )
    print(f"[t={payload.get('timestamp', '?'):.2f}] {summary}")

    return jsonify({"status": "ok"}), 200


@app.route("/latest", methods=["GET"])
def get_latest():
    if latest_payload is None:
        return jsonify({"message": "no data received yet"}), 200
    return jsonify(latest_payload), 200


@app.route("/history", methods=["GET"])
def get_history():
    return jsonify(history), 200


if __name__ == "__main__":
    print("Sensor receiver running at http://localhost:5000")
    print("  POST /sensor-data  <- Unity sends readings here")
    print("  GET  /latest       <- view the most recent reading")
    print("  GET  /history      <- view the last 200 readings")
    app.run(host="0.0.0.0", port=5000, debug=False)
