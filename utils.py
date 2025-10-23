import os
import pandas as pd
import matplotlib.pyplot as plt

def resolve_path(filename: str) -> str:
    """
    Résout le chemin absolu d'un fichier à partir de son nom seul.

    Comportement:
    - Si 'filename' est déjà un chemin existant (absolu ou relatif), on le renvoie tel quel.
    - Sinon on cherche récursivement à partir du dossier du projet (celui de ce fichier),
      en priorisant 'dataset_labelized' puis 'dataset', puis le reste des sous-dossiers.
    - Sur Windows, la recherche est insensible à la casse.
    - En cas de multiples correspondances, on choisit en priorité un chemin contenant
      'dataset_labelized', puis 'dataset', sinon le plus court.
    """
    # Si c'est déjà un chemin valide, le retourner
    if os.path.exists(filename):
        return filename

    base_dir = os.path.abspath(os.path.dirname(__file__))

    # Racines à prioriser pour la recherche
    preferred_dirs = ["dataset_walkingscan", "dataset_labelized", "dataset"]
    search_roots = []
    for d in preferred_dirs:
        p = os.path.join(base_dir, d)
        if os.path.isdir(p):
            search_roots.append(p)
    # Ajouter la racine du projet en dernier recours
    search_roots.append(base_dir)

    target = filename
    is_windows = os.name == "nt"
    matches = []
    seen = set()

    for root in search_roots:
        if root in seen or not os.path.isdir(root):
            continue
        seen.add(root)
        for dirpath, _, filenames in os.walk(root):
            if is_windows:
                for f in filenames:
                    if f.lower() == target.lower():
                        matches.append(os.path.abspath(os.path.join(dirpath, f)))
            else:
                if target in filenames:
                    matches.append(os.path.abspath(os.path.join(dirpath, target)))

    if not matches:
        # Lister quelques fichiers disponibles pour aider au debug
        samples = []
        for root in search_roots:
            try:
                for f in os.listdir(root):
                    samples.append(os.path.join(root, f))
            except FileNotFoundError:
                pass
        sample_text = "\n  ".join(samples[:50]) if samples else "(aucun fichier trouvé)"
        raise FileNotFoundError(
            f"Fichier introuvable: '{filename}'. Dossiers parcourus:\n  " +
            "\n  ".join(search_roots) +
            "\n\nExemples de fichiers disponibles:\n  " + sample_text
        )

    if len(matches) > 1:
        def rank(p: str):
            pl = p.lower()
            if "dataset_labelized" in pl:
                return (0, len(p))
            if "dataset" in pl:
                return (1, len(p))
            return (2, len(p))
        matches.sort(key=rank)

    return matches[0]


def plot_position_over_time(dataset_or_df, detected_steps=None, real_steps=None) -> None:
    """Plot position components over time.

    Accepts either a path (string) to a CSV or a pandas DataFrame.
    If `detected_steps` is provided (array-like of indices) they will be shown on the Z subplot.
    """
    # If a DataFrame was passed, use it directly; otherwise resolve path and read CSV
    if isinstance(dataset_or_df, pd.DataFrame):
        df = dataset_or_df
    else:
        df = pd.read_csv(resolve_path(dataset_or_df))

    time = df["Time"]
    fig, axes = plt.subplots(3, 1, figsize=(10, 8), sharex=True)
    axes[0].plot(time, df["PosX"], label="PosX")
    axes[0].set_ylabel("PosX (m)")
    axes[0].grid(True, alpha=0.3)

    axes[1].plot(time, df["PosY"], label="PosY", color="tab:orange")
    axes[1].set_ylabel("PosY (m)")
    axes[1].grid(True, alpha=0.3)

    axes[2].plot(time, df["PosZ"], label="PosZ", color="tab:green")
    axes[2].set_ylabel("PosZ (m)")
    axes[2].set_xlabel("Time (s)")
    axes[2].grid(True, alpha=0.3)

    if detected_steps is not None and len(detected_steps) > 0:
        try:
            # Plot detected steps on PosY subplot
            axes[1].scatter(df["Time"].iloc[detected_steps], df["PosY"].iloc[detected_steps],
                            label="Detected Steps", color="tab:red", zorder=5)
            axes[1].legend()
        except Exception:
            # ignore plotting overlay if indices don't match
            pass

    fig.suptitle("Positions vs Time")
    fig.tight_layout()
    title = "Positions vs Time"
    if real_steps is not None:
        title += f"\nPas réels: {real_steps} | Pas détectés: {len(detected_steps)}"
    else:
        title += f"\nPas détectés: {len(detected_steps)}"
    plt.title(title)
    # Use a blocking show so the window stays open until the user closes it
    plt.show(block=True)


def plot_position_filtered_over_time(PosY, t, detected_steps=None, real_steps=None) -> None:
    """Plot filtered PosY over time with optional detected steps."""
    plt.figure(figsize=(10, 5))
    plt.plot(t, PosY, label="Filtered PosY", color="tab:orange")
    plt.ylabel("PosY (m)")
    plt.xlabel("Time (s)")
    title = "Filtered PosY vs Time"
    if real_steps is not None:
        title += f"\nPas réels: {real_steps} | Pas détectés: {len(detected_steps)}"
    else:
        title += f"\nPas détectés: {len(detected_steps)}"
    plt.title(title)
    plt.grid(True, alpha=0.3)

    if detected_steps is not None and len(detected_steps) > 0:
        try:
            plt.scatter(t[detected_steps], PosY[detected_steps],
                        label="Detected Steps", color="tab:red", zorder=5)
            plt.legend()
        except Exception:
            # ignore plotting overlay if indices don't match
            pass

    plt.tight_layout()
    plt.show(block=True)


def plot_y_over_z(dataset_or_df):
    if isinstance(dataset_or_df, pd.DataFrame):
        df = dataset_or_df
    else:
        df = pd.read_csv(resolve_path(dataset_or_df))

    y = df["PosY"]
    z = df["PosZ"]
    plt.figure(figsize=(10,6))
    plt.plot(z, y, label="PosY vs PosX", color="tab:blue")
    plt.ylabel("PosY (m)")
    plt.xlabel("PosZ (m)")
    plt.show()


def plot_acceleration_over_time(dataset_or_df) -> None:
    """Plot acceleration components over time.

    Accepts either a path (string) to a CSV or a pandas DataFrame.
    """
    if isinstance(dataset_or_df, pd.DataFrame):
        df = dataset_or_df
    else:
        df = pd.read_csv(resolve_path(dataset_or_df))

    time = df["Time"]
    fig, axes = plt.subplots(3, 1, figsize=(10, 8), sharex=True)
    axes[0].plot(time, df["AccX"], label="AccX")
    axes[0].set_ylabel("AccX (m/s²)")
    axes[0].grid(True, alpha=0.3)

    axes[1].plot(time, df["AccY"], label="AccY", color="tab:orange")
    axes[1].set_ylabel("AccY (m/s²)")
    axes[1].grid(True, alpha=0.3)

    axes[2].plot(time, df["AccZ"], label="AccZ", color="tab:green")
    axes[2].set_ylabel("AccZ (m/s²)")
    axes[2].set_xlabel("Time (s)")
    axes[2].grid(True, alpha=0.3)

    fig.suptitle("Accelerations vs Time")
    fig.tight_layout()
    # Block here so the user can inspect the plot window
    plt.show(block=True)