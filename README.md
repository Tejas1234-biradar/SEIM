# SEIM - Disaster Simulation (Unity)

Unity-based disaster response and sensor network simulation project (`disaster-simulation`).

## Automated Sensor Placement

Sensor placement starts automatically at scene load through the runtime bootstrap. Configure
`Assets/Resources/SensorPlacementConfig.asset` before Play mode:

- Select either a geographic boundary polygon (at least three latitude/longitude points) or an area
  with a geographic center and positive width/height in meters. An area is converted to a rectangular
  GeoJSON boundary for the backend placement endpoint.
- The Unity request uses the backend contract: `boundary` (GeoJSON Polygon with coordinates ordered
  `[longitude, latitude]`), `sensorRadiusKm`, `coverageMultiplicity` (from Unity's `k` setting), and
  `gridResolutionMeters`. The response's `sensors` provide `lat`/`lon` and projected `x`/`y`;
  returned coordinates are cached under `Application.persistentDataPath` for fallback.
- Set the geographic origin and `worldUnitsPerMeter` so backend positions map onto the Unity city
  model. Keep this scale consistent with `SimManager.KmPerUnit`:
  `worldUnitsPerMeter = 1 / (1000 * KmPerUnit)`. `Terrain.activeTerrain` is sampled for each placed
  sensor when its position lies inside the Terrain bounds.

On a successful placement, seismic stations are replaced using `Resources/VirtualSensor.prefab` and
the existing `Station`/`StationNetwork` pipeline. In Play mode, select `SensorPlacementServices` and
use **Compute Sensor Placement** in the `SensorPlacementClient` Inspector to rerun the HTTP request.
The runtime HUD toggles the coverage overlay and metrics; the overlay colors grid cells by coverage
multiplicity (red: zero, yellow: 1..k-1, green: at least k) and draws sensor-radius discs. The
`SensorPlacementValidation` component's **Run Sensor Coverage Earthquake Test** context action starts
a simulated quake and logs actual P-wave detections beside predicted radius coverage.

The backend's estimate-only `areaKm2` mode returns no sensor placements, so use the configured area
or polygon boundary for the Unity client.

## Connecting to Backend via MQTT

The simulation connects to `disaster-backend` (Mosquitto MQTT broker → Kafka → PostgreSQL) to stream raw sensor telemetry in real time.

### Scene Setup (One-Time Step)

1. Open your persistent simulation or test scene (e.g. `Assets/Scenes/test.unity`).
2. Select the persistent GameObject that holds `SensorDataStore` (or any persistent manager object).
3. Click **Add Component** and add **`MqttPublisher`**.
4. In the Inspector, configure:
   - **Broker Host**: `localhost` (or the IP of your Docker host).
   - **Broker Port**: `11883` (matches the host port mapped in `disaster-backend`'s `docker-compose.yml`).
5. On the `SensorDataStore` component, ensure **`Publish To Mqtt`** is checked (`true`).

*Note: In `Assets/Scenes/test.unity`, `MqttPublisher` is already attached to the `SensorDataStore` GameObject.*

### How It Works

- `SensorDataStore` automatically forwards every recorded sensor reading (e.g., `SeismicReading`, `GnssReading`) to `MqttPublisher`.
- `MqttPublisher` publishes the raw JSON payload to:
  ```
  sensors/{ReadingType}/readings
  ```
  (e.g., `sensors/Seismic/readings`, `sensors/Gnss/readings`) using QoS 0.
- If the broker is unreachable, `MqttPublisher` logs a warning and allows the local simulation to continue without interruption. When the broker becomes available, it automatically reconnects.

### Epicenter Detection Events

In addition to raw sensor data, `EpicenterProcessorNode` computes derived epicenter estimates using multi-station P-wave trilateration and publishes them to:
```
detections/Epicenter/events
```
Each event contains:
```json
{
  "eventId": "...",
  "detectedAtTime": 8.42,
  "estimatedEpicenterX": 15.02,
  "estimatedEpicenterZ": 7.98,
  "contributingStationIds": ["ST_01", "ST_02", "ST_03"],
  "contributingStationCount": 3,
  "pWaveSpeedKmS": 6.0
}
```

### Verification

1. **Start the backend** in `disaster-backend` (or `SEIM-Backend`):
   ```bash
   docker compose up -d
   ```
2. **Run the simulation in Unity**:
2. **Listen for detection events** (optional terminal check):
   ```bash
   mosquitto_sub -h localhost -p 11883 -t 'detections/Epicenter/events'
   ```
3. **Run the simulation in Unity**:
   - Open `Assets/Scenes/test.unity`.
   - Enter **Play Mode**.
   - Select the GameObject with `DumbSensorPipelineTest`, right-click the component header in the Inspector, and click **Run Dumb Sensor Pipeline Test** (or use the menu bar: **SEIM > Run Dumb Sensor Test**).
3. **Verify message arrival at the backend**:
4. **Verify message arrival at the backend**:
   - **Kafka Stream**:
     ```bash
     docker compose exec kafka /opt/kafka/bin/kafka-console-consumer.sh \
       --bootstrap-server localhost:9092 \
       --topic sensor.readings \
       --from-beginning
     ```
   - **PostgreSQL Database**:
     ```bash
     docker compose exec postgres psql -U disaster -d disaster -c \
       "SELECT event_id, reading_type, station_id, timestamp, payload FROM sensor_readings ORDER BY received_at DESC LIMIT 10;"
     ```
