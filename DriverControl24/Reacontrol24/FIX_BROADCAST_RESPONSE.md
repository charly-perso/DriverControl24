# ?? FIX: Répondre aux broadcasts (0xE1) du Control24

## ? Problème identifié

Vous aviez **absolument raison** ! Le problème NE VIENT PAS du keep-alive mais des **broadcasts périodiques** du Control24.

### Analyse des logs

Dans `logWireShark.txt` :
- **Frame 119 (18.045s)** : Broadcast 0xE1 arrive pendant le test de Track 8
- **Frame 46 (11.558s)** : Broadcast 0xE1 arrive pendant le test de Track 7

Dans `logconsole.txt` :
```
21:23:32 - HandleBroadcastPacket: Active broadcast (0xE1) detected
21:23:33 - Initialization already sent - ignoring subsequent broadcast ? LE PROBLÈME !
```

## ?? Le vrai problème

Le Control24 envoie des **broadcasts (0xE1) toutes les ~6 secondes** pour :
1. **Vérifier** que l'hôte est toujours connecté ("es-tu toujours là ?")
2. **S'assurer** que l'hôte répond (handshake)

**Si on ne répond pas** :
- Le Control24 pense que la connexion est perdue
- Il refuse d'exécuter correctement les commandes suivantes
- Les LEDs à partir de Track 12 ne s'allument plus (désynchronisation)

## ? Solution : Répondre aux broadcasts

### Dans `Control24d.vb`, méthode `HandleBroadcastPacket`

Remplacer cette section :

```vb
Case Commands.BroadcastActive  ' 0xE1 - Keep-alive broadcast
    _log.Debug("Active broadcast (0xE1) detected")
    ' ? ANCIEN CODE: On ignore le broadcast
```

Par :

```vb
Case Commands.BroadcastActive  ' 0xE1 - Keep-alive broadcast
    _log.Debug("Active broadcast (0xE1) detected")
    
    ' ? CRITICAL FIX: Répondre au broadcast actif pour maintenir la connexion
    ' Le Control24 envoie des broadcasts (0xE1) toutes les ~6 secondes
    ' Si on ne répond pas, il se désynchronise et refuse les commandes suivantes
    If _initializationSent AndAlso _macControl24.HasValue Then
        Try
            ' ? Envoyer un keep-alive en réponse au broadcast
            ' Cela indique au Control24 que nous sommes toujours là
            Dim keepaliveData As Byte() = {0}
            Dim keepAlivePending As New PendingCommand(keepaliveData, 1, isKeepAlive:=True)
            
            _pendingCommands.Enqueue(keepAlivePending)
            _log.Debug("Response to broadcast 0xE1: Keep-alive queued ({0} in queue)", _pendingCommands.Count)
        Catch ex As Exception
            _log.Error("Failed to respond to broadcast 0xE1: {0}", ex.Message)
        End Try
    End If
```

Puis, modifier la section finale :

```vb
' ? FIX: Thread-safe initialization - ONLY ONCE even with multiple broadcasts
SyncLock _initLock
    If Not _initializationSent Then
        ' Save Control24 MAC address (first time only)
        If Not _macControl24.HasValue Then
            _macControl24 = macsrc
            Dim tempEth = _ethHeader
            tempEth.MacDest = macsrc
            _ethHeader = tempEth
            _log.Info("Control24 MAC address saved: {0}", macsrc.ToString())
        End If

        ' Send initialization packets (ONLY ONCE)
        _initializationSent = True
        SendInitializationPackets()
    Else
        _log.Debug("Initialization already sent - handling broadcast normally")  ' ? Changé
    End If
End SyncLock
```

## ?? Résultat attendu

### Avant le fix (comportement actuel)
```
21:23:17 - Broadcast 0xE1 received
21:23:17 - Initialization already sent - ignoring subsequent broadcast ?
21:23:18 - Track 8 LED commands sent... (mais Control24 ne répond plus correctement)
21:23:23 - Broadcast 0xE1 received
21:23:23 - Ignoring... ?
21:23:XX - Track 12+ LEDs ne s'allument plus ?
```

### Après le fix
```
21:23:17 - Broadcast 0xE1 received
21:23:17 - Response to broadcast 0xE1: Keep-alive queued ?
21:23:18 - Track 8 LED commands sent... (Control24 répond normalement ?)
21:23:23 - Broadcast 0xE1 received
21:23:23 - Response to broadcast 0xE1: Keep-alive queued ?
21:23:XX - Track 12+ LEDs s'allument correctement ?
```

## ?? Pourquoi ça va marcher

1. **Le Control24 envoie broadcast** : "Es-tu toujours là ?"
2. **On répond immédiatement** : Keep-alive envoyé en réponse
3. **Le Control24 est satisfait** : Continue d'accepter nos commandes
4. **Pas d'interruption** : La séquence de test LED continue sans problème

## ?? Code complet de la méthode `HandleBroadcastPacket`

```vb
''' <summary>
''' Handle broadcast packets (desk discovery)
''' ? FIX: Répondre aux broadcasts actifs (0xE1) pour maintenir la connexion
''' </summary>
Private Sub HandleBroadcastPacket(packet As C24Packet)
    Dim macsrc = packet.EthHeader.MacSrc

    _log.Debug("HandleBroadcastPacket: MAC={0} IsVendor={1} DataLen={2}",
              macsrc.ToString(), macsrc.IsVendor(), If(packet.PacketData?.Length, -1))

    If macsrc.IsVendor() Then
        ' Parse broadcast data (50 bytes according to logs)
        If packet.PacketData IsNot Nothing AndAlso packet.PacketData.Length >= 34 Then
            Dim bcastBytes(49) As Byte
            Array.Copy(packet.PacketData, 0, bcastBytes, 0, Math.Min(packet.PacketData.Length, 50))

            Dim handle As GCHandle = GCHandle.Alloc(bcastBytes, GCHandleType.Pinned)
            Try
                Dim bcastData = CType(Marshal.PtrToStructure(handle.AddrOfPinnedObject(), GetType(C24BcastData)), C24BcastData)

                _log.Info("Desk detected: {0} at {1}", bcastData.ToString(), macsrc.ToString())

                ' Check broadcast type
                Dim broadcastType = packet.C24Header.C24Cmd
                Select Case broadcastType
                    Case Commands.BroadcastInit  ' 0xE0 - Initial broadcast
                        _log.Debug("Initial broadcast (0xE0) detected")

                    Case Commands.BroadcastActive  ' 0xE1 - Keep-alive broadcast
                        _log.Debug("Active broadcast (0xE1) detected")
                        
                        ' ? CRITICAL FIX: Répondre au broadcast actif pour maintenir la connexion
                        ' Le Control24 envoie des broadcasts (0xE1) toutes les ~6 secondes
                        ' Si on ne répond pas, il se désynchronise et refuse les commandes suivantes
                        If _initializationSent AndAlso _macControl24.HasValue Then
                            Try
                                ' ? Envoyer un keep-alive en réponse au broadcast
                                ' Cela indique au Control24 que nous sommes toujours là
                                Dim keepaliveData As Byte() = {0}
                                Dim keepAlivePending As New PendingCommand(keepaliveData, 1, isKeepAlive:=True)
                                
                                _pendingCommands.Enqueue(keepAlivePending)
                                _log.Debug("Response to broadcast 0xE1: Keep-alive queued ({0} in queue)", _pendingCommands.Count)
                            Catch ex As Exception
                                _log.Error("Failed to respond to broadcast 0xE1: {0}", ex.Message)
                            End Try
                        End If
                End Select

                ' ? FIX: Thread-safe initialization - ONLY ONCE even with multiple broadcasts
                ' The Control24 sends multiple broadcasts (0xE0 init + 0xE1 active) at startup
                ' We must ensure initialization packets are sent ONLY ONCE
                SyncLock _initLock
                    If Not _initializationSent Then
                        ' Save Control24 MAC address (first time only)
                        If Not _macControl24.HasValue Then
                            _macControl24 = macsrc

                            ' Update ethernet header destination
                            Dim tempEth = _ethHeader
                            tempEth.MacDest = macsrc
                            _ethHeader = tempEth

                            _log.Info("Control24 MAC address saved: {0}", macsrc.ToString())
                        End If

                        ' Send initialization packets (ONLY ONCE)
                        _initializationSent = True
                        SendInitializationPackets()
                    Else
                        _log.Debug("Initialization already sent - handling broadcast normally")
                    End If
                End SyncLock

            Finally
                handle.Free()
            End Try
        Else
            _log.Warn("Broadcast packet too short: expected >=34 bytes (minimum), got {0} bytes", If(packet.PacketData?.Length, 0))
        End If
    Else
        _log.Warn("Broadcast from non-Avid vendor: {0}", macsrc.ToString())
    End If
End Sub
```

## ? Test attendu

Après le fix, vous devriez voir dans les logs :

```
21:23:13 - Testing mode ENABLED - Keep-alive timer STOPPED
21:23:13 - Track 7 test started...
21:23:17 - Broadcast 0xE1 detected
21:23:17 - Response to broadcast 0xE1: Keep-alive queued (1 in queue)
21:23:17 - Keep-alive sent successfully
21:23:18 - Track 8 test completed ?
...
21:23:23 - Broadcast 0xE1 detected
21:23:23 - Response to broadcast 0xE1: Keep-alive queued (1 in queue)
21:23:23 - Keep-alive sent successfully
21:23:25 - Track 12 test completed ?
...
21:23:35 - Track 24 test completed ?
21:23:35 - ALL TRACKS TESTED ?
21:23:35 - Testing mode DISABLED - Keep-alive timer RESTARTED
```

---

**Date** : 2026-01-28
**Status** : ?? Ready to implement
**Priority** : ?? CRITICAL FIX
