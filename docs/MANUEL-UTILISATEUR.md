# Manuel d'utilisation — DriverControl24 pour REAPER

🇬🇧 [English version](USER-MANUAL.md) · 🇫🇷 Français

Ce manuel décrit **tout ce que le driver fait avec la surface Digidesign Control|24 dans REAPER** : quel bouton, quel fader ou quel encodeur commande quoi.

Il complète le *Guide de la surface Control 24* d'origine (conçu pour Pro Tools) : les noms des boutons sont ceux imprimés sur la console. **Seules les fonctions décrites ici sont gérées par le driver** ; un bouton qui n'est pas cité n'a pas d'action dans REAPER.

> Installation, configuration du fichier `.ini` et débogage : voir le [README](../README.fr.md).

---

## Sommaire

1. [Principes de base](#1-principes-de-base)
2. [Vue Home : faders, encodeurs, mute, solo, sélection](#2-vue-home--faders-encodeurs-mute-solo-sélection)
3. [Navigation entre les pistes (banques)](#3-navigation-entre-les-pistes-banques)
4. [Enregistrement et monitoring](#4-enregistrement-et-monitoring)
5. [Transport](#5-transport)
6. [Départs (sends)](#6-départs-sends)
7. [Vue Pan/Send d'une piste](#7-vue-pansend-dune-piste)
8. [Inserts et plugins](#8-inserts-et-plugins)
9. [Paramètres d'un plugin](#9-paramètres-dun-plugin)
10. [Entrées, sorties et Master](#10-entrées-sorties-et-master)
11. [Automation](#11-automation)
12. [Molette (jog), navigation et zoom](#12-molette-jog-navigation-et-zoom)
13. [Outils d'édition](#13-outils-dédition)
14. [Pavé numérique, marqueurs et régions](#14-pavé-numérique-marqueurs-et-régions)
15. [Groupes, fenêtres, annuler et sauvegarder](#15-groupes-fenêtres-annuler-et-sauvegarder)
16. [Afficheurs, compteur et VU-mètres](#16-afficheurs-compteur-et-vu-mètres)
17. [Configuration du driver](#17-configuration-du-driver)
18. [Dépannage](#18-dépannage)
19. [Aide-mémoire des actions REAPER utilisées](#19-aide-mémoire-des-actions-reaper-utilisées)

---

## 1. Principes de base

### 1.1 Comment ça marche

Le driver relie la Control|24 (par réseau Ethernet) à REAPER :

- les **faders**, **encodeurs** et **boutons** de la console pilotent REAPER ;
- les **LED**, **afficheurs** (noms de pistes, valeurs), **VU-mètres** et **compteur** de la console reflètent l'état de REAPER, y compris quand vous agissez à la souris.

Le driver démarre automatiquement avec REAPER (réglage `Autostart` du fichier `.ini`).

### 1.2 Les touches de modification

Quatre boutons de la section « PROTOOLS SHORTCUTS » servent de modificateurs. Ce manuel les note ainsi :

| Dans ce manuel | Bouton sur la console |
|---|---|
| **SHIFT** | SHIFT / ADD |
| **OPT** | OPT (ALT) / ALL |
| **CTL** | CTL / CLUTCH |
| **CTRL (⌘)** | le bouton « (CTRL) » ⌘ |

Un modificateur se **maintient enfoncé** pendant l'action (bouton + modificateur), il ne se « verrouille » pas.

### 1.3 Rappel des termes

- **Strip** : une des 24 voies (fader, encodeur, afficheur 4 caractères, boutons SELECT / SOLO / MUTE / REC ARM…).
- **Encodeur (vpot)** : le potentiomètre rotatif sous chaque afficheur ; il a aussi un bouton-poussoir (SWITCH ACTIVE).
- **Banque / page** : un groupe de 24 pistes affiché sur la console.
- **Vue** : ce que représentent les 24 strips à un instant donné (pistes, départs, inserts, paramètres d'un plugin…).
- **LED fixe / clignotante** : l'état est « actif » (fixe) ou « partiel/en attente » (clignotante), selon la fonction.
- **Message temporaire** : certaines actions affichent un court texte sur les afficheurs ; la console revient ensuite à l'affichage normal.

### 1.4 Retour à la normale

- **ESC/CANCEL** (section principale, au-dessus de SCRUB/SHUTTLE) envoie la touche **Échap** à REAPER.
- Dans les vues Départs, Inserts, Entrées/Sorties, le bouton **ASSIGN** et les **ESC/CANCEL de la section Assignment** servent à valider ou annuler (voir §6, §8, §10).

---

## 2. Vue Home : faders, encodeurs, mute, solo, sélection

La vue **Home** est la vue de démarrage : les 24 strips représentent 24 pistes REAPER consécutives.

| Élément | Action |
|---|---|
| **Fader** (motorisé) | Volume de la piste, en dB. Suit aussi les changements faits dans REAPER. |
| **Afficheur du strip** | Nom de la piste. |
| **Encodeur** | Panoramique de la piste. Un anneau de 15 LED indique la position. |
| **MUTE** | Mute / démute la piste. Si la piste est en automation Touch/Write/Latch, le mute est écrit dans l'enveloppe de mute. |
| **SOLO** | Solo in place de la piste (additif). |
| **SELECT** | Sélectionne / désélectionne la piste. **DEFAULT + SELECT** remet le fader de cette piste à **0 dB**. |
| **AUTO** | Mode d'automation de la piste (voir §11). |
| **VU-mètre** | Niveau de la piste (voir §16). |

### FLIP

**FLIP** échange le rôle des faders et des encodeurs : les faders pilotent le panoramique et les encodeurs le volume. Le bouton clignote tant que FLIP est actif ; **FLIP** ou **ESC/CANCEL (Assignment)** pour en sortir. FLIP existe aussi dans les vues Départs et Paramètres plugin (voir plus bas).

### SOLO SAFE et SOLO CLEAR

- **SOLO CLEAR** : désactive tous les solos du projet (les pistes en « solo safe » gardent leur état de protection). La LED clignote tant qu'au moins une piste est en solo.
- **SOLO SAFE** : bouton clignotant = mode « solo safe » actif. Appuyez sur **SOLO** d'une ou plusieurs pistes pour les protéger / déprotéger (une piste « solo safe » n'est pas coupée quand d'autres pistes sont en solo). Appuyez de nouveau sur **SOLO SAFE** pour revenir au solo normal.

---

## 3. Navigation entre les pistes (banques)

| Bouton | Action |
|---|---|
| **BANK LEFT / BANK RIGHT** | Page précédente / suivante : les 24 pistes suivantes (ou précédentes) du projet. Actifs dans les vues Home, Sends et Paramètres plugin. |
| **NUDGE** (maintenu) | Passe la molette en « sélection de piste » : tournez la molette pour sélectionner la piste précédente/suivante dans REAPER. **Au relâchement**, la console saute à la page contenant la première piste sélectionnée. |
| **F1** | LED allumée : le mixer de REAPER suit les banques (il défile jusqu'à la première piste affichée). Mémorisé dans le projet. |
| **F2** | LED allumée : à chaque changement de banque, la première piste de la banque est sélectionnée et la vue Arrange la montre. Mémorisé dans le projet. |

---

## 4. Enregistrement et monitoring

| Bouton | Action |
|---|---|
| **REC ARM** (par strip) | Arme / désarme la piste pour l'enregistrement. LED **clignotante** : armée ; LED **fixe** : armée et REAPER enregistre. |
| **MASTER REC** | Clignote quand au moins une piste est armée. Si clignotant : **désarme toutes les pistes**. Sinon : **arme toutes les pistes** ayant une entrée d'enregistrement définie (les pistes sans entrée sont ignorées). |
| **REC SAFE** | Active le mode « record safe » : les boutons **REC ARM** des strips deviennent des interrupteurs de protection (la piste passe en mode d'enregistrement « désactivé » dans REAPER ; la LED reste allumée). Appuyez de nouveau sur **REC SAFE** pour revenir au mode normal. |
| **MONITOR** | Fait défiler trois états de monitoring d'entrée pour tout le projet (état mémorisé dans le projet) : <br>• LED éteinte : monitoring désactivé (les pistes armées sont désarmées temporairement) ; <br>• LED fixe : monitoring d'entrée activé ; <br>• LED clignotante : monitoring « automatique » (type magnétophone). <br>En revenant à fixe/clignotant, les pistes précédemment armées sont réarmées. |

### Quick Punch (enregistrement en insertion)

Trois modes d'enregistrement, indiqués par la LED de **QUICK PUNCH** :

| LED QUICK PUNCH | Mode |
|---|---|
| Éteinte | Enregistrement normal |
| Fixe | Auto-punch dans la **sélection temporelle** |
| Clignotante | Auto-punch dans les **items sélectionnés** |

- **QUICK PUNCH** : bascule entre enregistrement normal et auto-punch.
- **CAPTURE** : quand l'auto-punch est actif, passe entre « sélection » et « items ».

Le voyant suit aussi les changements faits dans REAPER.

---

## 5. Transport

### 5.1 Lecture, enregistrement, arrêt

| Bouton | Action |
|---|---|
| **PLAY** | Lecture ; appuyer pendant la lecture met en **pause**, appuyer en pause reprend. |
| **REC** puis **PLAY** | Appuyez sur **REC** et **PLAY** ensemble pour lancer l'enregistrement. Appuyer sur PLAY pendant l'enregistrement le met en pause. |
| **STOP** | Arrêt. |
| **FWD / REW** (maintenus) | Avance / retour rapide tant que le bouton est maintenu. |
| **GO TO START / GO TO END** | Curseur au début / à la fin du projet. |
| **IN / OUT** | Place le début / la fin de la **sélection temporelle** à la position du curseur. Les LED indiquent si le début / la fin existent. |
| **LOOP PLAY** | Active / désactive la lecture en boucle. |
| **LOOP REC** | Mémorise que l'enregistrement doit se faire en boucle : au moment où vous lancez l'enregistrement, la boucle est activée ; elle est restaurée à l'arrêt. |

Les LED PLAY / STOP / REC suivent l'état réel de REAPER (PLAY clignote en pause).

### 5.2 FWD / REW avec modificateurs : marqueurs et régions

Ces actions s'exécutent **au relâchement** de FWD ou REW :

| Combinaison | Action |
|---|---|
| **OPT + FWD / REW** | Curseur sur le **marqueur** suivant / précédent. |
| **OPT + CTL + FWD / REW** | Pose le début de sélection au curseur, va au marqueur suivant/précédent et y pose la fin de sélection : vous sélectionnez **l'intervalle entre deux marqueurs**. |
| **CTRL (⌘) + FWD / REW** | Curseur au **début de la région** suivante / précédente. |
| **CTRL (⌘) + SHIFT + FWD / REW** | Curseur à la **fin** de la région suivante / précédente. |
| **CTRL (⌘) + CTL + FWD / REW** | Va à la région suivante / précédente **et la sélectionne** (sélection temporelle = la région). Au relâchement de CTL la sélection est conservée. |

### 5.3 Pré-roll et post-roll

**PRE ROLL** et **POST ROLL** ajoutent un décompte avant et une durée après la sélection temporelle.

| Action | Résultat |
|---|---|
| **PRE ROLL** / **POST ROLL** | Active / désactive la fonction (LED allumée = active). |
| **SHIFT + PRE ROLL** (ou POST ROLL) | **Réglage de la durée** : le bouton clignote, une sélection de la durée du pré-roll (ou post-roll) apparaît et la molette sert à la modifier. Appuyez de nouveau sur le bouton pour **valider** : la durée est mémorisée dans le projet (2 s par défaut) et la fonction s'active. |

Avec le pré-roll/post-roll actif et une **sélection temporelle** existante, **PLAY** démarre `durée du pré-roll` avant le début de la sélection et la lecture s'arrête `durée du post-roll` après la fin.

### 5.4 EXT TRANS — générer les paramètres de plugins

**EXT TRANS** écrit les sections de paramètres FX dans `ReaperDriverControl24.ini` (message « Writing FxParams sections… »). À utiliser pour **ajouter** les paramètres de nouveaux plugins ; les paramètres déjà présents dans le fichier sont conservés. Voir §9 et §17.

---

## 6. Départs (sends)

Les 5 boutons **SEND A/F, B/G, C/H, D/I, E/J** affichent un départ donné de **toutes les pistes** à la fois.

| Action | Résultat |
|---|---|
| **SEND A/F … E/J** | Affiche le départ **A … E** sur toutes les pistes (vue Sends). Appuyer de nouveau revient à la vue Home. |
| **SHIFT + SEND …** | Affiche le départ **F … J** (seconde page de 5 départs). |
| **CTRL (⌘) + SEND …** | Réinitialise l'ordre du départ A … E sur toutes les pistes. **CTRL + SHIFT + SEND …** : départs F … J. (Message « Send X are reset for all tracks. ») |

Dans la vue Sends :

- **Encodeur** : niveau du départ ; **fader** : volume de la piste.
- **FLIP** : les faders pilotent le niveau du départ, les encodeurs son panoramique.
- **PRE/POST** : bascule le départ en pré- ou post-fader (affichage « Pre » / « Post »).
- **SEND MUTE** : mode coupure des départs (les boutons agissent sur le mute du départ). En mode FLIP, c'est le bouton **MUTE** de chaque strip qui coupe le départ.
- **ASSIGN** : modifie la destination des départs (voir ci-dessous).

### Assigner un départ (ASSIGN)

1. Appuyez sur **ASSIGN** (le bouton et les ESC/CANCEL de la section Assignment clignotent).
2. Tournez l'encodeur d'un strip pour parcourir la liste des destinations disponibles.
3. Confirmez avec **ASSIGN** ; **ESC/CANCEL (Assignment)** annule sans rien modifier.

---

## 7. Vue Pan/Send d'une piste

Le bouton **PAN/SEND** d'un strip ouvre la vue détaillée de **cette piste** : le strip montre le panoramique (« L<>R »), puis, par départ : nom du départ, nom de la destination, niveau et état pré/post. Les départs sont présentés par **pages de 5** (A–E puis F–J), les boutons **CHANNEL BAR SCROLL** (flèches de part et d'autre du bandeau) changent de page. Appuyez de nouveau sur **PAN/SEND** pour revenir.

Mêmes principes que §6 pour le niveau, le mute et l'assignation des départs.

---

## 8. Inserts et plugins

### 8.1 EQ, DYN et INSERTS

Chaque strip a trois boutons : **EQ**, **DYN** et **INSERTS**. Leurs LED montrent la présence de plugins de ce type sur la piste.

| Bouton | Action |
|---|---|
| **EQ** | Ouvre les paramètres du **premier plugin d'égalisation** de la piste (appuyer à nouveau : plugin d'EQ suivant). |
| **DYN** | Idem pour les plugins de **dynamique**. |
| **INSERTS** | Ouvre la **vue Inserts** de la piste (§8.2). |
| **EDIT/BYP** (général) | LED allumée : **EQ** et **DYN** deviennent des boutons de **bypass** (tous les plugins de ce type de la piste ; LED fixe = tout en bypass, clignotante = bypass partiel). |
| **INS/BYP** (général) | Idem pour **INSERTS** (tous les plugins de la piste). |

Les plugins considérés comme « EQ » ou « DYN » sont définis dans le fichier de configuration (sections `[EQ]` et `[DYN]`, §17).

### 8.2 Vue Inserts

La vue Inserts d'une piste affiche **8 emplacements** de plugin par page (**2 pages**, soit 16 plugins), chacun occupant 3 strips. Utilisez les boutons **CHANNEL BAR SCROLL** pour changer de page.

- Le nom court de chaque plugin (8 caractères, modifiable dans l'application de configuration) est affiché ; **en minuscules : actif, en MAJUSCULES : en bypass** ; « NoInsert » : emplacement vide.
- **Premier bouton du bloc** : sélectionne le plugin ; **appuyer à nouveau** sur le plugin déjà sélectionné ouvre ses **paramètres** (§9).
- **Deuxième bouton du bloc** : bypass du plugin sélectionné (« Byp: On » / « Byp: Off »).
- **MASTER BYPASS** : active / bypass **tous** les plugins de la piste.

#### Ajouter, remplacer ou supprimer un plugin (ASSIGN)

1. Appuyez sur **ASSIGN**.
2. Tournez l'encodeur d'un emplacement pour parcourir la liste des plugins (« NoInsert » + plugins marqués *visibles* dans la configuration + plugins déjà présents) ; appuyez sur le bouton de l'encodeur pour choisir.
3. **ASSIGN** de nouveau : les modifications sont **appliquées dans REAPER** (plugins retirés / ajoutés). **ESC/CANCEL (Assignment)** : annule.

### 8.3 Copier / coller des réglages

- **COPY SETTINGS FROM CHANNEL** : copie les paramètres du plugin sélectionné (la LED clignote).
- **PASTE SETTING TO CHANNEL** : colle dans un **plugin identique** sélectionné. Sinon : message « Not same Fx choose to paste settings ! ».

---

## 9. Paramètres d'un plugin

Quand un plugin est ouvert (depuis EQ, DYN ou INSERTS), les 24 strips affichent ses **paramètres**, par pages de 24. La fenêtre du plugin s'ouvre dans REAPER ; **PLUG-IN** (section WINDOW) la masque / l'affiche.

| Élément | Action |
|---|---|
| **Encodeur** | Valeur du paramètre. Les paramètres « interrupteur » affichent On / Off et utilisent le bouton SWITCH ACTIVE. |
| **FLIP** | Les paramètres passent sur les **faders**. |
| **CHANNEL BAR SCROLL** | Page précédente / suivante des paramètres (la flèche clignote si une autre page existe). |
| **BANK LEFT / RIGHT** | Naviguent entre les pistes. |
| **MASTER BYPASS** | Bypass / active le plugin ouvert. |
| **COMPARE** | Compare deux jeux de réglages **A / B** du plugin (LED fixe = A, clignotante = B). |
| **AUTO/SELECT** | LED allumée : les boutons des encodeurs servent à **choisir les paramètres à automatiser** (voir §11). |
| **INSERTS/PARAM** (clignotant) | Revient à la vue Inserts. |

### Choisir les paramètres affichés

L'ordre, les noms courts (4 caractères), les noms longs (8 caractères, sur 2 strips), les séparateurs de texte et la visibilité de chaque paramètre se règlent dans l'**application de configuration** (§17). Pour **ajouter** les paramètres d'un nouveau plugin : ajoutez-le dans REAPER puis appuyez sur **EXT TRANS** (§5.4).

---

## 10. Entrées, sorties et Master

Depuis la vue Home, trois boutons changent ce que représentent les strips :

| Bouton | Vue |
|---|---|
| **INPUT** | Affiche l'**entrée d'enregistrement** de chaque piste. |
| **OUTPUT** | Affiche les **sorties** de chaque piste. |
| **MASTER FADERS** | Affiche la piste **Master**. |

Dans les vues INPUT et OUTPUT, **ASSIGN** permet de modifier le routage avec l'encodeur du strip (même principe que §6 : tourner pour choisir, bouton d'encodeur pour sélectionner, **ASSIGN** pour valider, **ESC/CANCEL (Assignment)** pour annuler). Pour les sorties, plusieurs destinations peuvent être ajoutées ou retirées. Appuyez de nouveau sur le bouton de la vue (ou ESC/CANCEL) pour revenir.

---

## 11. Automation

### 11.1 Mode d'automation d'une piste

Le bouton **AUTO** de chaque strip règle le mode d'automation de la piste. Les LED indiquent le mode (Trim, Read, Touch, Write, Latch, Off).

| Action | Résultat |
|---|---|
| **AUTO** | Passe au mode suivant, dans l'ordre : Write → Touch → Latch → Trim → Read → Off, puis retour à Write. |
| **WRITE / TOUCH / LATCH / TRIM / READ / OFF** (section AUTOMATION, maintenu) **+ AUTO** | Règle directement ce mode sur la piste. |
| **CTRL (⌘) + AUTO** | Ouvre / ferme les enveloppes de la piste dans REAPER. |
| **SHIFT + AUTO** | Passe en **navigation d'automation** sur cette piste : seule cette piste est sélectionnée, toutes ses enveloppes sont affichées, les flèches et la molette servent à choisir l'enveloppe (voir §12). La LED AUTO clignote. Appuyez de nouveau (ou SHIFT + AUTO sur une autre piste) pour quitter ; les enveloppes sont masquées. |

### 11.2 Armer les enveloppes

Les boutons **FADER, PAN, MUTE, SEND LEVEL, SEND MUTE, PLUG-IN** (colonne ENABLE) activent / désactivent l'automation correspondante pour les pistes qui ont un mode d'automation. Si une piste n'a pas encore l'enveloppe demandée, un message (« Arm Env ») liste les pistes concernées.

### 11.3 Écrire jusqu'au début / à la fin / partout

Section **WRITE AUTOMATION TO** :

| Bouton | Action REAPER |
|---|---|
| **START** | Action 42014 |
| **END** | Action 42015 |
| **ALL** | Action 42017 (projet entier) ; avec **SHIFT** : action 42013 (sélection) |

### 11.4 AUTO SUSPEND

**AUTO SUSPEND** suspend l'automation globalement (action REAPER 40908, LED synchronisée).

---

## 12. Molette (jog), navigation et zoom

### 12.1 SHUTTLE : deux modes de la molette

Le bouton **SHUTTLE** bascule entre deux modes :

| SHUTTLE | Molette | Pavé numérique |
|---|---|---|
| **LED allumée** (par défaut) | Déplace le curseur, zoome, sélectionne… (§12.2) | Saisie de valeurs / marqueurs / calculs (§14) |
| **LED éteinte** (mode « Tab ») | Envoie des touches **Tab** à l'application au premier plan : sert à passer de champ en champ dans une boîte de dialogue | Les touches du pavé sont envoyées au clavier (utile pour saisir dans les boîtes de dialogue de REAPER) |

### 12.2 La molette en mode SHUTTLE

Sans outil d'édition actif, selon les modificateurs maintenus :

| Modificateurs | Action |
|---|---|
| (aucun) | Déplace le **curseur d'édition**. Avec **GRID** allumé, le curseur « accroche » la grille ; sinon, le pas dépend de la vitesse de rotation et du zoom. **CTRL (⌘)** maintenu = pas 10× plus fin. |
| **CTL** | Déplace le curseur **et sélectionne** (temps, items et points d'enveloppe rencontrés). |
| **SHIFT + CTL** | Sélection **additive** (ajoute au lieu de remplacer). |
| **SHIFT** | **Zoom horizontal**. |
| **SHIFT + OPT** | **Zoom vertical**. |
| **OPT** | Sélectionne la piste précédente / suivante (en navigation d'automation : l'enveloppe précédente / suivante). |
| **OPT + CTRL (⌘)** | Étend la sélection de pistes. |

Quand un **outil d'édition** est actif (§13), la molette agit sur l'item ou la sélection (déplacer, rogner, glisser le contenu…).

### 12.3 NAV, ZOOM, SEL ADJ et les flèches

Les boutons **NAV**, **ZOOM** et **SEL ADJ** (au-dessus de la croix de navigation) choisissent le rôle des 4 boutons **UP / DOWN / PREVIOUS / NEXT** :

| Mode | UP | DOWN | PREVIOUS | NEXT |
|---|---|---|---|---|
| **NAV** | Défiler vers le haut | Défiler vers le bas | Défiler à gauche | Défiler à droite |
| **ZOOM** | Zoom vertical + | Zoom vertical − | Zoom horizontal + | Zoom horizontal − |
| **SEL ADJ** | — | — | Curseur au **début** de la sélection | Curseur à la **fin** de la sélection |
| (aucun allumé) | Touche **↑** | Touche **↓** | Touche **←** | Touche **→** |

- **SEL ADJ** n'est disponible que s'il existe une sélection temporelle (sinon message « There is no selection avaiblable ! »). La molette ajuste alors le **point** (début ou fin) où se trouve le curseur.
- En navigation d'automation (§11.1), UP / DOWN choisissent l'enveloppe.

### 12.4 Présélections de zoom

| Bouton | Action |
|---|---|
| **ZOOM PRESET 1** | Zoom arrière sur tout le projet et bascule la hauteur minimale des pistes. |
| **ZOOM PRESET 2** | Fait défiler les hauteurs de piste (zoom vertical). |
| **ZOOM PRESET 3** | Bascule entre « zoom sur la sélection » et « zoom arrière sur le projet ». |

### 12.5 GRID, SPOT

- **GRID** : active / désactive l'**aimantation** (snap) de REAPER (LED synchronisée).
- **SPOT** : active / désactive le **verrouillage** (lock) du projet. **SHIFT + SPOT** : verrouille / déverrouille les **items sélectionnés**.

---

## 13. Outils d'édition

Les boutons **SELECT, GRAB, TRIM, SHUFFLE, SLIP, PENCIL** (section EDIT TOOLS / EDIT MODE) sont exclusifs entre eux. Ils règlent ce que fait la **molette** (§12.2) sur l'item sélectionné.

| Bouton | Effet de la molette |
|---|---|
| **SELECT** | Interrupteur « sélection d'items » (LED mémorisée dans le projet). Éteint : désélectionne tous les items. |
| **GRAB** | Ajuste l'item sélectionné (agrandit / réduit) ; la sélection temporelle est calée sur l'item. |
| **TRIM** | Rogne le début / la fin de l'item. |
| **SHUFFLE** | Déplace l'item. |
| **SLIP** | Déplace le **contenu** interne de l'item (slip edit). |
| **PENCIL** | Mode **marqueurs d'étirement** (stretch markers) : **CTL** ajoute un marqueur d'étirement à la position du curseur, la molette déplace vers le marqueur le plus proche. |
| **MIDI TOOLS** | Affiche / masque la fenêtre MIDI de REAPER ; **SHIFT** : ouvre l'item dans l'éditeur MIDI intégré. |

**GRAB** et **TRIM** exigent **un seul item sélectionné** : sinon les messages « No items selected ! » ou « Too many items selected ! » s'affichent.

### Fonctions d'édition

| Bouton | Action | Avec SHIFT |
|---|---|---|
| **CUT** | Couper (sélection temporelle, items ou points d'enveloppe selon le focus) | — |
| **COPY** | Copier | — |
| **PASTE** | Coller | Insérer du temps puis coller |
| **DELETE** | Supprimer items / pistes / points d'enveloppe selon le focus | — |
| **SEPARATE** | Découper les items à la sélection ou au curseur | **Fusionner** (glue) les items |
| **DUPLICATE** | Dupliquer les items (requiert une sélection) | Dupliquer la **zone** sélectionnée |
| **REPEAT** | Demande un nombre (pavé numérique, **ENTER** pour valider) puis duplique autant de fois | Idem sur la zone sélectionnée |

---

## 14. Pavé numérique, marqueurs et régions

### 14.1 Saisie

Quand le pavé numérique est en mode **saisie** (SHUTTLE allumé), il alimente les boîtes de dialogue du driver (affichées sur la console). **CLR** efface (touche retour arrière), **ENTER** valide, **ESC/CANCEL** annule. Un chronomètre de 1 minute quitte le mode automatiquement (message « Too Late — Exit Numpad Mode »).

### 14.2 Marqueurs (touche « / ») et régions (touche « * »)

Appuyez sur **/** pour agir sur les **marqueurs**, sur **\*** pour les **régions**. Puis :

| Touche | Marqueurs | Régions |
|---|---|---|
| (chiffres + **ENTER**) | Aller au marqueur n° N | Aller au début de la région n° N |
| **=** | **Définir** : place le marqueur n° N au curseur | Place la région n° N sur la **sélection temporelle** (sélection requise) |
| **+** | **Ajouter** un marqueur au curseur (ENTER pour confirmer) | **Ajouter** une région sur la sélection (ENTER pour confirmer) |
| **−** | **Supprimer** le marqueur n° N | **Supprimer** la région n° N |

Des messages confirment le résultat (« Added marker : 3 », « Region 2 is setted »…) ou signalent une erreur (« … is not an existing marker Nb »).

### 14.3 Calculatrice (touche « = »)

Quand un champ numérique est actif dans REAPER (en mode Tab, SHUTTLE éteint) : **=** copie la valeur du champ ; choisissez l'opérateur **+ − × ÷**, saisissez un nombre, **ENTER** : le résultat est collé dans le champ. **ESC/CANCEL** annule.

---

## 15. Groupes, fenêtres, annuler et sauvegarder

### 15.1 Groupes de pistes

Section **GROUPS** :

| Bouton | Action |
|---|---|
| **CREATE** | Crée un groupe (édition média / razor) avec les pistes sélectionnées. |
| **ENABLE** | Demande le **numéro** du groupe (1 à 128, pavé numérique + ENTER) et l'active / désactive. |
| **EDIT** | Ouvre les paramètres de groupe de REAPER. |
| **DELETE** | Retire les pistes sélectionnées de tous leurs groupes. |
| **SUSPEND** | Suspend / réactive tous les groupes (LED synchronisée). |

### 15.2 Fenêtres

Section **WINDOW** : **MIX** (mixer), **TRANS** (transport), **STATUS** (action REAPER 40240), **MEM-LOC** (gestionnaire de marqueurs/régions) affichent / masquent la fenêtre correspondante, et **PLUG-IN** celle du plugin sélectionné. Les LED suivent l'état des fenêtres.

### 15.3 UNDO / SAVE

| Bouton | Action |
|---|---|
| **UNDO** | Annuler (la LED indique qu'une action est annulable). |
| **CTRL (⌘) + UNDO** | **Rétablir** (redo). |
| **SHIFT + UNDO** | Affiche l'**historique d'annulation** (action 40072). |
| **SAVE** | Enregistre le projet. La LED **clignote** tant que le projet est modifié et non sauvegardé. |
| **CTL + SAVE** | Règle la **sauvegarde automatique** : saisissez les minutes au pavé numérique (0 = désactivée) puis ENTER ; **ESC/CANCEL** annule. Le réglage est mémorisé dans le projet. |

---

## 16. Afficheurs, compteur et VU-mètres

| Élément | Fonction |
|---|---|
| **SHOW VALUES** | Affiche temporairement les **valeurs** (volume/pan/paramètre) sur les afficheurs des strips. |
| **SHOW CHANNEL NAMES** | Affiche temporairement les **noms** des pistes. |
| **SHOW INFO** | Rappelle le dernier texte d'information affiché sur la ligne de texte. |
| **FINE ADJ ENCODERS** | Active / désactive la **sensibilité à la vitesse** des encodeurs (message « Velocity sensible : On/Off »). |
| **COUNTER MODE** | Change l'unité du compteur : heures:minutes:secondes.ms → timecode (images) → pieds.images → mesures.temps (le tempo et la signature viennent de REAPER). |
| **VU-mètres** | Niveau de chaque piste. |
| **METER SELECT : PRE FADER / POST FADER / INPUT** | Choisit la mesure : pré-fader ou post-fader (réglage de REAPER ; ces deux choix **désarment toutes les pistes**), ou entrée (**arme toutes les pistes** pour voir l'entrée). |
| **CLEAR PEAKS** | Efface les indicateurs de crête (action REAPER 40527). |

---

## 17. Configuration du driver

La configuration est dans le fichier **`ReaperDriverControl24.ini`**, à côté des plugins REAPER. Elle se modifie avec l'application **Control24 Config** (page web locale, `http://127.0.0.1:5119`) ou à la main.

### Réglages généraux (section `[Driver]`)

| Clé | Rôle |
|---|---|
| `Autostart` | Démarre le driver quand REAPER charge le plugin. |
| `NetworkInterface` | Interface réseau reliée à la Control|24. `TestWithoutC24` simule une console (pour tester sans matériel). |
| `VSDebug` | `True` : REAPER ne lance pas le driver, il se lance depuis Visual Studio (développeurs). |

### Plugins et paramètres (application Control24 Config)

- **Plugins** : visibilité de chaque plugin dans la liste d'assignation (§8.2), **nom court** de 8 caractères (caractères interdits : `|` `=` `[` `]` ; le nom doit être unique).
- **Paramètres FX** : pour chaque plugin, **ordre**, **nom court** (4 caractères) et **nom long** (affichage sur 2 strips), **visibilité**, **séparateurs de texte**. Un aperçu montre le rendu sur la console (24 strips par page). Les caractères non ASCII risquent de mal s'afficher sur les afficheurs.
- **Fichier & sauvegardes** : une copie du fichier est faite avant chaque enregistrement (30 conservées) et peut être restaurée.

Pour ajouter les paramètres d'un nouveau plugin : ajoutez-le dans REAPER, puis appuyez sur **EXT TRANS** sur la console (les paramètres déjà présents sont conservés).

---

## 18. Dépannage

| Symptôme | Piste |
|---|---|
| Le driver ne démarre pas avec REAPER | Vérifiez `Autostart=True` et `VSDebug=False` dans le `.ini`. Le message « No network interface specified » indique que `NetworkInterface` est vide. |
| « Control24 initialization timeout. Check hardware connection. » | La console ne répond pas : vérifiez la console allumée, le câble Ethernet et l'interface réseau choisie. |
| Aucun retour de la console sous macOS | Vérifiez que le paquet **ChmodBPF** est installé et que vous avez redémarré (voir le README). |
| Un plugin n'apparaît pas dans la liste d'assignation | Il n'est pas marqué *visible* dans l'application de configuration, ou n'est pas connu du fichier : lancez le driver puis appuyez sur **EXT TRANS**. |
| Les paramètres d'un plugin sont absents | Ajoutez le plugin dans REAPER puis appuyez sur **EXT TRANS**. |

---

## 19. Aide-mémoire des actions REAPER utilisées

Pour retrouver une action dans la liste des actions de REAPER, cherchez son numéro (colonne « ID de commande »).

| Fonction console | Action(s) REAPER |
|---|---|
| Lecture / pause / arrêt / enregistrer | 1007, 1008, 1016, 1013 |
| Avance / retour rapide | 40085, 40084 |
| Début / fin de projet | 40042, 40043 |
| Début / fin de sélection (IN / OUT) | 40625, 40626 |
| Boucle (LOOP PLAY) | 1068 |
| Auto-punch : normal / sélection / items | 40252, 40076, 40253 |
| Désarmer / armer toutes les pistes | 40491, 40490 |
| Pré-/post-fader des pics | 42076 |
| Effacer les crêtes | 40527 |
| Snap (GRID) | 1157 |
| Verrouillage (SPOT) / items sélectionnés | 1135, 40687 |
| Scroll haut / bas / gauche / droite (NAV) | 40138, 40139, 40140, 40141 |
| Zoom vertical +/− | 40111, 40112 |
| Zoom horizontal +/− | 1012, 1011 |
| Zoom projet / sélection | 40295, 40031 |
| Annuler / rétablir / historique | 40029, 40030, 40072 |
| Enregistrer | 40026 |
| Couper / copier / coller / insérer / supprimer | 41384, 41383, 42398, 41748, 40697 |
| Découper / fusionner | 40061 ou 40012, 42432 |
| Dupliquer items / zone | 41295, 41296 |
| Groupes : créer / retirer / paramètres / suspendre | 42578, 42579, 40772, 40771 |
| Groupe n activer/désactiver | 42510 + n |
| Fenêtres MIX / TRANS / STATUS / MEM-LOC | 40078, 40259, 40240, 40326 |
| Écrire l'automation : début / fin / sélection / tout | 42014, 42015, 42013, 42017 |
| Suspendre l'automation | 40908 |
| Éditeur MIDI | 40716, 40153 |
| Marqueur d'étirement | 41842 |
