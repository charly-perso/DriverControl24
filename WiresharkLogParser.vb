Imports System.IO
Imports System.Text.RegularExpressions

Public Class WiresharkLogParser
    Private Const AVID_MAC As String = "AvidTechnolo_a2:1d:46"
    Private Const DELL_MAC As String = "Dell_b7:13:84"

    Public Sub ConvertToCsv(inputPath As String, outputPath As String)
        Dim frames As New List(Of FrameData)

        Using reader As New StreamReader(inputPath)
            Dim currentFrame As FrameData = Nothing
            Dim inDataSection As Boolean = False
            Dim dataLines As New List(Of String)

            While Not reader.EndOfStream
                Dim line As String = reader.ReadLine().Trim()

                ' Détection d'une nouvelle frame
                If line.StartsWith("Frame ") Then
                    ' Sauvegarder la frame précédente si elle existe
                    If currentFrame IsNot Nothing AndAlso dataLines.Count > 0 Then
                        currentFrame.Data = ParseDataLines(dataLines)
                        frames.Add(currentFrame)
                        dataLines.Clear()
                    End If

                    currentFrame = New FrameData()
                    inDataSection = False

                ElseIf line.StartsWith("No.") AndAlso currentFrame IsNot Nothing Then
                    ' Extraction du numéro de frame et du temps
                    Dim match = Regex.Match(line, "^\s*(\d+)\s+([\d.]+)")
                    If match.Success Then
                        currentFrame.FrameNumber = Integer.Parse(match.Groups(1).Value)
                        currentFrame.Time = Double.Parse(match.Groups(2).Value)
                    End If

                ElseIf line.Contains("Src:") AndAlso line.Contains("Dst:") Then
                    ' Détection du sens de communication
                    If line.Contains(DELL_MAC) Then
                        If line.Contains($"Src: {DELL_MAC}") Then
                            currentFrame.Direction = "PC -> C24"
                        ElseIf line.Contains($"Dst: {DELL_MAC}") Then
                            currentFrame.Direction = "C24 -> PC"
                        End If
                    End If

                ElseIf line.StartsWith("Data (") Then
                    inDataSection = True

                ElseIf inDataSection AndAlso Regex.IsMatch(line, "^[0-9a-fA-F]{4}\s+") Then
                    ' Ligne de données hexadécimales
                    dataLines.Add(line)

                ElseIf inDataSection AndAlso String.IsNullOrWhiteSpace(line) Then
                    ' Fin de la section de données
                    inDataSection = False
                End If
            End While

            ' Sauvegarder la dernière frame
            If currentFrame IsNot Nothing AndAlso dataLines.Count > 0 Then
                currentFrame.Data = ParseDataLines(dataLines)
                frames.Add(currentFrame)
            End If
        End Using

        ' Écriture du fichier CSV
        WriteCsv(frames, outputPath)
    End Sub

    Private Function ParseDataLines(dataLines As List(Of String)) As String
        Dim hexBytes As New List(Of String)

        For Each line In dataLines
            ' Extraction des octets hexadécimaux (format: 0000  00 32 05 a3 ...)
            Dim match = Regex.Match(line, "^[0-9a-fA-F]{4}\s+((?:[0-9a-fA-F]{2}\s*)+)")
            If match.Success Then
                Dim bytes = match.Groups(1).Value.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
                hexBytes.AddRange(bytes)
            End If
        Next

        Return String.Join(" ", hexBytes)
    End Function

    Private Sub WriteCsv(frames As List(Of FrameData), outputPath As String)
        Using writer As New StreamWriter(outputPath, False, System.Text.Encoding.UTF8)
            ' En-tête
            writer.WriteLine("Frame nb,Time,Sens,Data")

            ' Données
            For Each frame In frames
                If Not String.IsNullOrEmpty(frame.Direction) Then
                    writer.WriteLine($"{frame.FrameNumber},{frame.Time.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},{frame.Direction},""{frame.Data}""")
                End If
            Next
        End Using
    End Sub

    Private Class FrameData
        Public Property FrameNumber As Integer
        Public Property Time As Double
        Public Property Direction As String
        Public Property Data As String
    End Class
End Class