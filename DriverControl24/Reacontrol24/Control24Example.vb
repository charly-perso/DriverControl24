''' <summary>
''' Example usage and test class for Control24 packet capture
''' </summary>

Imports System.Net
Imports System.Text

Namespace Reacontrol24

    ''' <summary>
    ''' Example class showing how to use the packet capture functionality
    ''' </summary>
    Public Class Control24Example
        Private _logger As Logger

        Sub New(_log As Logger)
            _logger = _log
        End Sub

        ''' <summary>
        ''' Example: List all available network devices
        ''' </summary>
        Public Sub ListNetworkDevices()
            Try
                _logger.Info("Available network devices:")
                _logger.Info("=" & New String("="c, 60))

                Dim devices = PacketCaptureManager.GetDevices()

                For i = 0 To devices.Count - 1
                    _logger.Info($"{i + 1}. {devices(i).FriendlyName}")
                    _logger.Info($"   Name: {devices(i).Name}")
                    _logger.Info($"   Description: {devices(i).Description}")
                    _logger.Info($"   MAC: {devices(i).MacAddress}")
                    _logger.Info($"   IPs: {String.Join(", ", devices(i).IpAddresses)}")
                    _logger.Info("")
                Next

            Catch ex As Exception
                _logger.Error($"Error: {ex.Message}")
                _logger.Info("Please ensure WinPcap or Npcap is installed.")
            End Try
        End Sub

        ''' <summary>
        ''' Example: Start a Control24 session
        ''' </summary>
        Public Function StartControl24Session(networkName As String, Optional logDir As String = ".\logs") As C24Session
            ' Get network information
            Dim networks As New NetworkHelper()
            Dim networkInfo = networks.Get(networkName)

            If networkInfo Is Nothing Then
                Throw New Exception($"Network interface '{networkName}' not found")
            End If

            ' Get first IP address as string
            Dim firstIp = If(networkInfo.IpAddresses?.Count > 0, networkInfo.IpAddresses(0).Address, "127.0.0.1")

            ' Create listen endpoint
            Dim listenAddress As New IPEndPoint(IPAddress.Parse(firstIp), Defaults.Daemon)

            ' Create and start session
            Dim session As New C24Session(networkInfo, listenAddress, _logger)
            session.Start()

            Return session
        End Function

        ''' <summary>
        ''' Example: Scroll text on Control24 clock display
        ''' The clock uses scribble strips on tracks 1 and 2 (8 characters total)
        ''' </summary>
        Public Sub ScrollClockDisplay(networkName As String, text As String, Optional durationSeconds As Integer = 30)
            _logger.Info($"Scrolling text on Control24 clock: '{text}'")
            _logger.Info($"Duration: {durationSeconds} seconds")
            _logger.Info("Press Ctrl+C to stop")
            _logger.Info("")

            Dim session As C24Session = Nothing
            Dim scrollTimer As Threading.Timer = Nothing
            Dim position As Integer = 0
            Dim isRunning As Boolean = True

            Try
                ' Start session
                session = StartControl24Session(networkName)
                _logger.Info("Session started, waiting for Control24 to be detected...")
                _logger.Info("")

                ' Wait a bit for desk detection
                Threading.Thread.Sleep(2000)

                ' Pad text to allow smooth scrolling
                ' Add spaces at beginning and end for smooth wrap
                Dim paddedText = New String(" "c, 8) & text & New String(" "c, 8)

                _logger.Info($"Starting clock scroll (text length: {paddedText.Length} chars)...")

                ' Create timer to update display every second
                scrollTimer = New Threading.Timer(
                    Sub(state)
                        If Not isRunning Then Return

                        Try
                            ' Get the 8-character window to display
                            Dim displayText As String
                            If position + 8 <= paddedText.Length Then
                                displayText = paddedText.Substring(position, 8)
                            Else
                                ' Wrap around
                                Dim remaining = paddedText.Length - position
                                displayText = paddedText.Substring(position, remaining) &
                                            paddedText.Substring(0, 8 - remaining)
                            End If

                            ' Split into two 4-character segments for tracks 1 and 2
                            Dim leftPart = displayText.Substring(0, 4)
                            Dim rightPart = displayText.Substring(4, 4)

                            ' Send scribble strip commands directly via session
                            ' Track 1 (left part of clock) - Track number 0 in 0-based indexing
                            ' Format: F0 13 01 40 [TrackNum] 00 [Char1] [Char2] [Char3] [Char4] F7 00 (12 bytes)
                            Dim cmd1 As Byte() = {&HF0, &H13, &H1, &H40, &H0, 0, 0, 0, 0, 0, &HF7, 0}
                            Dim bytes1 = Encoding.ASCII.GetBytes(leftPart)
                            For i = 0 To Math.Min(3, bytes1.Length - 1)
                                cmd1(6 + i) = bytes1(i)
                            Next

                            ' Track 2 (right part of clock) - Track number 1 in 0-based indexing
                            ' Format: F0 13 01 40 [TrackNum] 00 [Char1] [Char2] [Char3] [Char4] F7 00 (12 bytes)
                            Dim cmd2 As Byte() = {&HF0, &H13, &H1, &H40, &H1, 0, 0, 0, 0, 0, &HF7, 0}
                            Dim bytes2 = Encoding.ASCII.GetBytes(rightPart)
                            For i = 0 To Math.Min(3, bytes2.Length - 1)
                                cmd2(6 + i) = bytes2(i)
                            Next

                            ' Send commands to Control24
                            session.SendRawCommand(cmd1)
                            session.SendRawCommand(cmd2)

                            ' Log to logger
                            _logger.Debug($"[{DateTime.Now:HH:mm:ss}] Clock: [{displayText}]")

                            ' Move to next position
                            position += 1
                            If position >= paddedText.Length Then
                                position = 0 ' Wrap around
                            End If

                        Catch ex As Exception
                            _logger.Error($"Error updating display: {ex.Message}")
                        End Try
                    End Sub,
                    Nothing,
                    2000,  ' Start after 2 seconds
                    1000   ' Update every 1 second
                )

                ' Keep running for specified duration
                For i = 1 To durationSeconds
                    Threading.Thread.Sleep(1000)
                Next

                isRunning = False
                _logger.Info("")
                _logger.Info("Scroll complete!")

            Catch ex As Exception
                _logger.Error($"Error: {ex.Message}")
                _logger.Error(ex.StackTrace)
            Finally
                ' Clean up
                isRunning = False
                scrollTimer?.Dispose()

                If session IsNot Nothing Then
                    _logger.Info("Closing session...")
                    session.Close()
                End If
            End Try
        End Sub

        ''' <summary>
        ''' Example: Display time on Control24 clock
        ''' Shows current time updating every second
        ''' </summary>
        Public Sub DisplayTimeOnClock(networkName As String, Optional durationSeconds As Integer = 60)
            _logger.Info("Displaying current time on Control24 clock")
            _logger.Info($"Duration: {durationSeconds} seconds")
            _logger.Info("Press Ctrl+C to stop")
            _logger.Info("")

            Dim session As C24Session = Nothing
            Dim clockTimer As Threading.Timer = Nothing
            Dim isRunning As Boolean = True

            Try
                ' Start session
                session = StartControl24Session(networkName)
                _logger.Info("Session started, waiting for Control24 to be detected...")
                _logger.Info("")

                ' Wait for desk detection
                Threading.Thread.Sleep(2000)

                _logger.Info("Starting clock display...")

                ' Create timer to update time display every second
                clockTimer = New Threading.Timer(
                    Sub(state)
                        If Not isRunning Then Return

                        Try
                            ' Format current time as HH:MM:SS
                            Dim currentTime = DateTime.Now.ToString("HH:mm:ss")

                            ' Split into two parts for the clock display
                            ' Display format: "HH:mm:ss" (8 chars total)
                            Dim leftPart = currentTime.Substring(0, 4)   ' "HH:m"
                            Dim rightPart = currentTime.Substring(4, 4)  ' "m:ss"

                            ' Send scribble strip commands directly
                            ' Track 1 (left part of clock) - Track number 0 in 0-based indexing
                            ' Format: F0 13 01 40 [TrackNum] 00 [Char1] [Char2] [Char3] [Char4] F7 00 (12 bytes)
                            Dim cmd1 As Byte() = {&HF0, &H13, &H1, &H40, &H0, 0, 0, 0, 0, 0, &HF7, 0}
                            Dim bytes1 = Encoding.ASCII.GetBytes(leftPart)
                            For i = 0 To Math.Min(3, bytes1.Length - 1)
                                cmd1(6 + i) = bytes1(i)
                            Next

                            ' Track 2 (right part of clock) - Track number 1 in 0-based indexing
                            ' Format: F0 13 01 40 [TrackNum] 00 [Char1] [Char2] [Char3] [Char4] F7 00 (12 bytes)
                            Dim cmd2 As Byte() = {&HF0, &H13, &H1, &H40, &H1, 0, 0, 0, 0, 0, &HF7, 0}
                            Dim bytes2 = Encoding.ASCII.GetBytes(rightPart)
                            For i = 0 To Math.Min(3, bytes2.Length - 1)
                                cmd2(6 + i) = bytes2(i)
                            Next

                            ' Send commands to Control24
                            session.SendRawCommand(cmd1)
                            session.SendRawCommand(cmd2)

                            ' Log to logger
                            _logger.Debug($"[{DateTime.Now:HH:mm:ss}] Clock display updated: {currentTime}")

                        Catch ex As Exception
                            _logger.Error($"Error updating time: {ex.Message}")
                        End Try
                    End Sub,
                    Nothing,
                    1000,  ' Start after 1 second
                    1000   ' Update every 1 second
                )

                ' Keep running for specified duration
                For i = 1 To durationSeconds
                    Threading.Thread.Sleep(1000)
                Next

                isRunning = False
                _logger.Info("")
                _logger.Info("Time display complete!")

            Catch ex As Exception
                _logger.Error($"Error: {ex.Message}")
                _logger.Error(ex.StackTrace)
            Finally
                ' Clean up
                isRunning = False
                clockTimer?.Dispose()

                If session IsNot Nothing Then
                    _logger.Info("Closing session...")
                    session.Close()
                End If
            End Try
        End Sub

        ''' <summary>
        ''' Example: Monitor Control24 traffic
        ''' </summary>
        Public Sub MonitorControl24Traffic(networkName As String, Optional durationSeconds As Integer = 30)
            _logger.Info($"Monitoring Control24 traffic on {networkName} for {durationSeconds} seconds...")
            _logger.Info("Press Ctrl+C to stop early")
            _logger.Info("")

            Dim packetCount As Integer = 0

            Try
                ' Create packet capture manager
                Using capture As New PacketCaptureManager(_logger)
                    ' Get network info
                    Dim networks As New NetworkHelper()
                    Dim networkInfo = networks.Get(networkName)

                    If networkInfo Is Nothing Then
                        _logger.Error($"Network '{networkName}' not found")
                        Return
                    End If

                    ' Open device
                    capture.OpenDevice(networkInfo, networkInfo.Mac)
                    capture.SetFilter(networkInfo.Mac)

                    ' Handle packets
                    AddHandler capture.PacketReceived, Sub(sender, e)
                                                           packetCount += 1
                                                           _logger.Info($"[{DateTime.Now:HH:mm:ss.fff}] Packet #{packetCount}: {e.Data.Length} bytes")

                                                           ' Try to parse as Control24 packet
                                                           Try
                                                               If e.Data.Length >= 28 Then
                                                                   Dim packet As New C24Packet(e.Data)

                                                                   If packet.IsBroadcast() Then
                                                                       _logger.Info("  Type: BROADCAST (Desk Discovery)")
                                                                   Else
                                                                       _logger.Info($"  Type: Control24 Command")
                                                                       _logger.Info($"  Commands: {packet.C24Header.NumCommands}")
                                                                       _logger.Info($"  Cmd: 0x{packet.C24Header.C24Cmd:X2}")
                                                                   End If
                                                               End If
                                                           Catch
                                                               _logger.Info("  Type: Unknown/Invalid")
                                                           End Try
                                                           _logger.Info("")
                                                       End Sub

                    ' Start capture
                    capture.StartCapture(Sub(timestamp, data)
                                             ' Handler is already in event above
                                         End Sub)

                    ' Wait for specified duration
                    Threading.Thread.Sleep(durationSeconds * 1000)

                    ' Stop capture
                    capture.StopCapture()
                End Using

                _logger.Info("")
                _logger.Info($"Monitoring complete. Total packets captured: {packetCount}")

            Catch ex As Exception
                _logger.Error($"Error: {ex.Message}")
                _logger.Error(ex.StackTrace)
            End Try
        End Sub

        ''' <summary>
        ''' Example: Test packet creation and serialization
        ''' </summary>
        Public Sub TestPacketCreation()
            _logger.Info("Testing packet creation and serialization...")
            _logger.Info("")

            ' Create a simple test packet
            Dim packet As New C24Packet(10)

            ' Set up headers
            packet.EthHeader = New EthHeader(True)

            ' Set some test data
            For i = 0 To 9
                packet.PacketData(i) = CByte(i)
            Next

            ' Serialize to buffer
            Dim buffer = packet.ToBuffer()

            _logger.Info($"Packet created:")
            _logger.Info($"  Total length: {packet.TotalLength} bytes")
            _logger.Info($"  Data length: {packet.DataLength} bytes")
            _logger.Info($"  Hex: {Utils.HexL(buffer)}")
            _logger.Info("")

            ' Try to deserialize
            Dim packet2 As New C24Packet(buffer)
            _logger.Info($"Packet deserialized:")
            _logger.Info($"  Total length: {packet2.TotalLength} bytes")
            _logger.Info($"  Match: {buffer.SequenceEqual(packet2.ToBuffer())}")
            _logger.Info("")
        End Sub

        ''' <summary>
        ''' Example: Full session lifecycle
        ''' </summary>
        Public Sub RunFullExample(networkName As String)
            _logger.Info("Control24 Full Example")
            _logger.Info("=" & New String("="c, 60))
            _logger.Info("")

            Dim session As C24Session = Nothing

            Try
                ' Start session
                _logger.Info("1. Starting Control24 session...")
                session = StartControl24Session(networkName)
                _logger.Info("   Session started successfully")
                _logger.Info("")

                ' Wait for desk to be detected
                _logger.Info("2. Waiting for Control24 desk to be detected...")
                _logger.Info("   Please power on your Control24 if not already on")
                _logger.Info("   (This example will wait for 60 seconds)")
                _logger.Info("")

                ' Keep session alive for 60 seconds
                For i = 1 To 60
                    Threading.Thread.Sleep(1000)
                    If i Mod 10 = 0 Then
                        _logger.Debug(".")
                    End If
                Next
                _logger.Info("")
                _logger.Info("")

                _logger.Info("3. Session complete")

            Catch ex As Exception
                _logger.Error($"Error: {ex.Message}")
                _logger.Error(ex.StackTrace)
            Finally
                ' Clean up
                If session IsNot Nothing Then
                    _logger.Info("4. Closing session...")
                    session.Close()
                    _logger.Info("   Session closed")
                End If
            End Try

            _logger.Info("")
            _logger.Info("Example complete. Press any key to exit.")
        End Sub

        ''' <summary>
        ''' Example: Demo all clock display features
        ''' </summary>
        Public Sub DemoClockFeatures(networkName As String)
            _logger.Info("Control24 Clock Display Demo")
            _logger.Info("=" & New String("="c, 60))
            _logger.Info("")

            Try
                ' 1. Display current time for 10 seconds
                _logger.Info("1. Displaying current time for 10 seconds...")
                DisplayTimeOnClock(networkName, 10)
                _logger.Info("")

                ' 2. Scroll a message
                _logger.Info("2. Scrolling a welcome message for 20 seconds...")
                ScrollClockDisplay(networkName, "CONTROL24 READY", 20)
                _logger.Info("")

                ' 3. Scroll another message
                _logger.Info("3. Scrolling info message for 15 seconds...")
                ScrollClockDisplay(networkName, "VB.NET DRIVER v1.0", 15)
                _logger.Info("")

                _logger.Info("Demo complete!")

            Catch ex As Exception
                _logger.Error($"Error: {ex.Message}")
                _logger.Error(ex.StackTrace)
            End Try
        End Sub

    End Class

End Namespace
