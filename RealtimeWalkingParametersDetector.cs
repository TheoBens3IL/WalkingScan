using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Linq;

/// <summary>
/// Détecteur de pas et virages en temps réel pour casque Meta Quest
/// Ajout : Détection de virages (gauche/droite) basée sur la rotation du casque
/// Problèmes : ne compte pas les mini pas et détecte un pas juste lorsqu'on se lève, et compte un premier pas en se baissant
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
    [SerializeField] private TextMeshProUGUI speedText;            // Affichage de la vitesse
    [SerializeField] private TextMeshProUGUI stepDistanceText;     // Affichage distance du dernier pas
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
    private float stepSpeed = 0f;                                      // Vitesse de chaque pas (m/s)

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
        public Vector3 rotation;  // pitch (x), yaw (y), roll (z)
        public int stepNumber;
        public float stepDistance;
        public float speed;
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
        Vector3 eulerAngles = mainCamera.transform.eulerAngles;

        // Calculer l'angle de direction (yaw)
        float currentHeading = eulerAngles.y;

        // Filtrage passe-bas sur Y
        float filteredY = ApplyLowPassFilter(currentPos.y);

        // Ajout aux buffers
        PushToBuffers(currentTime, currentPos, currentHeading, filteredY);

        // Enregistrement des données brutes
        SaveRawPoint(currentTime, currentPos, filteredY, eulerAngles);

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

    /// <summary>
    /// Enregistre un point brut si l'enregistrement est actif.
    /// </summary>
    private void SaveRawPoint(float time, Vector3 pos, float filteredY, Vector3 eulerAngles)
    {
        if (!recordData || rawDataBuffer.Count >= 100000)
            return;

        // Utiliser les fonctions existantes au lieu de recalculer
        int currentStepNumber = stepCount;
        
        float stepDist = 0f;
        if (detectedSteps.Count > 0)
        {
            stepDist = detectedSteps[detectedSteps.Count - 1].horizontalDistance;
        }
        
        float stepSpeed = CalculateWalkingSpeed();

        rawDataBuffer.Add(new RawDataPoint
        {
            timestamp = time,
            positionRaw = pos,
            yFiltered = filteredY,
            rotation = eulerAngles,
            stepNumber = currentStepNumber,
            stepDistance = stepDist,
            speed = stepSpeed,
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
    /// <param name="index">Index du pas détecté dans le buffer filtré.</param>
    /// <param name="time">Timestamp du pas.</param>
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
            horizontalDistance = dist
        });

        stepSpeed = CalculateWalkingSpeed();

        // Mise à jour affichage écran
        UpdateStepCountDisplay();
        UpdateSpeedDisplay();
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
        totalTurnAngle = 0f;
        UpdateTurnDisplay();
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

        float distance = lastStep.horizontalDistance; // distance horizontale (XZ)
        float deltaTime = lastStep.timestamp - previousStep.timestamp;

        if (deltaTime <= 0f)
            return 0f;

        return distance / deltaTime; // vitesse en m/s
    }

    /// <summary>
    /// Met à jour le champ de texte indiquant la distance horizontale depuis le dernier pas.
    /// </summary>
    private void UpdateStepDistanceDisplay(float distance)
    {
        if (stepDistanceText != null)
            stepDistanceText.text = $"Distance pas: {distance:F2} m";
    }

    /// <summary>
    /// Met à jour le champ de texte de la vitesse de marche.
    /// </summary>
    private void UpdateSpeedDisplay()
    {
        if (speedText == null) return;

        if (detectedSteps.Count < 2)
        {
            speedText.text = "Vitesse: -- m/s";
            return;
        }

        speedText.text = $"Vitesse: {stepSpeed:F2} m/s";
    }
    /// <summary>
    /// Met a jour l'etat du virage en cours.
    /// </summary>
    private void UpdateTurn(float time, float delta)
    {
        totalTurnAngle += delta;

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
            Mathf.Abs(totalTurnAngle) > turnAngleThreshold)
        {
            detectedTurns.Add(new TurnEvent
            {
                startTime = turnStartTime,
                endTime = time,
                totalAngle = totalTurnAngle,
                direction = currentTurn
            });

            if (showDebugLogs)
            {
                Debug.Log($"[Turn] Virage {currentTurn} terminé: {totalTurnAngle:F1}° en {duration:F2}s");
            }
        }

        currentTurn = TurnDirection.None;
        UpdateTurnDisplay();
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
            stepCountText.text = "Pas: " + stepCount;
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
                turnStatusText.text = "Virage: GAUCHE";
                turnStatusText.color = Color.cyan;
                break;

            case TurnDirection.Right:
                turnStatusText.text = "Virage: DROITE";
                turnStatusText.color = Color.yellow;
                break;

            default:
                turnStatusText.text = "Virage: Aucun";
                turnStatusText.color = Color.white;
                break;
        }
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

        xPrev = new float[3];
        yPrev = new float[3];

        currentTurn = TurnDirection.None;

        UpdateStepCountDisplay();
        UpdateTurnDisplay();
    }

    /// <summary>
    /// Exporte les donnees brutes et evenements dans un fichier CSV.
    /// Format: Temps;X;Y;Z;RotX;RotY;RotZ;NbPas;DistancePas;Vitesse;Virage
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
                // En-tête CSV
                sw.WriteLine("Temps;X;Y;Z;RotX;RotY;RotZ;NbPas;DistancePas;Vitesse;Virage");

                foreach (var p in rawDataBuffer)
                {
                    // Code virage: 0=Aucun, L=Gauche, R=Droite
                    string turnCode = 
                        p.turnStatus == TurnDirection.Left ? "L" :
                        p.turnStatus == TurnDirection.Right ? "R" : "0";

                    sw.WriteLine(
                        $"{p.timestamp:F3};{p.positionRaw.x:F4};{p.positionRaw.y:F4};{p.positionRaw.z:F4};" +
                        $"{p.rotation.x:F2};{p.rotation.y:F2};{p.rotation.z:F2};" +
                        $"{p.stepNumber};{p.stepDistance:F4};{p.speed:F4};{turnCode}"
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
    public void ResetAndExport()
    {
        // Chemin par défaut pour l'export
        string path = System.IO.Path.Combine(Application.persistentDataPath, 
            $"WalkingData_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");

        // Exporter les données avant réinitialisation
        ExportData(path);

        // Réinitialiser le détecteur
        ResetStepCount();
    }

    // AJOUTER après ResetStepCount() pour permettre l'export automatique
    void OnApplicationQuit()
    {
        // Export automatique à la fermeture de l'application
        string path = System.IO.Path.Combine(Application.persistentDataPath, 
            $"WalkingData_{System.DateTime.Now:yyyyMMdd_HHmms}.csv");
        ExportData(path);
    }


    // ======================================================================
    // ACCES AUX DONNEES
    // ======================================================================

    /// <summary>
    /// Retourne le nombre total de pas detectes.
    /// </summary>
    public int GetStepCount() => stepCount;

    /// <summary>
    /// Retourne le nombre total de virages detectes.
    /// </summary>
    public int GetTurnCount() => detectedTurns.Count;

    /// <summary>
    /// Retourne la liste des pas detectes.
    /// </summary>
    public List<StepEvent> GetDetectedSteps() => new List<StepEvent>(detectedSteps);

    /// <summary>
    /// Retourne la liste des virages detectes.
    /// </summary>
    public List<TurnEvent> GetDetectedTurns() => new List<TurnEvent>(detectedTurns);
}


// ==========================================================================
// BUFFER CIRCULAIRE GENERIQUE
// ==========================================================================

/// <summary>
/// Buffer circulaire generique permettant de stocker un flux continu
/// en limitant la memoire. Les anciens elements sont ecrases par les nouveaux.
/// </summary>
public class CircularBuffer<T>
{
    private readonly T[] buffer;      // Tableau interne de stockage
    private int head = 0;             // Index du prochain emplacement a ecrire
    private int count = 0;            // Nombre d'elements reels contenus
    private readonly int capacity;    // Taille maximale du buffer

    public int Count => count;        // Nombre d'elements actuellement stockes

    public CircularBuffer(int capacity)
    {
        this.capacity = capacity;
        buffer = new T[capacity];
    }

    /// <summary>
    /// Ajoute un nouvel element dans le buffer. Ecrase l'ancien si plein.
    /// </summary>
    public void Add(T item)
    {
        buffer[head] = item;
        head = (head + 1) % capacity;

        if (count < capacity)
            count++;
    }

    /// <summary>
    /// Accede a un element par index logique (0 = plus ancien).
    /// </summary>
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

    /// <summary>
    /// Vide completement le buffer.
    /// </summary>
    public void Clear()
    {
        head = 0;
        count = 0;
    }
}
