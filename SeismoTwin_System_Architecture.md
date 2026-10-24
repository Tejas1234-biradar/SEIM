# Project SEIM — Core System Architecture & 3rd-Year Implementation Summary

## 1. Project Background and Objective

### 1.1 Context and Problem Statement

This project was developed to meet advanced coursework requirements, focusing on a fully virtual earthquake and tsunami detection pipeline with no physical hardware, no 3D printed sensors, and no “game-engine-only” visuals standing in for real seismology. The simulation code reflects that requirement in its own terminology and comments. The Unity project explicitly describes its sensor types as “USGS ShakeAlert-style” and “Japan’s S-net / DONET style,” and the physics model references a Boore-Atkinson GMPE formulation and Allen (1978) STA/LTA detection.

The goal was not to simulate a dashboard or a toy demo in isolation; it was to connect physics-based event generation, a distributed virtual sensor network, and a backend pipeline that could ingest and persist detection data in a production-like stack. That architecture appears across both repos: a Unity simulation generates seismic and GNSS-like readings, a virtual sensor layer estimates epicenters, and a Java/Spring backend bridges MQTT and Kafka into PostgreSQL.

### 1.2 System Scope and Requirements

The task was to design and build an end-to-end system spanning three layers: (1) a realistic earthquake and tsunami simulation, (2) a virtual sensing network capable of independent P-wave detection and epicenter localization, and (3) a backend that could ingest the telemetry and persist it in a database. The project’s code reflects that exact scope. `SimManager` centralizes the event lifecycle, `EarthquakeModel` computes shaking and arrival times using distance and GMPE logic, and `EpicenterProcessorNode` turns per-station raw readings into event detections. On the backend side, `MqttSensorBridge` subscribes to MQTT topics and pushes into Kafka topics, with JPA entities persisting the records to PostgreSQL.

### 1.3 Implementation and Algorithmic Design

The project implemented the core algorithms in the actual code rather than leaving them as design placeholders. The sensing layer uses a `PWaveDetector` that implements a Short-Term-Average / Long-Term-Average arrival detector, with comments specifically citing Allen (1978) and noting the P-wave/S-wave distinction used in real seismic networks. The localization layer uses `Trilateration.EstimateEpicenter(...)`, which is explicitly implemented as a Gauss-Newton least-squares solver: it converts each station’s arrival time into a distance, builds a Jacobian and residual system, and iteratively refines the epicenter estimate until convergence.

The simulation code also includes a real earthquake model rather than a flat displacement lookup. `EarthquakeModel.GetReadingAt(...)` computes 3-axis acceleration using hypocentral distance, independent P-wave and S-wave speeds, and a Boore-Atkinson-style GMPE. The comments directly document the model as using `sqrt(surfaceDistance² + depth²)` for hypocentral distance, a distance floor to avoid log(0), and separate propagation constants for P and S waves. `SensorDataStore` records each reading and forwards it to `MqttPublisher`, which publishes raw JSON to `sensors/{ReadingType}/readings` using QoS 0. `EpicenterProcessorNode` publishes detection events to `detections/Epicenter/events` once at least three stations detect P-wave triggers.

On the backend, `MqttSensorBridge` subscribes to both `sensors/+/readings` and `detections/+/events`, normalizes the incoming payloads, assigns a UUID if missing, and sends them through `SensorReadingProducer` and `DetectedEventProducer` to Kafka. The Kafka topics are defined in `KafkaTopics` as `sensor.readings` and `detected.events`. `SensorReadingConsumer` and `DetectedEventConsumer` then read those messages and persist them through Spring Data JPA repositories into PostgreSQL. The database schema is represented by `SensorReadingEntity` and `DetectedEventEntity`; both use `eventId` as a unique identifier, and the services explicitly check for `existsByEventId(...)` before insert and fall back to a `DataIntegrityViolationException` check for duplicate inserts.

### 1.4 Validation and Current Status

The codebase contains clear, code-backed validation logic rather than only planned architecture sketches. The repository includes tests that exercise the actual math: `TrilaterationTest` synthesizes station arrival times and logs the estimated epicenter error; `EarthquakeModelPhase2Test` verifies that farther stations arrive later and produce lower PGA; and `SimManagerPhase1Test` verifies state transitions and the offshore tsunami trigger condition. The backend README also documents the exact manual validation flow for MQTT-to-Kafka-to-PostgreSQL checks using `mosquitto_pub`, Kafka consumers, and PostgreSQL `SELECT` queries.

This is an academic project, not a production deployment, and the repo does not present production metrics, uptime numbers, or throughput claims. The honest result is: the repository implements the intended physics model, sensor detection pipeline, and backend ingest chain in code, and it contains unit-level and manual validation scripts aimed at proving those components work. What it does not appear to provide is a validated, measured production-grade operations story or a fully proven structural-damage model for buildings.

## 2. System Architecture

![alt text](image.png)


### 2.1 Component breakdown by layer

**1. Virtual sensing and simulation layer (Unity / C#)**
- `Station` defines the station types and positions, including land seismic, GNSS, ocean-bottom, DART, and tide gauges.
- `SensorDataStore` records the most recent and recent-history readings per station and forwards them to MQTT.
- `PWaveDetector` applies a STA/LTA detector to raw acceleration data and flags P-wave and S-wave triggers.
- `EpicenterProcessorNode` maintains one detector per station, aggregates triggered stations, and runs trilateration when there are enough readings.
- `Trilateration` is a pure-math, Gauss-Newton solution for a 2D epicenter estimate from station arrival times.
- `SimManager` holds the authoritative event lifecycle (`Idle`, `EarthquakeActive`, `TsunamiActive`, `Ended`) and the current earthquake parameters.
- `EarthquakeModel` applies the underlying physics: hypocentral distance, P/S wave speeds, GMPE, and 3-axis acceleration output. The project comments explicitly mention the Boore-Atkinson GMPE form and a distance floor fix from earlier prototype work.
- `BuildingShaker` is present as the structural-response component, using the same earthquake model as the sensor layer to drive inertial building motion.

**2. Backend layer (Java / Spring Boot)**
- `MqttSensorBridge` is the MQTT bridge between Unity and the event stream; it subscribes to the sensor and detection topics and converts raw payloads into typed envelopes.
- `SensorReadingProducer` and `DetectedEventProducer` send the messages to Kafka.
- `SensorReadingConsumer` and `DetectedEventConsumer` read Kafka messages and persist them.
- `SensorReadingController` provides a direct REST POST endpoint as a bypass path for manual ingestion (`/api/sensor-readings`).
- `SensorReadingEntity` and `DetectedEventEntity` map to PostgreSQL tables with JSONB payload columns and unique `eventId` constraints.

**3. Real-world grounding used by the design**
- The code comments explicitly align station types with real systems: USGS ShakeAlert for land seismic/GNSS stations and Japan’s S-net / DONET for ocean-bottom units.
- `PWaveDetector` cites Allen (1978) as the STA/LTA method basis.
- `EarthquakeModel` cites a Boore-Atkinson GMPE style formulation.
These are references to real methods and systems, not claims that the project is deployed as a real operational network.

## 3. Technology Stack and Honest Status

**Technology stack**
- Unity / C# for the simulation and virtual sensing layer
- Java 21 / Spring Boot for the backend service
- MQTT / Mosquitto for inter-process telemetry transport
- Kafka for event streaming
- PostgreSQL for persistence
- Docker Compose for local orchestration

**Implementation status**

The current codebase shows a credible end-to-end project architecture and a coherent set of implemented components. The strongest, most explicit evidence is in the project code and test scripts: the sim and backend are wired together around MQTT, Kafka, and PostgreSQL; the trilateration solver and STA/LTA arrival detector are implemented; the event lifecycle and GMPE-based earthquake physics are coded; and the repo includes manual validation instructions and test scripts for the main pathways.

