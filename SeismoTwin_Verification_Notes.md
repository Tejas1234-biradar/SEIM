# SeismoTwin claim verification notes

This file is a working reference for interview defense. Every substantive technical claim in the main summary is mapped to the code and the relevant repository commit(s) checked in this session.

Repository HEADs checked:
- SEIM: `bba0cef` — `unity feeds actual data to backend`
- SEIM-Backend: `a6cba5a` — `backend recives epicenter data as well`

## Core claims mapped to source

1. `PWaveDetector` implements a STA/LTA phase detector with separate P- and S-wave thresholds.
   - Verified in: `SEIM/Assets/Scripts/Sensing/PWaveDetector.cs`
   - Commit reference: current `SEIM` HEAD `bba0cef`
   - Supporting comments: “Short-Term-Average / Long-Term-Average (STA/LTA) phase arrival detector” and the Allen (1978) citation.

2. `Trilateration.EstimateEpicenter(...)` is implemented as a Gauss-Newton least-squares solver.
   - Verified in: `SEIM/Assets/Scripts/Sensing/Trilateration.cs`
   - Commit reference: current `SEIM` HEAD `bba0cef`
   - Supporting comment: “Method: Gauss-Newton least-squares. Each station gives one equation…”

3. `EpicenterProcessorNode` detects per-station P-wave triggers and publishes a detection event when at least three stations are available.
   - Verified in: `SEIM/Assets/Scripts/Sensing/EpicenterProcessorNode.cs`
   - Commit reference: current `SEIM` HEAD `bba0cef`
   - Supporting logic: `detectors[reading.stationId].AddSample(...)`, `if (trigger == PhaseTrigger.PWave)`, `if (triggered.Count >= minStationsToLocate)`.

4. The project is wired to MQTT and uses the topics `sensors/{ReadingType}/readings` and `detections/Epicenter/events`.
   - Verified in: `SEIM/Assets/Scripts/Sensing/MqttPublisher.cs` and `SEIM/Assets/Scripts/Sensing/SensorDataStore.cs`
   - Commit reference: current `SEIM` HEAD `bba0cef`
   - Supporting logic: `topicOrReadingType.Contains("/") ? topicOrReadingType : $"sensors/{topicOrReadingType}/readings"`; `MqttPublisher.Instance.Publish("detections/Epicenter/events", jsonPayload)` in `EpicenterProcessorNode`.

5. The backend subscribes to MQTT on `sensors/+/readings` and `detections/+/events` and bridges them into Kafka.
   - Verified in: `SEIM-Backend/src/main/java/com/example/seimbackend/mqtt/MqttSensorBridge.java`
   - Commit reference: current `SEIM-Backend` HEAD `a6cba5a`
   - Supporting logic: `mqttClient.subscribe(mqttProperties.getTopic(), sensorMessageListener());` and `mqttClient.subscribe(mqttProperties.getDetectionTopic(), detectionMessageListener());`

6. Kafka topics are `sensor.readings` and `detected.events`.
   - Verified in: `SEIM-Backend/src/main/java/com/example/seimbackend/kafka/KafkaTopics.java`
   - Commit reference: current `SEIM-Backend` HEAD `a6cba5a`

7. The backend persists sensor readings and detected events into PostgreSQL with unique `eventId` keys and duplicate checks.
   - Verified in: `SEIM-Backend/src/main/java/com/example/seimbackend/entity/SensorReadingEntity.java`, `SEIM-Backend/src/main/java/com/example/seimbackend/entity/DetectedEventEntity.java`, `SEIM-Backend/src/main/java/com/example/seimbackend/service/SensorReadingService.java`, and `SEIM-Backend/src/main/java/com/example/seimbackend/service/DetectedEventService.java`
   - Commit reference: current `SEIM-Backend` HEAD `a6cba5a`
   - Supporting logic: `@Column(nullable = false, unique = true)` on `eventId`; `if (repository.existsByEventId(eventId)) ...` and `catch (DataIntegrityViolationException ex)`.

8. The backend README explicitly documents the Docker setup and the end-to-end MQTT→Kafka→PostgreSQL validation flow.
   - Verified in: `SEIM-Backend/README.md` and `SEIM-Backend/docker-compose.yml`
   - Commit reference: current `SEIM-Backend` HEAD `a6cba5a`
   - Supporting services: PostgreSQL, Mosquitto, Kafka, app on ports 5434/11883/9092/8080.

9. The simulation uses a GMPE-based earthquake model, with independent P- and S-wave speeds and 3-axis acceleration output.
   - Verified in: `SEIM/Assets/Scripts/Simulation/EarthquakeModel.cs`
   - Commit reference: current `SEIM` HEAD `bba0cef`
   - Supporting constants: `P_WAVE_SPEED_KM_S = 6.0f;`, `S_WAVE_SPEED_KM_S = 3.5f;`, `GMPE_A = 0.5f`, `GMPE_B = 0.5f`, `GMPE_C = 0.01f`, `GMPE_D = -1.5f`.

10. The project describes the station types as real-world-aligned: USGS ShakeAlert and Japan’s S-net / DONET.
   - Verified in: `SEIM/Assets/Scripts/Sensing/Station.cs`
   - Commit reference: current `SEIM` HEAD `bba0cef`
   - Supporting comment block in the class summary.

11. The project mentions real-world methods and networks as references, not as proof of operational deployment.
   - Verified in: `SEIM/Assets/Scripts/Sensing/Station.cs`, `SEIM/Assets/Scripts/Sensing/PWaveDetector.cs`, and `SEIM/Assets/Scripts/Simulation/EarthquakeModel.cs`
   - Commit reference: current `SEIM` HEAD `bba0cef`

12. The project contains explicit validation scripts for the core math and state transitions.
   - Verified in: `SEIM/Assets/Scripts/Sensing/TrilaterationTest.cs`, `SEIM/Assets/Scripts/Simulation/Editor/EarthquakeModelPhase2Test.cs`, and `SEIM/Assets/Scripts/Simulation/Editor/SimManagerPhase1Test.cs`
   - Commit reference: current `SEIM` HEAD `bba0cef`
   - Supporting checks: logs “PASS” / “SUCCESS” conditions when the code paths satisfy the test logic.

13. `SimManager` centralizes the event lifecycle and tsunami trigger conditions.
   - Verified in: `SEIM/Assets/Scripts/Simulation/SimManager.cs`
   - Commit reference: current `SEIM` HEAD `bba0cef`
   - Supporting logic: `CheckTsunamiTriggerCondition(float mag, float depth, bool offshore)` with `offshore && mag >= 7.0f && depth <= 50.0f`.

14. `BuildingShaker` is implemented as an inertial building-response model, but the repo does not provide a validated structural-response proof suite.
   - Verified in: `SEIM/Assets/Scripts/Simulation/BuildingShaker.cs`
   - Commit reference: current `SEIM` HEAD `bba0cef`
   - Supporting statement: it does apply the same earthquake physics through `EarthquakeModel.GetReadingAt(...)` and includes configurable mass, stiffness, and damping, but no dedicated damage-validation test or measured structural-response benchmark is present in the repo.

15. The backend has a direct REST ingestion path used for manual bypass testing.
   - Verified in: `SEIM-Backend/src/main/java/com/example/seimbackend/controller/SensorReadingController.java`
   - Commit reference: current `SEIM-Backend` HEAD `a6cba5a`
   - Supporting logic: `@PostMapping` on `/api/sensor-readings`.

16. The repo does not claim production metrics or uptime.
   - Verified by absence in the code, docs, and commit messages checked in this session.
   - Acceptable interpretation: this is a prototype/course project, not a production operational deployment.

## Key repo history notes

- `SEIM` history at this repo shows the integration work landing in the final commit sequence:
  - `bba0cef` — `unity feeds actual data to backend`
  - `c3a632d` — `unity publishes epicenter events`
  - `8d7eafb` — `layer1+backend integrated`
- `SEIM-Backend` history shows the backend expansion around the same integration point:
  - `a6cba5a` — `backend recives epicenter data as well`
  - `592ee2f` — `SEIM- Backend Vertical Test`

This history supports the interpretation that the system is integrated and under active development but still aligns with the course-project / prototype status rather than a production deployment.
