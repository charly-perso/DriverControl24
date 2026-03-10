''' <summary>
''' Packet capture implementation using SharpPcap
''' Handles low-level Ethernet packet capture for Control24 communication
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' </summary>

Imports System.Net.NetworkInformation
Imports System.Threading
Imports SharpPcap
Imports SharpPcap.LibPcap
Imports PacketDotNet

Namespace Reacontrol24

    ''' <summary>
    ''' Packet capture manager using SharpPcap/WinPcap
    ''' </summary>
    Public Class PacketCaptureManager
        Implements IDisposable

        Private _device As SharpPcap.LibPcap.LibPcapLiveDevice
        Private _log As Logger
        Private _isCapturing As Boolean
        Private _captureThread As Thread
        Private _macFilter As String
        Private _packetHandler As Action(Of Double, Byte())

        ''' <summary>
        ''' Event raised when a packet is captured
        ''' </summary>
        Public Event PacketReceived As EventHandler(Of PacketReceivedEventArgs)

        Public Sub New(logger As Logger)
            _log = logger
        End Sub

        ''' <summary>
        ''' Get list of available network devices
        ''' </summary>
        Public Shared Function GetDevices() As List(Of NetworkDeviceInfo)
            Dim devices As New List(Of NetworkDeviceInfo)

            Try
                Dim pcapDevices = LibPcapLiveDeviceList.Instance

                For Each device In pcapDevices
                    Dim info As New NetworkDeviceInfo With {
                        .Name = device.Name,
                        .Description = device.Description,
                        .FriendlyName = If(device.Interface?.FriendlyName, device.Name),
                        .MacAddress = GetMacAddress(device),
                        .Device = device
                    }

                    ' Get IP addresses
                    If device.Interface IsNot Nothing AndAlso device.Interface.Addresses IsNot Nothing Then
                        For Each addr In device.Interface.Addresses
                            If addr.Addr?.type = Sockaddr.AddressTypes.AF_INET_AF_INET6 Then
                                info.IpAddresses.Add(addr.Addr.ipAddress.ToString())
                            End If
                        Next
                    End If

                    devices.Add(info)
                Next

                Return devices
            Catch ex As Exception
                Throw New Exception("Failed to enumerate network devices. Make sure WinPcap or Npcap is installed.", ex)
            End Try
        End Function

        ''' <summary>
        ''' Open capture device by network interface info
        ''' </summary>
        Public Sub OpenDevice(networkInfo As NetworkHelper.NetworkInterfaceInfo, macFilter As String)
            Try
                _macFilter = macFilter
                _log.Info("Opening capture device for network: {0}", networkInfo.Name)

                ' Find the matching device
                Dim devices = LibPcapLiveDeviceList.Instance
                _device = Nothing

                For Each dev In devices
                    ' Try to match by MAC address
                    Dim devMac = GetMacAddress(dev)
                    If devMac IsNot Nothing AndAlso devMac.Equals(networkInfo.Mac, StringComparison.OrdinalIgnoreCase) Then
                        _device = dev
                        Exit For
                    End If

                    ' Try to match by name/description
                    If dev.Name.Contains(networkInfo.PcapName) OrElse
                       (dev.Description IsNot Nothing AndAlso dev.Description.Contains(networkInfo.Name)) Then
                        _device = dev
                        Exit For
                    End If
                Next

                If _device Is Nothing Then
                    Throw New Exception($"Could not find network device matching: {networkInfo.Name}")
                End If

                ' Open the device with custom configuration
                ' Create configuration with large snaplen
                Dim config As New SharpPcap.DeviceConfiguration With {
                    .Mode = DeviceModes.MaxResponsiveness,
                    .ReadTimeout = 50,
                    .Snaplen = 65535,
                    .Immediate = True
                }

                _device.Open(config)

                _log.Info("Device opened: {0} (Snaplen: 65535)", _device.Description)

            Catch ex As Exception
                _log.Error("Failed to open capture device: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Set packet filter (Berkeley Packet Filter syntax)
        ''' </summary>
        Public Sub SetFilter(macAddress As String)
            If _device Is Nothing Then
                Throw New InvalidOperationException("Device not opened")
            End If

            Try
                ' Filter: packets destined to our MAC or broadcast, with protocol 0x885f
                Dim filterStr = String.Format("(ether dst {0} or broadcast) and ether[12:2]=0x885f", macAddress)

                _log.Debug("Setting filter: {0}", filterStr)
                _device.Filter = filterStr

            Catch ex As Exception
                _log.Error("Failed to set filter: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Start capturing packets
        ''' </summary>
        Public Sub StartCapture(packetHandler As Action(Of Double, Byte()))
            If _device Is Nothing Then
                Throw New InvalidOperationException("Device not opened")
            End If

            If _isCapturing Then
                Throw New InvalidOperationException("Already capturing")
            End If

            _packetHandler = packetHandler
            _isCapturing = True

            ' Handle packets on the event - use lambda to adapt signature
            AddHandler _device.OnPacketArrival, Sub(sender, e)
                                                    Try
                                                        Dim rawPacket = e.GetPacket()
                                                        Dim timestamp = rawPacket.Timeval.Date.Subtract(New DateTime(1970, 1, 1)).TotalSeconds
                                                        Dim data = rawPacket.Data

                                                        ' Call the registered handler
                                                        _packetHandler?.Invoke(timestamp, data)

                                                        ' Raise event
                                                        RaiseEvent PacketReceived(Me, New PacketReceivedEventArgs(timestamp, data))
                                                    Catch ex As Exception
                                                        _log.Error("Error processing packet: {0}", ex.Message)
                                                    End Try
                                                End Sub

            ' Start capture on background thread with HIGH PRIORITY
            _captureThread = New Thread(AddressOf CaptureLoop) With {
                .Name = "PacketCaptureThread",
                .IsBackground = True,
                .Priority = ThreadPriority.Highest ' ? CRITICAL FOR LOW LATENCY
            }
            _captureThread.Start()

            _log.Info("Packet capture started (High Priority)")
        End Sub

        ''' <summary>
        ''' Stop capturing packets
        ''' </summary>
        Public Sub StopCapture()
            If Not _isCapturing Then
                Return
            End If

            _isCapturing = False

            If _device IsNot Nothing AndAlso _device.Started Then
                _device.StopCapture()
            End If

            _captureThread?.Join(1000)

            _log.Info("Packet capture stopped")
        End Sub

        ''' <summary>
        ''' Send a raw packet
        ''' </summary>

        '<Obsolete("Allow ref structs", False)>
        Public Function SendPacket(packetData As Byte()) As Integer
            If _device Is Nothing Then
                Throw New InvalidOperationException("Device not opened")
            End If

            Try


                _device.SendPacket(packetData, packetData.Length)

                ' _log.Debug("Sent packet of {0} bytes", packetData.Length) ' DISABLED FOR PERFORMANCE
                Return packetData.Length
            Catch ex As Exception
                _log.Error("Failed to send packet: {0}", ex.Message)
                Return -1
            End Try
        End Function

        ''' <summary>
        ''' Capture loop (runs on background thread)
        ''' </summary>
        Private Sub CaptureLoop()
            Try
                _log.Debug("Capture loop started")
                _device.Capture()
            Catch ex As ThreadInterruptedException
                _log.Debug("Capture thread interrupted")
            Catch ex As Exception
                _log.Error("Capture loop error: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Get MAC address from device
        ''' </summary>
        Private Shared Function GetMacAddress(device As LibPcapLiveDevice) As String
            Try
                If device.Interface IsNot Nothing AndAlso device.Interface.MacAddress IsNot Nothing Then
                    Return device.Interface.MacAddress.ToString()
                End If

                ' Try to get from system NetworkInterface
                For Each nic In NetworkInterface.GetAllNetworkInterfaces()
                    If device.Name.Contains(nic.Id) OrElse
                       (device.Description IsNot Nothing AndAlso device.Description.Contains(nic.Name)) Then
                        Return BitConverter.ToString(nic.GetPhysicalAddress().GetAddressBytes()).Replace("-", ":")
                    End If
                Next

            Catch ex As Exception
                ' Ignore errors getting MAC
            End Try

            Return Nothing
        End Function

        ''' <summary>
        ''' Dispose resources
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            StopCapture()

            If _device IsNot Nothing Then
                If _device.Opened Then
                    _device.Close()
                End If
                _device = Nothing
            End If

            GC.SuppressFinalize(Me)
        End Sub

    End Class

    ''' <summary>
    ''' Information about a network device
    ''' </summary>
    Public Class NetworkDeviceInfo
        Public Property Name As String
        Public Property Description As String
        Public Property FriendlyName As String
        Public Property MacAddress As String
        Public Property IpAddresses As New List(Of String)
        Public Property Device As LibPcapLiveDevice

        Public Overrides Function ToString() As String
            Return $"{FriendlyName} ({MacAddress}) - {String.Join(", ", IpAddresses)}"
        End Function
    End Class

    ''' <summary>
    ''' Event args for packet received event
    ''' </summary>
    Public Class PacketReceivedEventArgs
        Inherits EventArgs

        Public Property Timestamp As Double
        Public Property Data As Byte()

        Public Sub New(timestamp As Double, data As Byte())
            Me.Timestamp = timestamp
            Me.Data = data
        End Sub
    End Class

End Namespace
