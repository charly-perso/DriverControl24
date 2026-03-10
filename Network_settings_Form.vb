Private Async Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
    Button1.Enabled = False

    Try
        ' ✅ Activer le mode test complet (keep-alive + broadcast ignore)
        _c24Session.SetTestingMode(True)

        ' ✅ NOUVEAU: Dire au NativePcapAdapter d'ignorer les broadcasts
        If TypeOf _c24Session._adapter Is Reacontrol24.NativePcapAdapter Then
            DirectCast(_c24Session._adapter, Reacontrol24.NativePcapAdapter).SetTestMode(True)
        End If

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
        ' ✅ Restaurer le mode normal
        _c24Session.SetTestingMode(False)

        ' ✅ Réactiver le traitement des broadcasts
        If TypeOf _c24Session._adapter Is Reacontrol24.NativePcapAdapter Then
            DirectCast(_c24Session._adapter, Reacontrol24.NativePcapAdapter).SetTestMode(False)
        End If

        TextBox1.Text &= "=== LED TEST ENDED (Keep-alive + broadcasts re-enabled) ===" & vbCrLf
        Button1.Enabled = True
    End Try
End Sub