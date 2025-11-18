import sys
from pathlib import Path
from estimate_walking_parameters import estimate_walking_parameters
from validation_utils import (
    load_ground_truth,
    load_excluded_files,
    extract_file_info,
    build_false_start_exclusion_list,
    list_valid_files,
    clean_filename
)

# Configuration
CURRENT_DIR = Path(__file__).resolve().parent
DATASET_DIR = CURRENT_DIR / "dataset_sorted" / "jeunes"
# DATASET_DIR = CURRENT_DIR / "dataset_sorted" / "âgés"
BDD_JEUNES = CURRENT_DIR / "dataset_sorted" / "BDD_Jeunes_E1.csv"
BDD_AGES = CURRENT_DIR / "dataset_sorted" / "BDD_Ages_E1.csv"
MAUVAIS_ESSAIS = CURRENT_DIR / "dataset_sorted" / "Mauvais_Essais.csv"


def test_performance(dataset_dir, bdd_path, excluded_files=None):
    """
    Teste la performance de estimate_walking_parameters sur tout le dataset.
    excluded_files: set de noms de fichiers à ignorer (depuis Mauvais_Essais.csv).
    """
    excluded_files = excluded_files or set()

    # Charger la vérité terrain
    print(f"\n📖 Chargement de la BDD: {bdd_path.name}")
    ground_truth = load_ground_truth(bdd_path)
    print(f"   {len(ground_truth)} participants chargés")
    if ground_truth:
        sample_keys = list(ground_truth.keys())[:3]
        print(f"   Exemples de participants: {sample_keys}")

    # Lister les fichiers
    files = list_valid_files(dataset_dir)
    print(f"\n📁 {len(files)} fichiers trouvés dans {dataset_dir.name}")

    # Construire la liste des fichiers à exclure à cause de FalseStart
    print(f"\n🔍 Détection des FalseStart et de leurs prédécesseurs...")
    false_start_excluded = build_false_start_exclusion_list(files)
    print(f"   {len(false_start_excluded)} fichiers exclus à cause de FalseStart (fichier + prédécesseur)")
    
    false_start_excluded_clean = {clean_filename(f) for f in false_start_excluded}

    # Résultats
    results = {
        'total': 0,
        'success': 0,
        'failures': [],
        'not_found_in_bdd': [],
        'errors': [],
        'skipped': 0,
        'large_diffs': [],
        'false_start_skipped': [],
        'excluded_skipped': []
    }

    # Traiter chaque fichier
    for fpath in files:
        filename = fpath.name
        fname_clean = clean_filename(filename)

        # Vérifier exclusions
        if fname_clean in excluded_files:
            print(f"⏭️  Ignoré (Mauvais_Essais.csv) : {filename}")
            results['excluded_skipped'].append({'file': filename})
            results['skipped'] += 1
            continue

        if fname_clean in false_start_excluded_clean:
            print(f"⏭️  Ignoré (FalseStart ou prédécesseur) : {filename}")
            results['false_start_skipped'].append({'file': filename})
            results['skipped'] += 1
            continue

        participant_id, id_test, essai = extract_file_info(filename)
        
        if not participant_id or not id_test:
            print(f"⚠️  Impossible d'extraire les infos de: {filename}")
            results['skipped'] += 1
            continue
        
        # Vérifier vérité terrain
        if participant_id not in ground_truth:
            results['not_found_in_bdd'].append({
                'file': filename,
                'participant': participant_id,
                'reason': 'Participant non trouvé dans BDD'
            })
            results['skipped'] += 1
            continue
        
        if (id_test, essai) not in ground_truth[participant_id]:
            if essai > 3:
                print(f"⚠️  Test sans vérité terrain: {filename} (essai {essai})")
                real_steps = None
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
        else:
            real_steps = ground_truth[participant_id][(id_test, essai)]
        
        # Exécuter l'estimation
        try:
            metrics = estimate_walking_parameters(
                str(fpath),
                smoothing_method='passe-bas',
                smoothing_kwargs={'fs': 10.0, 'fc': 1.0},
                plot=False,
                print_results=False
            )
            detected_steps = metrics['n_steps']
            results['total'] += 1
            
            if real_steps is not None:
                diff = detected_steps - real_steps
                if diff == 0 or diff == -1:
                    results['success'] += 1
                    if diff == 0:
                        print(f"✅ {filename}: {detected_steps} pas (correct)")
                    else:
                        print(f"✅ {filename}: {detected_steps} pas détectés, {real_steps} réels (toléré: -1)")
                else:
                    if abs(diff) > 3:
                        results['large_diffs'].append({
                            'file': filename,
                            'participant': participant_id,
                            'test': id_test,
                            'essai': essai,
                            'detected': detected_steps,
                            'real': real_steps,
                            'diff': diff
                        })
                    results['failures'].append({
                        'file': filename,
                        'participant': participant_id,
                        'test': id_test,
                        'essai': essai,
                        'detected': detected_steps,
                        'real': real_steps,
                        'diff': diff
                    })
                    print(f"❌ {filename}: {detected_steps} pas détectés, {real_steps} réels (diff: {diff:+d})")
            else:
                print(f"ℹ️  {filename}: {detected_steps} pas détectés (pas de vérité terrain)")
        
        except Exception as e:
            results['errors'].append({'file': filename, 'error': str(e)})
            results['skipped'] += 1
            print(f"⚠️  Erreur sur {filename}: {e}")
    
    return results


def print_summary(results):
    """Affiche un résumé des résultats."""
    print("\n" + "="*80)
    print("📊 RÉSUMÉ DES PERFORMANCES")
    print("="*80)
    
    total = results['total']
    success = results['success']
    failures = len(results['failures'])
    skipped = results['skipped']
    excluded = len(results.get('excluded_skipped', []))
    false_start = len(results.get('false_start_skipped', []))
    not_found = results.get('not_found_in_bdd', [])
    errors = results.get('errors', [])
    
    if total > 0:
        success_rate = (success / total) * 100
        print(f"\n✅ Taux de réussite: {success}/{total} ({success_rate:.1f}%)")
        print(f"❌ Échecs: {failures}/{total} ({100-success_rate:.1f}%)")
    else:
        print("\n⚠️  Aucun fichier testé")
    
    print(f"\n⏭️  Fichiers ignorés (total): {skipped}")
    if excluded > 0:
        print(f"   • {excluded} fichiers non valides (Mauvais_Essais.csv)")
    if false_start > 0:
        print(f"   • {false_start} fichiers avec FalseStart=True")
    if not_found:
        participant_not_found = sum(1 for x in not_found if x.get('reason') == 'Participant non trouvé dans BDD')
        trial_not_found = sum(1 for x in not_found if x.get('reason') == 'Test/essai non trouvé dans BDD')
        print(f"   • {len(not_found)} non trouvés dans BDD:")
        if participant_not_found > 0:
            print(f"      - {participant_not_found} participants absents")
        if trial_not_found > 0:
            print(f"      - {trial_not_found} essais absents (ex: essai 4+ non dans BDD)")
    if errors:
        print(f"   • {len(errors)} erreurs de traitement")
    
    # Écrire fichiers ignorés
    if not_found or errors:
        out_path = CURRENT_DIR / f"skipped_files_{DATASET_DIR.name}.txt"
        try:
            with open(out_path, 'w', encoding='utf-8') as fh:
                fh.write("Fichiers ignorés (non trouvés dans BDD ou erreurs)\n")
                fh.write("="*60 + "\n\n")
                if not_found:
                    fh.write("NON TROUVÉS DANS BDD:\n")
                    fh.write("-"*60 + "\n")
                    for item in not_found:
                        fh.write(f"{item['file']}: {item['reason']}\n")
                        if 'test' in item and 'essai' in item:
                            fh.write(f"  → Participant: {item['participant']}, Test: {item['test']}, Essai: {item['essai']}\n")
                    fh.write(f"\nTotal: {len(not_found)}\n\n")
                if errors:
                    fh.write("ERREURS DE TRAITEMENT:\n")
                    fh.write("-"*60 + "\n")
                    for item in errors:
                        fh.write(f"{item['file']}: {item['error']}\n")
                    fh.write(f"\nTotal: {len(errors)}\n")
            print(f"\n📄 {len(not_found)+len(errors)} fichiers ignorés détaillés dans: {out_path}")
        except Exception as e:
            print(f"\n⚠️  Impossible d'écrire le fichier des ignorés: {e}")

    # Écrire écarts importants
    large = results.get('large_diffs') or []
    if large:
        out_path = CURRENT_DIR / f"large_diffs_{DATASET_DIR.name}.txt"
        try:
            with open(out_path, 'w', encoding='utf-8') as fh:
                fh.write("Fichiers avec écart important (|diff| > 3)\n")
                fh.write("="*60 + "\n")
                for item in large:
                    fh.write(f"{item['file']}: détectés={item['detected']}, réels={item['real']}, diff={item['diff']:+d}\n")
                fh.write("\nTotal: {}\n".format(len(large)))
            print(f"\n⚠️  {len(large)} fichiers avec écart important écrits dans: {out_path}")
        except Exception as e:
            print(f"\n⚠️  Impossible d'écrire le fichier des écarts importants: {e}")


def main():
    if not DATASET_DIR.exists():
        print(f"❌ Dossier introuvable: {DATASET_DIR}")
        sys.exit(1)
    
    if not BDD_JEUNES.exists():
        print(f"❌ BDD introuvable: {BDD_JEUNES}")
        sys.exit(1)

    print("========== Démarrage des tests de performance ==========\n")

    col_name = 'Âgés' if 'âgés' in str(DATASET_DIR).lower() else 'Jeunes'
    excluded_files = load_excluded_files(MAUVAIS_ESSAIS, col_name)
    if excluded_files:
        print(f"📋 {len(excluded_files)} fichiers exclus chargés depuis Mauvais_Essais.csv (colonne {col_name})\n")
    
    results = test_performance(DATASET_DIR, BDD_JEUNES, excluded_files=excluded_files)
    print_summary(results)


if __name__ == "__main__":
    main()