# Guide de conversion Python ? VB.NET - ReaControl24

## Résumé de la conversion

? **Fichiers convertis avec succès :**
- `control24common.py` ? `Control24Common.vb`
- `control24map.py` ? `Control24Map.vb`
- `control24d.py` ? `Control24d.vb` (partiel - structures de base)
- `control24osc.py` ? `Control24Osc.vb` (partiel - classes principales)

## Correspondances Python ? VB.NET

### Types de données
| Python | VB.NET |
|--------|--------|
| `dict` | `Dictionary(Of TKey, TValue)` |
| `list` | `List(Of T)` ou `Array` |
| `tuple` | `Tuple(Of T1, T2, ...)` |
| `None` | `Nothing` |
| `True/False` | `True/False` |
| `str` | `String` |
| `bytes` | `Byte()` |

### Structures de contrôle
| Python | VB.NET |
|--------|--------|
| `if ... elif ... else` | `If ... ElseIf ... Else` |
| `for item in collection:` | `For Each item In collection` |
| `for i in range(n):` | `For i As Integer = 0 To n-1` |
| `while condition:` | `While condition` |
| `try ... except ... finally` | `Try ... Catch ... Finally` |

### Classes et objets
```python
# Python
class MyClass(BaseClass):
    def __init__(self, param):
        super().__init__()
        self.value = param
    
    def method(self):
        return self.value
```

```vb
' VB.NET
Public Class MyClass
    Inherits BaseClass
    
    Private _value As Integer
    
    Public Sub New(param As Integer)
        MyBase.New()
        _value = param
    End Sub
    
    Public Function Method() As Integer
        Return _value
    End Function
End Class
```

### Bibliothèques converties

#### netifaces ? System.Net.NetworkInformation
```python
# Python
import netifaces
interfaces = netifaces.interfaces()
addrs = netifaces.ifaddresses(interface)
```

```vb
' VB.NET
Imports System.Net.NetworkInformation
Dim interfaces = NetworkInterface.GetAllNetworkInterfaces()
Dim ipProps = nic.GetIPProperties()
```

#### ctypes ? System.Runtime.InteropServices
```python
# Python
from ctypes import Structure, c_uint16, c_ubyte

class MyStruct(Structure):
    _pack_ = 1
    _fields_ = [
        ("field1", c_uint16),
        ("field2", c_ubyte * 3)
    ]
```

```vb
' VB.NET
Imports System.Runtime.InteropServices

<StructLayout(LayoutKind.Sequential, Pack:=1)>
Public Structure MyStruct
    Public Field1 As UInt16
    <MarshalAs(UnmanagedType.ByValArray, SizeConst:=3)>
    Public Field2 As Byte()
End Structure
```

#### logging ? Custom Logger
```python
# Python
import logging
LOG = logging.getLogger(__name__)
LOG.info("Message")
LOG.debug("Debug message")
```

```vb
' VB.NET
Dim LOG As New Logger("MyApp", ".\logs", debug:=True)
LOG.Info("Message")
LOG.Debug("Debug message")
```

## Défis de la conversion

### 1. Gestion des octets et endianness
Le Python utilise `struct.pack/unpack` pour la conversion endianness. En VB.NET :

```vb
' Convertir UInt16 de little-endian à big-endian
Private Shared Function SwapBytes(value As UInt16) As UInt16
    Return CUShort((value >> 8) Or (value << 8))
End Function

' Convertir UInt32
Private Shared Function SwapBytes(value As UInt32) As UInt32
    Return ((value And &HFF) << 24) Or
           ((value And &HFF00) << 8) Or
           ((value And &HFF0000UI) >> 8) Or
           ((value And &HFF000000UI) >> 24)
End Function
```

### 2. Structures mutables vs immutables
En VB.NET, les structures sont des types valeur. Problème :
```vb
' ? Ne fonctionne pas
myPacket.C24Header.NumBytes = 100  ' Erreur!

' ? Solution
Dim tempHeader = myPacket.C24Header
tempHeader.NumBytes = 100
myPacket.C24Header = tempHeader
```

### 3. Delegates et callbacks
Python utilise des fonctions comme paramètres. VB.NET utilise des delegates :

```python
# Python
def callback(data):
    print(data)

def register_callback(func):
    func("test")

register_callback(callback)
```

```vb
' VB.NET
Public Delegate Sub CallbackDelegate(data As String)

Public Sub Callback(data As String)
    Console.WriteLine(data)
End Sub

Public Sub RegisterCallback(func As CallbackDelegate)
    func("test")
End Sub

RegisterCallback(AddressOf Callback)
```

### 4. Gestion des threads
```python
# Python
import threading
thread = threading.Thread(target=my_function, name='worker')
thread.daemon = True
thread.start()
```

```vb
' VB.NET
Dim thread As New Thread(AddressOf MyFunction)
thread.Name = "worker"
thread.IsBackground = True
thread.Start()
```

## Fonctionnalités à implémenter

### 1. Capture de paquets réseau (WinPcap)
Ajouter NuGet: `SharpPcap`
```vb
Imports SharpPcap
Imports PacketDotNet

Dim device = CaptureDeviceList.Instance(0)
device.Open(DeviceMode.Promiscuous, 1000)
device.OnPacketArrival += AddressOf OnPacketArrival
device.StartCapture()
```

### 2. Communication OSC complète
Le package `Rug.Osc` est déjà référencé. Implémentation :
```vb
Imports Rug.Osc

' Créer un client OSC
Dim client As New OscSender(IPAddress.Parse("127.0.0.1"), 9000)
client.Connect()

' Envoyer un message
Dim msg As New OscMessage("/track/1/volume", 0.75)
client.Send(msg)
```

### 3. Serveur TCP pour les clients
```vb
Dim listener As New TcpListener(IPAddress.Any, 9123)
listener.Start()

Dim clientTask = Task.Run(Sub()
    While True
        Dim client = listener.AcceptTcpClient()
        ' Traiter le client
    End While
End Sub)
```

## Tests suggérés

### 1. Test de conversion endianness
```vb
<TestMethod>
Public Sub Test_EndianSwap()
    Dim value As UInt16 = &H1234
    Dim swapped = SwapBytes(value)
    Assert.AreEqual(&H3412, swapped)
End Sub
```

### 2. Test de structure marshalling
```vb
<TestMethod>
Public Sub Test_StructureSize()
    Dim size = Marshal.SizeOf(GetType(C24Header))
    Assert.AreEqual(14, size)
End Sub
```

### 3. Test de réseau
```vb
<TestMethod>
Public Sub Test_NetworkDiscovery()
    Dim helper As New NetworkHelper()
    Dim defaultNet = helper.GetDefault()
    Assert.IsNotNull(defaultNet)
End Sub
```

## Performance

### Optimisations suggérées
1. **Pooling d'objets** : Réutiliser les packets au lieu de les recréer
2. **Async/Await** : Pour les opérations I/O
3. **Span<T>** : Pour manipuler les buffers sans allocation
4. **ArrayPool<T>** : Pour réutiliser les tableaux d'octets

```vb
' Exemple avec Span
Public Function ParsePacket(data As ReadOnlySpan(Of Byte)) As C24Packet
    Dim ethHeader = MemoryMarshal.Read(Of EthHeader)(data.Slice(0, 14))
    Dim c24Header = MemoryMarshal.Read(Of C24Header)(data.Slice(14, 14))
    ' ...
End Function
```

## Ressources

- [Documentation .NET](https://docs.microsoft.com/dotnet/)
- [SharpPcap](https://github.com/chmorgan/sharppcap)
- [Rug.Osc](https://bitbucket.org/rugcode/rug.osc/)
- [System.Runtime.InteropServices](https://docs.microsoft.com/dotnet/api/system.runtime.interopservices)

## Contact et support

Pour toute question sur cette conversion, consultez :
- Le code source original Python
- La documentation Control24
- Les spécifications OSC
