using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;

/// <summary>
/// Détecteur de pas et virages en temps réel pour Meta Quest
/// Détection de la longueur des pas et de la vitesse entre chaque pas
/// AJOUT: Détection de virages (gauche/droite) basée sur la rotation du casque
/// AJOUT: Analyse de la symétrie de marche (orientation tête + trajectoire)
/// AJOUT: Détection de la vitesse actuelle
/// </summary>
public enum TurnDirection { None, Left, Right }

public class RealtimeStepDetector : MonoBehaviour
{
    // ----------------------------------------------------------------------
    // References UI
    // ----------------------------------------------------------------------
    [Header("Références UI")]
    [SerializeField] private TextMeshProUGUI stepCountText;        // Affichage du nombre de pas
    [SerializeField] private TextMeshProUGUI turnStatusText;       // Affichage du statut de virage
    [SerializeField] private TextMeshProUGUI speedText;            // Affichage de la vitesse entre pas
    [SerializeField] private TextMeshProUGUI instantSpeedText;     // Affichage vitesse instantanée
    [SerializeField] private TextMeshProUGUI stepDistanceText;     // Affichage distance du dernier pas
    [SerializeField] private TextMeshProUGUI symmetryText;         // Affichage score de symétrie
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
    // Parametres d'analyse de la symetrie
    // ----------------------------------------------------------------------
    [Header("Paramètres d'analyse de symétrie")]
    [SerializeField] private int minStepsForSymmetry = 5;           // Nombre minimal de pas pour analyser la symétrie
    [SerializeField] private float symmetryWindowDuration = 2.0f;   // Fenêtre temporelle d'analyse (s)
    [SerializeField] private float headTiltThreshold = 10.0f;       // Seuil d'écart-type pour l'orientation de la tête (degrés)

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
    private float stepSpeed = 0f;                                      // Vitesse de chaque pas (m/s)
    private float instantaneousSpeed = 0f;                             // Vitesse instantanée en temps réel (m/s)
    private Vector3 previousPosition;                                  // Position précédente pour calcul vitesse instantanée
    private float previousTime;                                        // Temps précédent pour calcul vitesse instantanée

    // ----------------------------------------------------------------------
    // Variables internes - virages
    // ----------------------------------------------------------------------
    private TurnDirection currentTurn = TurnDirection.None;
    private float turnStartTime = 0f;            // Temps au début du virage
    private float turnStartAngle = 0f;           // Angle au début du virage
    private float accumulatedTurnAngle = 0f;     // Variable manquante (anciennement totalTurnAngle)

    // ----------------------------------------------------------------------
    // Variables internes - symetrie
    // ----------------------------------------------------------------------
    private float headOrientationScore = 100f;   // Score d'orientation de la tête (0-100%)
    private float trajectoryLinearity = 100f;    // Score de linéarité de trajectoire (0-100%)

    // ----------------------------------------------------------------------
    // Buffers circulaires (temps reel)
    // ----------------------------------------------------------------------
    private CircularBuffer<float> yRaw;          // Données brutes Y du casque
    private CircularBuffer<float> yFiltered;     // Données filtrées Y du casque
    private CircularBuffer<float> xRaw;          // Données brutes X du casque
    private CircularBuffer<float> zRaw;          // Données brutes Z du casque
    private CircularBuffer<float> timestamps;    // Timestamps des échantillons
    private CircularBuffer<float> headingAngles; // Angles de direction (YAW)
    private CircularBuffer<float> rollAngles;    // NOUVEAU: Angles d'inclinaison latérale (ROLL)

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
        public float stepDistance;
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
        public Vector3 rotation;  // pitch (x), yaw (y), roll (z)
        public int stepNumber;
        public float stepDistance;
        public float stepSpeed;              // Vitesse entre pas
        public float instantaneousSpeed;     // Vitesse instantanée
        public TurnDirection turnStatus;
        public float headOrientationSymmetry;
        public float trajectorySymmetry;
    }


    // ======================================================================
    // INITIALISATION
    // ======================================================================
    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        initialPosition = mainCamera.transform.position;
        previousPosition = initialPosition;
        previousTime = Time.time;

        int bufferSize = Mathf.CeilToInt(bufferDuration * samplingRate);
        InitializeBuffers(bufferSize);

        ComputeFilterCoefficients();
        UpdateStepCountDisplay();
        UpdateTurnDisplay();
        UpdateSymmetryDisplay();

        if (showDebugLogs)
            Debug.Log($"[StepDetector] Init - Détection pas + virages + symétrie");
    }


    // ======================================================================
    // UPDATE PRINCIPAL
    // ======================================================================
    void Update()
    {
        // Récupération des données du casque
        float currentTime = Time.time;
        Vector3 currentPos = mainCamera.transform.position;
        Vector3 eulerAngles = mainCamera.transform.eulerAngles;

        // Calculer la vitesse instantanée (indépendante des pas)
        float deltaTime = currentTime - previousTime;
        if (deltaTime > 0f)
        {
            float dx = currentPos.x - previousPosition.x;
            float dz = currentPos.z - previousPosition.z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            instantaneousSpeed = distance / deltaTime;
        }

        // Mise à jour pour le prochain calcul
        previousPosition = currentPos;
        previousTime = currentTime;

        // Mettre à jour l'affichage vitesse instantanée (chaque frame)
        UpdateInstantaneousSpeedDisplay();

        // Calculer les angles (yaw et roll)
        float currentHeading = eulerAngles.y;  // YAW (rotation horizontale)
        float currentRoll = eulerAngles.z;     // ROLL (inclinaison latérale)

        // Filtrage passe-bas sur Y
        float filteredY = ApplyLowPassFilter(currentPos.y);

        // Ajout aux buffers
        PushToBuffers(currentTime, currentPos, currentHeading, currentRoll, filteredY);

        // Enregistrement des données brutes
        SaveRawPoint(currentTime, currentPos, filteredY, eulerAngles);

        // Détection
        if (yFiltered.Count >= validationWindow * 2 + 1)
        {
            DetectStepsAsMinima();
            DetectTurns();
        }

        // Analyse de la symétrie
        AnalyzeSymmetry();
    }

    // ======================================================================
    // INITIALISATION DES BUFFERS
    // ======================================================================

    /// <summary>
    /// Initialise tous les buffers circulaires.
    /// </summary>
    private void InitializeBuffers(int size)
    {
        yRaw = new CircularBuffer<float>(size);
        yFiltered = new CircularBuffer<float>(size);
        xRaw = new CircularBuffer<float>(size);
        zRaw = new CircularBuffer<float>(size);
        timestamps = new CircularBuffer<float>(size);
        headingAngles = new CircularBuffer<float>(size);
        rollAngles = new CircularBuffer<float>(size);
    }

    /// <summary>
    /// Ajoute une nouvelle donnee dans chaque buffer circulaire.
    /// </summary>
    private void PushToBuffers(float time, Vector3 pos, float heading, float roll, float filteredY)
    {
        timestamps.Add(time);
        xRaw.Add(pos.x);
        yRaw.Add(pos.y);
        zRaw.Add(pos.z);
        yFiltered.Add(filteredY);
        headingAngles.Add(heading);
        rollAngles.Add(roll);
    }

    /// <summary>
    /// Enregistre un point brut si l'enregistrement est actif.
    /// </summary>
    private void SaveRawPoint(float time, Vector3 pos, float filteredY, Vector3 eulerAngles)
    {
        if (!recordData || rawDataBuffer.Count >= 100000)
            return;

        // Trouver le numéro de pas actuel (0 si aucun pas détecté)
        int currentStepNumber = stepCount;

        // Calculer la distance et vitesse du dernier pas
        float stepDist = 0f;
        if (detectedSteps.Count > 0)
        {
            stepDist = detectedSteps[detectedSteps.Count - 1].stepDistance;
        }
        
        float calculatedStepSpeed = CalculateWalkingSpeed();

        rawDataBuffer.Add(new RawDataPoint
        {
            timestamp = time,
            positionRaw = pos,
            yFiltered = filteredY,
            rotation = eulerAngles,
            stepNumber = currentStepNumber,
            stepDistance = stepDist,
            stepSpeed = calculatedStepSpeed,         // Vitesse entre pas
            instantaneousSpeed = instantaneousSpeed, // Vitesse instantanée
            turnStatus = currentTurn,
            headOrientationSymmetry = headOrientationScore,
            trajectorySymmetry = trajectoryLinearity
        });
    }

    /// <summary>
    /// Calcule la vitesse de marche en m/s basée sur les deux derniers pas.
    /// </summary>
    /// <returns>Vitesse en m/s. Retourne 0 si pas assez de données.</returns>
    private float CalculateWalkingSpeed()
    {
        if (detectedSteps.Count < 2)
            return 0f; // Pas assez de données pour calculer la vitesse

        StepEvent lastStep = detectedSteps[detectedSteps.Count - 1];
        StepEvent previousStep = detectedSteps[detectedSteps.Count - 2];

        float distance = lastStep.stepDistance; // Distance horizontale (XZ) entre les deux pas
        float deltaTime = lastStep.timestamp - previousStep.timestamp;

        if (deltaTime <= 0f)
            return 0f;

        return distance / deltaTime; // vitesse en m/s
    }


    // ======================================================================
    // FILTRE PASSE BAS
    // ======================================================================

    /// <summary>
    /// Calcule les coefficients du filtre IIR passe-bas (type biquad).
    /// </summary>
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

    /// <summary>
    /// Applique le filtre passe-bas IIR sur la valeur verticale.
    /// </summary>
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

    /// <summary>
    /// Detecte les pas en identifiant des minima locaux dans le signal vertical filtre.
    /// </summary>
    private void DetectStepsAsMinima()
    {
        int n = yFiltered.Count;
        int index = n - validationWindow - 1;

        if (index < validationWindow || index >= n - validationWindow)
            return;

        float time = timestamps[index];

        if (detectedPeakTimestamps.Any(t => Mathf.Abs(t - time) < 0.01f))
            return;

        if (!IsLocalMinimum(index)) return;
        if (!IsValidProminence(index)) return;
        if (!IsValidAmplitude(index)) return;
        if (!IsValidStepDistance(index)) return;

        RegisterStep(index, time);
    }

    /// <summary>
    /// Retourne vrai si l'index correspond a un minimum local.
    /// </summary>
    private bool IsLocalMinimum(int i)
    {
        return yFiltered[i] < yFiltered[i - 1] && yFiltered[i] < yFiltered[i + 1];
    }

    /// <summary>
    /// Verifie que la prominence du minimum est suffisante.
    /// </summary>
    private bool IsValidProminence(int i)
    {
        int start = Mathf.Max(0, i - validationWindow);
        int end = Mathf.Min(yFiltered.Count - 1, i + validationWindow);

        float max = float.MinValue;
        for (int k = start; k <= end; k++)
            if (yFiltered[k] > max) max = yFiltered[k];

        return (max - yFiltered[i]) >= prominence;
    }

    /// <summary>
    /// Verifie que l'amplitude locale du minimum est suffisante.
    /// </summary>
    private bool IsValidAmplitude(int i)
    {
        int start = Mathf.Max(0, i - validationWindow);
        int end = Mathf.Min(yFiltered.Count - 1, i + validationWindow);

        float sum = 0f;
        for (int k = start; k <= end; k++)
            sum += yFiltered[k];

        float mean = sum / (end - start + 1);
        float amp = Mathf.Abs(yFiltered[i] - mean);

        return amp >= amplitudeThreshold;
    }

    /// <summary>
    /// Verifie que la distance horizontale entre deux pas est suffisante.
    /// </summary>
    private bool IsValidStepDistance(int i)
    {
        Vector3 pos = new Vector3(xRaw[i], yFiltered[i], zRaw[i]);

        if (detectedPeakPositions.Count == 0)
            return HorizontalDistance(pos, initialPosition) >= minStepDistance;

        Vector3 last = detectedPeakPositions[detectedPeakPositions.Count - 1];
        return HorizontalDistance(pos, last) >= minStepDistance;
    }

    /// <summary>
    /// Distance horizontale (plan XZ) entre deux positions.
    /// </summary>
    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Enregistre un nouveau pas valide.
    /// </summary>
    private void RegisterStep(int index, float time)
    {
        stepCount++;

        Vector3 pos = new Vector3(xRaw[index], yFiltered[index], zRaw[index]);
        detectedPeakTimestamps.Add(time);
        detectedPeakPositions.Add(pos);

        float dist = detectedPeakPositions.Count > 1
            ? HorizontalDistance(pos, detectedPeakPositions[detectedPeakPositions.Count - 2])
            : HorizontalDistance(pos, initialPosition);

        detectedSteps.Add(new StepEvent
        {
            timestamp = time,
            position = pos,
            stepNumber = stepCount,
            stepDistance = dist
        });

        stepSpeed = CalculateWalkingSpeed();

        UpdateStepCountDisplay();
        UpdateSpeedDisplay();           // Met à jour vitesse entre pas
        UpdateStepDistanceDisplay(dist);
    }


    // ======================================================================
    // DETECTION DES VIRAGES
    // ======================================================================

    /// <summary>
    /// Detecte les virages en analysant les variations d'angle (yaw).
    /// </summary>
    private void DetectTurns()
    {
        int n = headingAngles.Count;
        if (n < 2) return;

        float time = timestamps[n - 1];
        float angle = headingAngles[n - 1];
        float prevAngle = headingAngles[n - 2];

        float delta = Mathf.DeltaAngle(prevAngle, angle);

        int windowSamples = Mathf.CeilToInt(turnWindowDuration * samplingRate);
        windowSamples = Mathf.Min(windowSamples, n);

        float angleStart = headingAngles[n - windowSamples];
        float windowDelta = Mathf.DeltaAngle(angleStart, angle);

        if (currentTurn == TurnDirection.None)
        {
            TryStartTurn(time, angle, windowDelta);
        }
        else
        {
            UpdateTurn(time, delta);
        }
    }

    /// <summary>
    /// Tente de detecter un debut de virage.
    /// </summary>
    private void TryStartTurn(float time, float angle, float windowDelta)
    {
        if (Mathf.Abs(windowDelta) <= turnAngleThreshold)
            return;

        currentTurn = windowDelta > 0 ? TurnDirection.Right : TurnDirection.Left;
        turnStartTime = time;
        turnStartAngle = angle;
        accumulatedTurnAngle = 0f;

        UpdateTurnDisplay();
    }

    /// <summary>
    /// Met a jour l'etat du virage en cours.
    /// </summary>
    private void UpdateTurn(float time, float delta)
    {
        accumulatedTurnAngle += delta;

        bool end = false;

        if ((currentTurn == TurnDirection.Right && delta < -5f) ||
            (currentTurn == TurnDirection.Left && delta > 5f))
            end = true;

        if (IsRotationStable())
            end = true;

        if (end)
            CompleteTurn(time);
    }

    /// <summary>
    /// Retourne vrai si la rotation est faible dans les dernieres 0.3 secondes.
    /// </summary>
    private bool IsRotationStable()
    {
        int n = headingAngles.Count;
        int samples = Mathf.CeilToInt(0.3f * samplingRate);
        if (n < samples) return false;

        float oldAngle = headingAngles[n - samples];
        float delta = Mathf.Abs(Mathf.DeltaAngle(oldAngle, headingAngles[n - 1]));

        return delta < 5f;
    }

    /// <summary>
    /// Termine le virage si ses conditions sont reunies.
    /// </summary>
    private void CompleteTurn(float time)
    {
        float duration = time - turnStartTime;

        if (duration >= minTurnDuration &&
            Mathf.Abs(accumulatedTurnAngle) > turnAngleThreshold)
        {
            detectedTurns.Add(new TurnEvent
            {
                startTime = turnStartTime,
                endTime = time,
                totalAngle = accumulatedTurnAngle,
                direction = currentTurn
            });

            if (showDebugLogs)
            {
                Debug.Log($"[Turn] Virage {currentTurn} terminé: {accumulatedTurnAngle:F1}° en {duration:F2}s");
            }
        }

        currentTurn = TurnDirection.None;
        UpdateTurnDisplay();
    }


    // ======================================================================
    // ANALYSE DE LA SYMETRIE
    // ======================================================================

    /// <summary>
    /// Analyse la symétrie de marche en temps réel.
    /// Affiche séparément symétrie de la tête et symétrie de la trajectoire
    /// </summary>
    private void AnalyzeSymmetry()
    {
        if (detectedSteps.Count < minStepsForSymmetry)
        {
            headOrientationScore = 100f;
            trajectoryLinearity = 100f;
            UpdateSymmetryDisplay();
            return;
        }

        headOrientationScore = AnalyzeHeadOrientation();
        trajectoryLinearity = AnalyzeTrajectoryLinearity();

        UpdateSymmetryDisplay();

        if (showDebugLogs)
        {
            Debug.Log($"[Symmetry] Tête: {headOrientationScore:F1}%, Trajectoire: {trajectoryLinearity:F1}%");
        }
    }

    /// <summary>
    /// Calcule le score d'orientation de la tête (stabilité YAW + ROLL).
    /// </summary>
    private float AnalyzeHeadOrientation()
    {
        int n = headingAngles.Count;
        int windowSamples = Mathf.CeilToInt(symmetryWindowDuration * samplingRate);
        windowSamples = Mathf.Min(windowSamples, n);

        if (windowSamples < 10)
            return 100f;

        float yawStdDev = CalculateStdDev(headingAngles, n - windowSamples, n - 1);
        float rollStdDev = CalculateStdDev(rollAngles, n - windowSamples, n - 1);

        float yawScore = Mathf.Clamp01(1f - (yawStdDev / headTiltThreshold)) * 100f;
        float rollScore = Mathf.Clamp01(1f - (rollStdDev / headTiltThreshold)) * 100f;

        return (yawScore + rollScore) / 2f;
    }

    /// <summary>
    /// Calcule le score de linéarité de la trajectoire.
    /// CORRECTION: Utilise uniquement les derniers pas (fenêtre glissante).
    /// </summary>
    private float AnalyzeTrajectoryLinearity()
    {
        // CHANGEMENT : Nombre fixe de pas à analyser (pas "minStepsForSymmetry + 2")
        int stepsToAnalyze = Mathf.Min(minStepsForSymmetry, detectedSteps.Count);

        if (stepsToAnalyze < 2)
            return 100f; // Pas assez de données → score parfait par défaut

        // Récupérer UNIQUEMENT les N derniers pas
        List<Vector3> recentPositions = new List<Vector3>();
        for (int i = detectedSteps.Count - stepsToAnalyze; i < detectedSteps.Count; i++)
        {
            recentPositions.Add(detectedSteps[i].position);
        }

        // Distance directe (vol d'oiseau)
        Vector3 start = recentPositions[0];
        Vector3 end = recentPositions[recentPositions.Count - 1];
        float directDistance = HorizontalDistance(start, end);

        // Distance réelle (somme des segments)
        float actualDistance = 0f;
        for (int i = 1; i < recentPositions.Count; i++)
        {
            actualDistance += HorizontalDistance(recentPositions[i - 1], recentPositions[i]);
        }

        // Éviter division par zéro
        if (actualDistance < 0.01f)
            return 100f; // Utilisateur immobile → score parfait

        // Ratio de linéarité
        float linearityRatio = directDistance / actualDistance;

        // AJOUT : Si le ratio est très proche de 1, forcer à 100%
        if (linearityRatio >= 0.98f)
            return 100f;

        return Mathf.Clamp01(linearityRatio) * 100f;
    }

    /// <summary>
    /// Calcule l'écart-type d'angles avec gestion du wrap-around.
    /// </summary>
    private float CalculateStdDev(CircularBuffer<float> buffer, int start, int end)
    {
        List<float> values = new List<float>();

        for (int i = start; i <= end; i++)
        {
            values.Add(buffer[i]);
        }

        if (values.Count < 2)
            return 0f;

        float mean = CalculateCircularMean(values);

        float variance = 0f;
        foreach (float v in values)
        {
            float diff = Mathf.DeltaAngle(mean, v);
            variance += diff * diff;
        }
        variance /= values.Count;

        return Mathf.Sqrt(variance);
    }

    /// <summary>
    /// Calcule la moyenne circulaire d'angles (gère 0°/360°).
    /// </summary>
    private float CalculateCircularMean(List<float> angles)
    {
        float sumSin = 0f;
        float sumCos = 0f;

        foreach (float angle in angles)
        {
            float rad = angle * Mathf.Deg2Rad;
            sumSin += Mathf.Sin(rad);
            sumCos += Mathf.Cos(rad);
        }

        float meanSin = sumSin / angles.Count;
        float meanCos = sumCos / angles.Count;

        float meanRad = Mathf.Atan2(meanSin, meanCos);
        float meanDeg = meanRad * Mathf.Rad2Deg;

        if (meanDeg < 0f)
            meanDeg += 360f;

        return meanDeg;
    }


    // ======================================================================
    // UI
    // ======================================================================

    /// <summary>
    /// Met a jour le texte du compteur de pas.
    /// </summary>
    private void UpdateStepCountDisplay()
    {
        if (stepCountText != null)
            stepCountText.text = $"Pas: {stepCount}";
    }

    /// <summary>
    /// Met a jour le texte affichant l'etat du virage actuel.
    /// </summary>
    private void UpdateTurnDisplay()
    {
        if (turnStatusText == null) return;

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

    /// <summary>
    /// Met à jour l'affichage de la vitesse de marche.
    /// </summary>
    private void UpdateSpeedDisplay()
    {
        if (speedText == null) return;

        if (detectedSteps.Count < 2)
        {
            speedText.text = "V.Pas: -- m/s";
            return;
        }

        speedText.text = $"V.Pas: {stepSpeed:F2} m/s";
    }

    /// <summary>
    /// Met à jour l'affichage de la vitesse INSTANTANÉE (appelée chaque frame).
    /// </summary>
    private void UpdateInstantaneousSpeedDisplay()
    {
        if (instantSpeedText == null) return;

        instantSpeedText.text = $"V.Inst: {instantaneousSpeed:F2} m/s";
    }

    /// <summary>
    /// Met à jour l'affichage de la distance du dernier pas.
    /// </summary>
    private void UpdateStepDistanceDisplay(float distance)
    {
        if (stepDistanceText != null)
            stepDistanceText.text = $"Distance pas: {distance:F2} m";
    }

    /// <summary>
    /// Met a jour l'affichage de la symetrie.
    /// Affiche séparément symétrie de la tête et symétrie de la trajectoire
    /// </summary>
    private void UpdateSymmetryDisplay()
    {
        if (symmetryText == null) return;

        // Affichage sur 2 lignes séparées
        symmetryText.text = $"Symétrie Tête: {headOrientationScore:F0}%\n" +
                           $"Symétrie Trajectoire: {trajectoryLinearity:F0}%";

        // Couleur selon le PIRE des deux scores
        float minScore = Mathf.Min(headOrientationScore, trajectoryLinearity);
        
        if (minScore >= 80f)
            symmetryText.color = Color.green;
        else if (minScore >= 60f)
            symmetryText.color = Color.yellow;
        else
            symmetryText.color = Color.red;
    }


    // ======================================================================
    // RESET ET EXPORT
    // ======================================================================

    /// <summary>
    /// Reinitialise l'ensemble du systeme de detection et les buffers.
    /// </summary>
    public void ResetStepCount()
    {
        stepCount = 0;
        stepSpeed = 0f;
        instantaneousSpeed = 0f;
        previousPosition = initialPosition;
        previousTime = Time.time;

        detectedPeakTimestamps.Clear();
        detectedPeakPositions.Clear();
        detectedSteps.Clear();
        detectedTurns.Clear();
        rawDataBuffer.Clear();

        yRaw.Clear();
        yFiltered.Clear();
        xRaw.Clear();
        zRaw.Clear();
        timestamps.Clear();
        headingAngles.Clear();
        rollAngles.Clear();

        xPrev = new float[3];
        yPrev = new float[3];

        currentTurn = TurnDirection.None;
        headOrientationScore = 100f;
        trajectoryLinearity = 100f;

        UpdateStepCountDisplay();
        UpdateTurnDisplay();
        UpdateSymmetryDisplay();
    }

    /// <summary>
    /// Exporte les donnees brutes et evenements dans un fichier CSV.
    /// Format: Temps;X;Y;Z;RotX;RotY;RotZ;NbPas;DistancePas;Vitesse;Virage;SymTete;SymTrajectoire
    /// </summary>
    public void ExportData(string path)
    {
        if (!recordData || rawDataBuffer.Count == 0)
        {
            Debug.LogWarning("[StepDetector] Aucune donnée à exporter.");
            return;
        }

        try
        {
            using (System.IO.StreamWriter sw = new System.IO.StreamWriter(path))
            {
                // En-tête CSV avec 2 colonnes de vitesse
                sw.WriteLine("Temps;X;Y;Z;RotX;RotY;RotZ;NbPas;DistancePas;VitessePas;VitesseInstantanee;Virage;SymTete;SymTrajectoire");

                foreach (var p in rawDataBuffer)
                {
                    // Code virage: 0=Aucun, L=Gauche, R=Droite
                    string turnCode = 
                        p.turnStatus == TurnDirection.Left ? "L" :
                        p.turnStatus == TurnDirection.Right ? "R" : "0";

                    sw.WriteLine(
                        $"{p.timestamp:F3};{p.positionRaw.x:F4};{p.positionRaw.y:F4};{p.positionRaw.z:F4};" +
                        $"{p.rotation.x:F2};{p.rotation.y:F2};{p.rotation.z:F2};" +
                        $"{p.stepNumber};{p.stepDistance:F4};{p.stepSpeed:F4};{p.instantaneousSpeed:F4};{turnCode};" +  // ✅ 2 vitesses
                        $"{p.headOrientationSymmetry:F2};{p.trajectorySymmetry:F2}"
                    );
                }
            }

            Debug.Log($"[StepDetector] Export terminé: {path} ({rawDataBuffer.Count} échantillons, {detectedSteps.Count} pas, {detectedTurns.Count} virages)");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[StepDetector] Erreur export: {e.Message}");
        }
    }

    /// <summary>
    /// Reinitialise l'ensemble du systeme de detection et les buffers.
    /// </summary>
    void OnApplicationQuit()
    {
        // Export automatique à la fermeture de l'application
        string path = System.IO.Path.Combine(Application.persistentDataPath, 
            $"WalkingDataSymmetry_{System.DateTime.Now:yyyyMMdd_HHmms}.csv");
        ExportData(path);
    }

    // OPTIONNEL: Ajout d'une touche pour exporter manuellement
    void LateUpdate()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, 
                $"WalkingDataSymmetry_{System.DateTime.Now:yyyyMMdd_HHmms}.csv");
            ExportData(path);
            Debug.Log($"[StepDetector] Export manuel vers: {path}");
        }
    }
}


/// ==========================================================================
/// BUFFER CIRCULAIRE GENERIQUE
/// ==========================================================================

public class CircularBuffer<T>
{
    private readonly T[] buffer;
    private int head = 0;
    private int count = 0;
    private readonly int capacity;

    public int Count => count;

    public CircularBuffer(int capacity)
    {
        this.capacity = capacity;
        buffer = new T[capacity];
    }

    public void Add(T item)
    {
        buffer[head] = item;
        head = (head + 1) % capacity;

        if (count < capacity)
            count++;
    }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= count)
                throw new System.IndexOutOfRangeException();

            int actual = (head - count + index + capacity) % capacity;
            return buffer[actual];
        }
    }

    public void Clear()
    {
        head = 0;
        count = 0;
    }
}
