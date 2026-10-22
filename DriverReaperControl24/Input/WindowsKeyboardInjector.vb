Imports System.Runtime.InteropServices
Imports System.Threading

Namespace DriverReaperControl24.Input
    Friend NotInheritable Class WindowsKeyboardInjector
        Implements IKeyboardInjector

        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function SendInput(nInputs As UInteger, ByRef pInputs As INPUT, cbSize As Integer) As UInteger
        End Function

        Private Const INPUT_KEYBOARD As UInteger = 1UI
        Private Const KEYEVENTF_KEYUP As UInteger = &H2UI
        Private Const VK_CONTROL As UShort = &H11US
        Private Const VK_SHIFT As UShort = &H10US

        <StructLayout(LayoutKind.Sequential)>
        Private Structure INPUT
            Public type As UInteger
            Public U As InputUnion
        End Structure

        <StructLayout(LayoutKind.Explicit)>
        Private Structure InputUnion
            <FieldOffset(0)>
            Public ki As KEYBDINPUT

            <FieldOffset(0)>
            Public mi As MOUSEINPUT

            <FieldOffset(0)>
            Public hi As HARDWAREINPUT
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Private Structure KEYBDINPUT
            Public wVk As UShort
            Public wScan As UShort
            Public dwFlags As UInteger
            Public time As UInteger
            Public dwExtraInfo As IntPtr
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Private Structure MOUSEINPUT
            Public dx As Integer
            Public dy As Integer
            Public mouseData As UInteger
            Public dwFlags As UInteger
            Public time As UInteger
            Public dwExtraInfo As IntPtr
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Private Structure HARDWAREINPUT
            Public uMsg As UInteger
            Public wParamL As UShort
            Public wParamH As UShort
        End Structure

        Public Sub KeyDown(key As KeyboardKey) Implements IKeyboardInjector.KeyDown
            SendKeyboardInput(MapVirtualKey(key), 0UI)
        End Sub

        Public Sub KeyUp(key As KeyboardKey) Implements IKeyboardInjector.KeyUp
            SendKeyboardInput(MapVirtualKey(key), KEYEVENTF_KEYUP)
        End Sub

        Public Sub PressKey(key As KeyboardKey, Optional holdMilliseconds As Integer = 20) Implements IKeyboardInjector.PressKey
            KeyDown(key)

            If holdMilliseconds > 0 Then
                Thread.Sleep(holdMilliseconds)
            End If

            KeyUp(key)
        End Sub

        Public Sub PressCtrlAndKey(key As KeyboardKey, Optional holdMilliseconds As Integer = 20) Implements IKeyboardInjector.PressCtrlAndKey
            SendKeyboardInput(VK_CONTROL, 0UI)
            KeyDown(key)

            If holdMilliseconds > 0 Then
                Thread.Sleep(holdMilliseconds)
            End If

            KeyUp(key)
            SendKeyboardInput(VK_CONTROL, KEYEVENTF_KEYUP)
        End Sub

        Public Sub PressShiftAndKey(key As KeyboardKey, Optional holdMilliseconds As Integer = 20) Implements IKeyboardInjector.PressShiftAndKey
            SendKeyboardInput(VK_SHIFT, 0UI)
            KeyDown(key)

            If holdMilliseconds > 0 Then
                Thread.Sleep(holdMilliseconds)
            End If

            KeyUp(key)
            SendKeyboardInput(VK_SHIFT, KEYEVENTF_KEYUP)
        End Sub

        Private Sub SendKeyboardInput(vk As UShort, flags As UInteger)
            Dim input As New INPUT
            input.type = INPUT_KEYBOARD
            input.U.ki.wVk = vk
            input.U.ki.wScan = 0US
            input.U.ki.dwFlags = flags
            input.U.ki.time = 0UI
            input.U.ki.dwExtraInfo = IntPtr.Zero

            Dim sent As UInteger = SendInput(1UI, input, Marshal.SizeOf(GetType(INPUT)))

            If sent = 0UI Then
                Dim lastError As Integer = Marshal.GetLastWin32Error()
                Console.WriteLine("SendInput failed. LastError=" & lastError.ToString(Globalization.CultureInfo.InvariantCulture))
            End If
        End Sub

        Private Function MapVirtualKey(key As KeyboardKey) As UShort
            Select Case key
                Case KeyboardKey.Equal
                    Return &HBBUS
                Case KeyboardKey.BackSpace
                    Return &H8US
                Case KeyboardKey.Substract
                    Return &H6DUS
                Case KeyboardKey.Divide
                    Return &H6FUS
                Case KeyboardKey.Multiply
                    Return &H6AUS
                Case KeyboardKey.Add
                    Return &H6BUS
                Case KeyboardKey.Decimal
                    Return &H6EUS
                Case KeyboardKey.Tab
                    Return &H9US
                Case KeyboardKey.Space
                    Return &H20US
                Case KeyboardKey.Enter
                    Return &HDUS
                Case KeyboardKey.Escape
                    Return &H1BUS
                Case KeyboardKey.LeftArrow
                    Return &H25US
                Case KeyboardKey.UpArrow
                    Return &H26US
                Case KeyboardKey.RightArrow
                    Return &H27US
                Case KeyboardKey.DownArrow
                    Return &H28US
                Case KeyboardKey.D0
                    Return &H60US
                Case KeyboardKey.D1
                    Return &H61US
                Case KeyboardKey.D2
                    Return &H62US
                Case KeyboardKey.D3
                    Return &H63US
                Case KeyboardKey.D4
                    Return &H64US
                Case KeyboardKey.D5
                    Return &H65US
                Case KeyboardKey.D6
                    Return &H66US
                Case KeyboardKey.D7
                    Return &H67US
                Case KeyboardKey.D8
                    Return &H68US
                Case KeyboardKey.D9
                    Return &H69US
                Case KeyboardKey.A
                    Return &H41US
                Case KeyboardKey.B
                    Return &H42US
                Case KeyboardKey.C
                    Return &H43US
                Case KeyboardKey.D
                    Return &H44US
                Case KeyboardKey.E
                    Return &H45US
                Case KeyboardKey.F
                    Return &H46US
                Case KeyboardKey.G
                    Return &H47US
                Case KeyboardKey.H
                    Return &H48US
                Case KeyboardKey.I
                    Return &H49US
                Case KeyboardKey.J
                    Return &H4AUS
                Case KeyboardKey.K
                    Return &H4BUS
                Case KeyboardKey.L
                    Return &H4CUS
                Case KeyboardKey.M
                    Return &H4DUS
                Case KeyboardKey.N
                    Return &H4EUS
                Case KeyboardKey.O
                    Return &H4FUS
                Case KeyboardKey.P
                    Return &H50US
                Case KeyboardKey.Q
                    Return &H51US
                Case KeyboardKey.R
                    Return &H52US
                Case KeyboardKey.S
                    Return &H53US
                Case KeyboardKey.T
                    Return &H54US
                Case KeyboardKey.U
                    Return &H55US
                Case KeyboardKey.V
                    Return &H56US
                Case KeyboardKey.W
                    Return &H57US
                Case KeyboardKey.X
                    Return &H58US
                Case KeyboardKey.Y
                    Return &H59US
                Case KeyboardKey.Z
                    Return &H5AUS
                Case Else
                    Return &H20US
            End Select
        End Function
    End Class
End Namespace