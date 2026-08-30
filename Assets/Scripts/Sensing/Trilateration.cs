using System;
using UnityEngine;

/// <summary>
/// Locates an earthquake epicenter from P-wave arrival times at 3+ known
/// station positions - no external math libraries required.
///
/// Method: Gauss-Newton least-squares. Each station gives one equation
/// (distance from guessed epicenter should match the distance implied by
/// its arrival time); we iteratively refine a guess until the error is
/// minimized. This is the same family of method real seismic networks use
/// for rapid epicenter estimation.
///
/// This class has NO dependency on Unity's scene/GameObjects - it's pure
/// math, which means you can unit test it with made-up numbers before any
/// simulation ever touches it.
/// </summary>
public static class Trilateration
{
    public struct StationReading
    {
        public Vector2 position;       // station's fixed (x, z) position
        public float pWaveArrivalTime; // seconds since the quake originated (t=0)
    }

    /// <summary>
    /// Estimates the epicenter (x, z) from 3+ station readings.
    /// pWaveSpeed is in the same distance units as your station positions
    /// per second (e.g. if positions are in km, use km/s).
    /// </summary>
    public static Vector2 EstimateEpicenter(
        StationReading[] readings,
        float pWaveSpeed,
        int maxIterations = 50,
        float convergenceThreshold = 0.0001f)
    {
        if (readings.Length < 3)
        {
            Debug.LogError("[Trilateration] Need at least 3 stations to triangulate.");
            return Vector2.zero;
        }

        // Convert arrival times to estimated distances from source
        float[] distances = new float[readings.Length];
        for (int i = 0; i < readings.Length; i++)
        {
            distances[i] = readings[i].pWaveArrivalTime * pWaveSpeed;
        }

        // Initial guess: centroid of all stations (reasonable starting point)
        Vector2 guess = Vector2.zero;
        foreach (var r in readings) guess += r.position;
        guess /= readings.Length;

        // Gauss-Newton iteration
        for (int iter = 0; iter < maxIterations; iter++)
        {
            // Build the Jacobian (partial derivatives) and residuals
            float[,] jacobian = new float[readings.Length, 2];
            float[] residuals = new float[readings.Length];

            for (int i = 0; i < readings.Length; i++)
            {
                Vector2 diff = guess - readings[i].position;
                float predictedDist = diff.magnitude;
                if (predictedDist < 0.0001f) predictedDist = 0.0001f; // avoid div by zero

                residuals[i] = predictedDist - distances[i];
                jacobian[i, 0] = diff.x / predictedDist; // d(residual)/d(guess.x)
                jacobian[i, 1] = diff.y / predictedDist; // d(residual)/d(guess.y)
            }

            // Solve normal equations: (J^T J) delta = -J^T r
            // For a 2-parameter fit this is just a 2x2 system - solve directly.
            float jtj00 = 0, jtj01 = 0, jtj11 = 0;
            float jtr0 = 0, jtr1 = 0;

            for (int i = 0; i < readings.Length; i++)
            {
                jtj00 += jacobian[i, 0] * jacobian[i, 0];
                jtj01 += jacobian[i, 0] * jacobian[i, 1];
                jtj11 += jacobian[i, 1] * jacobian[i, 1];
                jtr0 += jacobian[i, 0] * residuals[i];
                jtr1 += jacobian[i, 1] * residuals[i];
            }

            float det = jtj00 * jtj11 - jtj01 * jtj01;
            if (Mathf.Abs(det) < 1e-8f)
            {
                Debug.LogWarning("[Trilateration] Singular matrix - stations may be poorly positioned (e.g. collinear).");
                break;
            }

            float deltaX = -(jtj11 * jtr0 - jtj01 * jtr1) / det;
            float deltaY = -(jtj00 * jtr1 - jtj01 * jtr0) / det;

            guess.x += deltaX;
            guess.y += deltaY;

            if (Mathf.Abs(deltaX) < convergenceThreshold && Mathf.Abs(deltaY) < convergenceThreshold)
            {
                break; // converged
            }
        }

        return guess;
    }
}
