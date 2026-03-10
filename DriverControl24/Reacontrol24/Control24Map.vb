''' <summary>
''' Main Mapping dictionary tree and other reference tables
''' This file is part of ReaControl24. Control Surface Middleware.
''' Copyright (C) 2018  PhaseWalker
''' 
''' This program is free software: you can redistribute it and/or modify
''' it under the terms of the GNU General Public License as published by
''' the Free Software Foundation, either version 3 of the License, or
''' (at your option) any later version.
''' </summary>

Namespace Reacontrol24

    ''' <summary>
    ''' Mapping tree structure for Control24 commands
    ''' </summary>
    Public Class Control24Mapping

        ''' <summary>
        ''' Mapping node class
        ''' </summary>
        Public Class MappingNode
            Public Property Address As String
            Public Property ChildByte As Integer?
            Public Property ChildByteMask As Integer?
            Public Property ChildByteMatch As Integer?
            Public Property TrackByte As Integer?
            Public Property TrackByteMask As Integer?
            Public Property ValueByte As Integer?
            Public Property ValueByteMask As Integer?
            Public Property DirectionByte As Integer?
            Public Property DirectionByteMask As Integer?
            Public Property Zone As String
            Public Property LED As Boolean
            Public Property CmdClass As String
            Public Property SetMode As String
            Public Property Children As Dictionary(Of Integer, MappingNode)

            Public Sub New()
                Children = New Dictionary(Of Integer, MappingNode)
            End Sub
        End Class

        ''' <summary>
        ''' Main mapping tree - converted from Python dictionary structure
        ''' </summary>
        Public Shared ReadOnly Property MappingTree As Dictionary(Of Integer, MappingNode)
            Get
                Dim tree As New Dictionary(Of Integer, MappingNode)

                ' 0xB0: Track (Dials/Faders)
                tree(&HB0) = New MappingNode With {
                    .Address = "track",
                    .ChildByte = 1,
                    .ChildByteMask = &H40,
                    .TrackByte = 1,
                    .TrackByteMask = &H1F,
                    .Children = New Dictionary(Of Integer, MappingNode) From {
                        {&H0, New MappingNode With {.Address = "c24fader", .CmdClass = "C24fader"}},
                        {&H40, New MappingNode With {.Address = "c24vpot", .CmdClass = "C24vpot"}}
                    }
                }

                ' 0x90: Buttons
                tree(&H90) = CreateButtonMapping()

                ' 0xF0: LED
                tree(&HF0) = New MappingNode With {
                    .Address = "led",
                    .ChildByte = 3,
                    .Children = New Dictionary(Of Integer, MappingNode) From {
                        {&H30, New MappingNode With {.Address = "TimeCodeMessageID"}},
                        {&H40, New MappingNode With {.Address = "DisplayMessageID"}},
                        {&H20, New MappingNode With {.Address = "LedMessageID"}},
                        {&H10, New MappingNode With {.Address = "MeterMessageID"}},
                        {&H0, New MappingNode With {.Address = "TurnKnobLedMessageID"}}
                    }
                }

                ' 0xFF: Automation LED / Counter Mode LED
                tree(&HFF) = New MappingNode With {
                    .Address = "automation_LED_CounterMode_LED",
                    .ChildByte = 1,
                    .Children = New Dictionary(Of Integer, MappingNode) From {
                        {&HFE, New MappingNode With {
                            .Address = "AutomationLED",
                            .ChildByte = 2,
                            .Children = New Dictionary(Of Integer, MappingNode) From {
                                {&HF0, New MappingNode With {.Address = "Read"}},
                                {&HF1, New MappingNode With {.Address = "TM"}},
                                {&HF2, New MappingNode With {.Address = "Latch"}},
                                {&HF3, New MappingNode With {.Address = "Touch"}},
                                {&HF4, New MappingNode With {.Address = "Write"}},
                                {&HF5, New MappingNode With {.Address = "Off"}}
                            }
                        }},
                        {&HFF, New MappingNode With {
                            .Address = "CounterModeLED",
                            .ChildByte = 2,
                            .Children = New Dictionary(Of Integer, MappingNode) From {
                                {&HF0, New MappingNode With {.Address = "Hours_ms"}},
                                {&HF1, New MappingNode With {.Address = "Hours_Frames"}},
                                {&HF2, New MappingNode With {.Address = "Feet_Frames"}},
                                {&HF3, New MappingNode With {.Address = "Bars_beat"}},
                                {&HF4, New MappingNode With {.Address = "Off"}}
                            }
                        }},
                        {&H0, New MappingNode With {.Address = "UnknownNullFoundAfterFader"}}
                    }
                }

                ' 0x00: Null
                tree(&H0) = New MappingNode With {.Address = "Null"}

                ' 0xD0: Ackt
                tree(&HD0) = New MappingNode With {.Address = "Ackt"}

                Return tree
            End Get
        End Property

        Private Shared Function CreateButtonMapping() As MappingNode
            Dim buttonNode As New MappingNode With {
                .Address = "button",
                .ChildByte = 2,
                .ChildByteMatch = &H18,
                .ValueByte = 2,
                .ValueByteMask = &H40,
                .Children = New Dictionary(Of Integer, MappingNode)
            }

            ' Command buttons (0x18)
            buttonNode.Children(&H18) = CreateCommandButtonMapping()

            ' Track buttons (0x00)
            buttonNode.Children(&H0) = CreateTrackButtonMapping()

            Return buttonNode
        End Function

        Private Shared Function CreateCommandButtonMapping() As MappingNode
            Dim cmdNode As New MappingNode With {
                .Address = "command",
                .ChildByte = 2,
                .ChildByteMask = &HBF,
                .Children = New Dictionary(Of Integer, MappingNode)
            }

            ' Utility/Misc/Meter Select/Automation Enable (0x18)
            cmdNode.Children(&H18) = CreateUtilityMapping()

            ' Window/Zoom Presets/Navigation (0x19)
            cmdNode.Children(&H19) = CreateWindowNavigationMapping()

            ' Numeric Keypad (0x1A)
            cmdNode.Children(&H1A) = CreateNumericKeypadMapping()

            ' Edit Mode/Function/Banks (0x1B)
            cmdNode.Children(&H1B) = CreateEditModeMapping()

            ' Transport (0x1C)
            cmdNode.Children(&H1C) = CreateTransportMapping()

            ' Monitor/Pre/Inserts/Assignment/Sends/Pans/Scroll (0x1D)
            cmdNode.Children(&H1D) = CreateMonitorMapping()

            Return cmdNode
        End Function

        Private Shared Function CreateUtilityMapping() As MappingNode
            Dim utilNode As New MappingNode With {
                .Address = "utility_misc_meterselect_automationenable",
                .ChildByte = 1,
                .Children = New Dictionary(Of Integer, MappingNode)
            }

            ' F-keys and utility buttons
            Dim fKeys As New Dictionary(Of Integer, String) From {
                {&H0, "F1"}, {&H2, "F2"}, {&H4, "F3"}, {&H6, "F4"}, {&H8, "F5"},
                {&H32, "F6"}, {&H33, "F7"}, {&H34, "F8"}, {&H35, "F9"}, {&H36, "F10"}
            }

            For Each kvp In fKeys
                utilNode.Children(kvp.Key) = New MappingNode With {
                    .Address = kvp.Value,
                    .Zone = "Utility",
                    .LED = True
                }
            Next

            ' Modifier keys
            Dim modifiers As New Dictionary(Of Integer, String) From {
                {&H23, "Shift"}, {&H24, "Option"}, {&H25, "Control"}, {&H26, "Command"}
            }

            For Each kvp In modifiers
                utilNode.Children(kvp.Key) = New MappingNode With {
                    .Address = kvp.Value,
                    .Zone = "Modifiers",
                    .CmdClass = "C24modifiers"
                }
            Next

            ' Display mode buttons
            utilNode.Children(&H10) = New MappingNode With {
                .Address = "ShowValues",
                .SetMode = "/track/c24scribstrip/pan",
                .Zone = "Misc",
                .LED = True
            }
            utilNode.Children(&H11) = New MappingNode With {
                .Address = "ShowGroup",
                .SetMode = "/track/number",
                .Zone = "Misc",
                .LED = True
            }
            utilNode.Children(&H12) = New MappingNode With {
                .Address = "ShowChannelNames",
                .SetMode = "/track/c24scribstrip/name",
                .Zone = "Misc",
                .LED = True
            }
            utilNode.Children(&H13) = New MappingNode With {
                .Address = "ShowInfo",
                .SetMode = "/track/c24scribstrip/volume",
                .Zone = "Misc",
                .LED = True
            }

            Return utilNode
        End Function

        Private Shared Function CreateWindowNavigationMapping() As MappingNode
            Dim navNode As New MappingNode With {
                .Address = "Window+ZoomPresets+Navigation",
                .ChildByte = 1,
                .Children = New Dictionary(Of Integer, MappingNode)
            }

            ' Navigation buttons
            navNode.Children(&HC) = New MappingNode With {
                .Address = "Nav",
                .Zone = "Navigation",
                .LED = True,
                .CmdClass = "C24nav"
            }
            navNode.Children(&HD) = New MappingNode With {
                .Address = "Zoom",
                .Zone = "Navigation",
                .LED = True,
                .CmdClass = "C24nav"
            }
            navNode.Children(&HE) = New MappingNode With {
                .Address = "SelAdj",
                .Zone = "Navigation",
                .LED = True,
                .CmdClass = "C24nav"
            }

            ' Cursor keys
            Dim cursors As New Dictionary(Of Integer, String) From {
                {&HF, "CursorUp"}, {&H10, "CursorLeft"},
                {&H11, "CursorRight"}, {&H12, "CursorDown"}
            }

            For Each kvp In cursors
                navNode.Children(kvp.Key) = New MappingNode With {
                    .Address = kvp.Value,
                    .Zone = "Navigation",
                    .CmdClass = "C24nav"
                }
            Next

            ' Clock mode
            navNode.Children(&H13) = New MappingNode With {
                .Address = "CounterMode",
                .Zone = "Counter",
                .CmdClass = "C24clock"
            }

            Return navNode
        End Function

        Private Shared Function CreateNumericKeypadMapping() As MappingNode
            Dim numNode As New MappingNode With {
                .ChildByte = 1,
                .Children = New Dictionary(Of Integer, MappingNode)
            }

            ' Numeric keys 0-9
            For i = 0 To 9
                numNode.Children(i) = New MappingNode With {.Address = i.ToString()}
            Next

            ' Special keys
            Dim specialKeys As New Dictionary(Of Integer, String) From {
                {&HA, "Clear"}, {&HB, "="}, {&HC, "/"}, {&HD, "*"},
                {&HE, "-"}, {&HF, "+"}, {&H10, "."}, {&H11, "Enter"}
            }

            For Each kvp In specialKeys
                numNode.Children(kvp.Key) = New MappingNode With {.Address = kvp.Value}
            Next

            Return numNode
        End Function

        Private Shared Function CreateEditModeMapping() As MappingNode
            Dim editNode As New MappingNode With {
                .Address = "EditMode+Function+Banks",
                .ChildByte = 1,
                .Children = New Dictionary(Of Integer, MappingNode)
            }

            ' Edit modes
            Dim editModes As New Dictionary(Of Integer, Tuple(Of String, String)) From {
                {&H0, Tuple.Create("Shuffle", "Edit Mode")},
                {&H1, Tuple.Create("Slip", "Edit Mode")},
                {&H2, Tuple.Create("Spot", "Edit Mode")},
                {&H3, Tuple.Create("Grid", "Edit Mode")}
            }

            For Each kvp In editModes
                editNode.Children(kvp.Key) = New MappingNode With {
                    .Address = kvp.Value.Item1,
                    .Zone = kvp.Value.Item2,
                    .LED = True
                }
            Next

            Return editNode
        End Function

        Private Shared Function CreateTransportMapping() As MappingNode
            Dim transNode As New MappingNode With {
                .Address = "Transport",
                .ChildByte = 1,
                .Children = New Dictionary(Of Integer, MappingNode)
            }

            ' Transport controls
            Dim transportButtons As New Dictionary(Of Integer, String) From {
                {&HD, "Rewind"}, {&HE, "Forward"}, {&HF, "Stop"},
                {&H10, "Play"}, {&H11, "Record"}
            }

            For Each kvp In transportButtons
                transNode.Children(kvp.Key) = New MappingNode With {
                    .Address = kvp.Value,
                    .LED = True
                }
            Next

            ' Jog/Shuttle
            transNode.Children(&H12) = New MappingNode With {
                .Address = "Scrub",
                .LED = True,
                .CmdClass = "C24jpot"
            }
            transNode.Children(&H13) = New MappingNode With {
                .Address = "Shuttle",
                .LED = True,
                .CmdClass = "C24jpot"
            }

            Return transNode
        End Function

        Private Shared Function CreateMonitorMapping() As MappingNode
            Dim monNode As New MappingNode With {
                .Address = "Monitor+Pre+Inserts+Assignment+Sends+Pans+Scroll",
                .ChildByte = 1,
                .Children = New Dictionary(Of Integer, MappingNode)
            }

            ' Send buttons
            Dim sends As New Dictionary(Of Integer, String) From {
                {&HB, "A/F"}, {&HC, "B/G"}, {&HD, "C/H"},
                {&HE, "D/I"}, {&HF, "E/J"}
            }

            For Each kvp In sends
                monNode.Children(kvp.Key) = New MappingNode With {
                    .Address = kvp.Value,
                    .Zone = "Sends",
                    .LED = True
                }
            Next

            Return monNode
        End Function

        Private Shared Function CreateTrackButtonMapping() As MappingNode
            Dim trackNode As New MappingNode With {
                .Address = "track",
                .TrackByte = 2,
                .TrackByteMask = &H1F,
                .ChildByte = 1,
                .Children = New Dictionary(Of Integer, MappingNode)
            }

            ' Channel strip buttons
            Dim channelButtons As New Dictionary(Of Integer, Tuple(Of String, String, String)) From {
                {&H0, Tuple.Create("RecArm", "Channel", "")},
                {&H4, Tuple.Create("Switch_Active", "Channel", "")},
                {&H1, Tuple.Create("Pan_Send", "Channel", "")},
                {&H2, Tuple.Create("EQ", "Channel", "")},
                {&H3, Tuple.Create("Dynamics", "Channel", "")},
                {&HA, Tuple.Create("Inserts", "Channel", "")},
                {&H5, Tuple.Create("c24automode", "Channel", "C24automode")},
                {&H6, Tuple.Create("ChannelSelect", "Channel", "")},
                {&H7, Tuple.Create("Solo", "Channel", "")},
                {&H8, Tuple.Create("Mute", "Channel", "")},
                {&H9, Tuple.Create("Touch", "Faders", "C24fader")}
            }

            For Each kvp In channelButtons
                trackNode.Children(kvp.Key) = New MappingNode With {
                    .Address = kvp.Value.Item1,
                    .Zone = kvp.Value.Item2,
                    .LED = (kvp.Value.Item3 = ""),
                    .CmdClass = If(kvp.Value.Item3 <> "", kvp.Value.Item3, Nothing)
                }
            Next

            Return trackNode
        End Function

    End Class

End Namespace
