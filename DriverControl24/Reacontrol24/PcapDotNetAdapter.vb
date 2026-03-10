''' <summary>
''' Pcap.Net adapter for Control24 - Modern alternative to SharpPcap
''' High-performance Ethernet layer packet capture with better API
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' 
''' Requires: Pcap.Net NuGet package + Npcap driver
''' </summary>

Imports System.Net.NetworkInformation
Imports System.Threading
Imports PcapDotNet.Core
Imports PcapDotNet.Packets
Imports PcapDotNet.Packets.Ethernet

Namespace Reacontrol24

    ''' <summary>
    ''' Pcap.Net adapter implementing INetworkCapture
    ''' Modern, high-performance alternative to SharpPcap
    ''' </summary>
    Public Class PcapDotNetAdapter
        Implements INetworkCapture

        Private _device As LivePacketDevice
        Private _communicator As PacketCommunicator
        Private _log As Logger
        Private _isCapturing As Boolean
        Private _captureThread As Thread
        Private _captureHandler As Action(Of Double, Byte())
        Private _selectedDeviceName As String

        ''' <summary>
        ''' Event raised when packet received
        ''' </summary>
        Public Event PacketReceived As EventHandler(Of GenericPacketEventArgs) Implements INetworkCapture.PacketReceived

        Public Sub New(logger As Logger)
            _log = logger
        End Sub

        ''' <summary>
        ''' Get available network devices
        ''' </summary>
        Public Function GetDevices() As List(Of GenericNetworkDevice) Implements INetworkCapture.GetDevices
            Dim devices As New List(Of GenericNetworkDevice)

            Try
                Dim pcapDevices = LivePacketDevice.AllLocalMachine

                If pcapDevices Is Nothing OrElse pcapDevices.Count = 0 Then
                    _log?.Warn("Pcap.Net: No devices found. Make sure Npcap is installed.")
                    Return devices
                End If

                For Each device As LivePacketDevice In pcapDevices
                    Dim dev As New GenericNetworkDevice With {
                        .Name = device.Name,
                        .FriendlyName = If(device.Description, device.Name),
                        .Description = device.Description,
                        .MacAddress = GetMacAddress(device),
                        .IpAddresses = New List(Of String)
                    }

                    ' Get IP addresses
                    If device.Addresses IsNot Nothing Then
                        For Each addr In device.Addresses
                            Try
                                If addr.Address IsNot Nothing Then
                                    dev.IpAddresses.Add(addr.Address.ToString())
                                End If
                            Catch
                                ' Ignore address parsing errors
                            End Try
                        Next
                    End If

                    devices.Add(dev)
                Next

                _log?.Info("Pcap.Net: Found {0} network devices", devices.Count)

            Catch ex As Exception
                _log?.Error("Pcap.Net: Failed to enumerate devices: {0}", ex.Message)
                Throw New Exception("Failed to enumerate network devices. Make sure Npcap is installed.", ex)
            End Try

            Return devices
        End Function

        ''' <summary>
        ''' Get MAC address from device
        ''' </summary>
        Private Function GetMacAddress(device As LivePacketDevice) As String
            Try
                ' Try to get from system NetworkInterface
                For Each nic In NetworkInterface.GetAllNetworkInterfaces()
                    If device.Name.Contains(nic.Id) OrElse
                       (device.Description IsNot Nothing AndAlso device.Description.Contains(nic.Name)) Then
                        Return BitConverter.ToString(nic.GetPhysicalAddress().GetAddressBytes()).Replace("-", ":")
                    End If
                Next

            Catch ex As Exception
                ' Ignore errors
            End Try

            Return Nothing
        End Function

        ''' <summary>
        ''' Open device for capture
        ''' </summary>
        Public Sub OpenDevice(deviceName As String, macAddress As String) Implements INetworkCapture.OpenDevice
            Try
                _selectedDeviceName = deviceName
                _log?.Info("Pcap.Net: Opening capture device for network: {0}", deviceName)

                ' Find matching device
                Dim devices = LivePacketDevice.AllLocalMachine
                _device = Nothing

                For Each dev As LivePacketDevice In devices
                    ' Try to match by MAC address
                    Dim devMac = GetMacAddress(dev)
                    If devMac IsNot Nothing AndAlso devMac.Equals(macAddress, StringComparison.OrdinalIgnoreCase) Then
                        _device = dev
                        Exit For
                    End If

                    ' Try to match by name/description
                    If dev.Name.Contains(deviceName) OrElse
                       (dev.Description IsNot Nothing AndAlso dev.Description.Contains(deviceName)) Then
                        _device = dev
                        Exit For
                    End If
                Next

                If _device Is Nothing Then
                    Throw New Exception($"Could not find network device matching: {deviceName}")
                End If

                ' Open device with optimized settings for Control24
                ' Promiscuous mode + immediate mode for low latency
                _communicator = _device.Open(
                    65536,  ' snaplen (max packet size)
                    PacketDeviceOpenAttributes.Promiscuous,
                    50      ' read timeout in ms
                )

                _log?.Info("Pcap.Net: Device opened: {0} (Snaplen: 65536)", _device.Description)

            Catch ex As Exception
                _log?.Error("Pcap.Net: Failed to open capture device: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Set packet filter (BPF syntax)
        ''' </summary>
        Public Sub SetFilter(macAddress As String) Implements INetworkCapture.SetFilter
            If _communicator Is Nothing Then
                Throw New InvalidOperationException("Device not opened")
            End If

            Try
                ' Filter for Control24: Ethernet protocol 0x885F to our MAC or broadcast
                Dim filterStr = String.Format("(ether dst {0} or broadcast) and ether proto 0x885f", macAddress)

                _log?.Debug("Pcap.Net: Setting filter: {0}", filterStr)

                ' Set BPF filter
                _communicator.SetFilter(filterStr)

                _log?.Info("Pcap.Net: Filter applied successfully")

            Catch ex As Exception
                _log?.Error("Pcap.Net: Failed to set filter: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Start packet capture
        ''' </summary>
        Public Sub StartCapture(handler As Action(Of Double, Byte())) Implements INetworkCapture.StartCapture
            If _communicator Is Nothing Then
                Throw New InvalidOperationException("Device not opened")
            End If

            If _isCapturing Then
                Throw New InvalidOperationException("Already capturing")
            End If

            _captureHandler = handler
            _isCapturing = True

            ' Start capture on background thread with HIGH PRIORITY
            _captureThread = New Thread(AddressOf CaptureLoop) With {
                .Name = "PcapDotNetCaptureThread",
                .IsBackground = True,
                .Priority = ThreadPriority.Highest  ' Critical for low latency
            }
            _captureThread.Start()

            _log?.Info("Pcap.Net: Packet capture started (High Priority)")
        End Sub

        ''' <summary>
        ''' Capture loop - runs on background thread
        ''' </summary>
        Private Sub CaptureLoop()
            Try
                _log?.Debug("Pcap.Net: Capture loop started")

                ' Receive packets indefinitely
                _communicator.ReceivePackets(0, AddressOf PacketHandler)

            Catch ex As ThreadAbortException
                _log?.Debug("Pcap.Net: Capture thread aborted")
            Catch ex As Exception
                _log?.Error("Pcap.Net: Capture loop error: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Packet handler callback
        ''' </summary>
        Private Sub PacketHandler(packet As PcapDotNet.Packets.Packet)
            Try
                If Not _isCapturing Then
                    Return
                End If

                ' Convert timestamp to Unix epoch seconds (matching SharpPcap behavior)
                Dim timestamp = packet.Timestamp.Subtract(New DateTime(1970, 1, 1)).TotalSeconds

                ' Get packet data as byte array
                Dim data(packet.Length - 1) As Byte
                packet.CopyTo(data, 0)

                ' Call registered handler
                _captureHandler?.Invoke(timestamp, data)

                ' Raise event
                RaiseEvent PacketReceived(Me, New GenericPacketEventArgs(timestamp, data))

            Catch ex As Exception
                _log?.Error("Pcap.Net: Error processing packet: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Stop packet capture
        ''' </summary>
        Public Sub StopCapture() Implements INetworkCapture.StopCapture
            If Not _isCapturing Then
                Return
            End If

            Try
                _isCapturing = False

                ' Break the capture loop
                If _communicator IsNot Nothing Then
                    _communicator.Break()
                End If

                ' Wait for capture thread to finish
                _captureThread?.Join(1000)

                _log?.Info("Pcap.Net: Packet capture stopped")

            Catch ex As Exception
                _log?.Error("Pcap.Net: Error stopping capture: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Send a raw packet
        ''' </summary>
        Public Function SendPacket(data As Byte()) As Integer Implements INetworkCapture.SendPacket
            If _communicator Is Nothing Then
                Throw New InvalidOperationException("Device not opened")
            End If

            Try
                ' Pcap.Net requires creating a Packet object from bytes
                ' Use PacketBuilder to create packet from raw bytes
                Dim packet As New PcapDotNet.Packets.Packet(data, DateTime.Now, DataLinkKind.Ethernet)
                
                _communicator.SendPacket(packet)

                Return data.Length

            Catch ex As Exception
                _log?.Error("Pcap.Net: Failed to send packet: {0}", ex.Message)
                Return -1
            End Try
        End Function

        ''' <summary>
        ''' Check if currently capturing
        ''' </summary>
        Public ReadOnly Property IsCapturing As Boolean Implements INetworkCapture.IsCapturing
            Get
                Return _isCapturing
            End Get
        End Property

        ''' <summary>
        ''' Dispose resources
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            Try
                StopCapture()

                If _communicator IsNot Nothing Then
                    _communicator.Dispose()
                    _communicator = Nothing
                End If

                _device = Nothing

            Catch ex As Exception
                _log?.Error("Pcap.Net: Dispose error: {0}", ex.Message)
            End Try

            GC.SuppressFinalize(Me)
        End Sub

    End Class

End Namespace
