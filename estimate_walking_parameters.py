import os
import matplotlib.pyplot as plt
import pandas as pd
import numpy as np
from scipy.signal import medfilt, savgol_filter
from scipy.ndimage import gaussian_filter1d

from utils import resolve_path, plot_position_over_time, plot_position_filtered_over_time, plot_y_over_z
from bdd_utils import get_real_steps_for_file

# Dossier contenant les CSV
DATASET_DIR = os.path.join(os.path.dirname(__file__), 'dataset')
DATASET_LABELIZED_DIR = os.path.join(os.path.dirname(__file__), 'dataset_labelized')

def estimate_walking_parameters(filename, smoothing_method='passe-bas', smoothing_kwargs=None, plot=True, print_results=True, skip_rows: int = 0):
    """
    Estime :
    - le nombre de pas
    - la distance totale parcourue (plan XZ)
    - la longueur moyenne des pas
    - la vitesse moyenne par pas
    - la vitesse moyenne globale (distance totale / durée entre premier et dernier pas)
    """
    # Extraire le nombre de pas réel du nom de fichier
    # match = re.search(r'(\d+)[-_]?pas', filename, re.IGNORECASE)
    # real_steps = int(match.group(1)) if match else None

    # ================================================================ #
    # ==================== EXTRACTION DES DONNÉES ==================== #
    # ================================================================ #

    # Dataset
    filepath = resolve_path(filename)
    df = pd.read_csv(filepath, sep=';')

    # Optionnel : supprimer les premières lignes de données (après l'en-tête)
    if skip_rows and skip_rows > 0:
        df = df.iloc[skip_rows:].reset_index(drop=True)
        if df.empty:
            if print_results:
                print(f"Après suppression des {skip_rows} premières lignes, pas de données disponibles.")
            return None

    # Conversion sûre en float, en gérant les espaces et caractères parasites
    for col in ["ElapsedTime", "HeadPosition_X", "HeadPosition_Y", "HeadPosition_Z"]:
        df[col] = (
            df[col]
            .astype(str)
            .str.strip()                                   # retire espaces
            .str.replace(",", ".", regex=False)            # virgule → point
            .str.replace(r"[^0-9\.\-eE]", "", regex=True)  # supprime tout autre caractère
        )
        df[col] = pd.to_numeric(df[col], errors="coerce")  # force conversion en float

    # Données bruts
    # time = df["Time"].values
    # y = df["PosY"].values
    # x = df["PosX"].values
    # z = df["PosZ"].values
    time = df["ElapsedTime"].values
    y = df["HeadPosition_Y"].values
    x = df["HeadPosition_X"].values
    z = df["HeadPosition_Z"].values

    # ================================================================ #
    # ==================== TRAITEMENT DES DONNÉES ==================== #
    # ================================================================ #

    # Données lissées (enlever le bruit de mesure)
    smoothing_kwargs = smoothing_kwargs or {}
    # Utiliser la fonction flexible de lissage (par défaut 'median')
    y_lisse = smooth_signal(y, method=smoothing_method, **smoothing_kwargs)

    # Dérivée des données lissées
    dy_t = np.gradient(y_lisse, time)

    # ================================================================ #
    # ==================== DÉTECTION DES PAS ========================= #
    # ================================================================ #

    # Détection des points où la dérivée passe de négative à positive (minimums locaux) : correspond au moment où le pied touche le sol
    extremums = np.where((np.diff(np.sign(dy_t)) > 0))[0]
    # print("nbr de pas : ", len(extremums)-1)

    # Vérifier que la distance en x,z entre deux extremums successifs est significative (ex: > 0.10m)
    valid_extremums = [extremums[0]]
    min_step_distance = 0.10  # m
    for i in range(1, len(extremums)):
        idx_prev = extremums[i-1]
        idx_cur = extremums[i]
        dx = x[idx_cur] - x[idx_prev]
        dz = z[idx_cur] - z[idx_prev]
        step_distance = np.sqrt(dx**2 + dz**2)
        if step_distance >= min_step_distance:
            valid_extremums.append(idx_cur)

    if print_results:
        print(f"Nombre de pas détectés (avec distance > {min_step_distance}m) : {len(valid_extremums)}")

    # ================================================================ #
    # ==================== DÉTECTION DES VIRAGES ====================== #
    # ================================================================ #

    # Détecter les virages (toujours nécessaire pour les métriques)
    turn_intervals = detect_turns(x, z, valid_extremums, time, angle_threshold_deg=30.0)

    # Affichage des données brutes, lissées et extremums
    if plot :
        # récupérer le nombre de pas réel depuis la BDD si disponible
        real_steps = get_real_steps_for_file(filepath)

        plt.figure(figsize=(12, 4))
        plt.plot(time, y, label='Données brutes')
        plt.plot(time, y_lisse, label='Données lissées')
        if len(valid_extremums) > 0:
            plt.scatter(time[valid_extremums], y_lisse[valid_extremums], color='red', label='Extremums')

        # Titre : nom du fichier (ligne 1) puis "Pas réels | Pas détectés" (ligne 2)
        filename_only = os.path.basename(filepath)
        steps_title = f"Pas détectés: {len(valid_extremums)}"
        if real_steps is not None:
            steps_title = f"Pas réels: {real_steps} | {steps_title}"
        full_title = f"{filename_only}\n{steps_title}"
        plt.title(full_title)

        plt.legend()
        plt.xlabel('Temps (s)')
        plt.ylabel('Position Y (m)')

        # Colorier les virages en fond
        for (t0, t1) in turn_intervals:
            plt.axvspan(t0, t1, color='orange', alpha=0.3)

    # ====================================================================== #
    # ============= CALCUL DES AUTRES MÉTRIQUES DE MARCHE ================== #
    # ====================================================================== #

    # Calcul de la distance totale parcourue en X
    # Calculer la distance projetée sur le plan XZ (distance horizontale)
    # et la longueur + vitesse de chaque pas indépendamment
    total_distance = 0.0
    step_lengths = []
    step_speeds = []
    if len(valid_extremums) > 1:
        for i in range(1, len(valid_extremums)):
            idx_prev = valid_extremums[i-1]
            idx_cur = valid_extremums[i]
            dx = x[idx_cur] - x[idx_prev]
            dz = z[idx_cur] - z[idx_prev]
            step_distance = np.sqrt(dx**2 + dz**2)
            total_distance += step_distance
            step_lengths.append(step_distance)

            # durée du pas (entre deux contacts successifs)
            dt = time[idx_cur] - time[idx_prev]
            if dt > 0:
                step_speed = step_distance / dt
            else:
                step_speed = float('nan')
            step_speeds.append(step_speed)

        if print_results:
            print("Distance totale parcourue (plan XZ) : {:.3f} m".format(total_distance))

        # Afficher la longueur et la vitesse de chaque pas
        if print_results:
            print("Longueurs et vitesses de chaque pas (m, m/s, plan XZ) :")
            for index_step, (step_length, step_speed) in enumerate(zip(step_lengths, step_speeds), start=1):
                print(f"  Pas {index_step}: longueur={step_length:.3f} m, vitesse={step_speed:.3f} m/s")

        mean_step_length = np.mean(step_lengths)
        # calculer la vitesse moyenne des pas (moyenne arithmétique des vitesses par pas en ignorant NaN)
        mean_step_speed = np.nanmean(step_speeds) if len(step_speeds) > 0 else float('nan')

        if print_results:
            print("Longueur moyenne des pas (XZ) : {:.3f} m".format(mean_step_length))
            print("Vitesse moyenne par pas : {:.3f} m/s".format(mean_step_speed))

        # vitesse moyenne globale: distance totale / durée totale (entre premier et dernier pas)
        total_time_between_steps = time[extremums[-1]] - time[extremums[0]]
        if total_time_between_steps > 0:
            overall_avg_speed = total_distance / total_time_between_steps
        else:
            overall_avg_speed = float('nan')
        if print_results:
            print("Vitesse moyenne globale : {:.3f} m/s".format(overall_avg_speed))
    else:
        if print_results:
            print("Pas assez de données pour calculer la longueur/vitesse des pas.")

    # retourner un dictionnaire de métriques pour réutilisation
    metrics = {
        'n_steps': max(len(valid_extremums), 0),
        'total_distance': total_distance,
        'step_lengths': step_lengths,
        'step_speeds': step_speeds,
        'mean_step_length': float(np.mean(step_lengths)) if len(step_lengths) > 0 else float('nan'),
        'mean_step_speed': float(mean_step_speed) if len(step_speeds) > 0 else float('nan'),
        'overall_avg_speed': float(overall_avg_speed) if 'overall_avg_speed' in locals() else float('nan'),
        'extremums': extremums,
        'turn_intervals': turn_intervals,
    }

    if plot :
        plt.show()
    return metrics


def detect_turns(x, z, extremums_idx, time, angle_threshold_deg=20.0):
    """Detecte les segments de virage à partir des positions X,Z aux indices d'extrêmums.

    Approche :
      - calculer l'azimut entre points consécutifs (en radians)
      - calculer la variation d'angle absolue entre segments
      - marquer comme virage les endroits où la variation dépasse angle_threshold_deg
      - regrouper en intervalles de temps si nécessaire (min_duration_s)

    Retourne une liste de tuples (t_start, t_end) en secondes.
    """
    if len(extremums_idx) < 3:
        return []

    # positions aux extremums
    # Pour chaque extremums de y (pas), on récupère les positions correspondantes en X et Z
    xs = x[extremums_idx]
    zs = z[extremums_idx]

    # azimuts entre points consécutifs
    # Pour chaque segment entre deux extremums, on calcule l'angle dans le plan XZ
    deltas = np.column_stack((np.diff(xs), np.diff(zs)))
    azimuts = np.arctan2(deltas[:,1], deltas[:,0])  # atan2(dz, dx)

    # variation angulaire entre segments
    # Pour chaque segment entre deux extremums, on calcule la variation d'angle par rapport au segment précédent
    ang_diff = np.abs(np.diff(azimuts))
    # normaliser à [-pi, pi]
    ang_diff = (ang_diff + np.pi) % (2*np.pi) - np.pi
    ang_diff = np.abs(ang_diff)

    # seuil en radians
    thresh = np.deg2rad(angle_threshold_deg)
    print(f"Seuil de détection de virage : {angle_threshold_deg}° ({thresh:.3f} rad)")

    # points où la variation angulaire dépasse le seuil
    turn_points = np.where(ang_diff > thresh)[0]  # indices des points considérés comme virages dans ang_diff
    if len(turn_points) == 0:
        return []

    # Regrouper les points de virage contigus (dans ang_diff)
    groups = []
    cur_group = [turn_points[0]]
    for tp in turn_points[1:]:
        if tp == cur_group[-1] + 1:
            cur_group.append(tp)
        else:
            groups.append(cur_group)
            cur_group = [tp]
    groups.append(cur_group)

    # Pour chaque groupe, on prend le premier extremum avant le groupe
    # et le dernier extremum après le groupe pour couvrir l'ensemble du virage
    intervals = []
    n_ext = len(extremums_idx)
    for grp in groups:
        center_start = grp[0] + 1
        center_end = grp[-1] + 1

        # index du premier extremum à inclure (éventuellement center_start-1)
        start_ext_idx = max(center_start - 1, 0)
        # index du dernier extremum à inclure (éventuellement center_end+1)
        end_ext_idx = min(center_end + 1, n_ext - 1)

        t_start = time[extremums_idx[start_ext_idx]]
        t_end = time[extremums_idx[end_ext_idx]]
        intervals.append((t_start, t_end))

    # fusionner intervalles chevauchants
    intervals.sort()
    merged = []
    cur_start, cur_end = intervals[0]
    for s, e in intervals[1:]:
        if s <= cur_end:
            cur_end = max(cur_end, e)
        else:
            merged.append((cur_start, cur_end))
            cur_start, cur_end = s, e
    merged.append((cur_start, cur_end))

    return merged


def smooth_signal(y, method='median', **kwargs):
    """Retourne la version lissée de y.

    method: 'median' | 'moving' | 'savgol' | 'gaussian' | 'passe-bas'
    kwargs: paramètres spécifiques à chaque méthode
    """
    if method == 'median':
        k = int(kwargs.get('kernel_size', 5))
        # kernel_size doit être impair
        if k % 2 == 0:
            k += 1
        return medfilt(y, kernel_size=k)
    elif method == 'moving':
        w = int(kwargs.get('window', 5))
        if w < 1:
            w = 1
        # moving average via convolution
        window = np.ones(w) / w
        return np.convolve(y, window, mode='same')
    elif method == 'savgol':
        window = int(kwargs.get('window', 7))
        poly = int(kwargs.get('poly', 3))
        if window % 2 == 0:
            window += 1
        return savgol_filter(y, window_length=window, polyorder=poly)
    elif method == 'gaussian':
        sigma = float(kwargs.get('sigma', 2.0))
        return gaussian_filter1d(y, sigma=sigma)
    elif method == 'passe-bas':
        # --- IGNORE ---
        from scipy.signal import butter, filtfilt
        fs = float(kwargs.get('fs', 1.0))  # fréquence d'échantillonnage
        fc = float(kwargs.get('fc', 0.1))  # fréquence de coupure
        w = fc / (fs / 2)  # fréquence normalisée
        b, a = butter(4, w, 'low')
        return filtfilt(b, a, y)
    else:
        raise ValueError(f"Méthode de lissage inconnue: {method}")


# Parcourir chaque fichier du dossier dataset_labelized et appliquer l'estimation avec filtre passe-bas
# for filename in os.listdir(DATASET_LABELIZED_DIR):
#     if filename.endswith(".csv"):
#         fullpath = os.path.join(DATASET_LABELIZED_DIR, filename)
#         print(f"\n=== Analyse de {fullpath} ===")
#         if not os.path.exists(fullpath):
#             print(f"Warning: fichier introuvable : {fullpath} — je passe au suivant.")
#             continue
#         try:
#             estimate_walking_parameters(fullpath, smoothing_method='passe-bas', smoothing_kwargs={'fs':50.0,'fc':1.8})
#         except Exception as e:
#             print(f"Erreur lors de l'analyse de {fullpath}: {e}")

# plot_position_over_time("Trajectoire_HMD.csv")

# Construire le chemin absolu vers le fichier
# file_path = os.path.join(os.path.dirname(__file__), "Dataset_WalkingScan", "Jeunes", "Data_p001", "Data_p001​.csv")
# file_path = os.path.join(os.path.dirname(__file__), "dataset", "3.csv")
# estimate_walking_parameters(file_path)
