# Layer 2 — Accurate Simulation: In-Depth Implementation Plan

**Goal of this layer:** replace the old prototype (single epicenter → per-building
distance → PGA, tested only with hand-fed fake data) with a real, running
simulation that feeds your actual Layer 1 pipeline (`Station` →
`SensorDataStore` → `EpicenterProcessorNode`) live, every frame — plus a
rudimentary tsunami/ocean model.

**What's changing from the old `EarthquakeSimulator.cs`:** that script computed
shaking per-*building* and was never connected to the real sensor pipeline —
Layer 1's tests all had to hand-compute fake arrival times themselves. This
layer fixes that: one real physics model drives both the buildings (visual)
and the stations (real sensor data), and nothing downstream has to fake numbers
anymore.

---

## Step 0 — Folder structure

```
Assets/
  Scripts/
    Sensing/          <- Layer 1, already done
    Simulation/        <- everything below goes here
      SimManager.cs
      EarthquakeModel.cs
      TsunamiModel.cs
      StationSeismicFeeder.cs
      StationGnssFeeder.cs
      StationTsunamiFeeder.cs
```

---

## Step 1 — `SimManager.cs` (the central orchestrator)

**What it's for:** the single "God Object" that owns the current disaster
event's state and lifecycle — nothing else in the project should independently
decide "a quake is happening." Right now that logic is scattered (the old
`EarthquakeSimulator` had its own `TriggerQuake()`); this step centralizes it.

**What it needs to contain:**
- An event state enum: `Idle`, `EarthquakeActive`, `TsunamiActive`, `Ended`
- Public method `TriggerEarthquake(float magnitude, Vector2 epicenterSurfacePos, float depthKm)`
- Internal elapsed-time tracking for the current event (same idea the old
  script had, just centralized)
- A decision point: after an earthquake is triggered, check the tsunami
  trigger condition (offshore + magnitude ≥ ~7.0 + shallow depth) and if met,
  also kick off `TsunamiActive` state after a short delay — this is the
  "and stuff" cascade: one manager deciding what follows from what
- Everything else (`EarthquakeModel`, `TsunamiModel`, the station feeders)
  should read the current event's parameters FROM `SimManager`, not store
  their own copies

**Checkpoint:** you can call `SimManager.Instance.TriggerEarthquake(6.5f, pos, 10f)`
from a test script and see its state correctly move from `Idle` to
`EarthquakeActive`, logged to Console.

---

## Step 2 — Rebuild `EarthquakeModel.cs` (replaces old `EarthquakeSimulator.cs`)

**What it's for:** pure physics — given `SimManager`'s current event
parameters and any world position, return what that position is
experiencing right now. This is upgraded from the old version using what
Layer 1 taught you:

- [ ] **Hypocentral distance, not flat surface distance** — now that
      `SimManager` carries a real `depthKm`, distance should be
      `sqrt(surfaceDistanceKm² + depthKm²)`, not just flat 2D distance like
      before
- [ ] **3-axis output** — return acceleration as X/Y/Z (matching
      `SeismicReading`'s real shape), not a single float
- [ ] **Independent P-wave and S-wave arrival times**, each from its own
      real speed constant (`pWaveSpeedKmS`, `sWaveSpeedKmS`) — no derived
      ratios, exactly the fix already applied on the sensor side, applied
      here too
- [ ] **Soil amplification multiplier** — a simple per-zone or per-station
      multiplier (bedrock ~1.0x, soft sediment ~2-4x) applied to the GMPE
      output
- [ ] **Reuse the GMPE formula from the algorithms slide** (Boore-Atkinson
      style), but apply the clamp-floor fix already diagnosed once
      (`kmPerUnit` / distance-clamp interaction) from the start, not after
      debugging it again

**Important design note:** trilateration still only ever solves for a 2D
surface point `(x, z)` — that's fine and intentional, not a gap. Real
early-warning systems do the same thing: locate the surface epicenter first
(2D), estimate magnitude/depth separately. Depth only feeds into the GMPE's
*amplitude* calculation here, not into `Trilateration` itself.

**Checkpoint:** call `EarthquakeModel.GetReadingAt(someWorldPosition, currentTime)`
manually from a test script for a couple of positions at different distances,
confirm the numbers look sane (closer = stronger, farther = weaker + delayed).

---

## Step 3 — Live station feeders (this is what makes it "real" instead of tested-with-fake-data)

**What it's for:** every frame, compute what each real `Station` is
currently experiencing (via `EarthquakeModel`) and push it into
`SensorDataStore` — exactly like `DumbSensorPipelineTest` did manually,
except now it's the actual simulation driving it, not a test script.

### 3a. `StationSeismicFeeder.cs`
- [ ] Each frame (or on a fixed interval, e.g. every 0.05s to match realistic
      sensor sample rates), loop over every `Seismic`-type station,
      call `EarthquakeModel.GetReadingAt()`, push the result into
      `SensorDataStore.RecordSeismicReading()`
- [ ] This automatically flows into `EpicenterProcessorNode` exactly like
      before — no changes needed there, since the contract (`SeismicReading`)
      hasn't changed shape

### 3b. `StationGnssFeeder.cs`
- [ ] Same pattern, but for `Gnss`-type stations
- [ ] This is where the "placeholder, not real physics" displacement formula
      from the Layer 1 test can finally be replaced with something more
      grounded — a simplified static/permanent displacement model. Full
      accuracy here means implementing something like the Okada (1985)
      dislocation model, which is genuinely out of scope for this timeline —
      a reasonable middle ground is keeping the same ramp-and-hold *shape*
      but deriving the magnitude from the GMPE's PGA output scaled down,
      rather than an arbitrary constant. Worth noting in the report that
      full Okada-style modeling was considered and scoped out as beyond
      project timeline — that's an honest, defensible statement.

**Checkpoint:** trigger a quake via `SimManager`, watch `EpicenterProcessorNode`
in the Console detect real P-wave/S-wave arrivals and locate the epicenter —
using zero hand-fed data this time. This is the actual proof that Layer 2 is
wired correctly to Layer 1.

---

## Step 4 — Reconnect `BuildingShaker`

- [ ] Update `BuildingShaker` to pull from `EarthquakeModel.GetReadingAt()`
      instead of the old `EarthquakeSimulator.GetAccelerationAt()` — same
      visual jitter/tilt logic, just reading from the rebuilt model so
      buildings and sensor stations are driven by the exact same physics,
      not two diverging implementations
- [ ] No other changes needed here — this step is mechanical, not a redesign

**Checkpoint:** trigger a quake, visually confirm buildings still shake/tilt
correctly, distance-dependent, same as before the rebuild.

---

## Step 5 — Rudimentary `TsunamiModel.cs`

Kept intentionally simple, per your request — here's the minimum that's
still physically grounded, matching the algorithms slide:

- [ ] Trigger condition (already decided by `SimManager` in Step 1):
      offshore epicenter + magnitude ≥ ~7.0 + shallow depth
- [ ] Wave speed: `c = sqrt(g * depth)` — needs a **very rough** depth
      assumption per zone; simplest rudimentary approach: pick one constant
      "deep ocean" depth (e.g. 4000m) and one constant "coastal shallow"
      depth (e.g. 50m), switching between them based on distance from
      shore — a real bathymetry map is explicitly out of scope for
      "rudimentary," and that's a fine simplification to state outright
- [ ] Wave height (shoaling): Green's Law, `H2 = H1 * (h1/h2)^(1/4)` — as
      the wave moves from deep to shallow depth, height increases
- [ ] Expose `GetWaveReadingAt(position, time)` returning pressure change /
      estimated sea-surface height, same pattern as `EarthquakeModel`

### 5a. `StationTsunamiFeeder.cs`
- [ ] Same feeder pattern as Step 3, but for `DartBuoy` and `TideGauge`
      stations, calling `TsunamiModel` instead of `EarthquakeModel`
- [ ] This is the point where the previously-deferred DART/TideGauge testing
      finally becomes meaningful — now there's a real (if rudimentary) wave
      signal to test against, per the plan agreed on earlier

**Checkpoint:** trigger a large offshore quake via `SimManager`, confirm
`TsunamiActive` state kicks in, and `DartBuoy`/`TideGauge` stations start
receiving non-zero readings in `SensorDataStore`.

---

## Step 6 — Full Layer 2 integration test

The real "definition of done" for this layer:

1. Call `SimManager.Instance.TriggerEarthquake(...)` — nothing else, no
   manual data feeding
2. Confirm, from Console logs alone: stations receive real readings →
   `EpicenterProcessorNode` detects P/S waves → epicenter is located →
   (if the event qualifies) tsunami state activates → DART/TideGauge
   stations start reporting
3. Compare the located epicenter against the one actually passed into
   `TriggerEarthquake()` — the same accuracy check from Layer 1, now
   running on real simulated data instead of hand-computed fake data

If this passes, Layer 2 is genuinely done — not just visually convincing,
but proven to feed correct data through the exact pipeline Layer 3 will
depend on.

---

## Suggested order, session by session

1. **Session 1:** Step 0 (folders) + Step 1 (`SimManager.cs`)
2. **Session 2:** Step 2 (`EarthquakeModel.cs` rebuild) — the biggest single
   piece, give it its own session
3. **Session 3:** Step 3 (seismic + GNSS feeders) — where Layer 1 and
   Layer 2 actually connect for the first time
4. **Session 4:** Step 4 (reconnect `BuildingShaker`) — quick, mechanical
5. **Session 5:** Step 5 (`TsunamiModel.cs` + tsunami feeders)
6. **Session 6:** Step 6 (full integration test) — the real finish line for
   this layer

## What's still honestly deferred after this layer
- Full bathymetry-based tsunami propagation (rudimentary depth-switch
  model only, as requested)
- Okada-style precise GNSS displacement modeling (simplified GMPE-derived
  approximation instead, noted as a scoped-out limitation)
- Multi-event handling (two simultaneous quakes) — still out of scope, as
  flagged back in Layer 1