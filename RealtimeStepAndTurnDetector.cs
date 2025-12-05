using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;

/// <summary>
/// Détecteur de pas et virages en temps réel pour Meta Quest
/// AJOUT: Détection de virages (gauche/droite) basée sur la rotation du casque
/// Problèmes : ne compte pas les mini pas et détecte un pas juste lorsqu'on se lève
/// </summary>
public enum TurnDirection { None, Left, Right }

public class RealtimeStepDetector : MonoBehaviour
{
    // ----------------------------------------------------------------------
    // References UI
    // ----------------------------------------------------------------------
    [Header("Références UI")]
    [SerializeField] private TextMeshProUGUI stepCountText;     // Affichage du nombre de pas
    [SerializeField] private TextMeshProUGUI turnStatusText;    // Affichage du statut de virage
    [SerializeField] private Camera mainCamera;

    // ----------------------------------------------------------------------
    // Parametres de detection des pas
    // ----------------------------------------------------------------------
    [Header("Paramètres de détection de pas")]
    [SerializeField] private float prominence = 0.005f;         // Profondeur minimale des creux (m)
    [SerializeField] private float amplitudeThreshold = 0.008f; // Différence minimale du creux par rapport à la moyenne locale (m)
    [SerializeField] private float minPeakDistance = 0.2f;      // Temps minimal entre deux pas (s)
    [SerializeField] private int validationWindow = 20;         // Taille de la fenêtre d'analyse : Nombre d'échantillons avant/après pour valider un creux
    [SerializeField] private float minStepDistance = 0.05f;     // Distance horizontale minimale pour valider un pas

    // ----------------------------------------------------------------------
    // Parametres de detection des virages
    // ----------------------------------------------------------------------
    [Header("Paramètres de détection de virages")]
    [SerializeField] private float turnAngleThreshold = 30.0f;  // Angle minimal pour détecter un virage (degrés)
    [SerializeField] private float turnWindowDuration = 1.0f;   // Fenêtre de temps pour calculer l'angle (s)
    [SerializeField] private float minTurnDuration = 0.5f;      // Durée minimale d'un virage (s)

    // ----------------------------------------------------------------------
    // Filtre passe-bas
    // ----------------------------------------------------------------------
    [Header("Paramètres de filtrage passe-bas")]
    [SerializeField] private float cutoffFrequency = 2.0f;      // Fréquence de coupure (Hz)
    [SerializeField] private float samplingRate = 100.0f;       // Fréquence d'échantillonnage (Hz)
    
    // ----------------------------------------------------------------------
    // Buffers temps reel
    // ----------------------------------------------------------------------
    [Header("Buffer temps réel")]
    [SerializeField] private float bufferDuration = 3.0f;       // Durée du buffer (s)

    // ----------------------------------------------------------------------
    // Debug et enregistrement
    // ----------------------------------------------------------------------
    [Header("Debug & Enregistrement")]
    [SerializeField] private bool showDebugLogs = false;        // Afficher les logs de debug
    [SerializeField] private bool recordData = true;            // Enregistrer les données brutes

    // ----------------------------------------------------------------------
    // Variables internes - pas
    // ----------------------------------------------------------------------
    private int stepCount = 0;                                         // Compteur total de pas détectés
    private Vector3 initialPosition = Vector3.zero;                    // Position initiale du casque (pour le 1er pas)
    private List<float> detectedPeakTimestamps = new List<float>();    // Indices temporels des pics détectés
    private List<Vector3> detectedPeakPositions = new List<Vector3>(); // Positions des pics détectés

    // ----------------------------------------------------------------------
    // Variables internes - virages
    // ----------------------------------------------------------------------
    private TurnDirection currentTurn = TurnDirection.None;
    private float turnStartTime = 0f;   // Temps au début du virage
    private float turnStartAngle = 0f;  // Angle au début du virage
    private float totalTurnAngle = 0f;  // Angle total accumulé pendant le virage

     // ----------------------------------------------------------------------
    // Buffers circulaires (temps reel)
    // ----------------------------------------------------------------------
    private CircularBuffer<float> yRaw;          // Données brutes Y du casque
    private CircularBuffer<float> yFiltered;     // Données filtrées Y du casque
    private CircularBuffer<float> xRaw;          // Données brutes X du casque
    private CircularBuffer<float> zRaw;          // Données brutes Z du casque
    private CircularBuffer<float> timestamps;    // Timestamps des échantillons
    private CircularBuffer<float> headingAngles; // Angles de direction

    // Filtre passe-bas IIR Butterworth 2nd order
    private float[] xPrev = new float[3];
    private float[] yPrev = new float[3];
    private float a0, a1, a2, b1, b2;

    // Enregistrement
    private List<StepEvent> detectedSteps = new List<StepEvent>();       // Liste des pas détectés
    private List<TurnEvent> detectedTurns = new List<TurnEvent>();       // Liste des virages détectés
    private List<RawDataPoint> rawDataBuffer = new List<RawDataPoint>(); // Données brutes enregistrées

    [System.Serializable]
    public struct StepEvent
    {
        public float timestamp;
        public Vector3 position;
        public int stepNumber;
        public float horizontalDistance;
    }

    [System.Serializable]
    public struct TurnEvent
    {
        public float startTime;
        public float endTime;
        public float totalAngle;
        public TurnDirection direction;
    }

    [System.Serializable]
    public struct RawDataPoint
    {
        public float timestamp;
        public Vector3 positionRaw;
        public float yFiltered;
        public float headingAngle;
        public TurnDirection turnStatus;
    }


    // ======================================================================
    // INITIALISATION
    // ======================================================================
    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        initialPosition = mainCamera.transform.position;

        int bufferSize = Mathf.CeilToInt(bufferDuration * samplingRate);
        InitializeBuffers(bufferSize);

        ComputeFilterCoefficients();
        UpdateStepCountDisplay();
        UpdateTurnDisplay();

        if (showDebugLogs)
            Debug.Log($"[StepDetector] Init - Détection pas + virages (seuil: {turnAngleThreshold}°)");
    }


    // ======================================================================
    // UPDATE PRINCIPAL
    // ======================================================================
    void Update()
    {
        // Récupération des données du casque
        float currentTime = Time.time;
        Vector3 currentPos = mainCamera.transform.position;

        // Calculer l'angle de direction (yaw)
        float currentHeading = mainCamera.transform.eulerAngles.y;

        // Filtrage passe-bas sur Y
        float filteredY = ApplyLowPassFilter(currentPos.y);

        // Ajout aux buffers
        PushToBuffers(currentTime, currentPos, currentHeading, filteredY);

        // Enregistrement des données brutes
        SaveRawPoint(currentTime, currentPos, filteredY, currentHeading);

        // Détection
        if (yFiltered.Count >= validationWindow * 2 + 1)
        {
            DetectStepsAsMinima();
            DetectTurns();
        }
    }

    // ======================================================================
    // INITIALISATION DES BUFFERS
    // ======================================================================

    // Initialise tous les buffers circulaires.
    private void InitializeBuffers(int size)
    {
        yRaw = new CircularBuffer<float>(size);
        yFiltered = new CircularBuffer<float>(size);
        xRaw = new CircularBuffer<float>(size);
        zRaw = new CircularBuffer<float>(size);
        timestamps = new CircularBuffer<float>(size);
        headingAngles = new CircularBuffer<float>(size);
    }

    // Ajoute une nouvelle donnee dans chaque buffer circulaire.
    private void PushToBuffers(float time, Vector3 pos, float heading, float filteredY)
    {
        timestamps.Add(time);
        xRaw.Add(pos.x);
        yRaw.Add(pos.y);
        zRaw.Add(pos.z);
        yFiltered.Add(filteredY);
        headingAngles.Add(heading);
    }

    // Enregistre un point brut si l'enregistrement est actif.
    private void SaveRawPoint(float time, Vector3 pos, float filteredY, float heading)
    {
        if (!recordData || rawDataBuffer.Count >= 100000)
            return;

        rawDataBuffer.Add(new RawDataPoint
        {
            timestamp = time,
            positionRaw = pos,
            yFiltered = filteredY,
            headingAngle = heading,
            turnStatus = currentTurn
        });
    }


    // ======================================================================
    // FILTRE PASSE BAS
    // ======================================================================

    // Calcule les coefficients du filtre IIR passe-bas (type biquad).
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

    // Applique le filtre passe-bas IIR sur la valeur verticale.
    private float ApplyLowPassFilter(float rawValue)
    {
        xPrev[2] = xPrev[1];
        xPrev[1] = xPrev[0];
        xPrev[0] = rawValue;

        yPrev[2] = yPrev[1];
        yPrev[1] = yPrev[0];

        yPrev[0] = a0 * xPrev[0]
                 + a1 * xPrev[1]
                 + a2 * xPrev[2]
                 - b1 * yPrev[1]
                 - b2 * yPrev[2];

        return yPrev[0];
    }


    // ======================================================================
    // DETECTION DES PAS
    // ======================================================================

    // Detecte les pas en identifiant des minima locaux dans le signal vertical filtré.
    private void DetectStepsAsMinima()
    {
        int n = yFiltered.Count;
        int checkIndex = n - validationWindow - 1;
        
        if (checkIndex < validationWindow || checkIndex >= n - validationWindow)
            return;

        float checkTimestamp = timestamps[checkIndex];

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

        if (detectedPeakTimestamps.Count > 0)
        {
            float lastPeakTime = detectedPeakTimestamps[detectedPeakTimestamps.Count - 1];
            if (checkTimestamp - lastPeakTime < minPeakDistance)
                return;
        }

        float horizontalDist = 0f;
        Vector3 currentPeakPos = new Vector3(
            xRaw[checkIndex],
            yFiltered[checkIndex],
            zRaw[checkIndex]
        );

        if (detectedPeakPositions.Count > 0)
        {
            Vector3 lastPeakPos = detectedPeakPositions[detectedPeakPositions.Count - 1];
            float dx = currentPeakPos.x - lastPeakPos.x;
            float dz = currentPeakPos.z - lastPeakPos.z;
            horizontalDist = Mathf.Sqrt(dx * dx + dz * dz);

            if (horizontalDist < minStepDistance)
            {
                if (showDebugLogs)
                    Debug.Log($"[Step] REJETÉ - Distance XZ: {horizontalDist:F3}m");
                return;
            }
        }
        else
        {
            if (initialPosition != Vector3.zero)
            {
                float dx = currentPeakPos.x - initialPosition.x;
                float dz = currentPeakPos.z - initialPosition.z;
                horizontalDist = Mathf.Sqrt(dx * dx + dz * dz);

                if (horizontalDist < minStepDistance)
                {
                    if (showDebugLogs)
                        Debug.Log($"[Step] REJETÉ (1er pas) - Distance XZ: {horizontalDist:F3}m");
                    return;
                }
            }
        }

        stepCount++;
        detectedPeakTimestamps.Add(checkTimestamp);

        Vector3 peakPosition = new Vector3(xRaw[checkIndex], yFiltered[checkIndex], zRaw[checkIndex]);
        detectedPeakPositions.Add(peakPosition);

        detectedSteps.Add(new StepEvent
        {
            timestamp = checkTimestamp,
            position = peakPosition,
            stepNumber = stepCount,
            horizontalDistance = horizontalDist
        });

        UpdateStepCountDisplay();

        if (showDebugLogs)
        {
            Debug.Log($"[Step] #{stepCount} @ t={checkTimestamp:F2}s, Dist: {horizontalDist:F3}m");
        }
    }

    /// <summary>
    /// Détecte les virages en analysant la rotation du casque
    /// Basé sur l'algorithme Python estimate_walking_parameters
    /// </summary>
    private void DetectTurns()
    {
        int n = headingAngles.Count;
        if (n < 2)
            return;

        float currentTime = timestamps[n - 1];
        float currentAngle = headingAngles[n - 1];
        float previousAngle = headingAngles[n - 2];

        // Calculer la différence d'angle (gérer le wrap-around 0-360°)
        float angleDiff = Mathf.DeltaAngle(previousAngle, currentAngle);

        // Calculer l'angle total sur la fenêtre de temps
        int windowSamples = Mathf.CeilToInt(turnWindowDuration * samplingRate);
        windowSamples = Mathf.Min(windowSamples, n);

        float windowStartAngle = headingAngles[n - windowSamples];
        float totalAngleChange = Mathf.DeltaAngle(windowStartAngle, currentAngle);

        // Détecter le début d'un virage
        if (currentTurn == TurnDirection.None)
        {
            if (Mathf.Abs(totalAngleChange) > turnAngleThreshold)
            {
                currentTurn = totalAngleChange > 0 ? TurnDirection.Right : TurnDirection.Left;
                turnStartTime = currentTime;
                turnStartAngle = currentAngle;
                totalTurnAngle = 0f;

                UpdateTurnDisplay();

                if (showDebugLogs)
                    Debug.Log($"[Turn] Début virage {currentTurn} @ t={currentTime:F2}s");
            }
        }
        // Virage en cours : accumuler l'angle
        else
        {
            totalTurnAngle += angleDiff;

            // Vérifier si le virage est terminé (changement de direction ou arrêt)
            bool turnEnded = false;

            // Cas 1 : Changement de direction
            if ((currentTurn == TurnDirection.Right && angleDiff < -5f) ||
                (currentTurn == TurnDirection.Left && angleDiff > 5f))
            {
                turnEnded = true;
            }

            // Cas 2 : Angle stable (pas de rotation significative depuis 0.3s)
            int stabilityWindow = Mathf.CeilToInt(0.3f * samplingRate);
            if (n >= stabilityWindow)
            {
                float recentAngle = headingAngles[n - stabilityWindow];
                float recentAngleChange = Mathf.Abs(Mathf.DeltaAngle(recentAngle, currentAngle));
                
                if (recentAngleChange < 5f) // Moins de 5° en 0.3s
                {
                    turnEnded = true;
                }
            }

            if (turnEnded)
            {
                float turnDuration = currentTime - turnStartTime;

                // Valider le virage (durée minimale + angle significatif)
                if (turnDuration >= minTurnDuration && Mathf.Abs(totalTurnAngle) > turnAngleThreshold)
                {
                    detectedTurns.Add(new TurnEvent
                    {
                        startTime = turnStartTime,
                        endTime = currentTime,
                        totalAngle = totalTurnAngle,
                        direction = currentTurn
                    });

                    if (showDebugLogs)
                    {
                        Debug.Log($"[Turn] Fin virage {currentTurn} @ t={currentTime:F2}s\n" +
                                  $"  Durée: {turnDuration:F2}s, Angle: {totalTurnAngle:F1}°");
                    }
                }

                currentTurn = TurnDirection.None;
                UpdateTurnDisplay();
            }
        }
    }

    private void UpdateStepCountDisplay()
    {
        if (stepCountText != null)
            stepCountText.text = $"Pas: {stepCount}";
    }

    private void UpdateTurnDisplay()
    {
        if (turnStatusText != null)
        {
            switch (currentTurn)
            {
                case TurnDirection.Left:
                    turnStatusText.text = "Virage: GAUCHE ←";
                    turnStatusText.color = Color.cyan;
                    break;
                case TurnDirection.Right:
                    turnStatusText.text = "Virage: DROITE →";
                    turnStatusText.color = Color.yellow;
                    break;
                default:
                    turnStatusText.text = "Virage: Aucun";
                    turnStatusText.color = Color.white;
                    break;
            }
        }
    }

    public void ResetStepCount()
    {
        stepCount = 0;
        detectedPeakTimestamps.Clear();
        detectedPeakPositions.Clear();
        detectedSteps.Clear();
        detectedTurns.Clear();
        currentTurn = TurnDirection.None;
        rawDataBuffer.Clear();
        yRaw.Clear();
        yFiltered.Clear();
        xRaw.Clear();
        zRaw.Clear();
        timestamps.Clear();
        headingAngles.Clear();
        xPrev = new float[3];
        yPrev = new float[3];
        UpdateStepCountDisplay();
        UpdateTurnDisplay();
    }

    public void ExportData(string filepath)
    {
        if (!recordData) return;

        try
        {
            using (System.IO.StreamWriter sw = new System.IO.StreamWriter(filepath))
            {
                sw.WriteLine("Timestamp;X_Raw;Y_Raw;Z_Raw;Y_Filtered;Heading_Angle;Turn_Status;Step_Detected;Step_Number;Horizontal_Distance");

                foreach (var point in rawDataBuffer)
                {
                    var stepEvent = detectedSteps.FirstOrDefault(s => Mathf.Abs(s.timestamp - point.timestamp) < 0.01f);
                    bool stepDetected = stepEvent.stepNumber > 0;

                    string turnStatus = point.turnStatus == TurnDirection.None ? "0" : 
                                       (point.turnStatus == TurnDirection.Left ? "L" : "R");

                    sw.WriteLine($"{point.timestamp:F3};{point.positionRaw.x:F4};{point.positionRaw.y:F4};{point.positionRaw.z:F4};" +
                                 $"{point.yFiltered:F4};{point.headingAngle:F2};{turnStatus};" +
                                 $"{(stepDetected ? "1" : "0")};{stepEvent.stepNumber};{stepEvent.horizontalDistance:F3}");
                }
            }

            Debug.Log($"[StepDetector] Export → {filepath} ({detectedTurns.Count} virages détectés)");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[StepDetector] Erreur export: {e.Message}");
        }
    }

    public int GetStepCount() => stepCount;
    public int GetTurnCount() => detectedTurns.Count; // NOUVEAU
    public List<StepEvent> GetDetectedSteps() => new List<StepEvent>(detectedSteps);
    public List<TurnEvent> GetDetectedTurns() => new List<TurnEvent>(detectedTurns); // NOUVEAU
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