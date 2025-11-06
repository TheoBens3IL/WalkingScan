import os
import re
import functools
import pandas as pd

# Mapping: colonne BDD -> (ID_Test, numéro essai)
COLUMN_MAPPING = {
    'NP.MS1.XR': ('Marche', 1),
    'NP.MS2.XR': ('Marche', 2),
    'NP.MS3.XR': ('Marche', 3),
    'NP.MSDT1.XR': ('Marche2', 1),
    'NP.MSDT2.XR': ('Marche2', 2),
    'NP.MSDT3.XR': ('Marche2', 3),
    'NP.F81.XR': ('F8W1', 1),
    'NP.F82.XR': ('F8W1', 2),
    'NP.F83.XR': ('F8W1', 3),
    'NP.F8DT1.XR': ('F8W2', 1),
    'NP.F8DT2.XR': ('F8W2', 2),
    'NP.F8DT3.XR': ('F8W2', 3),
    'NP.TUG1.XR': ('TUG1', 1),
    'NP.TUG2.XR': ('TUG1', 2),
    'NP.TUG3.XR': ('TUG1', 3),
    'NP.TUGDT1.XR': ('TUG2', 1),
    'NP.TUGDT2.XR': ('TUG2', 2),
    'NP.TUGDT3.XR': ('TUG2', 3),
    'NP.FO1.XR': ('FO1', 1),
    'NP.FO2.XR': ('FO1', 2),
    'NP.FO3.XR': ('FO1', 3),
    'NP.FODT1.XR': ('FO2', 1),
    'NP.FODT2.XR': ('FO2', 2),
    'NP.FODT3.XR': ('FO2', 3),
}

# dossier dataset_sorted (assumé au même niveau que les autres modules)
DATASET_SORTED_DIR = os.path.join(os.path.dirname(__file__), 'dataset_sorted')

@functools.lru_cache(maxsize=2)
def load_bdd_cached(kind: str):
    """
    Charge et retourne la vérité terrain pour 'jeunes' ou 'ages'.
    Retourne dict: {participant_id: {(id_test, essai): nbr_pas_reel}}
    """
    if kind == 'jeunes':
        bdd_name = "BDD_Jeunes_E1.csv"
    else:
        bdd_name = "BDD_Ages_E1.csv"
    bdd_path = os.path.join(DATASET_SORTED_DIR, bdd_name)
    if not os.path.exists(bdd_path):
        return {}
    df = pd.read_csv(bdd_path, sep=';')
    ground_truth = {}
    id_col = df.columns[0]
    for _, row in df.iterrows():
        raw_id = str(row[id_col]).strip()
        if not raw_id or pd.isna(raw_id):
            continue
        # Normaliser l'ID participant en format "P###" si possible (ex: "34" -> "P034", "p34" -> "P034")
        m = re.search(r'(\d+)', raw_id)
        if m:
            participant_id = f"P{int(m.group(1)):03d}"
        else:
            participant_id = raw_id
        ground_truth[participant_id] = {}
        
        for col, (id_test, essai) in COLUMN_MAPPING.items():
            if col in df.columns:
                nbr_pas = row[col]
                if pd.notna(nbr_pas):
                    try:
                        nbr_pas_int = int(float(nbr_pas))
                        ground_truth[participant_id][(id_test, essai)] = nbr_pas_int
                    except (ValueError, TypeError):
                        pass
    return ground_truth

def extract_file_info_from_name(filename: str):
    """
    Extrait participant_id, id_test, essai, is_age depuis le nom de fichier.
    Gère 'Data_p031_TUG1_2.csv' et 'Data_pa034_F8W2.csv' et supprime caractères invisibles.
    Retourne tuple (participant_id, id_test, essai, is_age) ou (None, None, None, False).
    """
    fname = re.sub(r'[\u200B\u200C\u200D\uFEFF]', '', filename)
    pattern = r'Data_[Pp]([A-Za-z]?)(\d+)[_\s\-]*([^_\s\.]+)(?:[_\s\-]+(\d+))?\.csv'
    match = re.search(pattern, fname)
    if not match:
        return None, None, None, False
    letter = match.group(1)
    participant = match.group(2)
    id_test = match.group(3).strip()
    essai = int(match.group(4)) if match.group(4) else 1
    is_age = (letter.lower() == 'a') if letter else False
    participant_id = f"P{participant.zfill(3)}"
    return participant_id, id_test, essai, is_age

def get_real_steps_for_file(filepath: str):
    """
    Retourne le nombre de pas réel correspondant au fichier (ou None).
    """
    filename = os.path.basename(filepath)
    participant_id, id_test, essai, is_age = extract_file_info_from_name(filename)
    if not participant_id or not id_test:
        return None
    kind = 'ages' if is_age else 'jeunes'
    ground_truth = load_bdd_cached(kind)
    return ground_truth.get(participant_id, {}).get((id_test, essai))