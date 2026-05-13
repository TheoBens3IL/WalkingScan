"""
Module de validation et filtrage des fichiers du dataset.
Gère les exclusions (FalseStart, Mauvais_Essais), l'extraction d'infos et le mapping.
"""

import re
import pandas as pd
from pathlib import Path

# Mapping des préfixes de colonnes vers les noms de tests
TEST_NAME_MAPPING = {
    'NP.MS': 'Marche',
    'NP.MSDT': 'Marche2',
    'NP.F8': 'F8W1',
    'NP.F8DT': 'F8W2',
    'NP.TUG': 'TUG1',
    'NP.TUGDT': 'TUG2',
    'NP.FO': 'FO1',
    'NP.FODT': 'FO2',
}


def extract_test_info_from_column(col_name):
    """
    Extrait (test_name, essai) depuis un nom de colonne BDD.
    Exemple: 'NP.MS5.XR' → ('Marche', 5)
             'NP.TUGDT12.XR' → ('TUG2', 12)
    Retourne (None, None) si le format ne correspond pas.
    """
    sorted_prefixes = sorted(TEST_NAME_MAPPING.keys(), key=len, reverse=True)
    for prefix in sorted_prefixes:
        pattern = rf'^{re.escape(prefix)}(\d+)\.XR$'
        match = re.match(pattern, col_name)
        if match:
            essai = int(match.group(1))
            test_name = TEST_NAME_MAPPING[prefix]
            return test_name, essai
    return None, None


def load_ground_truth(bdd_path):
    """
    Charge la base de données et retourne un dictionnaire:
    {participant_id: {(id_test, essai): nbr_pas_reel}}
    """
    df = pd.read_csv(bdd_path, sep=';')
    ground_truth = {}
    
    id_col = df.columns[0]
    print(f"   Colonnes trouvées dans la BDD: {list(df.columns[:5])}...")
    
    for _, row in df.iterrows():
        participant_id = str(row[id_col]).strip()
        if not participant_id or pd.isna(participant_id):
            continue
            
        ground_truth[participant_id] = {}
        
        for col in df.columns:
            test_name, essai = extract_test_info_from_column(col)
            if test_name and essai:
                nbr_pas = row[col]
                if pd.notna(nbr_pas):
                    try:
                        nbr_pas = int(float(nbr_pas))
                        ground_truth[participant_id][(test_name, essai)] = nbr_pas
                    except (ValueError, TypeError):
                        pass
    
    return ground_truth


def load_excluded_files(csv_path, column_name):
    """
    Charge la liste des fichiers à exclure depuis Mauvais_Essais.csv.
    Retourne un set de noms de fichiers normalisés (sans caractères invisibles).
    """
    if not csv_path.exists():
        print(f"⚠️  Fichier Mauvais_Essais introuvable: {csv_path}")
        return set()
    try:
        df = pd.read_csv(csv_path, sep=';')
        print(f"📋 Colonnes trouvées dans Mauvais_Essais.csv: {list(df.columns)}")
        if column_name not in df.columns:
            print(f"⚠️  Colonne '{column_name}' introuvable dans {csv_path.name}")
            print(f"   Colonnes disponibles: {list(df.columns)}")
            return set()
        excluded = set()
        for val in df[column_name]:
            if pd.notna(val):
                fname = re.sub(r'[\u200B\u200C\u200D\uFEFF]', '', str(val).strip())
                if fname:
                    if not fname.lower().endswith('.csv'):
                        fname = fname + '.csv'
                    excluded.add(fname)
        return excluded
    except Exception as e:
        print(f"⚠️  Erreur lecture {csv_path.name}: {e}")
        return set()


def extract_file_info(filename):
    """
    Extrait les infos du nom de fichier: Data_pXXX_IDTest_Y.csv
    Retourne: (participant_id, id_test, essai)
    """
    fname = re.sub(r'[\u200B\u200C\u200D\uFEFF]', '', filename)
    pattern = r'Data_[Pp](?:[A-Za-z]?)(\d+)[_\s\-]*([^_\s\.]+)(?:[_\s\-]+(\d+))?\.csv'
    match = re.search(pattern, fname)

    if match:
        participant = match.group(1)
        id_test = match.group(2).strip()
        essai = int(match.group(3)) if match.group(3) else 1
        participant_id = f"P{participant.zfill(3)}"
        return participant_id, id_test, essai

    return None, None, None


def check_false_start(fpath, max_lines=200):
    """Vérifie si le fichier contient FalseStart=True."""
    try:
        with open(fpath, 'r', encoding='utf-8', errors='ignore') as fh:
            header = fh.readline()
            if not header:
                return False
            sep = ';' if ';' in header else (',' if ',' in header else ';')
            cols = [c.strip() for c in header.strip().split(sep)]
            idx = next((i for i, c in enumerate(cols) if c.lower() == 'falsestart'), None)
            if idx is None:
                return False
            for _ in range(max_lines):
                line = fh.readline()
                if not line:
                    break
                parts = line.strip().split(sep)
                if len(parts) <= idx:
                    continue
                v = parts[idx].strip().lower()
                if v in ('true', '1', 'yes', 'y'):
                    return True
    except Exception:
        pass
    return False


def build_false_start_exclusion_list(files):
    """
    Parcourt tous les fichiers, détecte ceux avec FalseStart=True,
    et retourne un set contenant ces fichiers + leurs prédécesseurs (essai N-1).
    """
    excluded = set()
    file_dict = {}
    
    for fpath in files:
        participant_id, id_test, essai = extract_file_info(fpath.name)
        if participant_id and id_test:
            file_dict[(participant_id, id_test, essai)] = fpath
    
    for (participant_id, id_test, essai), fpath in file_dict.items():
        if check_false_start(fpath):
            excluded.add(fpath.name)
            if essai > 1:
                prev_key = (participant_id, id_test, essai - 1)
                if prev_key in file_dict:
                    excluded.add(file_dict[prev_key].name)
    
    return excluded


def list_valid_files(dataset_dir):
    """
    Liste tous les fichiers CSV commençant par 'Data' dans le dataset.
    Retourne une liste triée de Path objects.
    """
    return sorted([p for p in dataset_dir.glob("Data*.csv") if p.is_file()])


def clean_filename(filename):
    """Nettoie un nom de fichier des caractères invisibles."""
    return re.sub(r'[\u200B\u200C\u200D\uFEFF]', '', filename)