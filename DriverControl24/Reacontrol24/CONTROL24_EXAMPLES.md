# Control24 Examples Documentation

Ce fichier documente les exemples d'utilisation du driver Control24.

## ?? Liste des exemples

### 1. ListNetworkDevices
**Description** : Liste tous les périphériques réseau disponibles  
**Usage** :
```vb
Control24Example.ListNetworkDevices()
```

**Sortie** :
```
Available network devices:
============================================================
1. Ethernet
   Name: \Device\NPF_{GUID}
   Description: Intel(R) Ethernet Connection
   MAC: 10:98:19:B7:13:84
   IPs: 192.168.1.100
```

---

### 2. MonitorControl24Traffic
**Description** : Surveille le trafic Control24 sur une interface réseau  
**Usage** :
```vb
Control24Example.MonitorControl24Traffic("Ethernet", 30)
```

**Paramètres** :
- `networkName` : Nom de l'interface réseau
- `durationSeconds` : Durée de capture (défaut: 30s)

**Sortie** :
```
[14:32:15.123] Packet #1: 60 bytes
  Type: BROADCAST (Desk Discovery)

[14:32:15.456] Packet #2: 42 bytes
  Type: Control24 Command
  Commands: 1
  Cmd: 0xA0
```

---

### 3. ScrollClockDisplay ? NOUVEAU
**Description** : Fait défiler du texte sur l'horloge du Control24  
**Usage** :
```vb
Control24Example.ScrollClockDisplay("Ethernet", "CONTROL24 READY", 30)
```

**Paramètres** :
- `networkName` : Nom de l'interface réseau
- `text` : Texte à afficher (sera scrollé)
- `durationSeconds` : Durée du défilement (défaut: 30s)

**Fonctionnement** :
- Utilise les scribble strips des pistes 25 et 26 (8 caractères au total)
- Défile le texte caractère par caractère toutes les secondes
- Ajoute des espaces au début et à la fin pour un défilement fluide
- Boucle automatiquement à la fin du texte

**Sortie** :
```
Scrolling text on Control24 clock: 'CONTROL24 READY'
Duration: 30 seconds
Starting clock scroll (text length: 31 chars)...
[14:45:01] Clock: [        ]
[14:45:02] Clock: [       C]
[14:45:03] Clock: [      CO]
[14:45:04] Clock: [     CON]
...
```

---

### 4. DisplayTimeOnClock ? NOUVEAU
**Description** : Affiche l'heure actuelle sur l'horloge du Control24  
**Usage** :
```vb
Control24Example.DisplayTimeOnClock("Ethernet", 60)
```

**Paramètres** :
- `networkName` : Nom de l'interface réseau
- `durationSeconds` : Durée d'affichage (défaut: 60s)

**Fonctionnement** :
- Affiche l'heure au format HH:mm:ss
- Met à jour toutes les secondes
- Utilise les scribble strips des pistes 25 et 26

**Sortie** :
```
[14:45:23] Clock display updated: 14:45:23
[14:45:24] Clock display updated: 14:45:24
[14:45:25] Clock display updated: 14:45:25
...
```

---

### 5. DemoClockFeatures ? NOUVEAU
**Description** : Démo complète de toutes les fonctionnalités d'horloge  
**Usage** :
```vb
Control24Example.DemoClockFeatures("Ethernet")
```

**Séquence** :
1. Affiche l'heure pendant 10 secondes
2. Défile "CONTROL24 READY" pendant 20 secondes
3. Défile "VB.NET DRIVER v1.0" pendant 15 secondes

**Durée totale** : ~45 secondes

---

### 6. TestPacketCreation
**Description** : Teste la création et sérialisation de paquets  
**Usage** :
```vb
Control24Example.TestPacketCreation()
```

**Sortie** :
```
Packet created:
  Total length: 38 bytes
  Data length: 10 bytes
  Hex: 00 00 00 00 00 00 ...

Packet deserialized:
  Total length: 38 bytes
  Match: True
```

---

### 7. RunFullExample
**Description** : Exemple complet du cycle de vie d'une session  
**Usage** :
```vb
Control24Example.RunFullExample("Ethernet")
```

**Durée** : 60 secondes

---

## ?? Comment utiliser depuis votre application

### Dans une application console :

```vb
Imports Reacontrol24

Module Program
    Sub Main()
        Try
            ' Lister les interfaces réseau
            Control24Example.ListNetworkDevices()
            
            ' Choisir une interface et démarrer un exemple
            Dim networkName = "Ethernet" ' Adaptez selon votre système
            
            ' Exemple 1 : Afficher l'heure
            Control24Example.DisplayTimeOnClock(networkName, 30)
            
            ' Exemple 2 : Défiler un message
            Control24Example.ScrollClockDisplay(networkName, "HELLO WORLD", 20)
            
            ' Exemple 3 : Démo complète
            Control24Example.DemoClockFeatures(networkName)
            
        Catch ex As Exception
            Console.WriteLine($"Erreur : {ex.Message}")
        End Try
        
        Console.WriteLine("Appuyez sur une touche pour quitter...")
        Console.ReadKey()
    End Sub
End Module
```

### Depuis une Windows Forms application :

```vb
Private Sub ButtonDemo_Click(sender As Object, e As EventArgs) Handles ButtonDemo.Click
    Try
        Dim networkName = "Ethernet" ' Ou depuis votre ComboBox
        
        ' Lancer dans un thread séparé pour ne pas bloquer l'UI
        Dim demoThread As New Threading.Thread(
            Sub()
                Control24Example.DemoClockFeatures(networkName)
            End Sub
        )
        demoThread.IsBackground = True
        demoThread.Start()
        
    Catch ex As Exception
        MessageBox.Show($"Erreur : {ex.Message}", "Erreur")
    End Try
End Sub
```

---

## ?? Format des commandes Scribble Strip

Les commandes d'affichage sur les scribble strips utilisent le format suivant :

```
F0 13 01 40 [TrackNum] 00 [Char1] [Char2] [Char3] [Char4] F7 00
```

**Détails** :
- `F0` : SysEx start
- `13 01` : Manufacturer ID (Digidesign/Avid)
- `40` : Scribble strip command
- `[TrackNum]` : Numéro de piste (0x00 = piste 1, 0x01 = piste 2, etc.)
- `00` : Padding/Unknown
- `[Char1-4]` : 4 caractères ASCII
- `F7` : SysEx end
- `00` : Padding final (requis après F7)

**Total** : **12 bytes**

**Exemple** : Afficher "TIME" sur la piste 1 (track 0)
```
F0 13 01 40 00 00 54 49 4D 45 F7 00
             ?  ?  T  I  M  E  ?  ?
             ?  ?? Padding      ?  ?? Padding final
             ?? Track 0    SysEx end??
```

**Exemple** : Afficher "abcd" sur la piste 1 (track 0)
```
F0 13 01 40 00 00 61 62 63 64 F7 00
             ?  ?  a  b  c  d  ?  ?
             ?  ?? Padding      ?  ?? Padding final
             ?? Track 0    SysEx end??
```

**?? Important** : L'ordre correct est `F7 00` (SysEx end PUIS padding), et non `00 F7`.

---

## ?? Mapping des pistes horloge

| Piste | Usage | Caractères |
|-------|-------|------------|
| 25 | Horloge gauche | 4 chars |
| 26 | Horloge droite | 4 chars |

**Total** : 8 caractères pour l'affichage complet de l'horloge

**Format standard** : `HH:mm:ss`
- Piste 25 : "HH:m" (4 chars)
- Piste 26 : "m:ss" (4 chars)

---

## ?? Notes importantes

### Prérequis
1. **WinPcap ou Npcap** : Doit être installé pour la capture de paquets
2. **Control24** : Doit être allumé et connecté au réseau
3. **Permissions** : L'application doit avoir les droits d'accès réseau

### Limitations
- Le Control24 doit être détecté avant l'envoi de commandes
- Attendez ~2 secondes après `session.Start()` pour la détection
- Les exemples sont conçus pour être exécutés un à la fois

### Debugging
- Les logs sont créés dans `.\logs\Control24-YYYY-MM-DD.log`
- Activez le mode debug pour voir tous les paquets :
  ```vb
  Dim logger = LogHelper.StartLogging("Control24", ".\logs", debug:=True)
  ```

---

## ?? Exemples avancés

### Affichage personnalisé sur l'horloge

```vb
Public Shared Sub DisplayCustomClock(session As C24Session, text As String)
    ' S'assurer que le texte fait exactement 8 caractères
    Dim clockText = text.PadRight(8).Substring(0, 8)
    
    ' Piste 25 (gauche)
    Dim cmd25 As Byte() = {&HF0, &H13, &H1, &H40, &H19, 0, 0, 0, 0, 0, &HF7}
    Dim bytes25 = Encoding.ASCII.GetBytes(clockText.Substring(0, 4))
    For i = 0 To 3
        cmd25(5 + i) = bytes25(i)
    Next
    
    ' Piste 26 (droite)
    Dim cmd26 As Byte() = {&HF0, &H13, &H1, &H40, &H1A, 0, 0, 0, 0, 0, &HF7}
    Dim bytes26 = Encoding.ASCII.GetBytes(clockText.Substring(4, 4))
    For i = 0 To 3
        cmd26(5 + i) = bytes26(i)
    Next
    
    ' Envoyer via C24ClientSendHandler...
End Sub
```

### Animation sur l'horloge

```vb
' Exemple d'animation clignotante
For i = 1 To 10
    DisplayCustomClock(session, "********")
    Threading.Thread.Sleep(500)
    DisplayCustomClock(session, "        ")
    Threading.Thread.Sleep(500)
Next
```

---

## ?? Références

- [Control24d.vb](Control24d.vb) : Session complète avec OSC + TCP
- [Control24Osc.vb](Control24Osc.vb) : Classes C24Desk et composants
- [PacketCapture.vb](PacketCapture.vb) : Capture de paquets réseau

---

**Dernière mise à jour** : 2024-01-15  
**Version** : 1.0.0
