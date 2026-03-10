''' <summary>
''' Control24 to OSC Command Mapper
''' Maps Control24 commands to OSC messages and vice versa
''' 
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' </summary>

Imports System.Text
Imports Rug.Osc

Namespace Reacontrol24

    ''' <summary>
    ''' Command mapper between Control24 and OSC
    ''' </summary>
    Public Class C24OscMapper
        Private _log As Logger
        Private _mappingTree As Dictionary(Of Integer, Control24Mapping.MappingNode)

        Public Sub New(logger As Logger)
            _log = logger
            _mappingTree = Control24Mapping.MappingTree
        End Sub

        ''' <summary>
        ''' Parse Control24 command bytes and extract information
        ''' </summary>
        Public Function ParseC24Command(cmdBytes As Byte()) As ParsedCommand
            If cmdBytes Is Nothing OrElse cmdBytes.Length = 0 Then
                Return Nothing
            End If

            Try
                Dim parsed As New ParsedCommand With {
                    .RawBytes = cmdBytes,
                    .Addresses = New List(Of String),
                    .LookupBytes = New List(Of Byte)
                }

                ' Start parsing from mapping tree
                Dim currentByte = 0
                Dim currentByteValue = cmdBytes(currentByte)
                Dim currentNode = _mappingTree

                While currentByte < cmdBytes.Length
                    parsed.LookupBytes.Add(currentByteValue)

                    ' Find node in tree
                    If Not currentNode.ContainsKey(currentByteValue) Then
                        ' Check for mask matching
                        Dim matchedNode As Control24Mapping.MappingNode = Nothing
                        For Each kvp In currentNode
                            Dim node = kvp.Value
                            If node.ChildByteMask.HasValue Then
                                Dim maskedValue = currentByteValue And node.ChildByteMask.Value
                                If maskedValue = kvp.Key Then
                                    matchedNode = node
                                    Exit For
                                End If
                            ElseIf node.ChildByteMatch.HasValue Then
                                If (currentByteValue And node.ChildByteMatch.Value) = node.ChildByteMatch.Value Then
                                    matchedNode = node
                                    Exit For
                                End If
                            End If
                        Next

                        If matchedNode Is Nothing Then
                            _log.Warn("Unmapped command byte {0:X2} at position {1}", currentByteValue, currentByte)
                            Return parsed
                        End If

                        ' Use matched node
                        If Not String.IsNullOrEmpty(matchedNode.Address) Then
                            parsed.Addresses.Add(matchedNode.Address)
                        End If

                        ' Extract metadata
                        ExtractMetadata(parsed, matchedNode, cmdBytes)

                        If matchedNode.Children Is Nothing OrElse matchedNode.Children.Count = 0 Then
                            Exit While
                        End If

                        currentNode = matchedNode.Children
                        If matchedNode.ChildByte.HasValue Then
                            currentByte = matchedNode.ChildByte.Value
                            If currentByte < cmdBytes.Length Then
                                currentByteValue = cmdBytes(currentByte)
                                If matchedNode.ChildByteMask.HasValue Then
                                    currentByteValue = currentByteValue And matchedNode.ChildByteMask.Value
                                End If
                            Else
                                Exit While
                            End If
                        Else
                            Exit While
                        End If
                    Else
                        Dim node = currentNode(currentByteValue)

                        If Not String.IsNullOrEmpty(node.Address) Then
                            parsed.Addresses.Add(node.Address)
                        End If

                        ' Extract metadata
                        ExtractMetadata(parsed, node, cmdBytes)

                        If node.Children Is Nothing OrElse node.Children.Count = 0 Then
                            Exit While
                        End If

                        currentNode = node.Children
                        If node.ChildByte.HasValue Then
                            currentByte = node.ChildByte.Value
                            If currentByte < cmdBytes.Length Then
                                currentByteValue = cmdBytes(currentByte)
                                If node.ChildByteMask.HasValue Then
                                    currentByteValue = currentByteValue And node.ChildByteMask.Value
                                End If
                            Else
                                Exit While
                            End If
                        Else
                            Exit While
                        End If
                    End If
                End While

                ' Build full OSC address
                parsed.OscAddress = "/" & String.Join("/", parsed.Addresses.Where(Function(a) Not String.IsNullOrEmpty(a)))

                Return parsed

            Catch ex As Exception
                _log.Error("Error parsing C24 command: {0}", ex.Message)
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Extract metadata from mapping node
        ''' </summary>
        Private Sub ExtractMetadata(parsed As ParsedCommand, node As Control24Mapping.MappingNode, cmdBytes As Byte())
            ' Track number
            If node.TrackByte.HasValue AndAlso node.TrackByte.Value < cmdBytes.Length Then
                Dim trackByte = cmdBytes(node.TrackByte.Value)
                If node.TrackByteMask.HasValue Then
                    trackByte = trackByte And node.TrackByteMask.Value
                End If
                parsed.TrackNumber = CInt(trackByte)
            End If

            ' Value
            If node.ValueByte.HasValue AndAlso node.ValueByte.Value < cmdBytes.Length Then
                Dim valueByte = cmdBytes(node.ValueByte.Value)
                If node.ValueByteMask.HasValue Then
                    valueByte = valueByte And node.ValueByteMask.Value
                    ' Normalize to 0 or 1
                    parsed.Value = If(valueByte = node.ValueByteMask.Value, 1.0, 0.0)
                Else
                    parsed.Value = CDbl(valueByte) / 127.0
                End If
            End If

            ' Direction
            If node.DirectionByte.HasValue AndAlso node.DirectionByte.Value < cmdBytes.Length Then
                Dim directionByte = cmdBytes(node.DirectionByte.Value)
                parsed.Direction = CInt(directionByte) - 64
            End If

            ' Command class
            If Not String.IsNullOrEmpty(node.CmdClass) Then
                parsed.CommandClass = node.CmdClass
            End If

            ' Set mode
            If Not String.IsNullOrEmpty(node.SetMode) Then
                parsed.SetMode = node.SetMode
            End If

            ' LED
            parsed.HasLED = node.LED
        End Sub

        ''' <summary>
        ''' Convert parsed command to OSC message
        ''' </summary>
        Public Function ToOscMessage(parsed As ParsedCommand) As OscMessage
            If parsed Is Nothing OrElse String.IsNullOrEmpty(parsed.OscAddress) Then
                Return Nothing
            End If

            Try
                Dim msg As New OscMessage(parsed.OscAddress)

                ' Add track number if present
                If parsed.TrackNumber.HasValue Then
                    msg.Append(parsed.TrackNumber.Value + 1) ' OSC uses 1-based indexing
                End If

                ' Add value if present
                If parsed.Value.HasValue Then
                    msg.Append(CSng(parsed.Value.Value))
                End If

                ' Add direction if present
                If parsed.Direction.HasValue Then
                    msg.Append(parsed.Direction.Value)
                End If

                Return msg

            Catch ex As Exception
                _log.Error("Error creating OSC message: {0}", ex.Message)
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Create OSC message for fader movement
        ''' </summary>
        Public Function CreateFaderMessage(trackNumber As Integer, gain As Double) As OscMessage
            Dim address = $"/track/{trackNumber + 1}/volume"
            Dim msg As New OscMessage(address)
            msg.Append(CSng(gain))
            Return msg
        End Function

        ''' <summary>
        ''' Create OSC message for VPot (pan) movement
        ''' </summary>
        Public Function CreateVPotMessage(trackNumber As Integer, pan As Double) As OscMessage
            Dim address = $"/track/{trackNumber + 1}/pan"
            Dim msg As New OscMessage(address)
            msg.Append(CSng(pan))
            Return msg
        End Function

        ''' <summary>
        ''' Create OSC message for button press
        ''' </summary>
        Public Function CreateButtonMessage(address As String, value As Double) As OscMessage
            Dim msg As New OscMessage(address)
            msg.Append(CSng(value))
            Return msg
        End Function

    End Class

    ''' <summary>
    ''' Parsed Control24 command
    ''' </summary>
    Public Class ParsedCommand
        Public Property RawBytes As Byte()
        Public Property Addresses As List(Of String)
        Public Property LookupBytes As List(Of Byte)
        Public Property OscAddress As String
        Public Property TrackNumber As Integer?
        Public Property Value As Double?
        Public Property Direction As Integer?
        Public Property CommandClass As String
        Public Property SetMode As String
        Public Property HasLED As Boolean

        Public Overrides Function ToString() As String
            Dim sb As New StringBuilder()
            sb.Append($"OSC: {OscAddress}")
            If TrackNumber.HasValue Then sb.Append($" Track:{TrackNumber.Value}")
            If Value.HasValue Then sb.Append($" Value:{Value.Value:F2}")
            If Direction.HasValue Then sb.Append($" Dir:{Direction.Value}")
            If Not String.IsNullOrEmpty(CommandClass) Then sb.Append($" Class:{CommandClass}")
            Return sb.ToString()
        End Function
    End Class

    ''' <summary>
    ''' OSC to Control24 reverse mapper
    ''' </summary>
    Public Class OscToC24Mapper
        Private _log As Logger

        Public Sub New(logger As Logger)
            _log = logger
        End Sub

        ''' <summary>
        ''' Parse OSC message and extract Control24-relevant information
        ''' </summary>
        Public Function ParseOscMessage(msg As OscMessageInfo) As OscToC24Command
            If msg Is Nothing Then
                Return Nothing
            End If

            Try
                Dim cmd As New OscToC24Command With {
                    .Address = msg.Address,
                    .Arguments = msg.Arguments
                }

                ' Parse address to extract track info
                Dim parts = msg.Address.Split("/"c).Where(Function(p) Not String.IsNullOrEmpty(p)).ToArray()

                If parts.Length > 0 Then
                    Select Case parts(0).ToLower()
                        Case "track"
                            ParseTrackCommand(cmd, parts, msg.Arguments)

                        Case "button"
                            ParseButtonCommand(cmd, parts, msg.Arguments)

                        Case "clock"
                            ParseClockCommand(cmd, parts, msg.Arguments)

                        Case "transport"
                            ParseTransportCommand(cmd, parts, msg.Arguments)

                        Case Else
                            _log.Debug("Unhandled OSC address: {0}", msg.Address)
                    End Select
                End If

                Return cmd

            Catch ex As Exception
                _log.Error("Error parsing OSC message: {0}", ex.Message)
                Return Nothing
            End Try
        End Function

        Private Sub ParseTrackCommand(cmd As OscToC24Command, parts As String(), args As List(Of Object))
            cmd.CommandType = "track"

            If parts.Length > 1 AndAlso Integer.TryParse(parts(1), cmd.TrackNumber) Then
                cmd.TrackNumber -= 1 ' Convert to 0-based
            End If

            If parts.Length > 2 Then
                cmd.Parameter = parts(2).ToLower()

                ' Extract value
                If args.Count > 0 Then
                    If TypeOf args(0) Is Single Then
                        cmd.Value = CDbl(CSng(args(0)))
                    ElseIf TypeOf args(0) Is Double Then
                        cmd.Value = CDbl(args(0))
                    ElseIf TypeOf args(0) Is Integer Then
                        cmd.Value = CDbl(CInt(args(0)))
                    ElseIf TypeOf args(0) Is String Then
                        cmd.StringValue = CStr(args(0))
                    End If
                End If
            End If
        End Sub

        Private Sub ParseButtonCommand(cmd As OscToC24Command, parts As String(), args As List(Of Object))
            cmd.CommandType = "button"

            If parts.Length > 1 Then
                cmd.Parameter = String.Join("/", parts.Skip(1))
            End If

            If args.Count > 0 AndAlso TypeOf args(0) Is Single Then
                cmd.Value = CDbl(CSng(args(0)))
            End If
        End Sub

        Private Sub ParseClockCommand(cmd As OscToC24Command, parts As String(), args As List(Of Object))
            cmd.CommandType = "clock"

            If parts.Length > 1 Then
                cmd.Parameter = parts(1)
            End If

            If args.Count > 0 AndAlso TypeOf args(0) Is String Then
                cmd.StringValue = CStr(args(0))
            End If
        End Sub

        Private Sub ParseTransportCommand(cmd As OscToC24Command, parts As String(), args As List(Of Object))
            cmd.CommandType = "transport"

            If parts.Length > 1 Then
                cmd.Parameter = parts(1)
            End If

            If args.Count > 0 AndAlso TypeOf args(0) Is Single Then
                cmd.Value = CDbl(CSng(args(0)))
            End If
        End Sub
    End Class

    ''' <summary>
    ''' OSC to Control24 command
    ''' </summary>
    Public Class OscToC24Command
        Public Property Address As String
        Public Property Arguments As List(Of Object)
        Public Property CommandType As String
        Public Property TrackNumber As Integer
        Public Property Parameter As String
        Public Property Value As Double?
        Public Property StringValue As String

        Public Overrides Function ToString() As String
            Dim sb As New StringBuilder()
            sb.Append($"Type:{CommandType}")
            If TrackNumber >= 0 Then sb.Append($" Track:{TrackNumber}")
            If Not String.IsNullOrEmpty(Parameter) Then sb.Append($" Param:{Parameter}")
            If Value.HasValue Then sb.Append($" Value:{Value.Value:F2}")
            If Not String.IsNullOrEmpty(StringValue) Then sb.Append($" Str:{StringValue}")
            Return sb.ToString()
        End Function
    End Class

End Namespace
