# WalkingScan

## Description du projet

WalkingScan est un système d'analyse de la marche combinant traitement de données offline et détection en temps réel sur casque VR Meta Quest. Le projet permet d'estimer des paramètres de marche (nombre de pas, longueur de pas, vitesse, symétrie, virages) à partir de données de capteurs 6-DOF.

**Objectifs principaux :**
- Validation algorithmique sur bases de données annotées (jeunes et âgés)
- Détection temps réel des pas et paramètres de marche en VR
- Analyse de la symétrie (orientation de la tête, linéarité de trajectoire)
- Export des données pour analyse post-session

---

## Structure des fichiers

### Scripts Python (Traitement offline)

- **`main.py`** : Fichier permettant de tester l'algorithme d'extraction des paramètres de marche sur l'ensemble des fichiers du dataset
- **`estimate_walking_parameters.py`** : Algorithme pour l'estimation des paramètres de marche (nombre de pas, vitesse, distance). Utilise un filtre Butterworth et détection de minima avec validation de prominence
- **`test_performances.py`** : Script de validation qui compare les résultats de détection automatique avec les vérités terrain des bases de données (BDD_Jeunes_E1.csv, BDD_Ages_E1.csv). Calcule les taux de succès et génère des rapports d'erreurs
- **`test_specific_file.py`** : Utilitaire pour tester l'algorithme sur un fichier CSV spécifique avec affichage détaillé des résultats
- **`optimize_filters.py`** : Script d'optimisation des paramètres de filtrage (fréquence de coupure, ordre, prominence, etc.) pour maximiser les performances de détection
- **`bdd_utils.py`** : Utilitaires pour charger les vérités terrain depuis les fichiers BDD, parser les noms de fichiers, et extraire les informations participant/test/essai
- **`validation_utils.py`** : Module de validation centralisé (détection de faux départs, gestion des fichiers exclus, mapping des noms de tests)
- **`filters.py`** : Implémentation des filtres numériques (Butterworth, etc.) pour le traitement du signal
- **`utils.py`** : Fonctions utilitaires générales (I/O, nettoyage de données, etc.)

### Scripts Unity C# (Temps réel XR)

- **`WalkingScan.cs`** : Script de détection des paramètres de marche en temps réel à implémenter dans un casque XR : détection de pas, longueur,  virages, symétrie (orientation tête + linéarité trajectoire), et double métrique de vitesse (instantannée et par pas) + export CSV complet des données.

**Architecture Unity :**
- Utilise OVRCameraRig pour tracking 6-DOF du casque Meta Quest
- Buffers circulaires pour stockage des données (3 secondes @ 100Hz)
- Filtre IIR Butterworth causal 2ème ordre (fc=2Hz)
- Validation en 5 étapes : minimum local, prominence, amplitude, distance temporelle, distance spatiale
- UI TextMeshPro pour affichage temps réel

### Données et configuration

- **`requirements.txt`** : Liste des dépendances Python nécessaires pour le projet (numpy, scipy, pandas, matplotlib)
- **`dataset_sorted/`** : Dossier contenant les fichiers CSV de données de marche
  - `BDD_Jeunes_E1.csv` : Vérité terrain pour la population jeune
  - `BDD_Ages_E1.csv` : Vérité terrain pour la population âgée
  - `Mauvais_Essais.csv` : Liste des fichiers à exclure de l'analyse
  - `jeunes/` : Données brutes des participants jeunes
  - `âgés/` : Données brutes des participants âgés
- **`filter_optimization_jeunes.txt`** : Résultats d'optimisation des paramètres de filtrage

### Documentation

- **`Livret_technique.md`** : Documentation technique complète du système (algorithmes, formules mathématiques, guide Unity, paramètres, format d'export)

---

## Installation et utilisation

### Python
```bash
pip install -r requirements.txt
```

### Unity
1. Importer les scripts C# dans un projet Unity avec Meta Quest SDK
2. Attacher `RealtimeWalkingParametersDetector.cs` au CenterEyeAnchor
3. Configurer les références UI (TextMeshPro)
4. Build pour Meta Quest
5. Appuyer sur 'E' pour exporter les données en fin de session