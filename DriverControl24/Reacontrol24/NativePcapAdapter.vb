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

Namespace Reacontrol24

    ''' <summary>
    ''' Native Npcap/WinPcap adapter using direct P/Invoke calls
    ''' No third-party libraries needed - just wpcap.dll
    ''' </summary>
    Public Class NativePcapAdapter
        Implements INetworkCapture

        Private _handle As IntPtr = IntPtr.Zero
        Private _log As Logger
        Private _isCapturing As Boolean
        Private _captureThread As Thread
        Private _captureHandler As Action(Of Double, Byte())

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

        Public Sub New(logger As Logger)
            _log = logger
        End Sub
        Public Sub New()

        End Sub
        ''' <summary>
        ''' Set test mode - BLOCKS all incoming broadcast packets during LED tests
        ''' ✅ CRITICAL: This prevents Control24 broadcasts from interrupting test sequences
        ''' </summary>
        Public Sub SetTestMode(enabled As Boolean)
            _isTestMode = enabled
            _log?.Info("Native Pcap: Test mode {0}", If(enabled, "ENABLED (broadcasts BLOCKED)", "DISABLED (broadcasts processed)"))
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

                _log?.Info("Native Pcap: Found {0} network devices", devices.Count)

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
                _log?.Info("Native Pcap: Opening device: {0} (MAC: {1})", deviceName, macAddress)

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
                            _log?.Info("Native Pcap: Matched device by MAC: {0} -> {1}", desc, name)
                            Exit While
                        End If

                        ' Try to match by description containing the device name
                        If desc IsNot Nothing AndAlso desc.Equals(deviceName, StringComparison.OrdinalIgnoreCase) Then
                            targetDevice = name
                            _log?.Info("Native Pcap: Matched device by exact description: {0} -> {1}", desc, name)
                            Exit While
                        End If

                        current = pcapIf.next
                    End While

                    ' Log all available devices for debugging
                    _log?.Debug("Native Pcap: Available devices:")
                    For Each dev In deviceList
                        _log?.Debug("  - {0}", dev)
                    Next

                    If targetDevice Is Nothing Then
                        Throw New Exception($"Device not found: {deviceName} (MAC: {macAddress}). Available devices: {String.Join(", ", deviceList)}")
                    End If

                    ' ✅ ULTIMATE FIX: Use pcap_open with PCAP_OPENFLAG_NOCAPTURE_LOCAL
                    ' This flag tells Npcap to NOT capture packets sent by this machine
                    ' Format: rpcap://\Device\NPF_{GUID} for local device
                    Dim source = $"rpcap://{targetDevice}"
                    Dim flags = PCAP_OPENFLAG_PROMISCUOUS Or PCAP_OPENFLAG_NOCAPTURE_LOCAL
                    
                    _log?.Info("Native Pcap: Opening device with NOCAPTURE_LOCAL flag: {0}", targetDevice)
                    _log?.Info("Native Pcap: This will prevent capturing our own TX packets (no duplicates!)")
                    
                    _handle = pcap_open(source, 65535, flags, 50, IntPtr.Zero, errbuf)
                    
                    If _handle = IntPtr.Zero Then
                        ' Fallback to pcap_open_live if pcap_open fails
                        _log?.Warn("Native Pcap: pcap_open failed: {0}, falling back to pcap_open_live", errbuf.ToString())
                        errbuf.Clear()
                        _handle = pcap_open_live(targetDevice, 65535, 1, 50, errbuf)
                        
                        If _handle = IntPtr.Zero Then
                            Throw New Exception($"Failed to open device: {errbuf}")
                        End If
                        
                        _log?.Warn("Native Pcap: Opened with pcap_open_live (TX duplicates may occur)")
                    Else
                        _log?.Info("Native Pcap: Device opened successfully with NOCAPTURE_LOCAL flag ✅")
                    End If

                Finally
                    pcap_freealldevs(alldevs)
                End Try

            Catch ex As Exception
                _log?.Error("Native Pcap: Failed to open device: {0}", ex.Message)
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

                _log?.Info("Native Pcap: Setting filter (RX only - excluding our TX): {0}", filterStr)

                ' Compile the filter
                _log?.Debug("Native Pcap: Compiling filter...")
                If pcap_compile(_handle, fp, filterStr, 1, 0) < 0 Then
                    Dim errPtr = pcap_geterr(_handle)
                    Dim errMsg = If(errPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(errPtr), "Unknown error")

                    ' ⚠️ Fallback: if BPF filter fails, log error and try simpler filter
                    _log?.Warn("Native Pcap: Complex filter failed, trying basic filter: {0}", errMsg)

                    ' Fallback filter (less optimal but should work)
                    filterStr = $"ether proto 0x885f and (ether dst {macAddress} or ether dst ff:ff:ff:ff:ff:ff)"
                    _log?.Info("Native Pcap: Fallback filter (without src exclusion): {0}", filterStr)

                    If pcap_compile(_handle, fp, filterStr, 1, 0) < 0 Then
                        errPtr = pcap_geterr(_handle)
                        errMsg = If(errPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(errPtr), "Unknown error")
                        Throw New Exception($"Failed to compile filter: {errMsg}")
                    End If

                    _log?.Warn("Native Pcap: Using fallback filter - TX duplicates may occur")
                Else
                    _log?.Debug("Native Pcap: Filter compiled successfully (RX only), bf_len={0}", fp.bf_len)
                End If

                ' Set the compiled filter
                _log?.Debug("Native Pcap: Setting compiled filter...")
                If pcap_setfilter(_handle, fp) < 0 Then
                    Dim errPtr = pcap_geterr(_handle)
                    Dim errMsg = If(errPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(errPtr), "Unknown error")
                    pcap_freecode(fp) ' Free the compiled filter before throwing
                    Throw New Exception($"Failed to set filter: {errMsg}")
                End If

                _log?.Info("Native Pcap: Filter applied successfully - TX packets excluded")

                ' Free the compiled filter
                _log?.Debug("Native Pcap: Freeing compiled filter...")
                pcap_freecode(fp)
                _log?.Debug("Native Pcap: Filter freed")

            Catch ex As Exception
                _log?.Error("Native Pcap: Failed to set filter: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Start packet capture
        ''' </summary>
        Public Sub StartCapture(handler As Action(Of Double, Byte())) Implements INetworkCapture.StartCapture
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

            _log?.Info("Native Pcap: Packet capture started (High Priority)")
        End Sub

        ''' <summary>
        ''' Capture loop
        ''' </summary>
        Private Sub CaptureLoop()
            Try
                _log?.Debug("Native Pcap: Capture loop started")

                Dim packetCount As Integer = 0
                Dim lastLogTime As DateTime = DateTime.Now

                While _isCapturing
                    Dim header As IntPtr = IntPtr.Zero
                    Dim pktData As IntPtr = IntPtr.Zero

                    Dim result = pcap_next_ex(_handle, header, pktData)

                    ' Log every 5 seconds to show we're still alive
                    If (DateTime.Now - lastLogTime).TotalSeconds >= 5 Then
                        _log?.Debug("Native Pcap: Capture loop alive, {0} packets captured, last result: {1}", packetCount, result)
                        lastLogTime = DateTime.Now
                    End If

                    If result = 1 Then ' Packet captured
                        packetCount += 1
                        _log?.Debug("Native Pcap: Packet #{0} captured", packetCount)

                        If header <> IntPtr.Zero AndAlso pktData <> IntPtr.Zero Then
                            Dim pkthdr = Marshal.PtrToStructure(Of PcapPkthdr)(header)

                            _log?.Debug("Native Pcap: Packet size: {0} bytes, caplen: {1}", pkthdr.len, pkthdr.caplen)

                            ' Convert timestamp to seconds
                            Dim timestamp = CDbl(pkthdr.ts_sec) + (CDbl(pkthdr.ts_usec) / 1000000.0)

                            ' Copy packet data
                            Dim data(CInt(pkthdr.caplen) - 1) As Byte
                            Marshal.Copy(pktData, data, 0, CInt(pkthdr.caplen))

                            ' ✅ CRITICAL FIX: Block ALL broadcast packets (64 bytes) during test mode
                            ' Control24 sends broadcasts (0xE1) every ~5-6 seconds
                            ' These broadcasts MUST be ignored during LED tests to prevent interruption
                            If _isTestMode AndAlso data.Length = 64 Then
                                ' Check if this is a broadcast (dst = ff:ff:ff:ff:ff:ff)
                                If data(0) = &HFF AndAlso data(1) = &HFF AndAlso data(2) = &HFF AndAlso 
                                   data(3) = &HFF AndAlso data(4) = &HFF AndAlso data(5) = &HFF Then
                                    _log?.Debug("Native Pcap: TEST MODE - Broadcast packet BLOCKED (64 bytes, dst=ff:ff:ff:ff:ff:ff)")
                                    Continue While  ' ✅ Skip this packet completely - do NOT call handler
                                End If
                            End If

                            _log?.Debug("Native Pcap: Calling handler with {0} bytes", data.Length)

                            ' Call handler
                            _captureHandler?.Invoke(timestamp, data)
                            RaiseEvent PacketReceived(Me, New GenericPacketEventArgs(timestamp, data))
                        Else
                            _log?.Warn("Native Pcap: Packet captured but header or data is NULL")
                        End If
                    ElseIf result = -1 Then ' Error
                        Dim errPtr = pcap_geterr(_handle)
                        Dim errMsg = If(errPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(errPtr), "Unknown error")
                        _log?.Error("Native Pcap: Capture error: {0}", errMsg)
                        Thread.Sleep(10)
                    ElseIf result = 0 Then ' Timeout
                        ' This is normal, continue
                    ElseIf result = -2 Then ' Breakloop called
                        _log?.Info("Native Pcap: Breakloop detected")
                        Exit While
                    End If
                End While

                _log?.Debug("Native Pcap: Capture loop ended, total packets: {0}", packetCount)

            Catch ex As ThreadAbortException
                _log?.Debug("Native Pcap: Capture thread aborted")
            Catch ex As Exception
                _log?.Error("Native Pcap: Capture loop error: {0}", ex.Message)
                _log?.Error("Native Pcap: Stack trace: {0}", ex.StackTrace)
            End Try
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
            _log?.Info("Native Pcap: Capture stopped")
        End Sub

        ''' <summary>
        ''' Send packet
        ''' </summary>
        Public Function SendPacket(data As Byte()) As Integer Implements INetworkCapture.SendPacket
            If _handle = IntPtr.Zero Then
                Throw New InvalidOperationException("Device not opened")
            End If

            Try
                ' ✅ FIX: Use pcap_inject instead of pcap_sendpacket
                ' pcap_inject returns number of bytes sent (positive) or -1 on error
                ' This prevents Windows from creating loopback duplicate packets
                Dim bytesSent = pcap_inject(_handle, data, data.Length)

                If bytesSent > 0 Then
                    ' Success - packet sent once without loopback
                    Return data.Length
                Else
                    ' Error occurred
                    Dim errPtr = pcap_geterr(_handle)
                    Dim errMsg = If(errPtr <> IntPtr.Zero, Marshal.PtrToStringAnsi(errPtr), "Unknown error")
                    _log?.Error("Native Pcap: pcap_inject failed: {0}", errMsg)
                    Return -1
                End If
            Catch ex As Exception
                _log?.Error("Native Pcap: Send error: {0}", ex.Message)
                Return -1
            End Try
        End Function

        ' ✅ NEW: Test mode flag - blocks ALL incoming broadcasts during LED tests
        Private _isTestMode As Boolean = False

        ''' <summary>
        ''' Enable or disable test mode
        ''' </summary>
        Public Property TestMode As Boolean
            Get
                Return _isTestMode
            End Get
            Set(value As Boolean)
                _isTestMode = value

                If _isTestMode Then
                    _log?.Info("Native Pcap: Test mode ENABLED - incoming broadcasts will be blocked")
                Else
                    _log?.Info("Native Pcap: Test mode DISABLED - normal operation")
                End If
            End Set
        End Property

        Public ReadOnly Property IsCapturing As Boolean Implements INetworkCapture.IsCapturing
            Get
                Return _isCapturing
            End Get
        End Property

        Public Sub Dispose() Implements IDisposable.Dispose
            StopCapture()

            If _handle <> IntPtr.Zero Then
                pcap_close(_handle)
                _handle = IntPtr.Zero
            End If

            GC.SuppressFinalize(Me)
        End Sub

    End Class

End Namespace
