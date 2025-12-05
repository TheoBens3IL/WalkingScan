from estimate_walking_parameters import estimate_walking_parameters
from utils import resolve_path

def test_estimate_walking_parameters(filename, skip_rows=0):
    """
    Teste la fonction estimate_walking_parameters sur un fichier spécifique.
    """
    file_path = resolve_path(filename)
    try:
        result = estimate_walking_parameters(str(file_path), plot=True, print_results=True, skip_rows=skip_rows)
        print(f"Résultats pour {filename}:")
        for key, value in result.items():
            print(f"  {key}: {value}")
    except Exception as e:
        print(f"Erreur lors du traitement de {filename}: {e}")


if __name__ == "__main__":

    file = "Data_pa010​_TUG1_2.csv"
    test_estimate_walking_parameters(file)