# Control24 Button LED Mapping Guide

## ?? Table des matières

1. [Vue d'ensemble](#vue-densemble)
2. [Format des adresses OSC](#format-des-adresses-osc)
3. [Boutons de piste (Track Buttons)](#boutons-de-piste-track-buttons)
4. [Boutons de transport](#boutons-de-transport)
5. [Boutons de commande/utilitaires](#boutons-de-commandeutilitaires)
6. [Boutons d'automation](#boutons-dautomation)
7. [Exemples d'utilisation](#exemples-dutilisation)
8. [Format des commandes MIDI](#format-des-commandes-midi)

---

## ?? Vue d'ensemble

Le système de mapping des LEDs de boutons du Control24 permet de contrôler tous les indicateurs LED de la surface de contrôle via des adresses OSC ou des appels directs à l'API.

### Caractéristiques principales

- ? **Support complet** de tous les boutons avec LED
- ? **Mapping bidirectionnel** : Control24 ? OSC ? DAW
- ? **États multiples** : OFF (0) / ON (1) / États intermédiaires (certains boutons)
- ? **Suivi d'état** : Mémorisation de l'état de chaque LED
- ? **Mise à jour simultanée** : Contrôle de plusieurs LEDs en même temps

---

## ?? Format des adresses OSC

Les adresses OSC suivent une hiérarchie logique :

```
/{category}/{identifier}/{parameter}
```

### Exemples

| Adresse | Description |
|---------|-------------|
| `/track/1/solo` | LED Solo de la piste 1 |
| `/transport/play` | LED du bouton Play |
| `/command/f1` | LED de la touche F1 |
| `/track/3/automode/write` | LED Write de l'automation (piste 3) |

---

## ??? Boutons de piste (Track Buttons)

### Disponibles sur chaque piste (1-24)

| Nom du bouton | Adresse OSC | Byte Code | Description |
|---------------|-------------|-----------|-------------|
| **RecArm** | `/track/{n}/recarm` | 0x00 | Armement d'enregistrement |
| **Pan/Send** | `/track/{n}/pan_send` | 0x01 | Sélecteur Pan/Send |
| **EQ** | `/track/{n}/eq` | 0x02 | Sélecteur EQ |
| **Dynamics** | `/track/{n}/dynamics` | 0x03 | Sélecteur Dynamics |
| **Switch/Active** | `/track/{n}/switch_active` | 0x04 | Bouton Switch/Active |
| **AutoMode** | `/track/{n}/automode` | 0x05 | Sélecteur mode automation |
| **Channel Select** | `/track/{n}/select` | 0x06 | Sélection de canal |
| **Solo** | `/track/{n}/solo` | 0x07 | Solo |
| **Mute** | `/track/{n}/mute` | 0x08 | Mute |
| **Touch** | `/track/{n}/touch` | 0x09 | Fader touch |
| **Inserts** | `/track/{n}/inserts` | 0x0A | Sélecteur Inserts |

### Exemple d'utilisation

```vb
' VB.NET
Dim track1 = desk.GetTrack(0)

' Activer Solo sur piste 1
track1.ButtonLed.SetButton("/track/1/solo", 1)

' Désactiver Mute sur piste 1
track1.ButtonLed.SetButton("/track/1/mute", 0)

' Activer RecArm sur pistes 1-8
For i = 0 To 7
    Dim track = desk.GetTrack(i)
    track.ButtonLed.SetButton($"/track/{i + 1}/recarm", 1)
Next
```

---

## ?? Boutons de transport

| Nom du bouton | Adresse OSC | Byte Code | Description |
|---------------|-------------|-----------|-------------|
| **Rewind** | `/transport/rewind` | 0x0D | Rembobinage |
| **Forward** | `/transport/forward` | 0x0E | Avance rapide |
| **Stop** | `/transport/stop` | 0x0F | Stop |
| **Play** | `/transport/play` | 0x10 | Lecture |
| **Record** | `/transport/record` | 0x11 | Enregistrement |
| **Scrub** | `/transport/scrub` | 0x12 | Mode Scrub |
| **Shuttle** | `/transport/shuttle` | 0x13 | Mode Shuttle |

### Exemple d'utilisation

```vb
' Activer Play
track0.ButtonLed.SetButton("/transport/play", 1)

' Désactiver Stop
track0.ButtonLed.SetButton("/transport/stop", 0)

' Activer Record
track0.ButtonLed.SetButton("/transport/record", 1)
```

---

## ?? Boutons de commande/utilitaires

### Touches de fonction (F1-F10)

| Touche | Adresse OSC | Byte Code |
|--------|-------------|-----------|
| **F1** | `/utility/f1` | 0x00 |
| **F2** | `/utility/f2` | 0x02 |
| **F3** | `/utility/f3` | 0x04 |
| **F4** | `/utility/f4` | 0x06 |
| **F5** | `/utility/f5` | 0x08 |
| **F6** | `/utility/f6` | 0x32 |
| **F7** | `/utility/f7` | 0x33 |
| **F8** | `/utility/f8` | 0x34 |
| **F9** | `/utility/f9` | 0x35 |
| **F10** | `/utility/f10` | 0x36 |

### Modes d'affichage

| Mode | Adresse OSC | Byte Code | Description |
|------|-------------|-----------|-------------|
| **Show Values** | `/command/showvalues` | 0x10 | Affiche les valeurs |
| **Show Group** | `/command/showgroup` | 0x11 | Affiche les groupes |
| **Show Names** | `/command/showchannelnames` | 0x12 | Affiche les noms |
| **Show Info** | `/command/showinfo` | 0x13 | Affiche les infos |

### Navigation

| Bouton | Adresse OSC | Byte Code | Description |
|--------|-------------|-----------|-------------|
| **Nav** | `/command/nav` | 0x0C | Mode Navigation |
| **Zoom** | `/command/zoom` | 0x0D | Mode Zoom |
| **Sel Adj** | `/command/seladj` | 0x0E | Select Adjust |

### Modes d'édition

| Mode | Adresse OSC | Byte Code | Description |
|------|-------------|-----------|-------------|
| **Shuffle** | `/command/shuffle` | 0x00 | Mode Shuffle |
| **Slip** | `/command/slip` | 0x01 | Mode Slip |
| **Spot** | `/command/spot` | 0x02 | Mode Spot |
| **Grid** | `/command/grid` | 0x03 | Mode Grid |

### Sends (A-E / F-J)

| Send | Adresse OSC | Byte Code |
|------|-------------|-----------|
| **A/F** | `/command/af` | 0x0B |
| **B/G** | `/command/bg` | 0x0C |
| **C/H** | `/command/ch` | 0x0D |
| **D/I** | `/command/di` | 0x0E |
| **E/J** | `/command/ej` | 0x0F |

---

## ??? Boutons d'automation

Les LEDs d'automation sont spécifiques à chaque piste.

| Mode | Adresse OSC | Byte Code | Description |
|------|-------------|-----------|-------------|
| **Read** | `/track/{n}/automode/read` | 0xF0 | Mode Read |
| **Trim** | `/track/{n}/automode/trim` | 0xF1 | Mode Trim |
| **Latch** | `/track/{n}/automode/latch` | 0xF2 | Mode Latch |
| **Touch** | `/track/{n}/automode/touch` | 0xF3 | Mode Touch |
| **Write** | `/track/{n}/automode/write` | 0xF4 | Mode Write |
| **Off** | `/track/{n}/automode/off` | 0xF5 | Automation Off |

### Exemple d'utilisation

```vb
' Activer Write mode sur piste 1
track1.ButtonLed.SetButton("/track/1/automode/write", 1)

' Désactiver tous les modes d'automation sur piste 2
track2.ButtonLed.SetButton("/track/2/automode/off", 1)
```

---

## ?? Exemples d'utilisation

### Exemple 1 : Activer plusieurs boutons

```vb
Dim track1 = desk.GetTrack(0)

' Activer plusieurs boutons en une fois
Dim buttons As New Dictionary(Of String, Integer) From {
    {"/track/1/solo", 1},
    {"/track/1/recarm", 1},
    {"/transport/record", 1}
}

track1.ButtonLed.SetButtons(buttons)
```

### Exemple 2 : Effet de chase (LED qui défilent)

```vb
' Créer un effet de chase sur les boutons Solo
For i = 0 To 23
    Dim track = desk.GetTrack(i)
    
    ' Activer LED actuelle
    track.ButtonLed.SetButton($"/track/{i + 1}/solo", 1)
    
    ' Désactiver LED précédente
    If i > 0 Then
        Dim prevTrack = desk.GetTrack(i - 1)
        prevTrack.ButtonLed.SetButton($"/track/{i}/solo", 0)
    End If
    
    Threading.Thread.Sleep(50) ' 50ms de délai
Next
```

### Exemple 3 : Synchronisation avec DAW via OSC

```vb
' Configuration OSC
oscSession.ConfigureOSC(
    serverPort:=9124,
    dawAddress:="127.0.0.1",
    dawPort:=9125
)

' Démarrer la session
oscSession.Start()

' Les messages OSC du DAW mettront automatiquement à jour les LEDs
' Par exemple, si Reaper envoie : /track/1/solo 1
' La LED Solo de la piste 1 s'allumera automatiquement
```

### Exemple 4 : Vérifier l'état d'une LED

```vb
Dim track1 = desk.GetTrack(0)

' Obtenir l'état actuel d'une LED
Dim soloState = track1.ButtonLed.GetLedState("/track/1/solo")

If soloState.HasValue Then
    Console.WriteLine($"Solo LED is: {If(soloState.Value = 1, "ON", "OFF")}")
Else
    Console.WriteLine("Solo LED state unknown")
End If
```

### Exemple 5 : Réinitialiser toutes les LEDs

```vb
' Éteindre toutes les LEDs d'une piste
Dim track1 = desk.GetTrack(0)

track1.ButtonLed.SetButton("/track/1/solo", 0)
track1.ButtonLed.SetButton("/track/1/mute", 0)
track1.ButtonLed.SetButton("/track/1/recarm", 0)
track1.ButtonLed.SetButton("/track/1/select", 0)

' Ou utiliser ClearAllLeds pour réinitialiser l'état mémorisé
track1.ButtonLed.ClearAllLeds()
```

---

## ?? Format des commandes MIDI

### Boutons de piste

```
Format: 0x90 [ButtonCode] [TrackNum] [Value]

Exemple - Solo ON sur piste 1:
90 07 00 40

90    : Note On
07    : Button Code (Solo)
00    : Track 0 (piste 1 en affichage)
40    : LED ON (00 = OFF)
```

### Boutons de transport

```
Format: 0x90 [ButtonCode] 0x1C [Value]

Exemple - Play ON:
90 10 1C 40

90    : Note On
10    : Button Code (Play)
1C    : Transport group (0x18 | 0x04)
40    : LED ON
```

### Boutons de commande

```
Format: 0x90 [ButtonCode] [ChildByte|0x18] [Value]

Exemple - F1 ON:
90 00 18 40

90    : Note On
00    : Button Code (F1)
18    : Command group
40    : LED ON
```

### Automation LEDs

```
Format: 0xFF 0xFE [ModeCode] [TrackNum] [Value] 0x00 0xF7

Exemple - Write mode ON sur piste 1:
FF FE F4 00 01 00 F7

FF    : SysEx automation
FE    : Automation LED flag
F4    : Mode Code (Write)
00    : Track 0
01    : LED ON (00 = OFF)
00    : Padding
F7    : SysEx end
```

---

## ?? Tests et débogage

### Test complet du mapping

```vb
' Utiliser la classe de test fournie
Dim examples As New C24OscSessionExamples()

' Tester tous les types de boutons
examples.TestButtonLedMapping()

' Tester les LEDs simultanées
examples.TestSimultaneousLeds()
```

### Logging

Le système de mapping des LEDs génère des logs détaillés :

```
DEBUG: Track button LED: Track 1, Button solo, Value 1
DEBUG: Transport button LED: play, Value 1
DEBUG: Command button LED: utility/f1, Value 1
DEBUG: Automation LED: Track 1, Mode write, Value 1
```

### Vérification des états

```vb
' Vérifier l'état de toutes les LEDs d'une piste
For Each address In {"solo", "mute", "recarm", "select"}
    Dim state = track.ButtonLed.GetLedState($"/track/1/{address}")
    Console.WriteLine($"{address}: {state}")
Next
```

---

## ?? Ressources supplémentaires

- **Control24Map.vb** : Définitions complètes du mapping
- **Control24Osc.vb** : Implémentation de C24ButtonLed
- **Control24Example.vb** : Exemples d'utilisation
- **CONTROL24_EXAMPLES.md** : Guide des exemples

---

## ?? Dépannage

### Problème : LED ne s'allume pas

**Solutions** :
1. Vérifier que le Control24 est connecté
2. Vérifier l'adresse OSC (respecter la casse et le format)
3. Vérifier les logs pour les erreurs
4. Vérifier que `C24ClientSend` est bien configuré

### Problème : État des LEDs non synchronisé

**Solutions** :
1. Appeler `ClearAllLeds()` pour réinitialiser
2. Vérifier que les messages OSC du DAW sont bien reçus
3. Vérifier la configuration OSC (ports, adresses)

### Problème : Certains boutons ne répondent pas

**Solutions** :
1. Vérifier le byte code dans Control24Map.vb
2. Vérifier que le bouton supporte les LEDs (`.LED = True` dans le mapping)
3. Tester avec un byte code connu fonctionnel

---

**Version** : 1.0  
**Date** : 2024  
**Auteur** : Control24 Middleware Project  
**Licence** : GPL v3
