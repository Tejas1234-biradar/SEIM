# SEIM - Disaster Simulation (Unity)

Unity-based disaster response and sensor network simulation project (`disaster-simulation`).

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

### Verification

1. **Start the backend** in `disaster-backend` (or `SEIM-Backend`):
   ```bash
   docker compose up -d
   ```
2. **Run the simulation in Unity**:
   - Open `Assets/Scenes/test.unity`.
   - Enter **Play Mode**.
   - Select the GameObject with `DumbSensorPipelineTest`, right-click the component header in the Inspector, and click **Run Dumb Sensor Pipeline Test** (or use the menu bar: **SEIM > Run Dumb Sensor Test**).
3. **Verify message arrival at the backend**:
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

