import sys
import traceback
from pathlib import Path
from pprint import pprint
from estimate_walking_parameters import estimate_walking_parameters

# Ensure we can import test.py from the same folder as this script
CURRENT_DIR = Path(__file__).resolve().parent
if str(CURRENT_DIR) not in sys.path:
    sys.path.insert(0, str(CURRENT_DIR))

DATASET_DIR = CURRENT_DIR / "dataset_sorted" / "jeunes"
# DATASET_DIR = CURRENT_DIR / "dataset_sorted" / "âgés"

def list_files(root: Path):
    '''List all CSV files that start with 'Data'.'''
    return sorted([p for p in root.rglob("Data*.csv") if p.is_file()])

def prompt_user(prompt: str) -> str:
    '''Prompt the user and return their input in lowercase.'''
    try:
        return input(prompt).strip().lower()
    except EOFError:
        return "q"  # quit if input stream closes

def main():
    """Main function to test walking parameter estimation on dataset files."""
    
    # Verify dataset directory exists
    if not DATASET_DIR.exists():
        print(f"Dossier introuvable: {DATASET_DIR}")
        sys.exit(1)

    # List files to process
    files = list_files(DATASET_DIR)
    if not files:
        print(f"Aucun fichier trouvé dans: {DATASET_DIR}")
        sys.exit(0)

    print("Contrôles: Entrée=traiter, s=suivant, a=traiter tout, q=quitter (ou Ctrl+C)")
    process_all = False

    try:
        for idx, fpath in enumerate(files, 1):
            rel = fpath.relative_to(CURRENT_DIR) if fpath.is_relative_to(CURRENT_DIR) else fpath
            if not process_all:
                ans = prompt_user(f"[{idx}/{len(files)}] {rel}\n  Entrée/traiter | s/suivant | a/tout | q/quitter > ")
                if ans == "q":
                    print("Arrêt demandé.")
                    return
                if ans == "s":
                    continue
                if ans == "a":
                    process_all = True

            print(f"Traitement: {rel}")
            try:
                result = estimate_walking_parameters(str(fpath))
                if result is not None:
                    print("Résultat:")
                    pprint(result, width=100, compact=True)
                else:
                    print("Aucun résultat (None).")
            except Exception as ex:
                print("Erreur lors du traitement:")
                traceback.print_exc()
            print("-" * 60)

    except KeyboardInterrupt:
        print("\nInterruption utilisateur. Fin.")

if __name__ == "__main__":
    main()