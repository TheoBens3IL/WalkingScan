import os
import re
import functools
import pandas as pd

# Mapping: préfixe colonne BDD -> ID_Test
COLUMN_MAPPING = {
    'NP.MS': 'Marche',
    'NP.MSDT': 'Marche2',
    'NP.F8': 'F8W1',
    'NP.F8DT': 'F8W2',
    'NP.TUG': 'TUG1',
    'NP.TUGDT': 'TUG2',
    'NP.FO': 'FO1',
    'NP.FODT': 'FO2',
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
        
        # Chercher toutes les colonnes qui correspondent au pattern NP.*[0-9]+.XR
        for col in df.columns:
            # Pattern: NP.<test><essai>.XR (ex: NP.MS1.XR, NP.F810.XR)
            pattern = r'^(NP\.[A-Z]+)(\d+)\.XR$'
            match = re.match(pattern, col)
            if match:
                prefix = match.group(1)  # Ex: "NP.MS"
                essai = int(match.group(2))  # Ex: 1, 2, 3, ou tout autre nombre
                
                # Vérifier si le préfixe est dans notre mapping
                if prefix in COLUMN_MAPPING:
                    id_test = COLUMN_MAPPING[prefix]
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