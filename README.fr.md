# DriverControl24

🇬🇧 [English version](README.md) · 🇫🇷 Français

Driver open source pour la console **Digidesign Control|24** (Avid), utilisable comme surface de contrôle de **REAPER** sous Windows et macOS.

Le dépôt contient :

- **le driver** (`DriverReaperControl24`) : dialogue avec la Control24 sur le réseau Ethernet et pilote REAPER (transport, faders, mute/solo, pan, sends, FX, jog wheel, afficheurs…) ;
- **le plugin REAPER** (`ReaperC24Bridge`) : chargé par REAPER, il démarre le driver et lui sert de passerelle vers l'API de REAPER ;
- **l'application de configuration** (`DriverConfig.Web`) : une petite application web locale pour modifier les paramètres du driver sans éditer le fichier `.ini` à la main ;
- les **installeurs macOS** (`.pkg`).

Licence : [GPL v3](LICENSE).

---

## Installation (utilisateurs)

Les installeurs sont dans le dossier [`Setup`](Setup) :

| Dossier | Contenu |
|---|---|
| [`Setup/osX64`](Setup/osX64) | Installeurs macOS Intel (x64). **Disponible.** |
| [`Setup/osARM`](Setup/osARM) | Réservé à macOS Apple Silicon (ARM). *Pas encore disponible.* |
| [`Setup/Win`](Setup/Win) | Installeur Windows. *Pas encore disponible.* |

### macOS (Intel x64)

Téléchargez les trois fichiers de [`Setup/osX64`](Setup/osX64) et installez-les **dans cet ordre** (double-clic sur chacun) :

1. **`Install ChmodBPF.pkg`** : donne au driver l'accès aux périphériques de capture réseau (`/dev/bpf*`) sans passer administrateur. La Control24 communique par trames Ethernet brutes et macOS réserve ces périphériques à root par défaut. **Redémarrez le Mac** (ou fermez puis rouvrez votre session) ensuite.
2. **`DriverReaperControl24-osx-x64-<version>.pkg`** : le driver et les plugins REAPER. Il installe dans `~/Library/Application Support/REAPER/UserPlugins` : `reaper_c24bridge.dylib`, `reaper_c24events.dylib`, le driver `DriverReaperControl24` (runtime .NET embarqué, rien d'autre à installer) et `ReaperDriverControl24.ini` (créé seulement s'il n'existe pas : votre configuration n'est jamais écrasée).
3. **`DriverConfig.Web-osx-x64-<version>.pkg`** *(optionnel)* : l'application `Control24 Config`, installée dans `/Applications`, pour modifier la configuration.

Lancez ensuite REAPER : le driver démarre automatiquement. Les paquets ne sont pas signés : si macOS en bloque un, clic droit → *Ouvrir*.

### Configuration

Le fichier `ReaperDriverControl24.ini` (à côté des plugins) contient notamment :

| Clé (section `[Driver]`) | Rôle |
|---|---|
| `Autostart` | Démarrer le driver automatiquement quand REAPER charge le plugin. |
| `NetworkInterface` | Interface réseau reliée à la Control24. `TestWithoutC24` simule une console (mode test, avec un terminal interactif). |
| `VSDebug` | `True` : REAPER **ne lance pas** le driver, vous le démarrez depuis Visual Studio (voir plus bas). |

**Application de configuration** : `Control24 Config` ouvre une page locale (`http://127.0.0.1:5119`) pour régler ces options et éditer les paramètres FX affichés sur la console (ordre, noms, visibilité).
Pour ajouter des paramètres FX : ajoutez des FX dans REAPER, puis appuyez sur le bouton **EXT TRANS** de la Control24. Les paramètres déjà présents dans le fichier sont conservés, seuls les nouveaux sont ajoutés.

---

## Développeurs : forker et déboguer en direct

### Outils

- **Visual Studio 2022+ (recommandé)** plutôt que VS Code : le débogage .NET (attache, points d'arrêt, évaluation à la volée, edit & continue) y est nettement plus rapide et plus confortable. VS Code fonctionne pour éditer, mais ce n'est pas le meilleur choix pour déboguer le driver.
- **.NET SDK 10**.
- NativeAOT pour le plugin : outils C++ de la plateforme (Visual Studio *Desktop development with C++*, ou Xcode Command Line Tools sur Mac).
- CMake + compilateur C++ pour `NativeReaperPluginDLL` (`reaper_c24events`).

### Démarrage

1. Forkez le dépôt sur GitHub, puis clonez votre fork.
2. Ouvrez `DriverControl24.sln` dans Visual Studio.
3. Compilez le plugin REAPER : `dotnet publish ReaperC24Bridge -r win-x64 -c Release` (ou `-r osx-x64` sur Mac) et copiez `reaper_c24bridge` dans le dossier `UserPlugins` de REAPER (`%APPDATA%\REAPER\UserPlugins` ou `~/Library/Application Support/REAPER/UserPlugins`). Les commandes sont aussi dans `ReaperC24Bridge/build command`.
4. Placez `ReaperDriverControl24.ini` à côté du plugin.

### Déboguer le driver en direct

1. Dans le `.ini`, mettez `Autostart=True` et **`VSDebug=True`**.
2. Lancez REAPER : le plugin charge, ouvre ses pipes de communication mais **ne démarre pas** le driver.
3. Dans Visual Studio, définissez `DriverReaperControl24` comme projet de démarrage et lancez-le (**F5**). Il se connecte aux pipes du plugin. Posez vos points d'arrêt, modifiez, relancez : pas besoin de redémarrer REAPER.
4. Sans console physique, utilisez `NetworkInterface=TestWithoutC24` (une fenêtre console interactive s'ouvre ; sur macOS, le plugin l'ouvre dans Terminal).

Attention : avec `VSDebug=False`, le driver est lancé par REAPER et verrouille ses fichiers ; fermez-le avant de redéployer depuis Visual Studio. Vérifiez aussi le dossier de sortie du projet `DriverReaperControl24` : il peut écrire directement dans le dossier `UserPlugins` de REAPER.

### Construire les installeurs macOS

```bash
./DriverReaperControl24/Setup/build-osx-x64.sh [version]   # driver + plugins REAPER
./DriverConfig.Web/Setup/build-osx-x64.sh [version]        # application de configuration
```

Installeurs reconstruits : copiez les `.pkg` obtenus (dossiers `Setup/out`) dans `Setup/osX64`.

---

## Architecture

```
 Control24  ⇄  Ethernet (pcap)  ⇄  DriverReaperControl24  ⇄  pipes  ⇄  reaper_c24bridge  ⇄  API REAPER
                                          │                                   │
                                          └── ReaperDriverControl24.ini ──────┘
                                                      ▲
                                              DriverConfig.Web
```

| Projet | Rôle |
|---|---|
| `DriverReaperControl24` | Le driver (processus .NET). Protocole Control24 (`Control24.vb`), logique de contrôle REAPER (`C24ReaperController.vb`), accès REAPER (`Reaper.vb`), point d'entrée (`Main.vb`). |
| `ReaperC24Bridge` | Point d'entrée du plugin REAPER (C#, NativeAOT → `reaper_c24bridge`). |
| `DriverControl24.Extension` | Logique du plugin : appels à l'API REAPER (`ReaperAPI.vb`), serveurs de pipes, démarrage/arrêt du driver (`ReaperBridge.vb`). |
| `NativeReaperPluginDLL` | Petit plugin natif C++ (`reaper_c24events`) pour les événements REAPER. |
| `DriverConfig` | Bibliothèque partagée : lecture/écriture du `.ini`, configuration de session, nommage des FX. |
| `DriverConfig.Web` | Application de configuration (Blazor Server, écoute uniquement sur `127.0.0.1`). |
| `DriverControl24` | Application WinForms (Reacontrol24) : outils réseau et tests de la console. |
| `Launcher` | Lanceur. |
| `*.Tests` | Tests unitaires (MSTest). |

### Pourquoi du VB.NET ?

Le code du driver est en **VB.NET**, un choix assumé : le langage est très verbeux, donc très lisible, et j'aime ça. Si vous préférez le C#, rien de bloquant : VB.NET et C# compilent vers le même IL et utilisent la même bibliothèque .NET, donc demander à une IA (ou à un convertisseur) de tout convertir en C# donne un résultat équivalent. Les projets C# existants (`ReaperC24Bridge`, `DriverConfig.Web`) interagissent déjà sans problème avec les projets VB.

---

## Contribuer

Toute demande d'évolution et toute correction de bug sont les bienvenues :

- **Une idée, une fonctionnalité, un bug ?** Ouvrez une **issue** dans l'onglet *Issues* du dépôt (version de REAPER, système, étapes pour reproduire, extrait de log si possible).
- **Vous voulez corriger ou développer vous-même ?** Forkez, créez une branche, puis ouvrez une **pull request**. Les contributeurs sont les bienvenus : n'hésitez pas à devenir contributeur du projet.
