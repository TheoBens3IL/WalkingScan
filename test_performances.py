import os
import sys
import pandas as pd
import re
from pathlib import Path
from estimate_walking_parameters import estimate_walking_parameters

# Configuration
CURRENT_DIR = Path(__file__).resolve().parent
DATASET_DIR = CURRENT_DIR / "dataset_sorted" / "jeunes"
# DATASET_DIR = CURRENT_DIR / "dataset_sorted" / "âgés"
BDD_JEUNES = CURRENT_DIR / "dataset_sorted" / "BDD_Jeunes_E1.csv"
BDD_AGES = CURRENT_DIR / "dataset_sorted" / "BDD_Ages_E1.csv"

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

def load_ground_truth(bdd_path):
    """
    Charge la base de données et retourne un dictionnaire:
    {participant_id: {(id_test, essai): nbr_pas_reel}}
    """
    df = pd.read_csv(bdd_path, sep=';')
    ground_truth = {}
    
    # La première colonne contient l'ID participant
    id_col = df.columns[0]
    
    print(f"   Colonnes trouvées dans la BDD: {list(df.columns[:5])}...")
    
    for _, row in df.iterrows():
        participant_id = str(row[id_col]).strip()
        if not participant_id or pd.isna(participant_id):
            continue
            
        ground_truth[participant_id] = {}
        
        for col, (id_test, essai) in COLUMN_MAPPING.items():
            if col in df.columns:
                nbr_pas = row[col]
                # Conversion en entier si possible
                if pd.notna(nbr_pas):
                    try:
                        nbr_pas = int(float(nbr_pas))
                        ground_truth[participant_id][(id_test, essai)] = nbr_pas
                    except (ValueError, TypeError):
                        pass
    
    return ground_truth

def extract_file_info(filename):
    """
    Extrait les infos du nom de fichier: Data_pXXX_IDTest_Y.csv ou Data_PXXX_IDTest_Y.csv
    Retourne: (participant_id, id_test, essai)
    """
    # Pattern flexible : accepte p ou P, avec ou sans zéros initiaux
    # Gère aussi les espaces invisibles comme le caractère zero-width space
    pattern = r'Data_[Pp](\d+)[_\s\u200B]+([^_\s\u200B]+)(?:[_\s\u200B]+(\d+))?\.csv'
    match = re.search(pattern, filename)
    
    if match:
        participant = match.group(1)
        id_test = match.group(2).strip()
        essai = int(match.group(3)) if match.group(3) else 1
        
        # Formatter participant_id comme dans la BDD: P001, P002, etc. (sans le A)
        participant_id = f"P{participant.zfill(3)}"
        
        return participant_id, id_test, essai
    
    return None, None, None

def test_performance(dataset_dir, bdd_path):
    """
    Teste la performance de estimate_walking_parameters sur tout le dataset.
    """
    # Charger la vérité terrain
    print(f"📖 Chargement de la BDD: {bdd_path.name}")
    ground_truth = load_ground_truth(bdd_path)
    print(f"   {len(ground_truth)} participants chargés")
    
    # Afficher quelques exemples de clés pour debug
    if ground_truth:
        sample_keys = list(ground_truth.keys())[:3]
        print(f"   Exemples de participants: {sample_keys}")
    print()
    
    # Lister tous les fichiers CSV
    files = sorted([p for p in dataset_dir.rglob("Data*.csv") if p.is_file()])
    print(f"📁 {len(files)} fichiers trouvés dans {dataset_dir.name}\n")
    
    # Résultats
    results = {
        'total': 0,
        'success': 0,
        'failures': [],
        'not_found_in_bdd': [],
        'extra_trials': [],  # Essais > 3
        'errors': [],
        'skipped': 0
    }
    
    # Traiter chaque fichier
    for fpath in files:
        filename = fpath.name
        participant_id, id_test, essai = extract_file_info(filename)
        
        if not participant_id or not id_test:
            print(f"⚠️  Impossible d'extraire les infos de: {filename}")
            results['skipped'] += 1
            continue
        
        # Vérifier si on a la vérité terrain
        if participant_id not in ground_truth:
            results['not_found_in_bdd'].append({
                'file': filename,
                'participant': participant_id,
                'reason': 'Participant non trouvé dans BDD'
            })
            results['skipped'] += 1
            continue
        
        if (id_test, essai) not in ground_truth[participant_id]:
            # Séparer les essais > 3 (normaux) des vrais problèmes
            if essai > 3:
                results['extra_trials'].append({
                    'file': filename,
                    'participant': participant_id,
                    'test': id_test,
                    'essai': essai
                })
            else:
                results['not_found_in_bdd'].append({
                    'file': filename,
                    'participant': participant_id,
                    'test': id_test,
                    'essai': essai,
                    'reason': 'Test/essai non trouvé dans BDD'
                })
            results['skipped'] += 1
            continue
        
        real_steps = ground_truth[participant_id][(id_test, essai)]
        
        # Exécuter l'estimation
        try:
            metrics = estimate_walking_parameters(str(fpath), plot=False, print_results=False)
            detected_steps = metrics['n_steps']
            
            results['total'] += 1
            
            if detected_steps == real_steps:
                results['success'] += 1
                print(f"✅ {filename}: {detected_steps} pas (correct)")
            else:
                results['failures'].append({
                    'file': filename,
                    'participant': participant_id,
                    'test': id_test,
                    'essai': essai,
                    'detected': detected_steps,
                    'real': real_steps,
                    'diff': detected_steps - real_steps
                })
                print(f"❌ {filename}: {detected_steps} pas détectés, {real_steps} réels (diff: {detected_steps - real_steps:+d})")
        
        except Exception as e:
            results['errors'].append({
                'file': filename,
                'error': str(e)
            })
            results['skipped'] += 1
            print(f"⚠️  Erreur sur {filename}: {e}")
    
    return results

def print_summary(results):
    """
    Affiche un résumé des résultats.
    """
    print("\n" + "="*80)
    print("📊 RÉSUMÉ DES PERFORMANCES")
    print("="*80)
    
    total = results['total']
    success = results['success']
    failures = len(results['failures'])
    skipped = results['skipped']
    
    if total > 0:
        success_rate = (success / total) * 100
        print(f"\n✅ Taux de réussite: {success}/{total} ({success_rate:.1f}%)")
        print(f"❌ Échecs: {failures}/{total} ({100-success_rate:.1f}%)")
        print(f"⏭️  Fichiers ignorés: {skipped}")
    else:
        print("\n⚠️  Aucun fichier testé")
        print(f"⏭️  Fichiers ignorés: {skipped}")


def main():
    # Vérifier l'existence des fichiers
    if not DATASET_DIR.exists():
        print(f"❌ Dossier introuvable: {DATASET_DIR}")
        sys.exit(1)
    
    if not BDD_JEUNES.exists():
        print(f"❌ BDD introuvable: {BDD_JEUNES}")
        sys.exit(1)

    print("========== Démarrage des tests de performance ==========\n")

    # Lancer les tests
    results = test_performance(DATASET_DIR, BDD_JEUNES)
    
    # Afficher le résumé
    print_summary(results)

if __name__ == "__main__":
    main()