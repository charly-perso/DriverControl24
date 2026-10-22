Imports System.Runtime.InteropServices
Imports System.Threading

Namespace DriverReaperControl24.Input
    Friend NotInheritable Class MacKeyboardInjector
        Implements IKeyboardInjector

        <DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")>
        Private Shared Function CGEventCreateKeyboardEvent(source As IntPtr, virtualKey As UShort, keyDown As Boolean) As IntPtr
        End Function

        <DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")>
        Private Shared Sub CGEventPost(tap As Integer, cgEvent As IntPtr)
        End Sub

        <DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")>
        Private Shared Sub CFRelease(handle As IntPtr)
        End Sub

        Private Const kCGHIDEventTap As Integer = 0
        Private Const kVK_Command As UShort = 55US
        Private Const kVK_Shift As UShort = 56US

        Public Sub KeyDown(key As KeyboardKey) Implements IKeyboardInjector.KeyDown
            PostKey(MapMacKeyCode(key), True)
        End Sub

        Public Sub KeyUp(key As KeyboardKey) Implements IKeyboardInjector.KeyUp
            PostKey(MapMacKeyCode(key), False)
        End Sub

        Public Sub PressKey(key As KeyboardKey, Optional holdMilliseconds As Integer = 20) Implements IKeyboardInjector.PressKey
            KeyDown(key)

            If holdMilliseconds > 0 Then
                Thread.Sleep(holdMilliseconds)
            End If

            KeyUp(key)
        End Sub

        Public Sub PressShiftAndKey(key As KeyboardKey, Optional holdMilliseconds As Integer = 20) Implements IKeyboardInjector.PressShiftAndKey
            PostKey(kVK_Shift, True)
            KeyDown(key)

            If holdMilliseconds > 0 Then
                Thread.Sleep(holdMilliseconds)
            End If

            KeyUp(key)
            PostKey(kVK_Command, False)
        End Sub
        Public Sub PressCtrlAndKey(key As KeyboardKey, Optional holdMilliseconds As Integer = 20) Implements IKeyboardInjector.PressCtrlAndKey
            PostKey(kVK_Command, True)
            KeyDown(key)

            If holdMilliseconds > 0 Then
                Thread.Sleep(holdMilliseconds)
            End If

            KeyUp(key)
            PostKey(kVK_Command, False)
        End Sub

        Private Sub PostKey(macKeyCode As UShort, isDown As Boolean)
            Dim evt As IntPtr = CGEventCreateKeyboardEvent(IntPtr.Zero, macKeyCode, isDown)

            If evt = IntPtr.Zero Then
                Return
            End If

            CGEventPost(kCGHIDEventTap, evt)
            CFRelease(evt)
        End Sub

        Private Function MapMacKeyCode(key As KeyboardKey) As UShort
            Select Case key
                Case KeyboardKey.Equal
                    Return 81US
                Case KeyboardKey.BackSpace
                    Return 51US
                Case KeyboardKey.Decimal
                    Return 65US
                Case KeyboardKey.Multiply
                    Return 67US
                Case KeyboardKey.Add
                    Return 69US
                Case KeyboardKey.Substract
                    Return 78US
                Case KeyboardKey.Divide
                    Return 75US
                Case KeyboardKey.Tab
                    Return 48US
                Case KeyboardKey.Shift
                    Return 56US
                Case KeyboardKey.Space
                    Return 49US
                Case KeyboardKey.Enter
                    Return 36US
                Case KeyboardKey.Escape
                    Return 53US
                Case KeyboardKey.LeftArrow
                    Return 123US
                Case KeyboardKey.RightArrow
                    Return 124US
                Case KeyboardKey.DownArrow
                    Return 125US
                Case KeyboardKey.UpArrow
                    Return 126US
                Case KeyboardKey.D0
                    Return 82US
                Case KeyboardKey.D1
                    Return 83US
                Case KeyboardKey.D2
                    Return 84US
                Case KeyboardKey.D3
                    Return 85US
                Case KeyboardKey.D4
                    Return 86US
                Case KeyboardKey.D5
                    Return 87US
                Case KeyboardKey.D6
                    Return 88US
                Case KeyboardKey.D7
                    Return 89US
                Case KeyboardKey.D8
                    Return 91US
                Case KeyboardKey.D9
                    Return 92US
                Case KeyboardKey.A
                    Return 0US
                Case KeyboardKey.B
                    Return 11US
                Case KeyboardKey.C
                    Return 8US
                Case KeyboardKey.D
                    Return 2US
                Case KeyboardKey.E
                    Return 14US
                Case KeyboardKey.F
                    Return 3US
                Case KeyboardKey.G
                    Return 5US
                Case KeyboardKey.H
                    Return 4US
                Case KeyboardKey.I
                    Return 34US
                Case KeyboardKey.J
                    Return 38US
                Case KeyboardKey.K
                    Return 40US
                Case KeyboardKey.L
                    Return 37US
                Case KeyboardKey.M
                    Return 46US
                Case KeyboardKey.N
                    Return 45US
                Case KeyboardKey.O
                    Return 31US
                Case KeyboardKey.P
                    Return 35US
                Case KeyboardKey.Q
                    Return 12US
                Case KeyboardKey.R
                    Return 15US
                Case KeyboardKey.S
                    Return 1US
                Case KeyboardKey.T
                    Return 17US
                Case KeyboardKey.U
                    Return 32US
                Case KeyboardKey.V
                    Return 9US
                Case KeyboardKey.W
                    Return 13US
                Case KeyboardKey.X
                    Return 7US
                Case KeyboardKey.Y
                    Return 16US
                Case KeyboardKey.Z
                    Return 6US
                Case Else
                    Return 49US
            End Select
        End Function
    End Class
End Namespace