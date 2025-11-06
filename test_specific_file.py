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
    # file = "Data_p031​_TUG1_2.csv"
    # test_estimate_walking_parameters(file)

    # file = "Data_p022​_F8W1.csv"
    # test_estimate_walking_parameters(file)

    # file = "Data_p030​_TUG1.csv"
    # test_estimate_walking_parameters(file)

    # file = "Data_p031​_F8W2_2.csv"
    # test_estimate_walking_parameters(file)

    file = "Data_p003​_TUG2_3.csv"
    test_estimate_walking_parameters(file)

    # file = "Data_pa034​_TUG2.csv"
    # test_estimate_walking_parameters(file)

    # +9
    # file = "Data_p026​_Marche.csv"
    # test_estimate_walking_parameters(file)

    # +3
    file = "Data_p035​_F8W2_3.csv"
    test_estimate_walking_parameters(file, skip_rows=3)