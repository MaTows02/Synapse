namespace Synapse.UI.Features.SoftwareApps.Services;

/// <summary>
/// French presentation layer for the upstream software catalog. Definitions stay stable for
/// import/export while the UI never falls back to an English sentence in French mode.
/// </summary>
internal static class FrenchAppDescriptionTranslator
{
    private static readonly IReadOnlyDictionary<string, string> Exact = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Privacy-focused browser with built-in ad blocking"] = "Navigateur axé sur la confidentialité avec bloqueur de publicités intégré.",
        ["WebView2 runtime for Windows applications"] = "Environnement WebView2 utilisé par les applications Windows.",
        ["Browser with sidebar tabs and Spaces, focused on design"] = "Navigateur au design soigné avec onglets latéraux et espaces de travail.",
        ["Google's web browser with sync and extension support"] = "Navigateur Google avec synchronisation et prise en charge des extensions.",
        ["Open source Chromium-based browser built on ungoogled-chromium with uBlock Origin"] = "Navigateur Chromium open source sans services Google, avec uBlock Origin.",
        ["Popular web browser known for privacy and customization"] = "Navigateur populaire reconnu pour sa confidentialité et sa personnalisation.",
        ["Privacy-focused browser designed to minimize tracking and fingerprints"] = "Navigateur conçu pour limiter le pistage et l’empreinte numérique.",
        ["Feature-rich web browser with built-in VPN and ad blocker"] = "Navigateur complet avec VPN et bloqueur de publicités intégrés.",
        ["Gaming-oriented version of Opera with unique features"] = "Version d’Opera pensée pour le jeu, avec des fonctions exclusives.",
        ["Privacy-focused browser that routes traffic through the Tor network"] = "Navigateur confidentiel acheminant le trafic par le réseau Tor.",
        ["Highly customizable browser with a focus on user control"] = "Navigateur très personnalisable privilégiant le contrôle de l’utilisateur.",
        ["Firefox-based browser with a focus on privacy and customization"] = "Navigateur basé sur Firefox, axé sur la confidentialité et la personnalisation.",
        ["Firefox-based browser with workspaces, split tabs, and a clean UI"] = "Navigateur basé sur Firefox avec espaces de travail, onglets divisés et interface épurée.",
        ["Compiler optimized, private Firefox fork"] = "Version privée de Firefox optimisée à la compilation.",
        ["Open Source, Goanna-based web browser focusing on efficiency and customization"] = "Navigateur open source basé sur Goanna, efficace et personnalisable.",
        ["Privacy focused browser with built-in ad blocking and VPN"] = "Navigateur confidentiel avec bloqueur de publicités et VPN intégrés.",
        ["Privacy focused browser with strong tracking protection"] = "Navigateur axé sur la confidentialité avec protection renforcée contre le pistage.",
        ["Privacy-focused web browser with built-in tracker blocking"] = "Navigateur confidentiel avec blocage intégré des traqueurs.",
        ["Open-source file archiver with a high compression ratio"] = "Archiveur open source offrant un taux de compression élevé.",
        ["File archiver with a high compression ratio"] = "Archiveur de fichiers offrant un taux de compression élevé.",
        ["Free file archiver utility. Open and extract RAR, TAR, ZIP files and more"] = "Archiveur gratuit pour ouvrir et extraire les formats RAR, TAR, ZIP et plus encore.",
        ["Open source fork of 7-zip intended for the modern Windows experience"] = "Version open source de 7-Zip adaptée à l’expérience Windows moderne.",
        ["Windows context menu customization tool"] = "Outil de personnalisation du menu contextuel de Windows.",
        ["Windows 11 Start menu and taskbar customization"] = "Personnalisation du menu Démarrer et de la barre des tâches de Windows 11.",
        ["Windows 10 Start menu and taskbar customization"] = "Personnalisation du menu Démarrer et de la barre des tâches de Windows 10.",
        ["Classic style Start Menu for Windows"] = "Menu Démarrer de style classique pour Windows.",
        ["Customization platform for Windows"] = "Plateforme de personnalisation pour Windows.",
        ["Free and open-source animated desktop wallpaper application"] = "Application gratuite et open source de fonds d’écran animés.",
        ["Open-source brightness controls for one or more external monitors using DDC/CI"] = "Contrôle open source de la luminosité d’un ou plusieurs écrans externes via DDC/CI, sans utiliser leurs boutons physiques.",
        ["Open-source alternative to Wallpaper Engine for animated desktop backgrounds"] = "Alternative open source à Wallpaper Engine pour les fonds d’écran animés.",
        ["Desktop customization tool for Windows"] = "Outil de personnalisation du Bureau Windows.",
        ["Utility that enhances the Windows Explorer experience"] = "Utilitaire améliorant l’expérience de l’Explorateur Windows.",
        ["Automatically changes your desktop wallpaper at regular intervals"] = "Change automatiquement le fond d’écran à intervalles réguliers.",
        ["Set of utilities for power users to tune and streamline their Windows experience"] = "Ensemble d’utilitaires pour régler et simplifier Windows.",
        ["The advanced docking system for Windows"] = "Système avancé de dock pour Windows.",
        ["Free macro-creation and automation scripting language (v2, current)"] = "Langage gratuit de création de macros et d’automatisation (v2 actuelle).",
        ["Legacy branch of AutoHotkey (v1.1) for older scripts"] = "Ancienne branche d’AutoHotkey (v1.1) destinée aux scripts historiques.",
        ["Valve's PC gaming storefront, library, and social platform"] = "Boutique, bibliothèque de jeux PC et plateforme sociale de Valve.",
        ["Epic's PC game store and launcher with weekly free games"] = "Boutique et lanceur Epic avec des jeux gratuits chaque semaine.",
        ["DRM-free PC game store and library manager from CD Projekt"] = "Boutique sans DRM et gestionnaire de bibliothèque de CD Projekt.",
        ["Open-source video game library manager with support for multiple game stores"] = "Gestionnaire open source regroupant plusieurs bibliothèques de jeux.",
        ["Cloud storage and file synchronization service"] = "Service de stockage cloud et de synchronisation de fichiers.",
        ["Free and open-source office suite"] = "Suite bureautique gratuite et open source.",
        ["Microsoft Office productivity suite"] = "Suite de productivité Microsoft Office.",
        ["Open-source multimedia player and framework"] = "Lecteur et environnement multimédia open source.",
        ["Free and open source software for video recording and live streaming"] = "Logiciel gratuit et open source d’enregistrement vidéo et de diffusion en direct.",
        ["Anti-malware software for Windows"] = "Logiciel de protection contre les programmes malveillants pour Windows.",
        ["Adware removal tool for Windows"] = "Outil de suppression des logiciels publicitaires pour Windows.",
        ["Secure messaging app with end-to-end encryption"] = "Messagerie sécurisée avec chiffrement de bout en bout.",
        ["Distributed version control system"] = "Système distribué de gestion de versions.",
        ["Microsoft's code editor with extensions, debugging, and Git integration"] = "Éditeur de code Microsoft avec extensions, débogage et intégration Git.",
        ["Python programming language"] = "Langage de programmation Python.",
        ["Remote desktop software for remote access and support"] = "Logiciel de bureau à distance pour l’accès et l’assistance.",
        ["Free SSH and telnet client"] = "Client SSH et Telnet gratuit.",
        ["Open source RGB lighting control software"] = "Logiciel open source de contrôle de l’éclairage RGB.",
        ["Hard drive health monitoring utility"] = "Utilitaire de surveillance de l’état des disques.",
        ["Disk space analyzer with extremely fast scanning"] = "Analyseur d’espace disque à balayage très rapide.",
        ["Uninstaller that scans for and removes leftover files and registry entries"] = "Désinstalleur recherchant et supprimant les fichiers et clés de registre résiduels."
    };

    public static string Translate(string description, string groupName)
    {
        if (Exact.TryGetValue(description, out var translated)) return translated;
        if (description.StartsWith(".NET Runtime ", StringComparison.Ordinal))
        {
            var version = description[".NET Runtime ".Length..]
                .Replace(" for running applications", string.Empty, StringComparison.Ordinal);
            return $"Environnement d’exécution .NET {version} pour les applications Windows.";
        }
        if (description.StartsWith("Visual C++ ", StringComparison.Ordinal))
        {
            var version = description.Replace(" runtime components", string.Empty, StringComparison.Ordinal);
            return $"Composants d’exécution {version} requis par certains logiciels.";
        }

        return groupName switch
        {
            "Browsers" => "Navigateur web offrant des fonctions de navigation, de confidentialité et de personnalisation.",
            "Compression" => "Outil de compression, d’ouverture et d’extraction d’archives.",
            "Customization Utilities" => "Utilitaire de personnalisation de l’expérience Windows.",
            "Development Apps" => "Outil destiné au développement, au code ou à l’automatisation.",
            "Document Viewers" => "Application de création, de lecture ou de gestion de documents.",
            "File & Disk Management" => "Utilitaire de gestion des fichiers, des disques ou du stockage.",
            "Gaming" => "Application liée aux jeux, aux bibliothèques ou aux lanceurs PC.",
            "Imaging" => "Application d’affichage, de conversion ou de retouche d’images.",
            "Messaging, Email & Calendar" => "Application de communication, de messagerie ou d’organisation.",
            "Multimedia (Audio & Video)" => "Application de lecture, de création ou de conversion audio et vidéo.",
            "Online Storage & Backup" => "Service de stockage, de synchronisation ou de sauvegarde.",
            "Optical Disc Tools" => "Utilitaire de lecture, de gravure ou de création de disques.",
            "Privacy & Security" => "Outil de confidentialité, de protection ou de sécurité Windows.",
            "Remote Access" => "Outil d’accès, de contrôle ou d’assistance à distance.",
            "Runtimes & Dependencies" => "Composant d’exécution requis par certaines applications Windows.",
            _ => "Utilitaire Windows destiné à simplifier ou compléter les fonctions du système."
        };
    }
}
