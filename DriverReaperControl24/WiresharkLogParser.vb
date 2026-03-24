Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Globalization

Public Class WiresharkLogParser
    Private Const AVID_MAC As String = "AvidTechnolo_a2:1d:46"
    Private Const DELL_MAC As String = "Dell_b7:13:84"
    Dim output_lines As List(Of String)
    Dim Jdatas As Dictionary(Of String, String) = Nothing
    Dim cmd_bytes As Byte() = {&H90, &HF0, &HB0}

    Public Sub ConvertToCsv(inputPath As String, outputPath As String)

        If IO.File.Exists(outputPath) Then
            IO.File.Delete(outputPath)
        End If

        'Try

        Dim last_C24cmd_Time As Double = 0
            Dim last_Computer_Time As Double = 0
            Dim separator As String = ","
            output_lines = New List(Of String)
            Dim input_lines = IO.File.ReadAllLines(inputPath)

            Dim j = 0
        For i = 0 To input_lines.Count - 1
            Dim line = input_lines(i)
            If line = "" Then
                j += 1

                    'Try

                    If j = 1 Then

                            line = input_lines(i - 2)

                            If line.Contains("No.") Then
                                Jdatas = New Dictionary(Of String, String)
                                Jdatas.Add("C24_delay_ms", "")
                        Jdatas.Add("Computer_delay_ms", "")

                        Dim column_Names As New List(Of String)
                                ParseString(line, column_Names)
                                Dim column_datas As New List(Of String)
                                ParseString(input_lines(i - 1), column_datas)
                                For k = 0 To column_Names.Count - 1
                                    Dim value = column_datas(k)
                                    If value.ToLower.Contains("avid") Then
                                        value = "C24"
                                    End If

                                    Jdatas.Add(column_Names(k), value)
                                Next


                            Else
                                line = input_lines(i - 1)
                                If line.Contains("No.") Then
                                    Jdatas = New Dictionary(Of String, String)
                                    Dim column_Names As New List(Of String)
                                    ParseString(line, column_Names)
                                    Dim column_datas As New List(Of String)
                                    For k = 0 To column_Names.Count - 1
                                        Jdatas.Add(column_Names(k), "")
                                    Next
                                End If


                            End If

                            'calcul des délais d'envois de messages
                            If Jdatas.ContainsKey("Destination") Then
                                Dim actual_time As Double = CDbl(Jdatas("Time").Replace(".", ","))
                                If Jdatas("Destination") = "C24" Then
                                    Dim value As Integer = CInt((actual_time - last_C24cmd_Time) * 1000)
                                    Jdatas("C24_delay_ms") = value
                                    last_C24cmd_Time = actual_time
                                Else
                                    Jdatas("Computer_delay_ms") = CInt((actual_time - last_Computer_Time) * 1000)
                                    last_Computer_Time = actual_time
                                End If
                            End If

                    If Jdatas.ContainsKey("No.") Then
                        If Jdatas("No.") = "1509" Then
                            Dim ded = ""
                        End If
                    End If

                    Jdatas.Add("Data_Lenght", "")
                    Jdatas.Add("Data", "")
                    Jdatas.Add("Cmd_ID", "")
                    Jdatas.Add("Track", "")
                    Jdatas.Add("Cmd", "")
                    Jdatas.Add("Value", "")
                ElseIf j = 2 Then
                                line = input_lines(i - 1)
                                If line.Contains("Data") Then
                                    Dim value = line.Split(" ")(1)
                                    value = value.Remove(0, 1)
                        Jdatas("Data_Lenght") = value
                    Else
                                    'ecriture des datas
                                    If output_lines.Count = 0 Then
                                        output_lines.Add(String.Join(separator, Jdatas.Keys.ToArray))
                                        output_lines.Add(String.Join(separator, Jdatas.Values.ToArray))
                                    Else
                                        output_lines.Add(String.Join(separator, Jdatas.Values.ToArray))
                                    End If
                                    j = 0
                                End If
                            ElseIf j = 3 Then
                                Dim data As New List(Of String)
                    Dim k = 1
                    line = input_lines(i - k)
                    Do Until line = ""
                                Dim _data As String = line
                                If _data <> "" Then
                                    _data = _data.Remove(0, 6)
                                    _data = _data.Substring(0, 47)
                                    _data = _data.Trim
                                    data.Add(_data)
                        End If
                        k += 1
                        line = input_lines(i - k)
                    Loop
                            data.Reverse()
                            Dim data_string = String.Join(" ", data.ToArray)
                    Jdatas("Data") = data_string

                    'découpe du multiplexage

                    Dim _Bytes As Byte() = GetBytes(data_string)

                            Dim _Multi_CmdBytes = Split_Multi_CmdBytes(_Bytes)
                            Dim Cmd_ID As Integer = 0
                            If _Bytes(14) = &H0 Then
                                Cmd_ID = BitConverter.ToInt16({_Bytes(7), _Bytes(6)})
                                'ajout des fonctions
                                SetC24Action(_Multi_CmdBytes)
                            ElseIf _Bytes(14) = &HA0 Then
                                Cmd_ID = BitConverter.ToInt16({_Bytes(11), _Bytes(10)})
                                Jdatas("Cmd") = "ACK"
                            End If


                            Jdatas("Cmd_ID") = CStr(Cmd_ID)



                            'ecriture des datas
                            If output_lines.Count = 0 Then
                                    output_lines.Add(String.Join(separator, Jdatas.Keys.ToArray))
                                    output_lines.Add(String.Join(separator, Jdatas.Values.ToArray))
                                Else
                                    output_lines.Add(String.Join(separator, Jdatas.Values.ToArray))
                                End If


                                j = 0
                            End If

                    'Catch ex As Exception
                    'j = 0
                    'output_lines.Add(ex.Message)
                    'End Try
                End If
        Next

        IO.File.WriteAllLines(outputPath, output_lines.ToArray)



        'Catch ex As Exception
        '    MessageBox.Show(ex.Message)
        'End Try


    End Sub

    Private Function Split_Multi_CmdBytes(_Bytes As Byte()) As List(Of Byte())

        Dim output_bytes As New List(Of Byte())
        For i = 16 To _Bytes.Count - 1 Step 0
            Dim _Byte As Byte = _Bytes(i)

            Dim cmd_bytes(0) As Byte
            If _Byte = &H90 Then
                cmd_bytes = New Byte(2) {}
            ElseIf _Byte = &HB0 Then
                cmd_bytes = New Byte(4) {}
            ElseIf _Byte = &HF0 Then
                Dim VisFnc_Byte As Byte = _Bytes(i + 3)
                'Vpot
                If VisFnc_Byte = &H0 Then
                    cmd_bytes = New Byte(7) {}
                    'strip
                ElseIf VisFnc_Byte = &H40 Then
                    cmd_bytes = New Byte(10) {}
                    'vuemeter
                ElseIf VisFnc_Byte = &H10 Then
                    cmd_bytes = New Byte(7) {}
                    'autoLED
                ElseIf VisFnc_Byte = &H20 Then
                    cmd_bytes = New Byte(6) {}
                    'Clock
                ElseIf VisFnc_Byte = &H30 Then
                    For k = 0 To _Bytes.Count - i - 1
                        If _Bytes(i + k) = &HF7 Then
                            cmd_bytes = New Byte(k) {}
                            Exit For
                        End If
                    Next
                End If

            End If

            If cmd_bytes.Count > 1 Then
                For j = 0 To cmd_bytes.Count - 1
                    cmd_bytes(j) = _Bytes(i + j)
                Next
                i += cmd_bytes.Count
                output_bytes.Add(cmd_bytes)
            Else
                output_bytes.Add(_Bytes)
                Return output_bytes
            End If

        Next
        Return output_bytes
    End Function

    Private Sub ParseString(text As String, Parsed_text As List(Of String))

        Do Until text = ""
            text = text.Trim
            Dim value As String = text.Split(" ")(0)
            Parsed_text.Add(value)
            text = text.Remove(0, value.Count)
        Loop
    End Sub

    Private Sub SetC24Action(Data As List(Of Byte()))

        'Dim ded As String = System.Text.Encoding.ASCII.GetString({&HF0, &H13, &H1, &H20})



        'Jdatas.Add("test", ded)
        For Each _Bytes In Data



            'Visualisation
            If _Bytes.Count > 3 AndAlso _Bytes(0) = &HF0 AndAlso _Bytes(1) = &H13 AndAlso _Bytes(2) = &H1 Then

                'Vpot
                If _Bytes(3) = &H0 Then
                    Jdatas("Cmd") &= "Vpot" & "|"
                    'ScribbleStrip
                ElseIf _Bytes(3) = &H40 Then
                    Jdatas("Cmd") &= "Strip" & "|"
                    Jdatas("Value") &= """" & System.Text.Encoding.ASCII.GetString({_Bytes(6), _Bytes(7), _Bytes(8), _Bytes(9)}) & """" & "|"
                    Jdatas("Track") &= BitConverter.ToInt16({_Bytes(4), &H0}) + 1 & "|"
                    'Vumeter
                ElseIf _Bytes(3) = &H10 Then
                    Jdatas("Cmd") &= "VuMeter" & "|"
                    'Dim _vumeter As New DriverControl24.Reacontrol24.C24VuMeter(Nothing, Nothing)
                    Jdatas("Value") &= "X|" '_vumeter.Get_ParamsValues(_Bytes) & "|"
                    Jdatas("Track") &= BitConverter.ToInt16({(_Bytes(4) And &H1F), &H0}) + 1 & "|"
                    'Automation Led
                ElseIf _Bytes(3) = &H20 Then
                    Jdatas("Track") &= BitConverter.ToInt16({_Bytes(4), &H0}) + 1 & "|"
                    Jdatas("Value") &= "ON" & "|"
                    Select Case _Bytes(5)
                        Case &H4
                            Jdatas("Cmd") &= "read" & "|"
                        Case &H40
                            Jdatas("Cmd") &= "write" & "|"
                        Case &H20
                            Jdatas("Cmd") &= "touch" & "|"
                        Case &H10
                            Jdatas("Cmd") &= "latch" & "|"
                        Case &H8
                            Jdatas("Cmd") &= "trim" & "|"
                        Case &H0
                            Jdatas("Cmd") &= "Allautomode" & "|"
                            Jdatas("Value") &= "OFF" & "|"
                        Case Else
                            Jdatas("Cmd") &= "LedX" & "|"
                    End Select
                    'clock
                ElseIf _Bytes(3) = &H30 Then
                    ' Dim _clock As New DriverControl24.Reacontrol24.C24Clock(Nothing, Nothing)

                    Jdatas("Value") &= "X|" '_clock.Get_ParamsValues(_Bytes) & "|"
                    Jdatas("Cmd") &= "Clock" & "|"
                End If

                'bouton LED
            ElseIf _Bytes.Count > 2 AndAlso _Bytes(0) = &H90 Then

                Select Case _Bytes(1)
                    Case &H0
                        Jdatas("Cmd") &= "recarm" & "|"
                    Case &H1
                        Jdatas("Cmd") &= "pan_send" & "|"
                    Case &H2
                        Jdatas("Cmd") &= "eq" & "|"
                    Case &H3
                        Jdatas("Cmd") &= "dyn" & "|"
                    Case &H4
                        Jdatas("Cmd") &= "switch" & "|"
                    Case &H5
                        Jdatas("Cmd") &= "automode" & "|"
                    Case &H6
                        Jdatas("Cmd") &= "select" & "|"
                    Case &H7
                        Jdatas("Cmd") &= "solo" & "|"
                    Case &H8
                        Jdatas("Cmd") &= "mute" & "|"
                    Case &H9
                        Jdatas("Cmd") &= "touch" & "|"
                    Case &HA
                        Jdatas("Cmd") &= "insert" & "|"
                    Case Else
                        Jdatas("Cmd") &= "BtnX" & "|"

                End Select


                SetTrackAndValue(_Bytes(2))

                'Fader
            ElseIf _Bytes.Count = 5 AndAlso _Bytes(0) = &HB0 Then
                Jdatas("Cmd") &= "Fader" & "|"
                Jdatas("Value") &= "X" & "|"
            Else

                Jdatas("Cmd_ID") = ""
            Jdatas("Cmd") = "" 'System.Text.Encoding.ASCII.GetString(_Bytes)
        End If

        Next
    End Sub

    Private Function GetBytes(Data As String) As Byte()
        Dim datas As String() = Data.Split(" ")
        Dim _Bytes As New List(Of Byte)
        For Each _Data In datas
            _Bytes.Add(Convert.ToByte(_Data, 16))
        Next
        Return _Bytes.ToArray
    End Function
    Sub SetTrackAndValue(_Byte As Byte)

        Dim bits As New BitArray({_Byte})
        If bits(6) = True Then
            Jdatas("Value") &= "ON" & "|"
        Else
            Jdatas("Value") &= "OFF" & "|"
        End If
        bits(6) = False
        Dim TrkBytes(1) As Byte
        bits.CopyTo(TrkBytes, 0)
        Jdatas("Track") &= BitConverter.ToInt16(TrkBytes) + 1 & "|"
    End Sub
End Class
