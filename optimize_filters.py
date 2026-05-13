"""
Script pour tester différentes configurations de filtres sur l'ensemble du dataset
et trouver les paramètres qui donnent le meilleur taux de réussite.
"""

import sys
from pathlib import Path
import pandas as pd
from estimate_walking_parameters import estimate_walking_parameters
from bdd_utils import load_bdd_cached, extract_file_info_from_name, COLUMN_MAPPING

# Configuration
CURRENT_DIR = Path(__file__).resolve().parent
DATASET_DIR = CURRENT_DIR / "dataset_sorted" / "jeunes"
# DATASET_DIR = CURRENT_DIR / "dataset_sorted" / "âgés"

# Configurations de filtres à tester
FILTER_CONFIGS = [
    # Méthode passe-bas avec différentes fréquences
    {'method': 'passe-bas', 'kwargs': {'fs': 50.0, 'fc': 1.5}, 'name': 'passe-bas_fc1.5'},
    {'method': 'passe-bas', 'kwargs': {'fs': 50.0, 'fc': 1.8}, 'name': 'passe-bas_fc1.8'},
    {'method': 'passe-bas', 'kwargs': {'fs': 50.0, 'fc': 2.0}, 'name': 'passe-bas_fc2.0'},
    {'method': 'passe-bas', 'kwargs': {'fs': 50.0, 'fc': 2.5}, 'name': 'passe-bas_fc2.5'},
    {'method': 'passe-bas', 'kwargs': {'fs': 50.0, 'fc': 3.0}, 'name': 'passe-bas_fc3.0'},
    
    # Méthode gaussienne avec différents sigmas
    {'method': 'gaussian', 'kwargs': {'sigma': 1.5}, 'name': 'gaussian_sigma1.5'},
    {'method': 'gaussian', 'kwargs': {'sigma': 2.0}, 'name': 'gaussian_sigma2.0'},
    {'method': 'gaussian', 'kwargs': {'sigma': 2.5}, 'name': 'gaussian_sigma2.5'},
    {'method': 'gaussian', 'kwargs': {'sigma': 3.0}, 'name': 'gaussian_sigma3.0'},
    
    # Méthode Savitzky-Golay avec différentes fenêtres
    {'method': 'savgol', 'kwargs': {'window': 7, 'poly': 3}, 'name': 'savgol_w7_p3'},
    {'method': 'savgol', 'kwargs': {'window': 9, 'poly': 3}, 'name': 'savgol_w9_p3'},
    {'method': 'savgol', 'kwargs': {'window': 11, 'poly': 3}, 'name': 'savgol_w11_p3'},
    {'method': 'savgol', 'kwargs': {'window': 13, 'poly': 3}, 'name': 'savgol_w13_p3'},
    
    # Méthode médiane avec différentes tailles de kernel
    {'method': 'median', 'kwargs': {'kernel_size': 5}, 'name': 'median_k5'},
    {'method': 'median', 'kwargs': {'kernel_size': 7}, 'name': 'median_k7'},
    {'method': 'median', 'kwargs': {'kernel_size': 9}, 'name': 'median_k9'},
]


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


def extract_file_info(filename):
    """Extrait participant_id, id_test, essai depuis le nom de fichier."""
    participant_id, id_test, essai, is_age = extract_file_info_from_name(filename)
    return participant_id, id_test, essai


def list_files(root: Path):
    """Liste tous les fichiers CSV commençant par 'Data'."""
    return sorted([p for p in root.rglob("Data*.csv") if p.is_file()])


def test_single_config(config, dataset_dir, ground_truth):
    """
    Teste une configuration de filtre sur l'ensemble du dataset.
    Retourne un dictionnaire avec les résultats.
    """
    files = list_files(dataset_dir)
    
    results = {
        'config_name': config['name'],
        'total': 0,
        'success': 0,
        'failures': [],
        'errors': 0,
        'skipped': 0,
    }
    
    for fpath in files:
        filename = fpath.name
        
        # Vérifier FalseStart
        if check_false_start(fpath):
            results['skipped'] += 1
            continue
        
        # Extraire les infos du fichier
        participant_id, id_test, essai = extract_file_info(filename)
        if not participant_id or not id_test:
            results['skipped'] += 1
            continue
        
        # Vérifier si on a la vérité terrain
        real_steps = ground_truth.get(participant_id, {}).get((id_test, essai))
        if real_steps is None:
            results['skipped'] += 1
            continue
        
        results['total'] += 1
        
        # Tester avec la config actuelle
        try:
            metrics = estimate_walking_parameters(
                str(fpath),
                smoothing_method=config['method'],
                smoothing_kwargs=config['kwargs'],
                plot=False,
                print_results=False
            )
            
            if metrics is None:
                results['errors'] += 1
                continue
            
            detected_steps = metrics['n_steps']
            
            if detected_steps == real_steps:
                results['success'] += 1
            else:
                diff = detected_steps - real_steps
                results['failures'].append({
                    'file': filename,
                    'detected': detected_steps,
                    'real': real_steps,
                    'diff': diff
                })
        
        except Exception as e:
            results['errors'] += 1
    
    return results


def run_optimization():
    """Execute l'optimisation complète."""
    print("="*80)
    print("🔍 OPTIMISATION DES PARAMÈTRES DE FILTRAGE")
    print("="*80)
    
    # Vérifier que le dataset existe
    if not DATASET_DIR.exists():
        print(f"❌ Dossier introuvable: {DATASET_DIR}")
        sys.exit(1)
    
    # Charger la vérité terrain
    kind = 'ages' if 'âgés' in str(DATASET_DIR) else 'jeunes'
    ground_truth = load_bdd_cached(kind)
    
    if not ground_truth:
        print(f"❌ Impossible de charger la vérité terrain pour {kind}")
        sys.exit(1)
    
    print(f"\n📁 Dataset: {DATASET_DIR.name}")
    print(f"📊 {len(ground_truth)} participants dans la BDD")
    print(f"🧪 {len(FILTER_CONFIGS)} configurations à tester\n")
    
    # Tester chaque configuration
    all_results = []
    for i, config in enumerate(FILTER_CONFIGS, 1):
        print(f"[{i}/{len(FILTER_CONFIGS)}] Test de {config['name']}...")
        result = test_single_config(config, DATASET_DIR, ground_truth)
        all_results.append(result)
        
        # Afficher résumé rapide
        if result['total'] > 0:
            success_rate = (result['success'] / result['total']) * 100
            print(f"  → {result['success']}/{result['total']} ({success_rate:.1f}%)")
        else:
            print(f"  → Aucun fichier testé")
    
    # Trier par taux de réussite
    all_results.sort(key=lambda r: r['success'] / max(r['total'], 1), reverse=True)
    
    # Afficher le classement
    print("\n" + "="*80)
    print("🏆 CLASSEMENT DES CONFIGURATIONS")
    print("="*80)
    
    for i, result in enumerate(all_results, 1):
        total = result['total']
        success = result['success']
        if total > 0:
            rate = (success / total) * 100
            print(f"{i}. {result['config_name']:30s} → {success}/{total} ({rate:.1f}%)")
        else:
            print(f"{i}. {result['config_name']:30s} → Aucun fichier testé")
    
    # Sauvegarder les résultats détaillés
    output_file = CURRENT_DIR / f"filter_optimization_{DATASET_DIR.name}.txt"
    try:
        with open(output_file, 'w', encoding='utf-8') as fh:
            fh.write("OPTIMISATION DES PARAMÈTRES DE FILTRAGE\n")
            fh.write("="*80 + "\n\n")
            fh.write(f"Dataset: {DATASET_DIR.name}\n\n")
            
            for i, result in enumerate(all_results, 1):
                fh.write(f"\n{i}. {result['config_name']}\n")
                fh.write("-" * 60 + "\n")
                total = result['total']
                success = result['success']
                if total > 0:
                    rate = (success / total) * 100
                    fh.write(f"Taux de réussite: {success}/{total} ({rate:.1f}%)\n")
                    fh.write(f"Erreurs: {result['errors']}\n")
                    fh.write(f"Fichiers ignorés: {result['skipped']}\n")
                    
                    if result['failures']:
                        fh.write(f"\nÉchecs ({len(result['failures'])}):\n")
                        for fail in result['failures'][:10]:  # Afficher les 10 premiers
                            fh.write(f"  {fail['file']}: détectés={fail['detected']}, réels={fail['real']} (diff={fail['diff']:+d})\n")
                        if len(result['failures']) > 10:
                            fh.write(f"  ... et {len(result['failures'])-10} autres\n")
                else:
                    fh.write("Aucun fichier testé\n")
                fh.write("\n")
        
        print(f"\n📄 Résultats détaillés sauvegardés dans: {output_file}")
    
    except Exception as e:
        print(f"\n⚠️ Impossible de sauvegarder les résultats: {e}")
    
    print("\n✅ Optimisation terminée!")


if __name__ == "__main__":
    run_optimization()