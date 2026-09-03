# Agent Task: Connect `disaster-simulation` (Unity) to `disaster-backend` via MQTT

## Project context

`disaster-simulation` (Unity/C#) already has a working Layer 1 sensor pipeline:
`Station` → `SensorDataStore` (in-memory) → `EpicenterProcessorNode`. It also
already writes readings to a local JSON file via `SensorDataStore`'s
`AppendJsonLine()`.

`disaster-backend` (Java/Spring Boot) is already built and running via
`docker compose up`, listening for MQTT on `sensors/{ReadingType}/readings`
(e.g. `sensors/Seismic/readings`), bridging into Kafka, and persisting to
PostgreSQL.

**This task connects the two**, entirely through code — no new per-station
manual setup in the Unity Editor. The only manual step is adding one MQTT
publisher component to the scene once (see Step 3).

## CRITICAL — read before writing any code

`disaster-backend`'s README contains **example** `mosquitto_pub` payloads
(e.g. GNSS shown with `latitude`/`longitude`/`altitude_m`) that **do not
match** Unity's actual sensor structs in `SensorReadings.cs` (Unity's
`GnssReading` uses `displacementX_m`/`displacementY_m`/`displacementZ_m`).
This is not a bug to fix — it's a leftover from an earlier planning session,
and it does not matter functionally: the backend stores the payload as an
opaque JSONB blob, so it accepts whatever valid JSON Unity sends.

**Unity's `SensorReadings.cs` structs are the canonical contract.** Serialize
them exactly as they are defined — do NOT rename fields to match the
backend README's examples. If you want to also fix the README's example
payloads to match reality for future clarity, that's a welcome but optional
cleanup, and should be a clearly separate, clearly labeled change.

## Library

Use **M2Mqtt** (`M2MqttUnity` wrapper by gpvigano:
https://github.com/gpvigano/M2MqttUnity ) — the established, callback-based
MQTT client for Unity. Do not use MQTTnet; its `async`/`await` model is
awkward inside Unity's main-thread update loop.

## Broker connection details

Match `disaster-backend`'s `docker-compose.yml`:
- Host: `localhost` (or whatever the developer's Docker host resolves to —
  expose this as an Inspector field, don't hardcode)
- Port: `11883` (host-mapped Mosquitto port per the backend README)
- No authentication (local dev, matches backend's documented simplification)

## Topic convention — must match the backend exactly

```
sensors/{ReadingType}/readings
```

Where `{ReadingType}` is one of: `Seismic`, `Gnss`, `DartBuoy`, `TideGauge`,
`OceanBottom` — these are the exact strings already used as the
`readingType` tag in `SensorDataStore.AppendJsonLine()`. Reuse those same
string literals for the MQTT topic segment, don't introduce new naming.

## Task steps

### Step 1 — `MqttPublisher.cs`
A single `MonoBehaviour` singleton (same `Instance` pattern as
`SensorDataStore`, `StationNetwork`, etc. already in the project):
- Inspector fields: `brokerHost` (string, default `"localhost"`),
  `brokerPort` (int, default `11883`)
- Connects on `Start()`, using a generated client ID (e.g. `"unity-" +
  System.Guid.NewGuid()`)
- Handle connection failure gracefully: log a clear warning and continue
  (don't throw/crash) — the simulation should still run locally even if the
  backend isn't up, same resilience principle as the existing JSON file
  export having its own independent toggle
- One method: `Publish(string readingType, string jsonPayload)` — serializes
  nothing itself, just sends whatever JSON string it's given to the correct
  topic, `QoS 0` (at-most-once — acceptable for this use case, no need for
  guaranteed delivery)
- Include basic reconnect logic: if a publish fails because the client is
  disconnected, attempt one reconnect before giving up and logging

### Step 2 — Wire it into `SensorDataStore`
Modify `SensorDataStore.AppendJsonLine()` (or add a parallel method called
from the same call sites) so that every time a reading is recorded, it is
ALSO published over MQTT, not just written to file. Reuse the JSON string
already being built for the file export — don't serialize twice with two
different code paths.

Add an Inspector toggle `publishToMqtt` (default `true`), independent of
the existing `exportToJsonFile` toggle, so either or both can be enabled.

**Important:** the JSON sent over MQTT should be the RAW reading struct
(e.g. just the `SeismicReading` fields), NOT the `{"readingType":...,
"data":...}` envelope currently used for the file export. The backend's
MQTT bridge is responsible for wrapping it in the Kafka envelope
(`eventId`, etc.) — Unity should publish the plain sensor payload, matching
what the backend's `mosquitto_pub` test examples structurally do (raw
fields at the top level, even though the specific field names in those
examples are wrong per the note above).

### Step 3 — One-time scene setup (the only manual step)
Add `MqttPublisher.cs` to the same persistent GameObject that already holds
`SensorDataStore` (or any single persistent object) — one component, one
time, not per-station. Document this single step clearly in a README
addition; do not require it per-station or per-test-scene.

### Step 4 — End-to-end verification
1. Run `docker compose up` in `disaster-backend`
2. Enter Play mode in Unity, run the existing `DumbSensorPipelineTest`
   (already in the project) to generate a real quake event stream
3. Confirm messages arrive at the backend — either via the backend's Kafka
   console consumer command (already documented in its README) or a direct
   PostgreSQL query, both already documented in `disaster-backend`'s README
4. This replaces manually running `mosquitto_pub` from the backend's Phase 6
   test — Unity itself is now the publisher

## Explicitly OUT OF SCOPE for this task
- Any change to `SensorReadings.cs` struct shapes
- Any change to `disaster-backend` code (README example-payload cleanup is
  optional and separate, not required)
- TLS/authentication on the MQTT connection
- Publishing GNSS/DartBuoy/TideGauge/OceanBottom readings before their
  respective Layer 2 feeders exist and produce real data for them — only
  wire up publishing for reading types that already have real (or
  test-synthetic) data flowing, per the project's existing "don't test
  ahead of a real data source" discipline

## Deliverables checklist
- [ok] `MqttPublisher.cs`
- [ ok ] `SensorDataStore.cs` updated to publish via MQTT alongside (not
      instead of) existing file export, behind its own toggle
- [ ok ] A short README addition in `disaster-simulation` documenting: the one
      manual setup step, the broker host/port fields, and how to verify the
      connection is working
- [ok] Confirmation (Console log or description of what you observed) that a
      real Unity-generated reading was seen arriving at the backend —
      Kafka consumer output or a PostgreSQL row, either is sufficient proof