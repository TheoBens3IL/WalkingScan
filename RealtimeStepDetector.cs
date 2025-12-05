using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;

/// <summary>
/// Détecteur de pas en temps réel pour Meta Quest - VERSION CORRIGÉE
/// CHANGEMENTS:
/// - Détecte les CREUX (minima) au lieu des pics
/// - Sampling 100Hz natif (pas de sous-échantillonnage)
/// - validationWindow adapté (20 échantillons = 0.2s)
/// </summary>
public class RealtimeStepDetector : MonoBehaviour
{
    [Header("Références UI")]
    [SerializeField] private TextMeshProUGUI stepCountText;
    [SerializeField] private Camera mainCamera;

    [Header("Paramètres de détection")]
    [SerializeField] private float prominence = 0.005f; // Prominence minimale (m)
    [SerializeField] private float minPeakDistance = 0.2f; // Distance temporelle minimale (s)
    [SerializeField] private float amplitudeThreshold = 0.008f; // Seuil d'amplitude (m)
    [SerializeField] private int validationWindow = 20; // Fenêtre ±0.2s à 100Hz
    [SerializeField] private float minStepDistance = 0.05f; // Distance X,Z minimale (m)

    [Header("Paramètres de filtrage passe-bas")]
    [SerializeField] private float cutoffFrequency = 2.0f; // 2Hz
    [SerializeField] private float samplingRate = 100.0f; // Fs = 100Hz (natif casque)

    [Header("Buffer temps réel")]
    [SerializeField] private float bufferDuration = 3.0f; // 3s = 300 échantillons à 100Hz

    [Header("Debug & Enregistrement")]
    [SerializeField] private bool showDebugLogs = false;
    [SerializeField] private bool recordData = true;

    // Variables internes
    private int stepCount = 0;
    private List<float> detectedPeakTimestamps = new List<float>();
    private List<Vector3> detectedPeakPositions = new List<Vector3>();

    // Buffers circulaires
    private CircularBuffer<float> yRaw;
    private CircularBuffer<float> yFiltered;
    private CircularBuffer<float> xRaw;
    private CircularBuffer<float> zRaw;
    private CircularBuffer<float> timestamps;

    // Filtre passe-bas IIR Butterworth 2nd order
    private float[] xPrev = new float[3];
    private float[] yPrev = new float[3];
    private float a0, a1, a2, b1, b2;

    // Enregistrement
    private List<StepEvent> detectedSteps = new List<StepEvent>();
    private List<RawDataPoint> rawDataBuffer = new List<RawDataPoint>();

    [System.Serializable]
    public struct StepEvent
    {
        public float timestamp;
        public Vector3 position;
        public int stepNumber;
        public float horizontalDistance;
    }

    [System.Serializable]
    public struct RawDataPoint
    {
        public float timestamp;
        public Vector3 positionRaw;
        public float yFiltered;
    }

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        int bufferSize = Mathf.CeilToInt(bufferDuration * samplingRate);
        yRaw = new CircularBuffer<float>(bufferSize);
        yFiltered = new CircularBuffer<float>(bufferSize);
        xRaw = new CircularBuffer<float>(bufferSize);
        zRaw = new CircularBuffer<float>(bufferSize);
        timestamps = new CircularBuffer<float>(bufferSize);

        ComputeFilterCoefficients();
        UpdateStepCountDisplay();

        if (showDebugLogs)
            Debug.Log($"[StepDetector] Init - Buffer: {bufferSize} samples, Window: {validationWindow} samples ({validationWindow/samplingRate:F2}s)");
    }

    void Update()
    {
        // PAS de sous-échantillonnage : on traite à 100Hz natif
        float currentTime = Time.time;

        // Acquisition position 3D
        Vector3 currentPos = mainCamera.transform.position;

        // Filtrage passe-bas sur Y
        float filteredY = ApplyLowPassFilter(currentPos.y);

        // Ajout aux buffers
        yRaw.Add(currentPos.y);
        yFiltered.Add(filteredY);
        xRaw.Add(currentPos.x);
        zRaw.Add(currentPos.z);
        timestamps.Add(currentTime);

        // Enregistrement
        if (recordData && rawDataBuffer.Count < 100000)
        {
            rawDataBuffer.Add(new RawDataPoint
            {
                timestamp = currentTime,
                positionRaw = currentPos,
                yFiltered = filteredY
            });
        }

        // Détection si buffer rempli
        if (yFiltered.Count >= validationWindow * 2 + 1)
        {
            DetectStepsAsMinima(); // détecte les CREUX
        }
    }

    private void ComputeFilterCoefficients()
    {
        float omega = 2.0f * Mathf.PI * cutoffFrequency / samplingRate;
        float alpha = Mathf.Sin(omega) / (2.0f * 0.7071f);

        float cosOmega = Mathf.Cos(omega);

        a0 = (1.0f - cosOmega) / 2.0f;
        a1 = 1.0f - cosOmega;
        a2 = (1.0f - cosOmega) / 2.0f;
        b1 = -2.0f * cosOmega;
        b2 = 1.0f - alpha;

        float norm = 1.0f + alpha;
        a0 /= norm;
        a1 /= norm;
        a2 /= norm;
        b1 /= norm;
        b2 /= norm;
    }

    private float ApplyLowPassFilter(float rawValue)
    {
        xPrev[2] = xPrev[1];
        xPrev[1] = xPrev[0];
        xPrev[0] = rawValue;

        yPrev[2] = yPrev[1];
        yPrev[1] = yPrev[0];

        yPrev[0] = a0 * xPrev[0] + a1 * xPrev[1] + a2 * xPrev[2]
                   - b1 * yPrev[1] - b2 * yPrev[2];

        return yPrev[0];
    }

    /// <summary>
    /// Détection de pas comme MINIMA (creux)
    /// </summary>
    private void DetectStepsAsMinima()
    {
        int n = yFiltered.Count;
        int minDistanceSamples = Mathf.CeilToInt(minPeakDistance * samplingRate);

        float mean = 0f;
        for (int i = 0; i < n; i++)
            mean += yFiltered[i];
        mean /= n;

        int checkIndex = n - validationWindow - 1;
        if (checkIndex < validationWindow || checkIndex >= n - validationWindow)
            return;

        float checkTimestamp = timestamps[checkIndex];

        // CORRECTION : Vérifier par timestamp au lieu d'indice
        if (detectedPeakTimestamps.Any(t => Mathf.Abs(t - checkTimestamp) < 0.01f))
            return;

        float y_curr = yFiltered[checkIndex];
        float y_prev = yFiltered[checkIndex - 1];
        float y_next = yFiltered[checkIndex + 1];

        bool isMinimum = (y_curr < y_prev && y_curr < y_next);
        if (!isMinimum)
            return;

        int windowStart = Mathf.Max(0, checkIndex - validationWindow);
        int windowEnd = Mathf.Min(n - 1, checkIndex + validationWindow);

        float windowMax = float.MinValue;
        for (int i = windowStart; i <= windowEnd; i++)
        {
            if (yFiltered[i] > windowMax)
                windowMax = yFiltered[i];
        }

        float valleyProminence = windowMax - y_curr;
        if (valleyProminence < prominence)
            return;

        float windowMean = 0f;
        for (int i = windowStart; i <= windowEnd; i++)
            windowMean += yFiltered[i];
        windowMean /= (windowEnd - windowStart + 1);

        float amplitude = Mathf.Abs(y_curr - windowMean);
        if (amplitude < amplitudeThreshold)
            return;

        // VALIDATION 3 : Distance temporelle (par timestamp)
        if (detectedPeakTimestamps.Count > 0)
        {
            float lastPeakTime = detectedPeakTimestamps[detectedPeakTimestamps.Count - 1];
            if (checkTimestamp - lastPeakTime < minPeakDistance)
                return;
        }

        float horizontalDist = 0f;
        if (detectedPeakPositions.Count > 0)
        {
            Vector3 lastPeakPos = detectedPeakPositions[detectedPeakPositions.Count - 1];
            Vector3 currentPeakPos = new Vector3(
                xRaw[checkIndex],
                yFiltered[checkIndex],
                zRaw[checkIndex]
            );

            float dx = currentPeakPos.x - lastPeakPos.x;
            float dz = currentPeakPos.z - lastPeakPos.z;
            horizontalDist = Mathf.Sqrt(dx * dx + dz * dz);

            if (horizontalDist < minStepDistance)
            {
                if (showDebugLogs)
                    Debug.Log($"[Step] REJETÉ - Distance XZ: {horizontalDist:F3}m < {minStepDistance}m");
                return;
            }
        }

        // === PAS DÉTECTÉ (creux validé) ===
        stepCount++;
        detectedPeakTimestamps.Add(checkTimestamp); // CHANGEMENT : Stocke le timestamp

        Vector3 peakPosition = new Vector3(xRaw[checkIndex], yFiltered[checkIndex], zRaw[checkIndex]);
        detectedPeakPositions.Add(peakPosition);

        float eventTime = timestamps[checkIndex];
        detectedSteps.Add(new StepEvent
        {
            timestamp = eventTime,
            position = peakPosition,
            stepNumber = stepCount,
            horizontalDistance = horizontalDist
        });

        UpdateStepCountDisplay();

        if (showDebugLogs)
        {
            Debug.Log($"[Step] #{stepCount} @ t={eventTime:F2}s (CREUX)\n" +
                      $"  Valley Prominence: {valleyProminence:F3}m, Amplitude: {amplitude:F3}m\n" +
                      $"  Dist XZ: {horizontalDist:F3}m, Y: {y_curr:F3}m");
        }
    }

    private void UpdateStepCountDisplay()
    {
        if (stepCountText != null)
            stepCountText.text = $"Pas: {stepCount}";
    }

    public void ResetStepCount()
    {
        stepCount = 0;
        detectedPeakTimestamps.Clear();
        detectedPeakPositions.Clear();
        detectedSteps.Clear();
        rawDataBuffer.Clear();
        yRaw.Clear();
        yFiltered.Clear();
        xRaw.Clear();
        zRaw.Clear();
        timestamps.Clear();
        xPrev = new float[3];
        yPrev = new float[3];
        UpdateStepCountDisplay();
    }

    public void ExportData(string filepath)
    {
        if (!recordData) return;

        try
        {
            using (System.IO.StreamWriter sw = new System.IO.StreamWriter(filepath))
            {
                sw.WriteLine("Timestamp;X_Raw;Y_Raw;Z_Raw;Y_Filtered;Step_Detected;Step_Number;Horizontal_Distance");

                foreach (var point in rawDataBuffer)
                {
                    var stepEvent = detectedSteps.FirstOrDefault(s => Mathf.Abs(s.timestamp - point.timestamp) < 0.01f);
                    bool stepDetected = stepEvent.stepNumber > 0;

                    sw.WriteLine($"{point.timestamp:F3};{point.positionRaw.x:F4};{point.positionRaw.y:F4};{point.positionRaw.z:F4};" +
                                 $"{point.yFiltered:F4};{(stepDetected ? "1" : "0")};{stepEvent.stepNumber};{stepEvent.horizontalDistance:F3}");
                }
            }

            Debug.Log($"[StepDetector] Export → {filepath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[StepDetector] Erreur export: {e.Message}");
        }
    }

    public int GetStepCount() => stepCount;
    public List<StepEvent> GetDetectedSteps() => new List<StepEvent>(detectedSteps);
}

public class CircularBuffer<T>
{
    private T[] buffer;
    private int head = 0;
    private int count = 0;
    private int capacity;

    public CircularBuffer(int capacity)
    {
        this.capacity = capacity;
        buffer = new T[capacity];
    }

    public void Add(T item)
    {
        buffer[head] = item;
        head = (head + 1) % capacity;
        if (count < capacity) count++;
    }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= count)
                throw new System.IndexOutOfRangeException();
            int actualIndex = (head - count + index + capacity) % capacity;
            return buffer[actualIndex];
        }
    }

    public int Count => count;

    public void Clear()
    {
        count = 0;
        head = 0;
    }
}