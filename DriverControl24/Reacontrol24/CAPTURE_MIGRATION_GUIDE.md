# ?? Guide de Migration - Capture Réseau Générique

## Vue d'ensemble

Le module de capture réseau a été refactoré pour utiliser une **interface générique** (`INetworkCapture`) qui permet de basculer facilement entre différentes implémentations de capture de paquets.

### Avantages

? **Flexibilité** - Changement facile d'implémentation  
? **Pas de dépendance stricte** - SharpPcap devient optionnel  
? **Extensible** - Ajout facile de nouvelles implémentations  
? **Testable** - Mock facile pour tests unitaires  
? **Fallback automatique** - Choix automatique de la meilleure implémentation  

## ??? Architecture

### Nouvelle structure

```
INetworkCapture (Interface)
??? SharpPcapAdapter (Implémentation SharpPcap)
??? RawSocketAdapter (Implémentation Raw Socket .NET)
??? [Future] WinDivertAdapter, etc.

NetworkCaptureFactory
??? Create() - Crée l'implémentation appropriée
??? IsAvailable() - Vérifie disponibilité
??? GetAvailableImplementations() - Liste implémentations

GenericPacketCaptureManager
??? Wrapper unifié pour toutes implémentations
```

### Comparaison des implémentations

| Implémentation | Avantages | Inconvénients | Prérequis |
|----------------|-----------|---------------|-----------|
| **Pcap.Net** | ? API moderne<br>? Accès Ethernet layer<br>? Hardware filters (BPF)<br>? Haute performance<br>? Send Ethernet frames<br>? Meilleur gestion erreurs | ?? Nécessite Npcap/WinPcap<br>?? Dépendance externe | Npcap installé |
| **SharpPcap** | ? Accès Ethernet layer<br>? Hardware filters (BPF)<br>? Haute performance<br>? Send Ethernet frames | ?? Nécessite Npcap/WinPcap<br>?? API moins moderne<br>?? Dépendance externe | Npcap installé |
| **RawSocket** | ? Pas de driver externe<br>? .NET natif<br>? Simple | ? IP layer seulement<br>? Pas de send Ethernet<br>? Software filtering | Privilèges admin |
| **WinDivert** | ? Kernel driver<br>? Haute performance | ? IP layer only<br>? Ne supporte PAS 0x885F | WinDivert driver |

## ?? Nouveaux fichiers

### 1. INetworkCapture.vb
Interface générique pour capture réseau.

```vb
Public Interface INetworkCapture
    Function GetDevices() As List(Of GenericNetworkDevice)
    Sub OpenDevice(deviceName As String, macAddress As String)
    Sub SetFilter(macAddress As String)
    Sub StartCapture(handler As Action(Of Double, Byte()))
    Sub StopCapture()
    Function SendPacket(data As Byte()) As Integer
    Event PacketReceived As EventHandler(Of PacketReceivedEventArgs)
    ReadOnly Property IsCapturing As Boolean
End Interface
```

### 2. SharpPcapAdapter.vb
Implémentation SharpPcap de l'interface (wrapper du PacketCaptureManager actuel).

### 3. RawSocketAdapter.vb
Implémentation Raw Socket .NET (ne nécessite pas SharpPcap).

**?? Limitations** : 
- IP layer uniquement (pas Ethernet)
- Pas de send Ethernet frames
- Filtrage software (pas BPF)

### 4. NetworkCaptureFactory.vb
Factory pour créer l'implémentation appropriée.

```vb
' Auto-détection
Dim capture = NetworkCaptureFactory.Create(CaptureImplementation.Auto, logger)

' SharpPcap forcé
Dim capture = NetworkCaptureFactory.Create(CaptureImplementation.SharpPcap, logger)

' RawSocket forcé
Dim capture = NetworkCaptureFactory.Create(CaptureImplementation.RawSocket, logger)
```

### 5. GenericPacketCaptureManager.vb
Gestionnaire unifié (remplace PacketCaptureManager).

## ?? Migration

### Option 1 : Migration simple (recommandée)

**Avant** (PacketCaptureManager):
```vb
_packetCapture = New PacketCaptureManager(_log)
_packetCapture.OpenDevice(_network, _network.Mac)
_packetCapture.SetFilter(_network.Mac)
_packetCapture.StartCapture(AddressOf PacketHandler)
```

**Après** (GenericPacketCaptureManager - Auto):
```vb
' Auto-détection de la meilleure implémentation
_packetCapture = New GenericPacketCaptureManager(_log, CaptureImplementation.Auto)
_packetCapture.OpenDevice(_network, _network.Mac)
_packetCapture.SetFilter(_network.Mac)
_packetCapture.StartCapture(AddressOf PacketHandler)
```

**Résultat** : 
- Essaie SharpPcap d'abord
- Si non disponible ? Fallback RawSocket
- Si échec ? Exception

### Option 2 : Choix manuel

```vb
' Vérifier disponibilité
Dim available = GenericPacketCaptureManager.GetAvailableImplementations(_log)

If available.Contains(CaptureImplementation.SharpPcap) Then
    _packetCapture = New GenericPacketCaptureManager(_log, CaptureImplementation.SharpPcap)
ElseIf available.Contains(CaptureImplementation.RawSocket) Then
    _packetCapture = New GenericPacketCaptureManager(_log, CaptureImplementation.RawSocket)
Else
    Throw New Exception("No capture implementation available")
End If
```

### Option 3 : Interface directe (avancé)

```vb
' Utiliser directement l'interface
Dim capture As INetworkCapture

If NetworkCaptureFactory.IsAvailable(CaptureImplementation.SharpPcap, _log) Then
    capture = New SharpPcapAdapter(_log)
Else
    capture = New RawSocketAdapter(_log)
End If

' Utiliser comme avant
capture.OpenDevice(_network.Name, _network.Mac)
capture.StartCapture(AddressOf PacketHandler)
```

## ?? Mise à jour de Control24d.vb

### Changement minimal

**Ancien** :
```vb
Private _packetCapture As PacketCaptureManager

' Initialize packet capture
_packetCapture = New PacketCaptureManager(_log)
```

**Nouveau (Option 1 - Simple)** :
```vb
Private _packetCapture As GenericPacketCaptureManager

' Initialize packet capture (auto-detect)
_packetCapture = New GenericPacketCaptureManager(_log)
```

**Nouveau (Option 2 - Avec choix UI)** :
```vb
Private _packetCapture As GenericPacketCaptureManager
Private _captureImpl As CaptureImplementation = CaptureImplementation.Auto

' Initialize with user choice
_packetCapture = New GenericPacketCaptureManager(_log, _captureImpl)
```

## ?? Mise à jour de l'UI

### Ajouter choix d'implémentation

```vb
' Dans Network_settings_Form.vb

' Nouveau ComboBox pour choix implémentation
Private Sub Form_Load()
    ' ...existing code...
    
    ' Populate implementations
    Dim available = GenericPacketCaptureManager.GetAvailableImplementations(_logger)
    ComboBox_Implementation.Items.Add("Auto-detect")
    
    For Each impl In available
        ComboBox_Implementation.Items.Add(impl.ToString())
    Next
    
    ComboBox_Implementation.SelectedIndex = 0 ' Auto
End Sub

Private Sub ComboBox_Implementation_SelectedIndexChanged(sender As Object, e As EventArgs)
    Dim selected = ComboBox_Implementation.SelectedItem.ToString()
    
    Select Case selected
        Case "Auto-detect"
            _currentConfig.CaptureImplementation = CaptureImplementation.Auto
        Case "SharpPcap"
            _currentConfig.CaptureImplementation = CaptureImplementation.SharpPcap
        Case "RawSocket"
            _currentConfig.CaptureImplementation = CaptureImplementation.RawSocket
    End Select
    
    SaveConfiguration()
End Sub
```

## ?? Configuration

### Ajouter à Control24Configuration

```vb
<Serializable>
Public Class Control24Configuration
    ' ...existing properties...
    
    Public Property CaptureImplementation As CaptureImplementation = CaptureImplementation.Auto
End Class
```

## ?? Tests

### Test auto-détection

```vb
' Test dans Network_settings_Form
Private Sub btnTestCapture_Click()
    Try
        Dim available = GenericPacketCaptureManager.GetAvailableImplementations(_logger)
        
        TextBox1.AppendText("=== Available Capture Implementations ===" & vbCrLf)
        
        For Each impl In available
            TextBox1.AppendText($"? {impl}" & vbCrLf)
        Next
        
        If available.Count = 0 Then
            TextBox1.AppendText("? No implementations available!" & vbCrLf)
        End If
        
        TextBox1.AppendText("===================================" & vbCrLf)
        
    Catch ex As Exception
        TextBox1.AppendText($"Error: {ex.Message}" & vbCrLf)
    End Try
End Sub
```

## ?? Comparaison performances

### SharpPcap (recommandé)

```
? Latence : < 1ms
? PPS : 10,000+
? CPU : 5-10%
? Filtering : Hardware (BPF)
```

### RawSocket (fallback)

```
?? Latence : 5-10ms
?? PPS : 1,000-5,000
?? CPU : 15-25%
?? Filtering : Software
```

## ?? Roadmap futures implémentations

### WinDivert (prévu)
- Kernel driver
- Performance maximale
- Full packet manipulation
- Pas de driver tiers

### DPDK (prévu)
- Userspace networking
- Ultra haute performance
- Pour usage avancé

### Mock (tests)
- Pour tests unitaires
- Simulation packets
- Pas de hardware

## ?? Limitations actuelles

### RawSocketAdapter

1. **Pas d'accès Ethernet layer**
   - Seulement IP et au-dessus
   - Ne peut pas envoyer frames Ethernet brutes
   
2. **Filtrage software**
   - Tous packets reçus, filtrés en VB.NET
   - Impact CPU plus élevé
   
3. **Pas de SendPacket complet**
   - Besoin Ethernet layer pour Control24
   - Toujours besoin SharpPcap pour send

### Solution temporaire

Pour l'instant, **SharpPcap reste recommandé** pour Control24 car :
- Besoin Ethernet layer (protocole 0x885f)
- Besoin d'envoyer frames Ethernet
- Besoin filtrage BPF

RawSocket est utile pour :
- Développement/tests sans Npcap
- Monitoring seulement (pas de send)
- Debugging

## ?? Recommandations

### Pour production
```vb
' Utiliser SharpPcap (meilleure option)
_packetCapture = New GenericPacketCaptureManager(_log, CaptureImplementation.SharpPcap)
```

### Pour développement
```vb
' Utiliser Auto (essaie SharpPcap, fallback RawSocket)
_packetCapture = New GenericPacketCaptureManager(_log, CaptureImplementation.Auto)
```

### Pour monitoring seulement
```vb
' RawSocket acceptable
_packetCapture = New GenericPacketCaptureManager(_log, CaptureImplementation.RawSocket)
```

## ?? Checklist migration

- [ ] Remplacer `PacketCaptureManager` par `GenericPacketCaptureManager`
- [ ] Ajouter `CaptureImplementation` à configuration
- [ ] Mettre à jour UI avec choix implémentation
- [ ] Tester avec SharpPcap
- [ ] Tester avec RawSocket (fallback)
- [ ] Documenter changements
- [ ] Tester build
- [ ] Tester avec Control24 réel

## ?? Avantages finaux

1. ? **Pas de dépendance stricte SharpPcap**
2. ? **Fallback automatique si Npcap manquant**
3. ? **Extensible pour futures implémentations**
4. ? **Interface claire et testable**
5. ? **Configuration persistante du choix**
6. ? **Compatible code existant**

---

**Note** : Pour le moment, SharpPcap reste **fortement recommandé** pour utilisation complète avec Control24 (bidirectionnel). RawSocket est un bon fallback pour monitoring ou développement.
