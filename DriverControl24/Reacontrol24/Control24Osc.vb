''' <summary>
''' Control24 to OSC client
''' Communicate between the daemon process and an OSC Client/Listener pair
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' </summary>

Imports System.Diagnostics.Metrics
Imports System.Net
Imports System.Threading


Namespace Reacontrol24

    ''' <summary>
    ''' OSC Timing constants
    ''' </summary>
    Public Class OscTiming
        Public Const MainLoop As Double = 10.0
        Public Const ServerPoll As Double = 2.0
        Public Const MpPoll As Double = 1.0
        Public Const WaitOscListener As Double = 4.0
        Public Const OscListenerRestart As Double = 1.0
        Public Const OscClientRestart As Double = 1.0
        Public Const OscClientLoop As Double = 4.0
        Public Const ScribbleStripRestore As Double = 4.0
        Public Const FaderEcho As Double = 0.1
    End Class

    ''' <summary>
    ''' Base class for C24 components
    ''' </summary>
    Public MustInherit Class C24Base
        Protected Shared Function InitBytes(byteList As Byte()) As Byte()
            Return byteList.ToArray()
        End Function

        Protected Shared Function TenBits(num As Integer) As Tuple(Of Byte, Byte)
            num = num And &H3FF
            Dim high As Byte = CByte(num >> 3)
            Dim low As Byte = CByte((num And 7) << 4)
            Return Tuple.Create(high, low)
        End Function

        Protected Shared Function CalcFaderScale() As Dictionary(Of Tuple(Of Byte, Byte), Double)
            Dim scale As New Dictionary(Of Tuple(Of Byte, Byte), Double)
            For num As Integer = 0 To Constants.FaderRange - 1
                Dim bits = TenBits(num)
                scale(bits) = num * Constants.FaderStep
            Next
            Return scale
        End Function
    End Class

    ''' <summary>
    ''' Mode Manager class for handling multiple modes
    ''' </summary>
    Public Class ModeManager
        Private _modes As Dictionary(Of String, Dictionary(Of String, Object))
        Private _modesList As List(Of String)
        Private _currentMode As String

        Public Sub New(modesDict As Dictionary(Of String, Dictionary(Of String, Object)))
            _modes = modesDict
            _modesList = modesDict.Keys.ToList()

            ' Find default mode
            For Each kvp In _modes
                If kvp.Value.ContainsKey("default") AndAlso CBool(kvp.Value("default")) Then
                    _currentMode = kvp.Key
                    Exit For
                End If
            Next

            If String.IsNullOrEmpty(_currentMode) Then
                _currentMode = _modesList.First()
            End If
        End Sub

        Public Property Mode As String
            Get
                Return _currentMode
            End Get
            Set(value As String)
                If IsValidMode(value) Then
                    _currentMode = value
                Else
                    Throw New ArgumentException("Invalid mode")
                End If
            End Set
        End Property

        Public Function IsValidMode(mode As String) As Boolean
            Return _modes.ContainsKey(mode)
        End Function

        Public Sub ToggleMode()
            Dim currentIndex = _modesList.IndexOf(_currentMode)
            If currentIndex < _modesList.Count - 1 Then
                _currentMode = _modesList(currentIndex + 1)
            Else
                _currentMode = _modesList(0)
            End If
        End Sub

        Public Function GetData() As Dictionary(Of String, Object)
            Return _modes(_currentMode)
        End Function

        Public Function [Get](key As String) As Object
            Dim data = GetData()
            If data.ContainsKey(key) Then
                Return data(key)
            End If
            Return Nothing
        End Function
    End Class

    ''' <summary>
    ''' C24 Desk class - represents the entire desk
    ''' </summary>
    Public Class C24Desk
        Inherits C24Base

        Private _mode As String
        Private _tracks As C24Track()
        Private _log As Logger

        Public Delegate Sub OscClientSendDelegate(message As Object)
        Public Delegate Sub C24ClientSendDelegate(cmdBytes As Byte())

        Private _oscClientSend As OscClientSendDelegate
        Private _c24ClientSend As C24ClientSendDelegate
        Private _TransportButton As C24ButtonLed
        Private _clock As C24Clock

        Public Property OscClientSend As OscClientSendDelegate
            Get
                Return _oscClientSend
            End Get
            Set(value As OscClientSendDelegate)
                _oscClientSend = value
            End Set
        End Property

        Public Property C24ClientSend As C24ClientSendDelegate
            Get
                Return _c24ClientSend
            End Get
            Set(value As C24ClientSendDelegate)
                _c24ClientSend = value
            End Set
        End Property

        Public ReadOnly Property TransportButton As C24ButtonLed
            Get
                Return _TransportButton
            End Get
        End Property

        Public Property Clock As C24Clock
            Get
                Return _clock
            End Get
            Set(value As C24Clock)
                _clock = value
            End Set
        End Property

        Public Sub New(oscSend As OscClientSendDelegate, c24Send As C24ClientSendDelegate, logger As Logger)
            _log = logger
            _oscClientSend = oscSend
            _c24ClientSend = c24Send
            _mode = Defaults.Scribble

            ' Initialize 32 tracks (24 main + 8 virtual)
            ReDim _tracks(31)
            For i As Integer = 0 To 31
                _tracks(i) = New C24Track(Me, i, _log)
            Next

            ' Initialize Transport button
            _TransportButton = New C24ButtonLed(Me, _log)

            ' Initialize Clock display
            _clock = New C24Clock(Me, _log)
        End Sub

        Public Property Mode As String
            Get
                Return _mode
            End Get
            Set(value As String)
                _log.Debug("Desk mode set: {0}", value)
                _mode = value
                ' Restore scribble strips
                For Each track In _tracks
                    If track.ScribbleStrip IsNot Nothing Then
                        track.ScribbleStrip.RestoreDeskDisplay()
                    End If
                Next
            End Set
        End Property

        Public Function GetTrack(trackNumber As Integer?) As C24Track
            If trackNumber.HasValue AndAlso trackNumber.Value >= 0 AndAlso trackNumber.Value < _tracks.Length Then
                Return _tracks(trackNumber.Value)
            End If
            Return Nothing
        End Function

        Public Sub LongScribble(longText As String)
            ' Display long text across multiple scribble strips
            For i As Integer = 0 To Math.Min(_tracks.Length - 1, 23)
                If _tracks(i).ScribbleStrip IsNot Nothing Then
                    Dim position = i * 4
                    Dim piece = If(position + 4 <= longText.Length,
                                  longText.Substring(position, 4),
                                  longText.Substring(position).PadRight(4))
                    _tracks(i).ScribbleStrip.SetText(piece)
                End If
            Next
        End Sub
    End Class

    ''' <summary>
    ''' C24 Track class - represents a channel strip
    ''' </summary>
    Public Class C24Track
        Inherits C24Base

        Private ReadOnly _desk As C24Desk
        Private ReadOnly _trackNumber As Integer
        Private ReadOnly _oscTrackNumber As Integer
        Private _log As Logger

        Public Property Fader As C24Fader
        Public Property VPot As C24VPot
        Public Property ScribbleStrip As C24ScribbleStrip
        Public Property VuMeter As C24VuMeter
        Public Property ButtonLed As C24ButtonLed
        Public Property AutoMode As C24AutoMode

        Public Sub New(desk As C24Desk, trackNumber As Integer, logger As Logger)
            _desk = desk
            _trackNumber = trackNumber
            _oscTrackNumber = trackNumber + 1
            _log = logger

            ' Initialize components for main tracks (0-23)
            If trackNumber < Constants.Channels Then
                Fader = New C24Fader(Me, _log)
                VPot = New C24VPot(Me, _log)
                VuMeter = New C24VuMeter(Me, _log)
                ButtonLed = New C24ButtonLed(_desk, _log, Me)
                AutoMode = New C24AutoMode(_desk, Me, _log)
            ElseIf trackNumber >= Constants.Channels AndAlso trackNumber < 26 Then
                VuMeter = New C24VuMeter(Me, _log)

            End If

            ' Scribble strips for tracks 0-23 and 29-30
            If trackNumber < Constants.Channels OrElse (trackNumber >= 29 AndAlso trackNumber <= 30) Then
                ScribbleStrip = New C24ScribbleStrip(Me, _log)
            End If

            ' Special jpot for track 28
            If trackNumber = 28 Then
                VPot = New C24JPot(Me, _log)
            End If
        End Sub

        Public ReadOnly Property TrackNumber As Integer
            Get
                Return _trackNumber
            End Get
        End Property

        Public ReadOnly Property OscTrackNumber As Integer
            Get
                Return _oscTrackNumber
            End Get
        End Property

        Public ReadOnly Property Desk As C24Desk
            Get
                Return _desk
            End Get
        End Property
    End Class

    ''' <summary>
    ''' C24 Fader class
    ''' </summary>
    Public Class C24Fader
        Inherits C24Base

        Private ReadOnly _track As C24Track
        Private _gain As Double
        Private _Percent As Double
        Private ReadOnly _cmdBytesTemplate As Byte()
        Private ReadOnly _log As Logger
        Private Shared ReadOnly _faderScale As Dictionary(Of Tuple(Of Byte, Byte), Double) = CalcFaderScale()

        Public Sub New(track As C24Track, logger As Logger)
            _track = track
            _log = logger

            _cmdBytesTemplate = {&HB0, CByte(track.TrackNumber And &H1F), &H0, CByte(track.TrackNumber + &H20), &H0}
        End Sub

        Public Property Gain As Double
            Get
                Return _gain
            End Get
            Set
                _gain = Value
                SendFaderCommand(GainToGainValue(_gain))
            End Set
        End Property

        Public Property Percent As Double
            Get
                Return _Percent
            End Get
            Set
                _Percent = Value
                '_Percent = Math.Max(0, Math.Min(100, Value))
                SendFaderCommand(_Percent / 100)
            End Set
        End Property


        Private Sub SendFaderCommand(gainValue As Double)
            ' ✅ FIX: Create a NEW buffer instead of modifying template
            Dim sendBuffer(_cmdBytesTemplate.Length - 1) As Byte
            Array.Copy(_cmdBytesTemplate, sendBuffer, _cmdBytesTemplate.Length)

            ' Calculate gain values
            Dim clampedGain = Math.Max(0, Math.Min(1, gainValue))
            Dim gainTenBits = CInt(clampedGain * Constants.FaderRange) - 1
            If gainTenBits < 0 Then gainTenBits = 0

            Dim bits = TenBits(gainTenBits)

            ' ✅ FIX: Modify COPY, not template
            sendBuffer(2) = bits.Item1
            sendBuffer(4) = bits.Item2

            ' Send copy (thread-safe)
            _track.Desk.C24ClientSend?.Invoke(sendBuffer)
        End Sub

        Private Function GainToGainValue(Gain) As Double
            'byte_value from 0 to 1023
            Dim byte_value As Integer = 0

            If Gain < -90 Then
                Return 0
            End If

            If Gain < -20 Then
                byte_value = CInt(563.83 * Math.Exp(0.0298 * Gain))
            Else
                byte_value = CInt((27.308 * Gain) + 861.66)
            End If

            Return byte_value / 1023

        End Function
        Public Sub Clear()
            SendFaderCommand(0)
        End Sub

    End Class

    ''' <summary>
    ''' C24 VPot (rotary encoder) class
    ''' </summary>
    Public Class C24VPot
        Inherits C24Base
        'Led gauche i = 0
        'Led droite i = 14
        Dim led_Bit_indexes As Integer() = {1, 9, 10, 11, 12, 13, 14, 15, 17, 18, 19, 20, 21, 22, 23}
        Dim Pan_bins As Integer() = {-100, -99, -83, -67, -50, -33, -17, 0, 0, 17, 33, 50, 67, 83, 99, 100}
        Dim Gauge_bins As Integer() = {0, 1, 8, 15, 23, 31, 38, 46, 54, 62, 69, 77, 85, 92, 99, 100}
        Protected ReadOnly _track As C24Track
        Protected _pan As Double = 0.5
        Private _Gauge As Double
        Private _Gauge_Dot As Double
        Protected ReadOnly _cmdBytesTemplate As Byte()  ' ✅ RENAMED: Template instead of working buffer
        Protected ReadOnly _log As Logger
        Private Enum LedMode
            PanMode
            DotMode
            GaugeMode
        End Enum

        Public Sub New(track As C24Track, logger As Logger)
            _track = track
            _log = logger
            ' ✅ FIX: This is now a TEMPLATE (never modified)
            _cmdBytesTemplate = {&HF0, &H13, &H1, &H0, CByte(track.TrackNumber And &H3F), &H0, &H0, &HF7}
        End Sub
        ''' <summary>
        ''' Panner -100% / +100%
        ''' </summary>
        ''' <returns></returns>
        Public Property Pan As Double
            Get
                Return _pan
            End Get
            Set(value As Double)
                'borned at -100/+100
                _pan = Math.Max(-100, Math.Min(100, value))
                UpdateLed(LedMode.PanMode)
            End Set
        End Property
        ''' <summary>
        ''' Gauge with persistant led On 0% / 100%
        ''' </summary>
        ''' <returns></returns>
        Public Property Gauge As Double
            Get
                Return _Gauge
            End Get
            Set
                _Gauge = Math.Max(0, Math.Min(100, Value))
                UpdateLed(LedMode.GaugeMode)
            End Set
        End Property
        ''' <summary>
        ''' Gauge with not persistant led On 0% / 100%
        ''' </summary>
        ''' <returns></returns>
        Public Property Gauge_Dot As Double
            Get
                Return _Gauge_Dot
            End Get
            Set
                _Gauge_Dot = Math.Max(0, Math.Min(100, Value))
                UpdateLed(LedMode.DotMode)
            End Set
        End Property

        ''' <summary>
        ''' Update LED ring with temporary buffer (thread-safe)
        ''' ✅ FIX: Create new buffer for each send
        ''' </summary>
        Private Sub UpdateLed(Mode As LedMode)
            Dim Led_Bytes As Byte() = Nothing
            ' ✅ FIX: Create a NEW buffer instead of modifying template
            Dim sendBuffer(_cmdBytesTemplate.Length - 1) As Byte
            Array.Copy(_cmdBytesTemplate, sendBuffer, _cmdBytesTemplate.Length)

            ' Calculate LED display 
            Dim Bit_indexes As New List(Of Integer)
            If Mode = LedMode.PanMode Then
                Bit_indexes.Clear()

                If Pan = 0 Then
                    Bit_indexes.Add(led_Bit_indexes(7))

                ElseIf Pan <> 0 Then
                    If Pan = 100 Then
                        Bit_indexes.Add(led_Bit_indexes(14))
                    ElseIf Pan = -100 Then
                        Bit_indexes.Add(led_Bit_indexes(0))
                    Else
                        For i = 0 To Pan_bins.Count - 2
                            If Pan > Pan_bins(i) AndAlso Pan <= Pan_bins(i + 1) Then
                                Bit_indexes.Add(led_Bit_indexes(i))
                                Exit For
                            End If
                        Next
                    End If
                End If


            ElseIf Mode = LedMode.DotMode Then
                Bit_indexes.Clear()

                If _Gauge_Dot = 0 Then
                    Bit_indexes.Add(led_Bit_indexes(0))

                ElseIf _Gauge_Dot = 100 Then

                    Bit_indexes = led_Bit_indexes.ToList

                Else
                    For i = 0 To Gauge_bins.Count - 2
                        If _Gauge_Dot > Gauge_bins(i) AndAlso _Gauge_Dot <= Gauge_bins(i + 1) Then
                            Bit_indexes.Add(led_Bit_indexes(i))
                            Exit For
                        End If
                    Next

                End If

            ElseIf Mode = LedMode.GaugeMode Then
                Bit_indexes.Clear()

                If Gauge = 0 Then
                    Bit_indexes.Add(led_Bit_indexes(0))

                ElseIf Gauge = 100 Then

                    Bit_indexes = led_Bit_indexes.ToList

                Else
                    For i = 0 To Gauge_bins.Count - 2
                        If Gauge >= Gauge_bins(i + 1) Then

                            Bit_indexes.Add(led_Bit_indexes(i))


                        End If
                    Next

                End If

            End If
            'If Mode = LedMode.GaugeMode Then
            '    Led_Bytes = Utility.Bit.GetBytes(Bit_indexes.ToArray, {&HFF, &HFF, &HFF}, False)
            'Else
            Led_Bytes = Utility.Bit.GetBytes(Bit_indexes.ToArray, {&H0, &H0, &H0})
            'End If

            sendBuffer(4) = Led_Bytes(0)
            sendBuffer(5) = Led_Bytes(1)
            sendBuffer(6) = Led_Bytes(2)
            ' ✅ FIX: Update COPY with track number
            sendBuffer(4) = sendBuffer(4) Or (CByte(_track.TrackNumber) And &H3F)

            ' Send copy (thread-safe)
            _track.Desk.C24ClientSend?.Invoke(sendBuffer)


        End Sub

        Public Sub Clear()
            Dim sendBuffer(_cmdBytesTemplate.Length - 1) As Byte
            Array.Copy(_cmdBytesTemplate, sendBuffer, _cmdBytesTemplate.Length)
            sendBuffer(4) = &H0
            sendBuffer(5) = &H0
            sendBuffer(6) = &H0
            ' ✅ FIX: Update COPY with track number
            sendBuffer(4) = sendBuffer(4) Or (CByte(_track.TrackNumber) And &H3F)

            ' Send copy (thread-safe)
            _track.Desk.C24ClientSend?.Invoke(sendBuffer)
        End Sub



    End Class

    ''' <summary>
    ''' C24 JPot (jog wheel) class
    ''' </summary>
    Public Class C24JPot
        Inherits C24VPot

        Public Sub New(track As C24Track, logger As Logger)
            MyBase.New(track, logger)
        End Sub
    End Class

    ''' <summary>
    ''' C24 Scribble Strip class
    ''' </summary>
    Public Class C24ScribbleStrip
        Inherits C24Base

        Private ReadOnly _track As C24Track
        Private _text As New Dictionary(Of String, String)
        Private _displayText As String
        Private ReadOnly _cmdBytesTemplate As Byte()  ' ✅ RENAMED: Template instead of working buffer
        Private _restoreTimer As Timer
        Private ReadOnly _log As Logger

        Public Sub New(track As C24Track, logger As Logger)
            _track = track
            _log = logger
            _displayText = $"  {track.OscTrackNumber:D2}"
            _text("/track/number") = _displayText

            ' ✅ FIX: This is now a TEMPLATE (never modified)
            ' Correct format: F0 13 01 40 [TrackNum] 00 [Char1] [Char2] [Char3] [Char4] F7 00 (12 bytes total)
            _cmdBytesTemplate = {&HF0, &H13, &H1, &H40, CByte(track.TrackNumber), 0, 0, 0, 0, 0, &HF7, 0}
        End Sub

        Public Sub SetText(text As String)
            _displayText = text.PadRight(4).Substring(0, 4)
            UpdateDisplay()
        End Sub

        ''' <summary>
        ''' Update scribble strip display with temporary buffer (thread-safe)
        ''' ✅ FIX: Create new buffer for each send to prevent corruption
        ''' </summary>
        Private Sub UpdateDisplay()
            ' ✅ FIX: Create a NEW buffer instead of modifying template
            Dim sendBuffer(_cmdBytesTemplate.Length - 1) As Byte
            Array.Copy(_cmdBytesTemplate, sendBuffer, _cmdBytesTemplate.Length)

            ' Write text to COPY
            Dim textBytes = Text.Encoding.ASCII.GetBytes(_displayText)
            For i As Integer = 0 To Math.Min(3, textBytes.Length - 1)
                sendBuffer(6 + i) = textBytes(i)
            Next

            ' Position 10 is F7, position 11 is padding 00 (already set in template)

            ' ✅ FIX: Send COPY (not template!)
            _track.Desk.C24ClientSend?.Invoke(sendBuffer)
        End Sub

        Public Sub RestoreDeskDisplay()
            ' Restore to default mode display
            UpdateDisplay()
        End Sub
    End Class

    ''' <summary>
    ''' C24 VU Meter class
    ''' </summary>
    Public Class C24VuMeter
        Inherits C24Base

        Private ReadOnly _track As C24Track
        Private ReadOnly _cmdBytesTemplate As Byte()  ' ✅ RENAMED: Template (but not modified in this class)
        Private ReadOnly _log As Logger
        Private _Percent_Left As Double
        Private led_Bit_indexes As Integer() = {15, 14, 13, 12, 11, 10, 9, 7, 6, 5, 4, 3, 2, 1}
        Dim Gauge_bins As Integer() = {0, 1, 8, 17, 25, 33, 42, 50, 58, 67, 75, 83, 92, 99, 100}
        Private _Percent_Right As Double

        Enum Stereo
            Left
            Right
            Mono
        End Enum

        Public Sub New(track As C24Track, logger As Logger)
            _track = track
            _log = logger
            ' ✅ NOTE: VU Meter doesn't modify buffer, but renamed for consistency
            If _track IsNot Nothing Then
                _cmdBytesTemplate = {&HF0, &H13, &H1, &H10, CByte(track.TrackNumber), &H7F, &H7F, &HF7}
            End If

        End Sub
        ''' <summary>
        ''' Left Vu meter value 0-100%
        ''' </summary>
        ''' <returns></returns>
        Public Property Percent_Left As Double
            Get
                Return _Percent_Left
            End Get
            Set
                _Percent_Left = Value
                _Percent_Left = Math.Max(0, Math.Min(100, Value))
                Percent_UpdateLevel(Stereo.Left, _Percent_Left)
            End Set
        End Property
        Public Property Percent_Right As Double
            Get
                Return _Percent_Right
            End Get
            Set
                _Percent_Right = Value
                _Percent_Right = Math.Max(0, Math.Min(100, Value))
                Percent_UpdateLevel(Stereo.Right, _Percent_Right)
            End Set
        End Property

        Public Function Get_ParamsValues(_bytes As Byte()) As String
            Dim values As String = ""
            Dim bits_indexes As Integer() = Utility.Bit.GetBits_Indexes({_bytes(5), _bytes(6)})
            If (_bytes(4) And &H20) = &H20 Then
                values &= "(R)"
            Else
                values &= "(L)"
            End If
            values &= bits_indexes.Count
            Return values
        End Function
        ''' <summary>
        ''' Update VU meter level
        ''' ✅ NOTE: This class doesn't modify the buffer yet, but could be extended later
        ''' </summary>
        Private Sub Percent_UpdateLevel(Channel As Stereo, Value As Double)


            Dim sendBuffer(_cmdBytesTemplate.Length - 1) As Byte
            Array.Copy(_cmdBytesTemplate, sendBuffer, _cmdBytesTemplate.Length)

            Dim Bit_indexes As New List(Of Integer)

            For i = 0 To Gauge_bins.Count - 2
                If Value >= Gauge_bins(i + 1) Then
                    Bit_indexes.Add(led_Bit_indexes(i))
                End If
            Next

            Dim Led_Bytes = Utility.Bit.GetBytes(Bit_indexes.ToArray, {&H0, &H0})
            'End If


            sendBuffer(5) = Led_Bytes(0)
            sendBuffer(6) = Led_Bytes(1)

            If Channel = Stereo.Left Then
                sendBuffer(4) = &H0
            Else
                sendBuffer(4) = &H20
            End If
            sendBuffer(4) = sendBuffer(4) Or (CByte(_track.TrackNumber) And &H3F)

            _track.Desk.C24ClientSend?.Invoke(sendBuffer)
        End Sub


        Public Sub Clear(Optional Left As Boolean = True, Optional Right As Boolean = True)
            Dim sendBuffer(_cmdBytesTemplate.Length - 1) As Byte
            Array.Copy(_cmdBytesTemplate, sendBuffer, _cmdBytesTemplate.Length)
            sendBuffer(4) = &H0
            sendBuffer(5) = &H0
            sendBuffer(6) = &H0
            If Left Then
                sendBuffer(4) = sendBuffer(4) Or (CByte(_track.TrackNumber) And &H3F)
                _track.Desk.C24ClientSend?.Invoke(sendBuffer)
            End If
            If Right Then
                sendBuffer(4) = &H20
                sendBuffer(4) = sendBuffer(4) Or (CByte(_track.TrackNumber) And &H3F)
                _track.Desk.C24ClientSend?.Invoke(sendBuffer)
            End If



        End Sub

    End Class

    ''' <summary>
    ''' C24 Button LED class
    ''' </summary>
    Public Class C24ButtonLed
        Inherits C24Base

        Private ReadOnly _desk As C24Desk
        Private ReadOnly _track As C24Track
        Private ReadOnly _log As Logger

        ' LED state tracking
        Private ReadOnly _ledStates As New Dictionary(Of String, Integer)

        ' ✅ FIX: Command byte templates - NOW USED for optimization!
        Private Shared ReadOnly _ledCommandTemplates As New Dictionary(Of String, Byte())

        Shared Sub New()
            ' Initialize LED command templates (pre-allocated, reusable)
            _ledCommandTemplates("track") = {&H90, 0, 0} ' Track buttons (3 bytes: cmd, button, track|state)
            _ledCommandTemplates("command") = {&H90, 0, 0} ' Command buttons (4 bytes)

            ' Automation LEDs (SysEx with padding) - 30 bytes total
            ' F0 13 01 20 XX YY F7 + 23 bytes padding (00)
            Dim autoTemplate(29) As Byte
            autoTemplate(0) = &HF0
            autoTemplate(1) = &H13
            autoTemplate(2) = &H1
            autoTemplate(3) = &H20
            ' Bytes 4-5: Track and Mode (set dynamically)
            autoTemplate(6) = &HF7
            ' Bytes 7-29: padding (already 0)
            _ledCommandTemplates("automation") = autoTemplate
        End Sub

        Public Sub New(desk As C24Desk, logger As Logger, Optional track As C24Track = Nothing)
            _desk = desk
            _track = track
            _log = logger
        End Sub

        ''' <summary>
        ''' Set button LED state by OSC address
        ''' </summary>
        Public Sub SetButton(address As String, value As Integer)
            Try
                ' Clamp value to valid range (0=off, 1=on, some buttons support more states)
                Dim ledValue = Math.Max(0, Math.Min(127, value))

                ' Store LED state
                _ledStates(address) = ledValue

                ' Parse address and find button mapping
                Dim parts = address.Split("/"c).Where(Function(s) Not String.IsNullOrEmpty(s)).ToArray()

                If parts.Length = 0 Then
                    _log.Warn("Empty button address")
                    Return
                End If

                ' Handle different button types
                If address.Contains("/track/") Then
                    If parts.Contains("automode") Then
                        ' Automation mode buttons
                        SetAutomationButton(parts, ledValue)
                    Else
                        ' Track-specific buttons (Solo, Mute, RecArm, etc.)
                        SetTrackButton(parts, ledValue)
                    End If

                ElseIf address.Contains("/transport/") Then
                    ' Transport buttons (Play, Stop, Record, etc.)
                    SetTransportButton(parts, ledValue)
                ElseIf address.Contains("/command/") OrElse address.Contains("/utility/") Then
                    ' Command/utility buttons (F1-F10, Shift, etc.)
                    SetCommandButton(parts, ledValue)
                ElseIf address.Contains("/automation/") Then
                    ' Automation mode buttons
                    SetAutomationButton(parts, ledValue)
                Else
                    _log.Debug("Unhandled button address: {0}", address)
                End If

            Catch ex As Exception
                _log.Error("Error setting button LED {0}: {1}", address, ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Set track-specific button LED (Solo, Mute, RecArm, etc.)
        ''' ✅ OPTIMIZED: Now uses template instead of creating new buffer
        ''' </summary>
        Private Sub SetTrackButton(addressParts As String(), value As Integer)
            Try
                ' Expected format: track/{n}/{button}
                If addressParts.Length < 3 OrElse addressParts(0) <> "track" Then
                    Return
                End If

                Dim buttonName = addressParts(2).ToLower()

                ' Map button name to button code
                Dim buttonCode As Byte = 0
                Select Case buttonName
                    Case "recarm", "rec"
                        buttonCode = &H0
                    Case "pan_send", "pan"
                        buttonCode = &H1
                    Case "eq"
                        buttonCode = &H2
                    Case "dynamics", "dyn"
                        buttonCode = &H3
                    Case "switch_active", "switch"
                        buttonCode = &H4
                    Case "automode", "auto"
                        buttonCode = &H5
                    Case "channelselect", "select", "sel"
                        buttonCode = &H6
                    Case "solo"
                        buttonCode = &H7
                    Case "mute"
                        buttonCode = &H8
                    Case "touch"
                        buttonCode = &H9
                    Case "inserts", "insert"
                        buttonCode = &HA
                    Case Else
                        _log.Warn("Unknown track button: {0}", buttonName)
                        Return
                End Select

                ' ✅ OPTIMIZED: Use template instead of creating new buffer
                Dim template = _ledCommandTemplates("track")
                Dim cmdBytes(template.Length - 1) As Byte
                Array.Copy(template, cmdBytes, template.Length)

                ' Fill in dynamic values
                ' Byte 0: 0x90 (already set in template)
                ' Byte 1: Button code
                cmdBytes(1) = buttonCode

                ' Byte 2: LED state | Track number
                '         Bit 6 (0x40) = LED ON
                '         Bits 0-5 (0x00-0x1F) = Track number (0-31)
                Dim ledState As Byte = If(value > 0, &H40, &H0)
                cmdBytes(2) = ledState Or CByte(_track.TrackNumber And &H1F)

                _desk.C24ClientSend?.Invoke(cmdBytes)
                _log.Debug("Track button LED: Track {0}, Button {1}, Value {2}, Bytes: {3:X2} {4:X2} {5:X2}",
                          _track.OscTrackNumber, buttonName, value, cmdBytes(0), cmdBytes(1), cmdBytes(2))

            Catch ex As Exception
                _log.Error("Error setting track button: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Set transport button LED (Play, Stop, Record, etc.)
        ''' ✅ OPTIMIZED: Now uses template instead of creating new buffer
        ''' </summary>
        Private Sub SetTransportButton(addressParts As String(), value As Integer)
            Try
                ' Expected format: transport/{button}
                If addressParts.Length < 2 OrElse addressParts(0) <> "transport" Then
                    Return
                End If

                Dim buttonName = addressParts(1).ToLower()

                ' Map button name to button code
                Dim buttonCode As Byte = 0
                Select Case buttonName
                    Case "rewind", "rwd"
                        buttonCode = &HD
                    Case "forward", "fwd", "ff"
                        buttonCode = &HE
                    Case "stop"
                        buttonCode = &HF
                    Case "play"
                        buttonCode = &H10
                    Case "record", "rec"
                        buttonCode = &H11
                    Case "scrub"
                        buttonCode = &H12
                    Case "shuttle"
                        buttonCode = &H13
                    Case Else
                        _log.Warn("Unknown transport button: {0}", buttonName)
                        Return
                End Select

                ' ✅ OPTIMIZED: Use template instead of creating new buffer
                Dim template = _ledCommandTemplates("command")
                Dim cmdBytes(template.Length - 1) As Byte
                Array.Copy(template, cmdBytes, template.Length)
                Dim Led_status As Byte = If(value > 0, &H40, &H0)

                ' Fill in dynamic values
                ' Byte 0: 0x90 (already set in template)
                ' Byte 1: Button code

                cmdBytes(1) = buttonCode

                ' Byte 2: Child byte (0x1C for transport, OR with 0x18)

                cmdBytes(2) = Led_status Or &H1C

                ' Byte 3: LED state


                'cmdBytes(3) = Led_status

                _desk.C24ClientSend?.Invoke(cmdBytes)
                _log.Debug("Transport button LED: {0}, Value {1}", buttonName, value)

            Catch ex As Exception
                _log.Error("Error setting transport button: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Set command/utility button LED (F1-F10, Shift, Nav, etc.)
        ''' ✅ OPTIMIZED: Now uses template instead of creating new buffer
        ''' </summary>
        Private Sub SetCommandButton(addressParts As String(), value As Integer)
            Try
                If addressParts.Length < 2 Then
                    Return
                End If

                Dim category = addressParts(0).ToLower()
                Dim buttonName = addressParts(1).ToLower()

                ' Map button name to button code and child byte
                Dim buttonCode As Byte = 0
                Dim childByte As Byte = &H18 ' Default to utility group

                Select Case buttonName
                    ' F-keys (child byte 0x18)
                    Case "f1"
                        buttonCode = &H0
                    Case "f2"
                        buttonCode = &H2
                    Case "f3"
                        buttonCode = &H4
                    Case "f4"
                        buttonCode = &H6
                    Case "f5"
                        buttonCode = &H8
                    Case "f6"
                        buttonCode = &H32
                    Case "f7"
                        buttonCode = &H33
                    Case "f8"
                        buttonCode = &H34
                    Case "f9"
                        buttonCode = &H35
                    Case "f10"
                        buttonCode = &H36

                    ' Display mode buttons
                    Case "showvalues"
                        buttonCode = &H10
                    Case "showgroup"
                        buttonCode = &H11
                    Case "showchannelnames", "shownames"
                        buttonCode = &H12
                    Case "showinfo"
                        buttonCode = &H13

                    ' Navigation buttons (child byte 0x19)
                    Case "nav"
                        buttonCode = &HC
                        childByte = &H19
                    Case "zoom"
                        buttonCode = &HD
                        childByte = &H19
                    Case "seladj", "selectadjust"
                        buttonCode = &HE
                        childByte = &H19

                    ' Edit modes (child byte 0x1B)
                    Case "shuffle"
                        buttonCode = &H0
                        childByte = &H1B
                    Case "slip"
                        buttonCode = &H1
                        childByte = &H1B
                    Case "spot"
                        buttonCode = &H2
                        childByte = &H1B
                    Case "grid"
                        buttonCode = &H3
                        childByte = &H1B

                    ' Sends (child byte 0x1D)
                    Case "a/f", "af", "senda"
                        buttonCode = &HB
                        childByte = &H1D
                    Case "b/g", "bg", "sendb"
                        buttonCode = &HC
                        childByte = &H1D
                    Case "c/h", "ch", "sendc"
                        buttonCode = &HD
                        childByte = &H1D
                    Case "d/i", "di", "sendd"
                        buttonCode = &HE
                        childByte = &H1D
                    Case "e/j", "ej", "sende"
                        buttonCode = &HF
                        childByte = &H1D

                    Case Else
                        _log.Warn("Unknown command button: {0}", buttonName)
                        Return
                End Select

                ' ✅ OPTIMIZED: Use template instead of creating new buffer
                Dim template = _ledCommandTemplates("command")
                Dim cmdBytes(template.Length - 1) As Byte
                Array.Copy(template, cmdBytes, template.Length)

                ' Fill in dynamic values
                ' Byte 0: 0x90 (already set in template)
                ' Byte 1: Button code
                cmdBytes(1) = buttonCode

                ' Byte 2: Child byte OR with 0x18
                cmdBytes(2) = childByte Or &H18

                ' Byte 3: LED state
                cmdBytes(3) = If(value > 0, &H40, &H0)

                _desk.C24ClientSend?.Invoke(cmdBytes)
                _log.Debug("Command button LED: {0}/{1}, Value {2}", category, buttonName, value)

            Catch ex As Exception
                _log.Error("Error setting command button: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Set automation mode button LED
        ''' ✅ OPTIMIZED: Now uses template instead of creating new buffer
        ''' </summary>
        Private Sub SetAutomationButton(addressParts As String(), value As Integer)
            Try
                ' Expected format: automation/{mode} or track/{n}/automode/{mode}
                Dim modeName As String = Nothing
                Dim trackNum As Integer = _track.TrackNumber

                If addressParts.Length >= 2 AndAlso addressParts(0) = "automation" Then
                    modeName = addressParts(1).ToLower()
                ElseIf addressParts.Length >= 4 AndAlso addressParts(2) = "automode" Then
                    modeName = addressParts(3).ToLower()
                    ' Get track number from address
                    trackNum = _track.TrackNumber
                Else
                    Return
                End If

                ' Map mode name to mode code (bits for byte 5)
                Dim modeCode As Byte = 0
                Select Case modeName
                    Case "read"
                        modeCode = &H4  ' Bit 2
                    Case "write"
                        modeCode = &H40 ' Bit 4
                    Case "touch"
                        modeCode = &H20  ' Bit 3
                    Case "latch"
                        modeCode = &H10  ' Bit 1
                    Case "trim", "tm"
                        modeCode = &H8  ' Bit 0
                    Case "off"
                        modeCode = &H0  ' All bits off
                    Case Else
                        _log.Warn("Unknown automation mode: {0}", modeName)
                        Return
                End Select

                ' ✅ OPTIMIZED: Use template instead of creating new buffer
                Dim template = _ledCommandTemplates("automation")
                Dim cmdBytes(template.Length - 1) As Byte
                Array.Copy(template, cmdBytes, template.Length)

                ' Fill in dynamic values
                ' Bytes 0-3: F0 13 01 20 (already set in template)
                ' Byte 4: Track number
                cmdBytes(4) = CByte(trackNum)

                ' Byte 5: Mode code (only if ON)
                cmdBytes(5) = If(value > 0, modeCode, &H0)

                ' Byte 6: F7 (already set in template)
                ' Bytes 7-29: 00 padding (already set in template)

                _desk.C24ClientSend?.Invoke(cmdBytes)
                _log.Debug("Automation LED: Track {0}, Mode {1}, Value {2}, Bytes: {3}",
                          trackNum, modeName, value, Utils.HexL(cmdBytes))

            Catch ex As Exception
                _log.Error("Error setting automation button: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Get current LED state for a button
        ''' </summary>
        Public Function GetLedState(address As String) As Integer?
            If _ledStates.ContainsKey(address) Then
                Return _ledStates(address)
            End If
            Return Nothing
        End Function

        ''' <summary>
        ''' Clear all LED states
        ''' </summary>
        Public Sub ClearAllLeds()
            _ledStates.Clear()
            _log.Debug("All LED states cleared for track {0}", _track.OscTrackNumber)
        End Sub

        ''' <summary>
        ''' Set multiple buttons at once (for bank switching, etc.)
        ''' </summary>
        Public Sub SetButtons(buttons As Dictionary(Of String, Integer))
            For Each kvp In buttons
                SetButton(kvp.Key, kvp.Value)
            Next
        End Sub

    End Class

    ''' <summary>
    ''' C24 Automation Mode class
    ''' </summary>
    Public Class C24AutoMode
        Inherits C24Base

        Private ReadOnly _desk As C24Desk
        Private ReadOnly _track As C24Track
        Private ReadOnly _modes As New Dictionary(Of String, Boolean)
        Private ReadOnly _cmdBytes As Byte()
        Private ReadOnly _log As Logger

        Public Sub New(desk As C24Desk, track As C24Track, logger As Logger)
            _desk = desk
            _track = track
            _log = logger
            _cmdBytes = {&HF0, &H13, &H1, &H20, CByte(track.TrackNumber And &H1F), 0, &HF7}

            ' Initialize modes
            _modes("write") = False
            _modes("touch") = False
            _modes("latch") = False
            _modes("trim") = False
            _modes("read") = False
        End Sub

        Public Sub SetMode(mode As String, state As Boolean)
            If _modes.ContainsKey(mode) Then
                _modes(mode) = state
                UpdateLed()
            End If
        End Sub

        Private Sub UpdateLed()
            ' Update automation mode LEDs
            _desk.C24ClientSend?.Invoke(_cmdBytes)
        End Sub
    End Class

    ''' <summary>
    ''' C24 Clock Display class
    ''' Manages the main clock display with 7-segment encoding
    ''' </summary>
    Public Class C24Clock
        Inherits C24Base

        ' 7-segment display encoding (7 bits, bit 7 not used)
        ' Segment layout:
        '     T
        '   L   R
        '     M
        '   L   R
        '     B
        ' Bits: 6543210 = TRBBLML
        Private Shared BytesToChar As New Dictionary(Of Byte, Char)
        Private Shared ReadOnly SevenSegment As New Dictionary(Of Char, Byte) From {
            {"0"c, &B1111110},
            {"1"c, &B110000},
            {"2"c, &B1101101},
            {"3"c, &B1111001},
            {"4"c, &B110011},
            {"5"c, &B1011011},
            {"6"c, &B1011111},
            {"7"c, &B1110000},
            {"8"c, &B1111111},
            {"9"c, &B1111011},
            {"-"c, &B1},
            {" "c, &H0},
            {"L"c, &HE},
            {"h"c, &H17},
            {"o"c, &H1D},
            {"b"c, &H1F},
            {"H"c, &H37},
            {"J"c, &H38},
            {"Y"c, &H3B},
            {"d"c, &H3D},
            {"U"c, &H3E},
            {"R"c, &H46},
            {"F"c, &H47},
            {"C"c, &H4E},
            {"E"c, &H4F},
            {"S"c, &B1011011},
            {"P"c, &H67},
            {"Z"c, &B1101101},
            {"A"c, &H77}
        }

        ' Command templates ' 
        Private Shared ReadOnly ClockBytesTemplate As Byte() = {
            &HF0, &H13, &H1, &H30, &H19, &H14, &H1,
            &H46, &H4F, &H67, &H77, &H4F, &H46, &H1, &HF7
        }

        Private Shared ReadOnly LedBytesTemplate As Byte() = {
            &HF0, &H13, &H1, &H20, &H19, &H0, &HF7
        }

        '' Clock modes configuration
        'Private Shared ReadOnly ClockModesDict As New Dictionary(Of String, Dictionary(Of String, Object)) From {
        '    {"time", New Dictionary(Of String, Object) From {
        '        {"address", "/clock/time"},
        '        {"dots", CByte(&B10101)},
        '        {"LED", CByte(&H40)},
        '        {"formatter", "FormatTime"},
        '        {"default", False}
        '    }},
        '    {"frames", New Dictionary(Of String, Object) From {
        '        {"address", "/clock/frames"},
        '        {"dots", CByte(&B101010)},
        '        {"LED", CByte(&H20)},
        '        {"formatter", "FormatTime"},
        '        {"default", False}
        '    }},
        '    {"samples", New Dictionary(Of String, Object) From {
        '        {"address", "/clock/samples"},
        '        {"dots", CByte(&H0)},
        '        {"LED", CByte(&H10)},
        '        {"formatter", "FormatDefault"},
        '        {"default", False}
        '    }},
        '    {"beat", New Dictionary(Of String, Object) From {
        '        {"address", "/clock/beat"},
        '        {"dots", CByte(&B10100)},
        '        {"LED", CByte(&H8)},
        '        {"formatter", "FormatBeat"},
        '        {"default", True}
        '    }}
        '}

        Private _dots As Byte
        Private ReadOnly _desk As C24Desk
        Private ReadOnly _text As New Dictionary(Of String, String)

        Private ReadOnly _cmdBytes As Byte()

        Private ReadOnly _log As Logger
        Private _formatter As Func(Of String, String)
        Private _Mode As ClockMode
        Private _ClockText As String

        Public Enum ClockMode
            time
            frames
            samples
            beat
        End Enum
        Public Property Mode As ClockMode
            Get
                Return _Mode
            End Get
            Set
                _Mode = Value
                UpdateMode()
            End Set
        End Property
        Public Property ClockText As String
            Get
                Return _ClockText
            End Get
            Set
                _ClockText = Value
                UpdateDisplay()
            End Set
        End Property

        Public Sub New(desk As C24Desk, logger As Logger)
            _desk = desk
            _log = logger
            _Mode = ClockMode.beat



            ' Clone template arrays (don't modify templates)
            _cmdBytes = CType(ClockBytesTemplate.Clone(), Byte())
            '_ledBytes = CType(LedBytesTemplate.Clone(), Byte())

            BytesToChar = New Dictionary(Of Byte, Char)
            For Each _kv As KeyValuePair(Of Char, Byte) In SevenSegment
                If BytesToChar.ContainsKey(_kv.Value) = False Then
                    BytesToChar.Add(_kv.Value, _kv.Key)
                End If

            Next

        End Sub
        Public Function Get_ParamsValues(_bytes As Byte()) As String
            Dim clock_string As String = ""
            For i = 6 To _bytes.Count - 1
                If _bytes(i) <> &HF7 Then
                    clock_string &= (BytesToChar(_bytes(i)))
                Else
                    Exit For
                End If
            Next

            Dim clock_chars = clock_string.Reverse
            Dim nb_X As Integer = 8 - clock_string.Count

            If nb_X > 0 Then
                For i = 0 To nb_X
                    clock_chars = clock_chars.Prepend("X")
                Next

            End If
            clock_string = New String(clock_chars.ToArray)
            Return clock_string
        End Function
        Private Sub UpdateMode()
            Select Case Mode
                Case ClockMode.beat
                    _dots = CByte(&B10100)
                    _formatter = AddressOf FormatBeat
                Case ClockMode.frames
                    _dots = CByte(&B101010)
                    _formatter = AddressOf FormatTime
                Case ClockMode.samples
                    _dots = CByte(&H0)
                    _formatter = AddressOf FormatDefault
                Case ClockMode.time
                    _dots = CByte(&B10101)
                    _formatter = AddressOf FormatTime
            End Select

        End Sub

        ''' <summary>
        ''' Transform text to 7-segment encoding (8 digits, right-to-left)
        ''' </summary>
        Private Shared Function TransformText(text As String) As List(Of Byte)
            Dim result As New List(Of Byte)
            Dim position = text.Length - 1
            Dim outputCount = 0

            ' Process from right to left (7-segment displays are right-justified)
            While outputCount < 8 AndAlso position >= 0
                Dim currentChar = text(position)
                Dim segmentValue As Byte = 0

                If SevenSegment.TryGetValue(currentChar, segmentValue) Then
                    result.Add(segmentValue)
                    outputCount += 1
                End If

                position -= 1
            End While

            ' Fill remaining positions with blank (0x00)
            While outputCount < 8
                result.Add(&H0)
                outputCount += 1
            End While

            Return result
        End Function

        ''' <summary>
        ''' Format beat text (e.g., "001.1.01" -> "001 1.01 ")
        ''' </summary>
        Private Shared Function FormatBeat(text As String) As String
            If text.Length >= 5 AndAlso text(text.Length - 5) = "."c Then
                Return text.Substring(0, text.Length - 4) & " " & text.Substring(text.Length - 4) & " "
            Else
                Return text & " "
            End If
        End Function

        ''' <summary>
        ''' Format time text (last 13 chars)
        ''' </summary>
        Private Shared Function FormatTime(text As String) As String
            If text.Length >= 13 Then
                Return text.Substring(text.Length - 13)
            Else
                Return text
            End If
        End Function

        ''' <summary>
        ''' Default formatter (append space)
        ''' </summary>
        Private Shared Function FormatDefault(text As String) As String
            Return text & " "
        End Function



        ''' <summary>
        ''' Update display with current text
        ''' </summary>
        Private Sub UpdateDisplay()

            _cmdBytes(5) = _dots

            ' Apply formatter
            Dim formattedText = _formatter(ClockText)

            ' Transform to 7-segment encoding
            Dim segmentBytes = TransformText(formattedText)

            ' Copy to command buffer (positions 6-13)
            For i = 0 To Math.Min(7, segmentBytes.Count - 1)
                _cmdBytes(6 + i) = segmentBytes(i)
            Next

            ' Send to desk
            _desk.C24ClientSend?.Invoke(_cmdBytes)
        End Sub



        Public Overrides Function ToString() As String
            Return $"C24Clock: Mode={Mode.ToString}, Text={ClockText}
"
        End Function
    End Class

    ''' <summary>
    ''' C24 OSC Session class
    ''' Manages OSC communication between Control24 and DAW
    ''' </summary>
    Public Class C24OscSession
        Implements IDisposable

        Private _desk As C24Desk
        Private _log As Logger
        Private _isClosing As Boolean

        ' OSC integration
        Private _oscManager As OscManager
        Private _oscServerPort As Integer = Defaults.Control24Osc
        Private _oscDawAddress As String = "127.0.0.1"
        Private _oscDawPort As Integer = Defaults.OscDaw

        Public Sub New(logger As Logger)
            _log = logger
            _desk = New C24Desk(AddressOf OscClientSendImpl, AddressOf C24ClientSendImpl, _log)
        End Sub

        ''' <summary>
        ''' Configure OSC settings
        ''' </summary>
        Public Sub ConfigureOSC(serverPort As Integer, dawAddress As String, dawPort As Integer)
            _oscServerPort = serverPort
            _oscDawAddress = dawAddress
            _oscDawPort = dawPort
            _log.Info("OSC configured - Server:{0} DAW:{1}:{2}", serverPort, dawAddress, dawPort)
        End Sub

        ''' <summary>
        ''' Start the OSC session
        ''' </summary>
        Public Sub Start()
            Try
                ' Start OSC manager
                _oscManager = New OscManager(_oscServerPort, _oscDawAddress, _oscDawPort, _log)
                AddHandler _oscManager.MessageReceived, AddressOf OnOscMessageReceived
                _oscManager.Start()
                _log.Info("OSC Manager started")
            Catch ex As Exception
                _log.Error("Failed to start OSC Manager: {0}", ex.Message)
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' Handle OSC messages received from DAW
        ''' </summary>
        Private Sub OnOscMessageReceived(sender As Object, e As OscMessageReceivedEventArgs)
            Try
                _log.Debug("OSC from DAW: {0}", e.Message.ToString())

                ' Parse OSC address to determine track and parameter
                Dim address = e.Message.Address
                Dim parts = address.Split("/"c).Where(Function(s) Not String.IsNullOrEmpty(s)).ToArray()

                If parts.Length >= 3 AndAlso parts(0) = "track" Then
                    ' Track command: /track/{n}/{parameter}
                    Dim trackNum = Integer.Parse(parts(1)) - 1 ' Convert to 0-based
                    Dim parameter = parts(2)
                    Dim track = _desk.GetTrack(trackNum)

                    If track IsNot Nothing AndAlso e.Message.Arguments.Count > 0 Then
                        Select Case parameter.ToLower()
                            Case "volume"
                                If track.Fader IsNot Nothing Then
                                    Dim value = Convert.ToSingle(e.Message.Arguments(0))
                                    track.Fader.Gain = value
                                End If

                            Case "pan"
                                If track.VPot IsNot Nothing Then
                                    Dim value = Convert.ToSingle(e.Message.Arguments(0))
                                    track.VPot.Pan = value
                                End If

                            Case "name"
                                If track.ScribbleStrip IsNot Nothing Then
                                    Dim text = Convert.ToString(e.Message.Arguments(0))
                                    track.ScribbleStrip.SetText(text)
                                End If

                            Case "vu"
                                If track.VuMeter IsNot Nothing AndAlso parts.Length >= 4 Then
                                    Dim speaker = Integer.Parse(parts(3))
                                    Dim value = Convert.ToSingle(e.Message.Arguments(0))
                                    'track.VuMeter.UpdateLevel(speaker, value)
                                End If

                            Case Else
                                _log.Debug("Unhandled track parameter: {0}", parameter)
                        End Select
                    End If
                End If

            Catch ex As Exception
                _log.Error("Error handling OSC message: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' OSC client send implementation
        ''' Sends OSC messages to the DAW
        ''' </summary>
        Private Sub OscClientSendImpl(message As Object)
            Try
                ' Check if OSC manager is ready
                If _oscManager Is Nothing OrElse Not _oscManager.IsReady Then
                    _log.Warn("OSC Manager not ready, cannot send message")
                    Return
                End If

                ' Handle different message types
                If TypeOf message Is Rug.Osc.OscMessage Then
                    ' Direct OSC message
                    Dim oscMsg = CType(message, Rug.Osc.OscMessage)
                    _oscManager.SendToDAW(oscMsg)
                    _log.Debug("OSC Sent: {0}", oscMsg.ToString())

                ElseIf TypeOf message Is String Then
                    ' Simple string address (no args)
                    Dim address = CStr(message)
                    _oscManager.SendToDAW(address)
                    _log.Debug("OSC Sent: {0}", address)

                ElseIf TypeOf message Is Tuple(Of String, Object) Then
                    ' Address + single argument
                    Dim tuple = CType(message, Tuple(Of String, Object))
                    _oscManager.SendToDAW(tuple.Item1, tuple.Item2)
                    _log.Debug("OSC Sent: {0} [{1}]", tuple.Item1, tuple.Item2)

                ElseIf TypeOf message Is Tuple(Of String, Object()) Then
                    ' Address + multiple arguments
                    Dim tuple = CType(message, Tuple(Of String, Object()))
                    Dim oscMsg As New Rug.Osc.OscMessage(tuple.Item1)
                    For Each arg In tuple.Item2
                        oscMsg.Append(arg)
                    Next
                    _oscManager.SendToDAW(oscMsg)
                    _log.Debug("OSC Sent: {0} [{1} args]", tuple.Item1, tuple.Item2.Length)

                Else
                    _log.Warn("Unknown OSC message type: {0}", message.GetType().Name)
                End If

            Catch ex As Exception
                _log.Error("Error sending OSC message: {0}", ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' C24 client send implementation
        ''' Sends commands to Control24 hardware (not implemented in standalone OSC session)
        ''' </summary>
        Private Sub C24ClientSendImpl(cmdBytes As Byte())
            ' In a standalone OSC session, we don't have direct hardware access
            ' This would be implemented if this class was integrated with C24Session
            _log.Debug("C24 Send: {0} bytes: {1}", cmdBytes.Length, Utils.HexL(cmdBytes))
            _log.Warn("C24ClientSend not implemented in OSC-only session")
        End Sub

        ''' <summary>
        ''' Get the desk instance
        ''' </summary>
        Public ReadOnly Property Desk As C24Desk
            Get
                Return _desk
            End Get
        End Property

        ''' <summary>
        ''' Check if OSC is ready
        ''' </summary>
        Public ReadOnly Property IsReady As Boolean
            Get
                Return _oscManager IsNot Nothing AndAlso _oscManager.IsReady
            End Get
        End Property

        ''' <summary>
        ''' Close the session
        ''' </summary>
        Public Sub Close()
            _log.Info("C24OscSession closing")
            _isClosing = True

            ' Stop OSC manager
            If _oscManager IsNot Nothing Then
                _oscManager.Stop()
                _oscManager.Dispose()
                _oscManager = Nothing
            End If

            _log.Info("C24OscSession closed")
        End Sub

        ''' <summary>
        ''' Dispose resources
        ''' </summary>
        Public Sub Dispose() Implements IDisposable.Dispose
            Close()
            GC.SuppressFinalize(Me)
        End Sub
    End Class

    ''' <summary>
    ''' Examples of using C24OscSession for testing/development
    ''' </summary>
    Public Class C24OscSessionExamples

        ''' <summary>
        ''' Test button LED mapping
        ''' </summary>
        Public Sub TestButtonLedMapping()
            Dim logger = LogHelper.StartLogging("ButtonLEDTest", ".\logs", True)

            Using oscSession As New C24OscSession(logger)
                ' Configure and start
                oscSession.ConfigureOSC(9124, "127.0.0.1", 9125)
                oscSession.Start()

                While Not oscSession.IsReady
                    Threading.Thread.Sleep(100)
                End While

                Console.WriteLine("Testing Button LED Mapping...")
                Console.WriteLine("================================")

                ' Test track buttons (Solo, Mute, RecArm) on first 8 tracks
                Console.WriteLine(vbCrLf & "1. Testing Track Buttons...")
                For i = 0 To 7
                    Dim track = oscSession.Desk.GetTrack(i)
                    If track.ButtonLed IsNot Nothing Then
                        ' Flash Solo
                        track.ButtonLed.SetButton($"/track/{i + 1}/solo", 1)
                        Threading.Thread.Sleep(200)
                        track.ButtonLed.SetButton($"/track/{i + 1}/solo", 0)

                        ' Flash Mute
                        track.ButtonLed.SetButton($"/track/{i + 1}/mute", 1)
                        Threading.Thread.Sleep(200)
                        track.ButtonLed.SetButton($"/track/{i + 1}/mute", 0)

                        Console.WriteLine($"  Track {i + 1}: Solo/Mute tested")
                    End If
                Next

                Threading.Thread.Sleep(500)

                ' Test transport buttons
                Console.WriteLine(vbCrLf & "2. Testing Transport Buttons...")
                Dim track0 = oscSession.Desk.GetTrack(0)
                If track0.ButtonLed IsNot Nothing Then
                    Dim transportButtons = {"play", "stop", "record", "rewind", "forward"}
                    For Each btn In transportButtons
                        track0.ButtonLed.SetButton($"/transport/{btn}", 1)
                        Console.WriteLine($"  {btn}: ON")
                        Threading.Thread.Sleep(300)
                        track0.ButtonLed.SetButton($"/transport/{btn}", 0)
                    Next
                End If

                Threading.Thread.Sleep(500)

                ' Test F-keys
                Console.WriteLine(vbCrLf & "3. Testing F-Keys...")
                If track0.ButtonLed IsNot Nothing Then
                    For i = 1 To 10
                        track0.ButtonLed.SetButton($"/utility/f{i}", 1)
                        Console.WriteLine($"  F{i}: ON")
                        Threading.Thread.Sleep(200)
                        track0.ButtonLed.SetButton($"/utility/f{i}", 0)
                    Next
                End If

                Threading.Thread.Sleep(500)

                ' Test display mode buttons
                Console.WriteLine(vbCrLf & "4. Testing Display Mode Buttons...")
                If track0.ButtonLed IsNot Nothing Then
                    Dim displayModes = {"showvalues", "showgroup", "showchannelnames", "showinfo"}
                    For Each mode In displayModes
                        track0.ButtonLed.SetButton($"/command/{mode}", 1)
                        Console.WriteLine($"  {mode}: ON")
                        Threading.Thread.Sleep(300)
                        track0.ButtonLed.SetButton($"/command/{mode}", 0)
                    Next
                End If

                Threading.Thread.Sleep(500)

                ' Test automation LEDs
                Console.WriteLine(vbCrLf & "5. Testing Automation LEDs...")
                For i = 0 To 3
                    Dim track = oscSession.Desk.GetTrack(i)
                    If track.ButtonLed IsNot Nothing Then
                        Dim autoModes = {"read", "write", "touch", "latch", "trim"}
                        For Each mode In autoModes
                            track.ButtonLed.SetButton($"/track/{i + 1}/automode/{mode}", 1)
                            Console.WriteLine($"  Track {i + 1} - {mode}: ON")
                            Threading.Thread.Sleep(200)
                            track.ButtonLed.SetButton($"/track/{i + 1}/automode/{mode}", 0)
                        Next
                    End If
                Next

                Threading.Thread.Sleep(500)

                ' Test navigation buttons
                Console.WriteLine(vbCrLf & "6. Testing Navigation Buttons...")
                If track0.ButtonLed IsNot Nothing Then
                    Dim navButtons = {"nav", "zoom", "seladj"}
                    For Each btn In navButtons
                        track0.ButtonLed.SetButton($"/command/{btn}", 1)
                        Console.WriteLine($"  {btn}: ON")
                        Threading.Thread.Sleep(300)
                        track0.ButtonLed.SetButton($"/command/{btn}", 0)
                    Next
                End If

                Threading.Thread.Sleep(500)

                ' Test edit mode buttons
                Console.WriteLine(vbCrLf & "7. Testing Edit Mode Buttons...")
                If track0.ButtonLed IsNot Nothing Then
                    Dim editModes = {"shuffle", "slip", "spot", "grid"}
                    For Each mode In editModes
                        track0.ButtonLed.SetButton($"/command/{mode}", 1)
                        Console.WriteLine($"  {mode}: ON")
                        Threading.Thread.Sleep(300)
                        track0.ButtonLed.SetButton($"/command/{mode}", 0)
                    Next
                End If

                Threading.Thread.Sleep(500)

                ' Test send buttons
                Console.WriteLine(vbCrLf & "8. Testing Send Buttons...")
                If track0.ButtonLed IsNot Nothing Then
                    Dim sends = {"af", "bg", "ch", "di", "ej"}
                    For Each send In sends
                        track0.ButtonLed.SetButton($"/command/{send}", 1)
                        Console.WriteLine($"  Send {send}: ON")
                        Threading.Thread.Sleep(300)
                        track0.ButtonLed.SetButton($"/command/{send}", 0)
                    Next
                End If

                Console.WriteLine(vbCrLf & "================================")
                Console.WriteLine("Button LED mapping test complete!")
                Console.WriteLine("Press any key to exit...")
                Console.ReadKey()
            End Using
        End Sub

        ''' <summary>
        ''' Test simultaneous button LEDs (like a VU meter effect)
        ''' </summary>
        Public Sub TestSimultaneousLeds()
            Dim logger = LogHelper.StartLogging("SimultaneousLEDTest", ".\logs", True)

            Using oscSession As New C24OscSession(logger)
                oscSession.ConfigureOSC(9124, "127.0.0.1", 9125)
                oscSession.Start()

                While Not oscSession.IsReady
                    Threading.Thread.Sleep(100)
                End While

                Console.WriteLine("Testing Simultaneous LEDs...")
                Console.WriteLine("Creating LED chase effect...")

                ' Create a chase effect across all tracks
                For cycle = 0 To 4
                    For i = 0 To 23
                        Dim track = oscSession.Desk.GetTrack(i)
                        If track.ButtonLed IsNot Nothing Then
                            ' Turn on current track
                            track.ButtonLed.SetButton($"/track/{i + 1}/solo", 1)

                            ' Turn off previous track
                            If i > 0 Then
                                Dim prevTrack = oscSession.Desk.GetTrack(i - 1)
                                prevTrack.ButtonLed?.SetButton($"/track/{i}/solo", 0)
                            End If

                            Threading.Thread.Sleep(50)
                        End If
                    Next

                    ' Turn off last track
                    oscSession.Desk.GetTrack(23).ButtonLed?.SetButton("/track/24/solo", 0)
                Next

                Console.WriteLine("Chase effect complete!")
                Console.WriteLine("Press any key to exit...")
                Console.ReadKey()
            End Using
        End Sub

        ''' <summary>
        ''' Test OSC communication without hardware
        ''' </summary>
        Public Sub TestOscWithoutHardware()
            Dim logger = LogHelper.StartLogging("OSCTest", ".\logs", True)

            Using oscSession As New C24OscSession(logger)
                ' Configure OSC
                oscSession.ConfigureOSC(
                    serverPort:=9124,
                    dawAddress:="127.0.0.1",
                    dawPort:=9125
                )

                ' Start session
                oscSession.Start()

                ' Wait for OSC to be ready
                While Not oscSession.IsReady
                    Threading.Thread.Sleep(100)
                End While

                Console.WriteLine("OSC Session ready!")

                ' Simulate fader movements
                For i = 0 To 23
                    Dim track = oscSession.Desk.GetTrack(i)
                    If track.Fader IsNot Nothing Then
                        track.Fader.Gain = 0.75
                        Console.WriteLine($"Track {i + 1} fader set to 0.75")
                    End If
                Next

                ' Keep running
                Console.WriteLine("Press any key to stop...")
                Console.ReadKey()
            End Using
        End Sub

    End Class

End Namespace
