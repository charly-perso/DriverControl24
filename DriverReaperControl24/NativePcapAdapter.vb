''' <summary>
''' Native Npcap adapter using P/Invoke - NO SharpPcap dependency
''' Direct access to wpcap.dll for maximum performance and control
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' 
''' Requires: Npcap driver installed (wpcap.dll)
''' </summary>

Imports System.Net.NetworkInformation
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Text

Namespace Control24

    ''' <summary>
    ''' Native Npcap/WinPcap adapter using direct P/Invoke calls
    ''' No third-party libraries needed - just wpcap.dll
    ''' </summary>
    Public Class NativePcapAdapter
        Implements INetworkCapture

        Private _handle As IntPtr = IntPtr.Zero       ' RX capture + CMD TX
        Private _handleAck As IntPtr = IntPtr.Zero    ' ✅ ACK TX dédié
        Private _isCapturing As Boolean
        Private _captureThread As Thread
        Private _captureHandler As Action(Of Byte())
        Private disposedValue As Boolean

        ' bpf_program structure (for filter compilation)
        <StructLayout(LayoutKind.Sequential)>
        Private Structure BpfProgram
            Public bf_len As UInteger
            Public bf_insns As IntPtr
        End Structure

        ' P/Invoke declarations for wpcap.dll (Npcap/WinPcap)
        <DllImport("wpcap.dll", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Function pcap_findalldevs(ByRef alldevs As IntPtr, errbuf As StringBuilder) As Integer
        End Function

        <DllImport("wpcap.dll", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Sub pcap_freealldevs(alldevs As IntPtr)
        End Sub

        <DllImport("wpcap.dll", CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
        Private Shared Function pcap_open_live(device As String, snaplen As Integer, promisc As Integer,
                                               timeout As Integer, errbuf As StringBuilder) As IntPtr
        End Function

        ' ✅ NEW: pcap_open with flags support (Npcap specific - Windows only)
        ' This allows us to use PCAP_OPENFLAG_NOCAPTURE_LOCAL to prevent loopback capture
        <DllImport("wpcap.dll", CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
        Private Shared Function pcap_open(source As String, snaplen As Integer, flags As Integer,
                                          read_timeout As Integer, auth As IntPtr, errbuf As StringBuilder) As IntPtr
        End Function

        ' Npcap flags for pcap_open
        Private Const PCAP_OPENFLAG_PROMISCUOUS As Integer = 1         ' Promiscuous mode
        Private Const PCAP_OPENFLAG_NOCAPTURE_LOCAL As Integer = 8     ' Don't capture packets sent by this machine

        Public ReadOnly Property IsCapturing As Boolean Implements INetworkCapture.IsCapturing
            Get
                Return _isCapturing   ' ✅
            End Get
        End Property

        <DllImport("wpcap.dll", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Sub pcap_close(handle As IntPtr)
        End Sub

        <DllImport("wpcap.dll", CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
        Private Shared Function pcap_compile(handle As IntPtr, ByRef fp As BpfProgram, filter As String,
                                             optimize As Integer, netmask As UInteger) As Integer
        End Function

        <DllImport("wpcap.dll", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Function pcap_setfilter(handle As IntPtr, ByRef fp As BpfProgram) As Integer
        End Function

        <DllImport("wpcap.dll", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Sub pcap_freecode(ByRef fp As BpfProgram)
        End Sub

        <DllImport("wpcap.dll", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Function pcap_next_ex(handle As IntPtr, ByRef header As IntPtr, ByRef pktData As IntPtr) As Integer
        End Function

        ' ✅ FIX: Use pcap_inject instead of pcap_sendpacket to avoid duplicate sends
        ' pcap_sendpacket can cause loopback duplicates on some Windows configurations
        ' pcap_inject sends the packet ONCE without triggering capture loopback
        <DllImport("wpcap.dll", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Function pcap_inject(handle As IntPtr, data As Byte(), size As Integer) As Integer
        End Function

        <DllImport("wpcap.dll", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Function pcap_sendpacket(handle As IntPtr, data As Byte(), size As Integer) As Integer
        End Function

        <DllImport("wpcap.dll", CallingConvention:=CallingConvention.Cdecl)>
        Private Shared Sub pcap_breakloop(handle As IntPtr)
        End Sub

        <DllImport("wpcap.dll", CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
        Private Shared Function pcap_geterr(handle As IntPtr) As IntPtr
        End Function

        ' pcap_pkthdr structure
        <StructLayout(LayoutKind.Sequential)>
        Private Structure PcapPkthdr
            Public ts_sec As UInteger    ' timestamp seconds
            Public ts_usec As UInteger   ' timestamp microseconds
            Public caplen As UInteger    ' length of portion present
            Public len As UInteger       ' length of this packet (off wire)
        End Structure

        ' pcap_if structure (device list)
        <StructLayout(LayoutKind.Sequential)>
        Private Structure PcapIf
            Public [next] As IntPtr
            Public name As IntPtr
            Public description As IntPtr
            Public addresses As IntPtr
            Public flags As UInteger
        End Structure

        ''' <summary>
        ''' Event raised when packet received
        ''' </summary>
        Public Event PacketReceived As EventHandler(Of GenericPacketEventArgs) Implements INetworkCapture.PacketReceived

        Public Sub New()

        End Sub


        ''' <summary>
        ''' Get available network devices
        ''' </summary>
        Public Function GetDevices() As List(Of GenericNetworkDevice) Implements INetworkCapture.GetDevices
            Dim devices As New List(Of GenericNetworkDevice)
            Dim alldevs As IntPtr = IntPtr.Zero
            Dim errbuf As New StringBuilder(256)

            Try
                ' Get all devices via native pcap
                If pcap_findalldevs(alldevs, errbuf) = -1 Then
                    Throw New Exception($"Failed to find devices: {errbuf}")
                End If

                ' Iterate through devices
                Dim current = alldevs
                While current <> IntPtr.Zero
                    Dim pcapIf = Marshal.PtrToStructure(Of PcapIf)(current)

                    Dim dev As New GenericNetworkDevice With {
                        .Name = If(pcapIf.name <> IntPtr.Zero, Marshal.PtrToStringAnsi(pcapIf.name), ""),
                        .Description = If(pcapIf.description <> IntPtr.Zero, Marshal.PtrToStringAnsi(pcapIf.description), ""),
                        .FriendlyName = If(pcapIf.description <> IntPtr.Zero, Marshal.PtrToStringAnsi(pcapIf.description), ""),
                        .IpAddresses = New List(Of String)
                    }

                    ' Try to get MAC from system
                    dev.MacAddress = GetMacAddress(dev.Name)

                    devices.Add(dev)
                    current = pcapIf.next
                End While


                Return devices

            Finally
                If alldevs <> IntPtr.Zero Then
                    pcap_freealldevs(alldevs)
                End If
            End Try
        End Function

        ''' <summary>
        ''' Get MAC address from system NetworkInterface
        ''' </summary>
        Private Function GetMacAddress(deviceName As String) As String
            Try
                For Each nic In NetworkInterface.GetAllNetworkInterfaces()
                    If deviceName.Contains(nic.Id) OrElse deviceName.Contains(nic.Name) Then
                        Return BitConverter.ToString(nic.GetPhysicalAddress().GetAddressBytes()).Replace("-", ":")
                    End If
                Next
            Catch
                ' Ignore
            End Try
            Return Nothing
        End Function

        ''' <summary>
        ''' Open device for capture
        ''' </summary>
        Public Sub OpenDevice(deviceName As String, macAddress As String) Implements INetworkCapture.OpenDevice
            Dim errbuf As New StringBuilder(256)

            Try

                ' Find matching device
                Dim alldevs As IntPtr = IntPtr.Zero
                If pcap_findalldevs(alldevs, errbuf) = -1 Then
                    Throw New Exception($"Failed to find devices: {errbuf}")
                End If

                Try
                    Dim current = alldevs
                    Dim targetDevice As String = Nothing
                    Dim deviceList As New List(Of String)

                    While current <> IntPtr.Zero
                        Dim pcapIf = Marshal.PtrToStructure(Of PcapIf)(current)
                        Dim name = If(pcapIf.name <> IntPtr.Zero, Marshal.PtrToStringAnsi(pcapIf.name), "")
                        Dim desc = If(pcapIf.description <> IntPtr.Zero, Marshal.PtrToStringAnsi(pcapIf.description), "")

                        deviceList.Add($"{desc} -> {name}")

                        ' Try to match by MAC address first (most reliable)
                        Dim devMac = GetMacAddress(name)
                        If devMac IsNot Nothing AndAlso devMac.Equals(macAddress, StringComparison.OrdinalIgnoreCase) Then
                            targetDevice = name
                            Exit While
                        End If

                        ' Try to match by description containing the device name
                        If desc IsNot Nothing AndAlso desc.Equals(deviceName, StringComparison.OrdinalIgnoreCase) Then
                            targetDevice = name
                            Exit While
                        End If

                        current = pcapIf.next
                    End While



                    If targetDevice Is Nothing Then
                        Throw New Exception($"Device not found: {deviceName} (MAC: {macAddress}). Available devices: {String.Join(", ", deviceList)}")
                    End If

                    ' ✅ ULTIMATE FIX: Use pcap_open with PCAP_OPENFLAG_NOCAPTURE_LOCAL
                    ' This flag tells Npcap to NOT capture packets sent by this machine
                    ' Format: rpcap://\Device\NPF_{GUID} for local device
                    Dim flags = PCAP_OPENFLAG_PROMISCUOUS Or PCAP_OPENFLAG_NOCAPTURE_LOCAL


                    ' ✅ Handle principal — accès direct driver, sans rpcap://
                    _handle = pcap_open(targetDevice, 65535, flags, 1, IntPtr.Zero, errbuf)   ' ✅ 1ms au lieu de 50ms
                    If _handle = IntPtr.Zero Then
                        ' ⚠️ Fallback : pcap_open_live sans NOCAPTURE_LOCAL
                        ' Les TX loopbacks sont filtrés par BPF ("not ether src {mac}")
                        errbuf.Clear()
                        _handle = pcap_open_live(targetDevice, 65535, 1, 1, errbuf)   ' ✅ 1ms
                        If _handle = IntPtr.Zero Then
                            Throw New Exception($"Failed to open device: {errbuf}")
                        End If
                    End If

                    ' ✅ Handle ACK dédié (même flags → NOCAPTURE_LOCAL)
                    _handleAck = pcap_open(targetDevice, 65535, flags, 1, IntPtr.Zero, errbuf)
                    If _handleAck = IntPtr.Zero Then
                    End If

                Finally
                    pcap_freealldevs(alldevs)
                End Try

            Catch ex As Exception
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Set packet filter (BPF)
        ''' </summary>
        Public Sub SetFilter(macAddress As String) Implements INetworkCapture.SetFilter
            If _handle = IntPtr.Zero Then
                Throw New InvalidOperationException("Device not opened")
            End If

            Dim fp As BpfProgram
            fp.bf_len = 0
            fp.bf_insns = IntPtr.Zero

            Try
                ' ✅ FIX: Exclude packets sent by US (our TX) - prevents duplicates from TX capture
                ' Filter: protocol 0x885f AND (destination is our MAC OR broadcast) AND source is NOT our MAC
                Dim filterStr = $"ether proto 0x885f and (ether dst {macAddress} or ether dst ff:ff:ff:ff:ff:ff) and not ether src {macAddress}"


                ' Compile the filter
                If pcap_compile(_handle, fp, filterStr, 1, 0) < 0 Then
                    Dim errPtr = pcap_geterr(_handle)
                    Dim errMsg = If(errPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(errPtr), "Unknown error")

                    ' ⚠️ Fallback: if BPF filter fails, log error and try simpler filter

                    ' Fallback filter (less optimal but should work)
                    filterStr = $"ether proto 0x885f and (ether dst {macAddress} or ether dst ff:ff:ff:ff:ff:ff)"

                    If pcap_compile(_handle, fp, filterStr, 1, 0) < 0 Then
                        errPtr = pcap_geterr(_handle)
                        errMsg = If(errPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(errPtr), "Unknown error")
                        Throw New Exception($"Failed to compile filter: {errMsg}")
                    End If

                Else
                End If

                ' Set the compiled filter
                If pcap_setfilter(_handle, fp) < 0 Then
                    Dim errPtr = pcap_geterr(_handle)
                    Dim errMsg = If(errPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(errPtr), "Unknown error")
                    pcap_freecode(fp) ' Free the compiled filter before throwing
                    Throw New Exception($"Failed to set filter: {errMsg}")
                End If


                ' Free the compiled filter
                pcap_freecode(fp)

            Catch ex As Exception
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Start packet capture
        ''' </summary>
        Public Sub StartCapture(handler As Action(Of Byte())) Implements INetworkCapture.StartCapture
            If _handle = IntPtr.Zero Then
                Throw New InvalidOperationException("Device not opened")
            End If

            _captureHandler = handler
            _isCapturing = True

            _captureThread = New Thread(AddressOf CaptureLoop) With {
                .Name = "NativePcapCapture",
                .IsBackground = True,
                .Priority = ThreadPriority.Highest
            }
            _captureThread.Start()

        End Sub

        ' ✅ Buffer ACK pré-alloué partagé — écrit et lu uniquement depuis CaptureLoop (1 seul thread)
        Private _ackBuffer As Byte() = Nothing
        Private Const CMDTYPE_COMMAND As Byte = &H0

        ''' <summary>
        ''' Fournit le buffer ACK pré-alloué (appelé après init session).
        ''' CaptureLoop l'utilise directement pour 0 allocation.
        ''' </summary>
        Public Sub SetAckBuffer(ackBuffer As Byte())
            _ackBuffer = ackBuffer
        End Sub

        ''' <summary>
        ''' Capture loop
        ''' </summary>
        Private Sub CaptureLoop()
            While _isCapturing
                Dim header As IntPtr = IntPtr.Zero
                Dim rawPktData As IntPtr = IntPtr.Zero

                If pcap_next_ex(_handle, header, rawPktData) <= 0 Then Continue While
                If header = IntPtr.Zero OrElse rawPktData = IntPtr.Zero Then Continue While

                ' ✅ ACK ULTRA-RAPIDE — lecture directe depuis la mémoire non-managée
                ' 0 Marshal.Copy, 0 allocation, 0 appel managé → ~0.01ms
                If _ackBuffer IsNot Nothing AndAlso
                   Marshal.ReadByte(rawPktData, 28) = CMDTYPE_COMMAND Then

                    ' Copie SendCounter (offset 18-21) → ReceivedCounter dans l'ACK (offset 22-25)
                    _ackBuffer(22) = Marshal.ReadByte(rawPktData, 18)
                    _ackBuffer(23) = Marshal.ReadByte(rawPktData, 19)
                    _ackBuffer(24) = Marshal.ReadByte(rawPktData, 20)
                    _ackBuffer(25) = Marshal.ReadByte(rawPktData, 21)

                    ' ✅ pcap_inject directement — avant toute allocation managée !
                    Dim h = If(_handleAck <> IntPtr.Zero, _handleAck, _handle)
                    pcap_inject(h, _ackBuffer, _ackBuffer.Length)
                End If

                ' ✅ Traitement différé — allocation acceptable (chemin asynchrone)
                Dim pkthdr = Marshal.PtrToStructure(Of PcapPkthdr)(header)
                Dim data(CInt(pkthdr.caplen) - 1) As Byte
                Marshal.Copy(rawPktData, data, 0, CInt(pkthdr.caplen))
                _captureHandler?.Invoke(data)
            End While
        End Sub

        ''' <summary>
        ''' Stop capture
        ''' </summary>
        Public Sub StopCapture() Implements INetworkCapture.StopCapture
            If Not _isCapturing Then Return

            _isCapturing = False

            If _handle <> IntPtr.Zero Then
                pcap_breakloop(_handle)
            End If

            _captureThread?.Join(1000)
        End Sub



        ''' ✅ ACK urgent — handle dédié, 0 contention, 0 lock, 0 spin
        Public Function SendAckPacket(data As Byte()) As Integer Implements INetworkCapture.SendAckPacket
            Dim h = If(_handleAck <> IntPtr.Zero, _handleAck, _handle)
            Return If(pcap_inject(h, data, data.Length) > 0, data.Length, -1)
        End Function

        ''' CMD normal — handle principal, 0 lock (un seul thread CMD actif)
        Public Function SendPacket(data As Byte()) As Integer Implements INetworkCapture.SendPacket
            If _handle = IntPtr.Zero Then Throw New InvalidOperationException("Device not opened")
            Return If(pcap_inject(_handle, data, data.Length) > 0, data.Length, -1)
        End Function

        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not disposedValue Then
                If disposing Then
                    ' TODO: supprimer l'état managé (objets managés)
                End If

                ' TODO: libérer les ressources non managées (objets non managés) et substituer le finaliseur
                ' TODO: affecter aux grands champs une valeur null
                If _handleAck <> IntPtr.Zero Then
                    pcap_close(_handleAck)
                    _handleAck = IntPtr.Zero
                End If
                If _handle <> IntPtr.Zero Then
                    pcap_close(_handle)
                    _handle = IntPtr.Zero
                End If
                disposedValue = True
            End If
        End Sub

        ' ' TODO: substituer le finaliseur uniquement si 'Dispose(disposing As Boolean)' a du code pour libérer les ressources non managées
        ' Protected Overrides Sub Finalize()
        '     ' Ne changez pas ce code. Placez le code de nettoyage dans la méthode 'Dispose(disposing As Boolean)'
        '     Dispose(disposing:=False)
        '     MyBase.Finalize()
        ' End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            ' Ne changez pas ce code. Placez le code de nettoyage dans la méthode 'Dispose(disposing As Boolean)'
            Dispose(disposing:=True)
            GC.SuppressFinalize(Me)
        End Sub
    End Class

End Namespace

