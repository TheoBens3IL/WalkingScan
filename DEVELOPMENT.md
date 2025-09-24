# Guide de Développement - WalkingScan

## Démarrage Rapide

1. **Première ouverture :**
   - Unity va importer tous les packages (cela peut prendre quelques minutes)
   - Les erreurs de compilation initiales sont normales et se résoudront après l'import

2. **Création d'une nouvelle scène :**
   - Fichier > New Scene
   - Sauvegarder dans `Assets/Scenes/`

3. **Ajout de scripts :**
   - Créer les scripts dans `Assets/Scripts/`
   - Utiliser le namespace `WalkingScan`
   - Les Assembly Definitions sont déjà configurées

## Structure Recommandée

### Scripts
- `Assets/Scripts/` : Tous les scripts C#
- Utilisez des dossiers par fonctionnalité (ex: `Player/`, `UI/`, `Enemies/`)

### Assets
- `Assets/Prefabs/` : GameObjects réutilisables
- `Assets/Materials/` : Matériaux pour les renderers
- `Assets/Textures/` : Images et textures
- `Assets/Scenes/` : Scènes du jeu

### Bonnes Pratiques

1. **Nommage :**
   - Classes : PascalCase (`PlayerController`)
   - Variables privées : camelCase (`currentHealth`)
   - Variables publiques : PascalCase (`MaxHealth`)
   - Constantes : UPPER_CASE (`MAX_PLAYER_SPEED`)

2. **Organisation :**
   - Un script par fichier
   - Grouper les fonctionnalités similaires
   - Utiliser des régions pour organiser le code

3. **Performance :**
   - Évitez les appels coûteux dans Update()
   - Utilisez des pools d'objets pour les objets fréquents
   - Cachéz les références aux components

## Collaboration

- Toujours tester avant de push
- Créer des branches pour les nouvelles fonctionnalités
- Documenter les changements importants
- Utiliser des messages de commit descriptifs

## Dépannage Commun

- **Erreur "Assembly not found"** : Recompiler les scripts
- **Assets manquants** : `git lfs pull`
- **Erreur de version Unity** : Vérifier ProjectVersion.txt