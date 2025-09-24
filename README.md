# WalkingScan

Un projet Unity3D partagé pour le développement collaboratif.

## Configuration du Projet

Ce projet Unity est configuré pour le développement en équipe avec les bonnes pratiques suivantes :

### Prérequis

- **Unity 2022.3 LTS** (Long Term Support recommandé)
- **Git** avec **Git LFS** activé
- Un éditeur de code (Visual Studio, Visual Studio Code, ou Rider)

### Installation et Configuration

1. **Cloner le repository :**
   ```bash
   git clone https://github.com/TheoBens3IL/WalkingScan.git
   cd WalkingScan
   ```

2. **Initialiser Git LFS (si pas déjà fait) :**
   ```bash
   git lfs install
   git lfs pull
   ```

3. **Ouvrir le projet dans Unity :**
   - Lancer Unity Hub
   - Cliquer sur "Open" ou "Add"
   - Sélectionner le dossier du projet WalkingScan

### Structure du Projet

```
WalkingScan/
├── Assets/                 # Ressources Unity
│   ├── Scripts/           # Scripts C#
│   ├── Scenes/            # Scènes Unity
│   ├── Prefabs/           # Prefabs réutilisables
│   ├── Materials/         # Matériaux
│   └── Textures/          # Textures et images
├── ProjectSettings/        # Configuration Unity
├── Packages/              # Packages Unity
├── .gitignore             # Fichiers à ignorer par Git
├── .gitattributes         # Configuration Git LFS
└── README.md              # Documentation
```

### Conventions de Développement

- **Namespace :** Utilisez `WalkingScan` pour tous les scripts
- **Assembly Definitions :** Le projet utilise `WalkingScan.Runtime.asmdef`
- **Nommage :** Utilisez PascalCase pour les classes et méthodes
- **Commentaires :** Documentez les classes publiques avec des commentaires XML

### Git LFS

Le projet utilise Git LFS pour gérer les gros fichiers Unity :
- Textures (.png, .jpg, .psd)
- Modèles 3D (.fbx, .obj, .blend)
- Audio (.wav, .mp3, .ogg)
- Vidéos (.mp4, .mov)
- Archives (.zip, .rar)

### Collaboration

1. **Avant de commencer :**
   ```bash
   git pull origin main
   git lfs pull
   ```

2. **Pour contribuer :**
   - Créer une branche pour votre fonctionnalité
   - Faire vos modifications
   - Tester le projet
   - Faire un commit et push
   - Créer une Pull Request

### Dépannage

- **Problème avec Git LFS :** Vérifiez que Git LFS est installé et configuré
- **Fichiers manquants :** Exécutez `git lfs pull` pour télécharger les gros fichiers
- **Erreurs Unity :** Vérifiez que vous utilisez la bonne version d'Unity (2022.3 LTS)

## Contribution

Ce projet est ouvert aux contributions. Veuillez suivre les conventions de code et tester vos modifications avant de soumettre une Pull Request.