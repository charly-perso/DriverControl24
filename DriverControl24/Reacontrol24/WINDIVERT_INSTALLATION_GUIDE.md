# ?? GUIDE D'INSTALLATION WinDivert pour Control24

## ?? LIMITATION CRITIQUE DÉCOUVERTE

**WinDivert NE PEUT PAS capturer les paquets Control24 !**

### Pourquoi ?

- **Control24** utilise le protocole **Ethernet 0x885F** (non-IP)
- **WinDivert** fonctionne au **niveau IP** uniquement
- Les paquets Control24 **ne passent pas** par la stack IP de Windows
- WinDivert **ne verra jamais** ces paquets ! ??

```
????????????????????????????????????????????
? Control24 Packets (0x885F)              ?
? ?                                        ?
? Ethernet Layer ? SharpPcap voit ici ?  ?
? ?                                        ?
? [PAS DE IP!] ? WinDivert cherche ici ? ?
? ?                                        ?
? Windows IP Stack                         ?
????????????????????????????????????????????
```

---

## ? SOLUTION : Utiliser SharpPcap (Npcap)

**SharpPcap est LA SEULE solution** qui fonctionne pour Control24 car :

1. ? Capture au **niveau Ethernet** (layer 2)
2. ? Supporte les **protocoles custom** (0x885F)
3. ? Peut **envoyer ET recevoir** des frames Ethernet
4. ? Support **BPF filters** hardware

---

## ?? CORRECTIF DANS LE CODE

Le code a été modifié pour :

1. **Détecter** que WinDivert ne fonctionnera pas
2. **Logger des warnings** clairs
3. **Fallback automatique** vers SharpPcap

### Dans les logs, vous verrez maintenant :

```
?? Initializing WinDivertAdapter (kernel-mode, NO Npcap!)...
?? WinDivert LIMITATION: Cannot capture Ethernet protocol 0x885F (Control24)
?? WinDivert only works with IP-based protocols
?? This adapter will NOT receive Control24 packets!
?? Recommendation: Use SharpPcap (with Npcap) for Control24
?? WinDivert failed: ... - Falling back to SharpPcap
Falling back to SharpPcap...
? SharpPcap initialized successfully
```

---

## ?? RECOMMANDATION FINALE

### Pour Control24 : **UTILISER SharpPcap UNIQUEMENT**

**Pourquoi SharpPcap est obligatoire :**

| Fonctionnalité | SharpPcap | WinDivert |
|----------------|-----------|-----------|
| **Ethernet Layer** | ? OUI | ? NON |
| **Protocole 0x885F** | ? Supporte | ? **Ne voit pas** |
| **Control24** | ? **Fonctionne** | ? **Ne fonctionne PAS** |

---

## ?? ALTERNATIVES (aucune ne fonctionne)

| Solution | Peut capturer 0x885F ? |
|----------|------------------------|
| SharpPcap + Npcap | ? **OUI** (seule option) |
| WinDivert | ? NON (IP layer uniquement) |
| RawSocket | ? NON (IP layer uniquement) |
| NDIS direct | ?? Possible mais trop complexe |

---

## ? INSTALLATION FINALE : SharpPcap + Npcap

### ÉTAPE 1 : Installer Npcap

1. Télécharger : https://npcap.com/#download
2. Installer avec options :
   - ? "WinPcap API-compatible Mode"
   - ? "Install Npcap in WinPcap API-compatible Mode"

### ÉTAPE 2 : Le code est déjà configuré

Le fallback automatique vers SharpPcap est déjà en place dans `Control24d.vb` :

```visualbasic
Try
    _packetCapture = New WinDivertAdapter(_log)
Catch ex As Exception
    ' ? Fallback automatique vers SharpPcap
    _packetCapture = New PacketCaptureManager(_log)
End Try
```

---

## ?? CONCLUSION

**WinDivert n'est PAS compatible avec Control24** en raison du protocole Ethernet custom (0x885F).

**Solution définitive** : 
- ? Utiliser **SharpPcap avec Npcap**
- ? Garder le système anti-doublon dans `C24ClientSendHandler`
- ? Le code fallback automatique est déjà en place

---

## ?? FICHIERS CONCERNÉS

- ? **Modifié** : `WinDivertAdapter.vb` (warnings ajoutés)
- ? **Modifié** : `Control24d.vb` (fallback déjà en place)
- ? **Mis à jour** : `WINDIVERT_INSTALLATION_GUIDE.md`

---

**Status** : ?? **WinDivert NON COMPATIBLE avec Control24**

**Recommandation** : **Utiliser SharpPcap (Npcap) UNIQUEMENT** ?
