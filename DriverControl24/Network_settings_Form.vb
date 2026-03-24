Imports System.Net
Imports System.Net.NetworkInformation
Imports System.Net.Sockets
Imports System.Windows.Forms.VisualStyles.VisualStyleElement.TrackBar
Imports Nager
Imports Reacontrol24

Public Class Network_settings_Form


    Dim Control24_EndPoint As IPEndPoint
    Dim OSCClient_EndPoint As IPEndPoint
    Dim DAW_EndPoint As IPEndPoint

    Dim Control24_port As Integer = 9123
    Dim OSC_Client_port As Integer = 9124
    Dim DAW_port As Integer = 9125
    Dim auth = "be_in-control"
    Dim server As TcpListener
    Dim Net_interfaces As New Dictionary(Of String, NetworkInformation.NetworkInterface)
    Dim selected_Net_interface As NetworkInformation.NetworkInterface
    Dim Unicast_IP As String = ""

    ' New: Control24 session
    Private _c24Session As Reacontrol24.C24Session
    Private WithEvents _logger As Reacontrol24.Logger
    Private _isMonitoring As Boolean = False
    Private _configManager As Reacontrol24.ConfigurationManager
    Private _currentConfig As Reacontrol24.Control24Configuration
    Private WithEvents _statsTimer As New Timer()
    Private Pause_Log As Boolean = False


    Private Sub _logger_OnLogMessage(LogMessage As String) Handles _logger.OnLogMessage

        Try
            TextBox1.Invoke(CType((Sub()
                                       TextBox1.AppendText(LogMessage & vbCrLf)
                                       ' Auto-scroll to bottom
                                       TextBox1.SelectionStart = TextBox1.Text.Length
                                       TextBox1.ScrollToCaret()
                                   End Sub), [Delegate]))
        Catch ex As Exception
            ' Ignore if form is closing
        End Try
    End Sub

    Private Sub Network_settings_Form_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        ' Initialize configuration manager
        _configManager = Reacontrol24.ConfigurationManager.Instance

        ' Initialize logger
        Try
            _logger = Reacontrol24.LogHelper.StartLogging("NetworkSettings", ".\logs", debug:=True)
            _configManager.SetLogger(_logger)
            _logger.Info("Network Settings Form loaded")
        Catch ex As Exception
            TextBox1.Text &= "Logger init failed: " & ex.Message & vbCrLf
        End Try

        ' Load configuration
        Try
            _currentConfig = _configManager.LoadConfiguration()

            _currentConfig.AutoConnectOSC = False
            _currentConfig.TcpServerEnabled = False
            _currentConfig.AutoStartMonitoring = False


            ApplyConfiguration()
            TextBox1.Text &= "Configuration loaded" & vbCrLf
        Catch ex As Exception
            TextBox1.Text &= "Configuration load failed: " & ex.Message & vbCrLf
        End Try

        ' Load network interfaces
        For Each Net_interface In Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces
            Net_interfaces.Add(Net_interface.Name, Net_interface)
            ComboBox1.Items.Add(Net_interface.Name)
        Next

        ' Select previously used interface
        If Not String.IsNullOrEmpty(_currentConfig.NetworkInterfaceName) AndAlso
           Net_interfaces.ContainsKey(_currentConfig.NetworkInterfaceName) Then
            ComboBox1.SelectedItem = _currentConfig.NetworkInterfaceName
        End If

        ' Try to list SharpPcap devices
        Try
            TextBox1.Text &= "=== Available Capture Devices ===" & vbCrLf
            Dim devices = Reacontrol24.PacketCaptureManager.GetDevices()
            For Each dev In devices
                TextBox1.Text &= $"  {dev.FriendlyName} - {dev.MacAddress}" & vbCrLf
            Next
            TextBox1.Text &= "=== End Available Capture Devices ===" & vbCrLf
            TextBox1.Text &= vbCrLf
        Catch ex As Exception
            TextBox1.Text &= "WinPcap/Npcap not installed or error: " & ex.Message & vbCrLf
            TextBox1.Text &= "Download from: https://npcap.com" & vbCrLf & vbCrLf
        End Try

        ' Setup statistics timer (updates every second)
        _statsTimer.Interval = 1000
        _statsTimer.Enabled = False

        ' Auto-start if configured
        If _currentConfig.AutoStartMonitoring AndAlso ComboBox1.SelectedItem IsNot Nothing Then
            StartControl24Monitoring()
        End If

    End Sub

    ''' <summary>
    ''' Apply loaded configuration
    ''' </summary>
    Private Sub ApplyConfiguration()
        ' Apply ports
        Control24_port = _currentConfig.TcpServerPort
        OSC_Client_port = _currentConfig.OscServerPort
        DAW_port = _currentConfig.OscDawPort
    End Sub

    ''' <summary>
    ''' Save current configuration
    ''' </summary>
    Private Sub SaveConfiguration()
        Try
            _currentConfig.NetworkInterfaceName = selected_Net_interface?.Name
            _currentConfig.NetworkMacAddress = selected_Net_interface?.GetPhysicalAddress().ToString()
            _currentConfig.NetworkIpAddress = Unicast_IP
            _currentConfig.TcpServerPort = Control24_port
            _currentConfig.OscServerPort = OSC_Client_port
            _currentConfig.OscDawPort = DAW_port

            _configManager.SaveConfiguration(_currentConfig)
            _logger?.Info("Configuration saved")

        Catch ex As Exception
            _logger?.Error("Failed to save configuration: {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Start Control24 packet monitoring
    ''' </summary>
    Private Sub StartControl24Monitoring()
        If _isMonitoring Then
            TextBox1.Text &= "Already monitoring!" & vbCrLf
            Return
        End If

        Try
            If selected_Net_interface Is Nothing Then
                TextBox1.Text &= "Please select a network interface first!" & vbCrLf
                Return
            End If

            TextBox1.Text &= "=== Starting Control24 Monitoring ===" & vbCrLf

            ' Create NetworkHelper and get info
            Dim networks As New Reacontrol24.NetworkHelper()
            Dim networkInfo = networks.Get(selected_Net_interface.Id)

            If networkInfo Is Nothing Then
                TextBox1.Text &= "Failed to get network info!" & vbCrLf
                Return
            End If

            ' Get IP address
            Dim firstIp = If(networkInfo.IpAddresses?.Count > 0, networkInfo.IpAddresses(0).Address, "127.0.0.1")
            Dim listenAddress As New IPEndPoint(IPAddress.Parse(firstIp), Control24_port)

            ' Create and start session
            _c24Session = New Reacontrol24.C24Session(networkInfo, listenAddress, _logger)


            ' Configure OSC
            If _currentConfig.AutoConnectOSC Then
                _c24Session.ConfigureOSC(
                    _currentConfig.OscServerPort,
                    _currentConfig.OscDawAddress,
                    _currentConfig.OscDawPort
                )
                TextBox1.Text &= $"OSC configured: {_currentConfig.OscDawAddress}:{_currentConfig.OscDawPort}" & vbCrLf
            End If

            ' Configure TCP Server
            If _currentConfig.TcpServerEnabled Then
                _c24Session.ConfigureTcpServer(_currentConfig.TcpServerPort)
                TextBox1.Text &= $"TCP Server configured: port {_currentConfig.TcpServerPort}" & vbCrLf
            End If

            ' Wire up statistics events
            If _c24Session.StatisticsMonitor IsNot Nothing Then
                AddHandler _c24Session.StatisticsMonitor.PerformanceUpdate, AddressOf OnPerformanceUpdate
            End If

            ' Start session
            _c24Session.Start()
            _isMonitoring = True
            _statsTimer.Enabled = True

            TextBox1.Text &= $"? Monitoring started on {selected_Net_interface.Name}" & vbCrLf
            TextBox1.Text &= "? OSC Server: Active" & vbCrLf
            TextBox1.Text &= "? TCP Server: Active" & vbCrLf
            TextBox1.Text &= "? Message Router: Active" & vbCrLf
            TextBox1.Text &= "? Statistics Monitor: Active" & vbCrLf
            TextBox1.Text &= vbCrLf
            TextBox1.Text &= "Waiting for Control24 broadcast..." & vbCrLf
            TextBox1.Text &= "(Make sure Control24 is powered on)" & vbCrLf

            ' Save configuration
            SaveConfiguration()

        Catch ex As Exception
            TextBox1.Text &= $"? Error starting monitoring: {ex.Message}" & vbCrLf
            _logger?.Error("Monitoring start failed: {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Stop Control24 packet monitoring
    ''' </summary>
    Private Sub StopControl24Monitoring()
        If Not _isMonitoring Then
            Return
        End If

        Try
            TextBox1.Text &= "=== Stopping Control24 Monitoring ===" & vbCrLf

            ' Stop statistics timer
            _statsTimer.Enabled = False

            If _c24Session IsNot Nothing Then
                ' Unwire statistics events
                If _c24Session.StatisticsMonitor IsNot Nothing Then
                    RemoveHandler _c24Session.StatisticsMonitor.PerformanceUpdate, AddressOf OnPerformanceUpdate
                End If

                _c24Session.Close()
                _c24Session = Nothing
            End If

            _isMonitoring = False
            TextBox1.Text &= "? Monitoring stopped." & vbCrLf

            ' Reset form title
            Me.Text = "Control24 Network Settings"

        Catch ex As Exception
            TextBox1.Text &= $"? Error stopping monitoring: {ex.Message}" & vbCrLf
            _logger?.Error("Monitoring stop failed: {0}", ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Statistics timer tick - updates UI with real-time stats
    ''' </summary>
    Private Sub _statsTimer_Tick(sender As Object, e As EventArgs) Handles _statsTimer.Tick
        If _c24Session Is Nothing OrElse _c24Session.StatisticsMonitor Is Nothing Then
            Return
        End If

        Try
            Dim summary = _c24Session.StatisticsMonitor.GetSummary()
            ' Update status bar or label with stats
            ' For now just update form title
            Me.Text = $"Control24 Network Settings - {summary}"

        Catch ex As Exception
            ' Ignore errors during updates
        End Try
    End Sub

    ''' <summary>
    ''' Handle performance updates from statistics monitor
    ''' </summary>
    Private Sub OnPerformanceUpdate(sender As Object, perfData As Reacontrol24.PerformanceData)
        Try
            ' Update UI on UI thread
            Me.Invoke(Sub()
                          ' You can update labels, progress bars, charts here
                          ' For now just log
                          _logger?.Debug("Performance: {0}", perfData.ToString())
                      End Sub)
        Catch ex As Exception
            ' Form might be closing
        End Try
    End Sub

    'Start OSC
    Private Async Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        Button1.Enabled = False

        Try

            TextBox1.Text &= "✅ Native Pcap: Broadcast blocking ENABLED" & vbCrLf

            TextBox1.Text &= "=== LED TEST STARTED (Keep-alive + broadcasts disabled) ===" & vbCrLf

            ' Test des tracks
            For i = 0 To 23
                Dim tck = _c24Session._desk.GetTrack(i)

                ' Scribble strip
                If i < 9 Then
                    tck.ScribbleStrip.SetText("Tr0" & i + 1)
                Else
                    tck.ScribbleStrip.SetText("Tr" & i + 1)
                End If
                Await Task.Delay(200)

                ' Test LEDs
                Await TestTrackButtonLedAsync(tck)
                Await Task.Delay(300)

                ' Clear scribble
                tck.ScribbleStrip.SetText("")
                Await Task.Delay(200)

                TextBox1.Text &= $"Track {i + 1} test completed" & vbCrLf
                TextBox1.Refresh()
            Next

            TextBox1.Text &= "=== ALL TRACKS TESTED ===" & vbCrLf

        Finally
            ' ✅ RESTAURATION: Réactiver le keep-alive + broadcasts
            _c24Session.SetTestingMode(False)


            TextBox1.Text &= "✅ Native Pcap: Broadcast blocking DISABLED" & vbCrLf

            TextBox1.Text &= "=== LED TEST ENDED (Keep-alive + broadcasts re-enabled) ===" & vbCrLf
            Button1.Enabled = True
        End Try
    End Sub

    Private Async Function TestTrackButtonLedAsync(trk As Reacontrol24.C24Track) As Task
        Dim list_of_buttonLedAddr As String() = {"rec", "switch", "pan", "eq", "dyn", "insert", "auto", "sel", "solo", "mute"}
        Dim AutoModes As String() = {"read", "trim", "latch", "touch", "write", "off"}
        Dim addr As String = "/track/" & trk.TrackNumber + 1 & "/"

        ' Phase 1: Clear ALL LEDs FIRST
        TextBox1.Text &= $"  Clearing track {trk.TrackNumber + 1} LEDs..." & vbCrLf

        For Each led_addr In list_of_buttonLedAddr
            trk.ButtonLed.SetButton(addr & led_addr, 0)
            ' ✅ FIXED: Wait for command to be actually sent (not just delay)
            If Not _c24Session.WaitForQueueEmpty(500) Then
                TextBox1.Text &= $"    Warning: Queue timeout for {led_addr}" & vbCrLf
            End If
        Next

        ' Clear automation modes
        For Each mode In AutoModes
            trk.ButtonLed.SetButton(addr & "automode/" & mode, 0)
            If Not _c24Session.WaitForQueueEmpty(500) Then
                TextBox1.Text &= $"    Warning: Queue timeout for automode {mode}" & vbCrLf
            End If
        Next

        ' Phase 2: Extra buffer after clear
        Await Task.Delay(200)  ' Visual buffer

        ' Phase 3: Test basic buttons
        TextBox1.Text &= $"  Testing basic buttons..." & vbCrLf

        For Each led_addr In list_of_buttonLedAddr
            If led_addr <> "auto" Then
                ' Turn ON
                trk.ButtonLed.SetButton(addr & led_addr, 1)
                _c24Session.WaitForQueueEmpty(500)
                Await Task.Delay(100)  ' ✅ Visual delay (pour que l'humain voit la LED)

                ' Turn OFF
                trk.ButtonLed.SetButton(addr & led_addr, 0)
                _c24Session.WaitForQueueEmpty(500)
                Await Task.Delay(100)  ' ✅ Visual delay
            End If
        Next

        ' Phase 4: Buffer before automation
        Await Task.Delay(200)

        ' Phase 5: Test automation modes
        TextBox1.Text &= $"  Testing automation modes..." & vbCrLf

        For Each mode In AutoModes
            ' Turn ON
            trk.ButtonLed.SetButton(addr & "automode/" & mode, 1)
            _c24Session.WaitForQueueEmpty(500)
            Await Task.Delay(100)  ' ✅ Visual delay

            ' Turn OFF
            trk.ButtonLed.SetButton(addr & "automode/" & mode, 0)
            _c24Session.WaitForQueueEmpty(500)
            Await Task.Delay(100)  ' ✅ Visual delay
        Next

        ' Phase 6: Final wait
        Await Task.Delay(200)
    End Function

    Private Sub test_led_buton(trk As Reacontrol24.C24Track, addr As String)
        For i = 0 To 127
            trk.ButtonLed.SetButton(addr, i)
            Threading.Thread.Sleep(100)
        Next
        For i = 127 To 0 Step -1
            trk.ButtonLed.SetButton(addr, i)
            Threading.Thread.Sleep(100)
        Next
    End Sub

    Private Sub test_fader_gain(trk As Reacontrol24.C24Track)
        For i = 0 To 0.86 Step 0.01
            trk.Fader.Gain = i
            Threading.Thread.Sleep(100)

        Next
        ' trk.Fader.Gain = 0
    End Sub

    Sub Connect(server As [String], message As [String])
        Try
            ' Create a TcpClient.
            ' Note, for this client to work you need to have a TcpServer 
            ' connected to the same address as specified by the server, port
            ' combination.
            Dim port As Int32 = 4242

            ' Prefer using declaration to ensure the instance is Disposed later.
            Using client As New TcpClient(server, port)

                ' Translate the passed message into ASCII and store it as a Byte array.
                Dim data As [Byte]() = System.Text.Encoding.ASCII.GetBytes(message)

                ' Get a client stream for reading and writing.
                Dim stream As NetworkStream = client.GetStream()

                ' Send the message to the connected TcpServer. 
                stream.Write(data, 0, data.Length)

                TextBox1.Text &= "Sent: " & message & vbCrLf
                TextBox1.Refresh()
                ' Receive the server response.
                ' Buffer to store the response bytes.
                data = New [Byte](256) {}

                ' String to store the response ASCII representation.
                Dim responseData As [String] = [String].Empty

                ' Read the first batch of the TcpServer response bytes.
                Dim bytes As Int32 = stream.Read(data, 0, data.Length)
                responseData = System.Text.Encoding.ASCII.GetString(data, 0, bytes)
                TextBox1.Text &= "Received: " & responseData & vbCrLf

                ' Explicit close is not necessary since TcpClient.Dispose() will be
                ' called automatically.
                ' stream.Close()
                ' client.Close()
            End Using
        Catch e As ArgumentNullException
            MessageBox.Show(e.Message)
        Catch e As SocketException
            MessageBox.Show(e.Message)
        End Try


    End Sub

    Sub Me_Disposed() Handles Me.Disposed
        'Client_Control24.Disconnect()
        'Client_Control24.Dispose()
    End Sub

    Private Sub ComboBox1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBox1.SelectedIndexChanged
        selected_Net_interface = Net_interfaces(ComboBox1.SelectedItem)

        Dim Uni_addrs = selected_Net_interface.GetIPProperties.UnicastAddresses
        If Uni_addrs.Count > 0 Then
            For Each Uni_addr In Uni_addrs
                TextBox1.Text &= "Unicast IP :" & Uni_addr.Address.ToString & vbCrLf
                Unicast_IP = Uni_addr.Address.ToString

            Next

        End If

        Dim _C24desk As New Control24.Hardware.C24_Desk(selected_Net_interface)
        _C24desk.StartNetworkCommunication()

        'Dim C24_exemple As New Reacontrol24.Control24Example(_logger)
        'C24_exemple.DemoClockFeatures(selected_Net_interface.Name)

        'StartControl24Monitoring()
    End Sub



    ''' <summary>
    ''' Form closing - cleanup
    ''' </summary>
    Private Sub Network_settings_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        StopControl24Monitoring()
        _logger?.Close()
    End Sub

    'Pause
    Private Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
        If Pause_Log = False Then
            RemoveHandler _logger.OnLogMessage, AddressOf _logger_OnLogMessage
            Pause_Log = True
        Else
            AddHandler _logger.OnLogMessage, AddressOf _logger_OnLogMessage
            Pause_Log = False
        End If
    End Sub

    'monitoring
    Private Sub CheckBox1_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBox1.CheckedChanged
        If CheckBox1.Checked Then
            _c24Session.StatisticsMonitor.Start()
        Else
            _c24Session.StatisticsMonitor.Stop()
        End If
    End Sub

    Private Sub Button3_Click(sender As Object, e As EventArgs) Handles Button3.Click
        _logger_OnLogMessage(_c24Session.StatisticsMonitor.GetSummary)
    End Sub
    'test track names
    Private Sub Button4_Click(sender As Object, e As EventArgs) Handles Button4.Click
        For i = 0 To 23
            Dim trk = _c24Session._desk.GetTrack(i)
            trk.ScribbleStrip.SetText("")
        Next
        For i = 0 To 23
            Dim trk = _c24Session._desk.GetTrack(i)
            trk.ScribbleStrip.SetText("")
            If i < 9 Then
                trk.ScribbleStrip.SetText("Tr0" & i + 1)
            Else
                trk.ScribbleStrip.SetText("Tr" & i + 1)
            End If

            Threading.Thread.Sleep(250)
            'trk.ScribbleStrip.SetText("")
        Next
    End Sub
    'generate CSV
    Private Sub Button5_Click(sender As Object, e As EventArgs) Handles Button5.Click
        Dim files As String() = Nothing
        Using _OpenFileDialog As New OpenFileDialog
            _OpenFileDialog.Multiselect = True
            If _OpenFileDialog.ShowDialog = DialogResult.OK Then
                files = _OpenFileDialog.FileNames
            End If
        End Using
        'Try

        If files IsNot Nothing AndAlso files.Count > 0 Then
            For Each txt_filepath As String In files

                If txt_filepath <> "" Then
                    Dim parser As New WiresharkLogParser()
                    Dim csv_filepath As String = IO.Path.ChangeExtension(txt_filepath, "csv")
                    If IO.File.Exists(csv_filepath) Then
                        IO.File.Delete(csv_filepath)
                    End If

                    parser.ConvertToCsv(
                txt_filepath,
                csv_filepath)

                End If
            Next
            MessageBox.Show("csv files created")
        End If



        'Catch ex As Exception
        '    MessageBox.Show(ex.Message)
        'End Try





    End Sub
    'test
    Private Sub Button6_Click(sender As Object, e As EventArgs) Handles Button6.Click

        Dim _trk = _c24Session._desk.GetTrack(29)
        Dim _dsk = _c24Session._desk
        _trk.ScribbleStrip.SetText("abcd")
        _trk = _c24Session._desk.GetTrack(30)
        _trk.ScribbleStrip.SetText("efgh")

        'For i = -100 To 100 Step 1
        '    _trk.VPot.Pan = CDbl(i)
        '    Threading.Thread.Sleep(10)
        'Next

        'For i = 0 To 100 Step 1
        '    _trk.VPot.Gauge = CDbl(i)
        '    Threading.Thread.Sleep(10)
        'Next
        '_trk.VPot.Clear()
        'For i = 0 To 100 Step 1
        '    _trk.VPot.Gauge_Dot = CDbl(i)
        '    Threading.Thread.Sleep(10)
        'Next

        'Dim _b = Utility.Bit.BitManager.GetBytes({1}, {&H0, &H0, &H0})
        'For i = 0 To 100
        '    _trk.VuMeter.Percent_Left = i
        '    _trk.VuMeter.Percent_Right = i
        'Next
        '_trk.VuMeter.Clear()

        'test fader
        'For i = 0 To 100
        '    _trk.Fader.Percent = i
        '    Threading.Thread.Sleep(10)
        'Next
        '_trk.Fader.Gain = 3

        'test transport button
        'Dim address As String = "/transport/"
        'Dim transport_names As String() = {"rewind", "forward", "stop", "play", "record", "scrub", "shuttle"}

        'For Each _Name In transport_names
        '    Dim addr_button = address & _Name
        '    _dsk.TransportButton.SetButton(addr_button, 0)
        '    Threading.Thread.Sleep(100)
        '    _dsk.TransportButton.SetButton(addr_button, 1)
        '    Threading.Thread.Sleep(100)
        '    _dsk.TransportButton.SetButton(addr_button, 0)
        '    Threading.Thread.Sleep(100)
        'Next
        '_dsk.Clock.Mode = Reacontrol24.C24Clock.ClockMode.time
        '_dsk.Clock.ClockText = "00000000"
        'For i = 0 To 99999999 Step 10
        '    Dim nb As String = i.ToString("00000000")
        '    _dsk.Clock.ClockText = nb
        '    'Threading.Thread.Sleep(i)
        'Next


    End Sub
End Class
