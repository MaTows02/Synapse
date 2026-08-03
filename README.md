# Synapse by MaTows

**Synapse** est un centre de contrôle et d’optimisation pour Windows 10 et 11, conçu en français par défaut. Le projet réunit la télémétrie temps réel, l’inventaire matériel, l’optimisation des jeux, la maintenance et les réglages Windows dans une application WinUI 3.

[Documentation](docs/CONTROL_CENTER.md) · [Signaler un problème](https://github.com/MaTows02/Synapse/issues/new/choose) · [Contribuer](CONTRIBUTING.md) · [Soutenir le projet](SUPPORT.md)

> Synapse modifie certains paramètres système. Les opérations sensibles affichent une confirmation et peuvent créer un point de restauration. Testez d’abord les changements sur une machine non critique.

## Fonctions principales

### Tableau de bord temps réel

- Charge CPU, GPU, mémoire et disque, débit réseau et température ACPI lorsqu’elle est exposée.
- Détection automatique des ventilateurs, pompes, hubs ARGB et contrôleurs de refroidissement publiés par ACPI, USB, HID et Plug & Play.
- Architecture d’adaptateurs pour les contrôleurs de ventilateurs et RGB compatibles.
- Profils de contrôle persistants, réappliqués au démarrage de Synapse.

Windows ne fournit pas d’API universelle permettant d’écrire sur tous les ventilateurs ou systèmes RGB. Synapse reste donc en lecture seule tant qu’un adaptateur constructeur sûr n’est pas disponible, au lieu de simuler un contrôle matériel.

### Jeux et Game Booster

- Détection des bibliothèques Steam, Epic et des jeux enregistrés dans Windows.
- Profils par jeu enregistrés localement.
- Passage optionnel du jeu en priorité haute.
- Passage temporaire au plan hautes performances et blocage de la mise en veille, avec restauration à la fermeture du jeu.
- Demande contrôlée d’une résolution de timer à 0,5 ms.
- Suspension uniquement des processus explicitement cochés par l’utilisateur.
- Liste interne de processus Windows critiques qui ne peuvent jamais être suspendus.
- Reprise automatique des processus à la fermeture du jeu.
- Adaptateurs de réglages vérifiés pour Apex Legends et Grand Theft Auto V, avec sauvegarde et restauration des fichiers de configuration.

### Informations matérielles

- CPU, GPU, mémoire, stockage, carte mère et BIOS/UEFI avec constructeur, modèle, firmware et pilotes lorsque disponibles.
- Masquage automatique des numéros de série pour les captures d’écran.
- État de Secure Boot, HVCI, virtualisation, TPM et profil mémoire.
- Indication honnête « état inconnu » pour Resizable BAR, Above 4G et les fonctions non standardisées par Windows.

### Nettoyage et désinstallation profonde

- 13 catégories de nettoyage : fichiers temporaires, caches navigateurs, Windows Update, Prefetch, caches de polices, rapports d’erreurs, miniatures, shaders DirectX, corbeille, journaux et dumps.
- Analyse avant suppression et estimation de l’espace récupérable.
- Confirmation explicite et point de restauration avant nettoyage.
- Analyse des résidus d’une application avant désinstallation.
- Suppression limitée aux résidus classés sûrs ; les clés ambiguës restent en contrôle manuel.

### Santé système

- 21 contrôles couvrant stockage, SMART, système de fichiers, services, sécurité, pilotes, réseau, température, charge et stabilité.
- Résultats classés en sain, avertissement, critique, non applicable ou inconnu.
- Score de santé et filtres par catégorie ou gravité.

### Optimisations Windows

- Réglages de confidentialité, performances, alimentation, mises à jour et personnalisation.
- Bouclier de télémétrie basé sur des stratégies Windows documentées et réversibles.
- Import/export au format `.synapse`.
- Génération de configurations et de scripts Autounattend.

## Compilation

Prérequis :

- Windows 10 ou 11 x64 ;
- Visual Studio 2022 ou ultérieur avec les charges de travail « Développement Desktop .NET » et « Développement Desktop en C++ » ;
- SDK .NET `10.0.302` ;
- Inno Setup 6 uniquement pour produire l’installateur.

Dans PowerShell :

```powershell
dotnet restore .\Synapse.sln
dotnet test .\Synapse.sln -c Release
.\extras\build-and-package.ps1 -SkipTests:$false
```

Le projet cible `net10.0-windows10.0.19041.0` et produit une application x64 autonome.

## Structure

- `src/Synapse.Core` : modèles, contrats et définitions de réglages.
- `src/Synapse.Infrastructure` : accès Windows, WMI, registre, processus et persistance.
- `src/Synapse.UI` : application WinUI 3 et interface française.
- `tests` : tests unitaires et d’intégration.
- `extras` : scripts de développement et création de l’installateur.

## Identité

Nom du produit : **Synapse**  
Créateur : **MaTows**  
Signature : **Synapse by MaTows**

## Licence et redistribution

Les contributions propres à **Synapse by MaTows** sont distribuées sous licence [MIT](LICENSE.txt). Toute redistribution doit conserver le fichier `LICENSE.txt` ainsi que les licences et mentions applicables aux composants tiers.

Synapse est un projet indépendant, sans affiliation avec Microsoft. Windows est une marque déposée de Microsoft Corporation.
