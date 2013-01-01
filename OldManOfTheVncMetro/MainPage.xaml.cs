//-------------------------------------------------------------------------------------------------
//  Copyright 2012 Paul C. Roberts
//
//  Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file 
//  except in compliance with the License. You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software distributed under the 
//  License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, 
//  either express or implied. See the License for the specific language governing permissions and 
//  limitations under the License.
//-------------------------------------------------------------------------------------------------

namespace OldManOfTheVncMetro
{
    using System;
    using System.Runtime.InteropServices.WindowsRuntime;
    using System.Threading.Tasks;
    using PollRobots.OmotVncProtocol;
    using Windows.Devices.Input;
    using Windows.Networking;
    using Windows.Networking.Sockets;
    using Windows.Storage;
    using Windows.System;
    using Windows.UI.Core;
    using Windows.UI.Input;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;
    using Windows.UI.Xaml.Media.Animation;
    using Windows.UI.Xaml.Media.Imaging;
    using Windows.UI.Xaml.Navigation;

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        const int KeyboardHeight = 350;
        private AConnectionOperations connection;
        private WriteableBitmap frameBufferBitmap;
        private WriteableBitmap zoomBufferBitmap;
        private DispatcherTimer updateTimer;
        private bool isKeyboardVisible;
        private bool hasRectangle;
        private bool isConnected;
        private bool hasSettings;
        private bool isLeftShiftDown;
        private bool isRightShiftDown;

        public MainPage()
        {
            this.InitializeComponent();
            this.Keyboard.KeyChange += Keyboard_KeyChange;

            this.Server.Text = this.GetLocalSetting("Server");
            this.Port.Text = this.GetLocalSetting("Port", "5900");
            this.Password.Password = this.GetLocalSetting("Password");

            if (this.hasSettings)
            {
                this.Invoke(() => this.ConnectButton.Focus(FocusState.Programmatic));
            }
        }

        private string GetLocalSetting(string name, string defaultValue = "")
        {
            object value = ApplicationData.Current.LocalSettings.Values[name];
            if (value == null)
            {
                return defaultValue;
            }

            this.hasSettings = true;
            return value.ToString();
        }

        private void SetLocalSetting(string name, string value)
        {
            ApplicationData.Current.LocalSettings.Values[name] = value;
        }

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached.  The Parameter
        /// property is typically used to configure the page.</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
        }

        private void ClickConnect(object sender, RoutedEventArgs e)
        {
            var server = this.Server.Text;
            var port = this.Port.Text;
            var password = this.Password.Password;

            var client = new StreamSocket();

            Task.Run(async () =>
            {
                try
                {
                    await client.ConnectAsync(new HostName(server), port);

                    this.connection = Connection.CreateFromStreamSocket(
                        client,
                        r => this.Invoke(() => this.OnRectangle(r)),
                        s => this.Invoke(() => OnStateChange(s)),
                        f => this.Invoke(() => OnException(f)));

                    var requiresPassword = await this.connection.Handshake();

                    if (requiresPassword)
                    {
                        await this.connection.SendPassword(password);
                    }

                    var name = await this.connection.Initialize(true);

                    var connectionInfo = this.connection.GetConnectionInfo();

                    this.Invoke(() =>
                    {
                        this.SetLocalSetting("Server", server);
                        this.SetLocalSetting("Port", port);
                        this.SetLocalSetting("Password", password);

                        this.StartFrameBuffer(connectionInfo);
                    });
                }
                catch
                {
                    this.Invoke(() =>
                    {
                        this.OnStateChange(ConnectionState.Disconnected);
                        this.connection = null;
                    });
                }
            });
        }

        void Invoke(Action action)
        {
            this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => action());
        }

        void OnRectangle(Rectangle rectangle)
        {
            if (!this.hasRectangle)
            {
                this.hasRectangle = true;
                this.FinishConnecting();
            }

            var dest = this.frameBufferBitmap.PixelBuffer;

            var width = this.frameBufferBitmap.PixelWidth;
            var height = this.frameBufferBitmap.PixelHeight;

            var leftOffset = rectangle.Left * 4;
            var scan = rectangle.Width * 4;

            var src = WindowsRuntimeBufferExtensions.AsBuffer(rectangle.Pixels);

            for (int y = 0; y < rectangle.Height; y++)
            {
                var destOffset = (y + rectangle.Top) * width * 4 + leftOffset;
                var srcOffset = y * scan;

                src.CopyTo((uint)srcOffset, dest, (uint)destOffset, (uint)scan);
            }

            this.frameBufferBitmap.Invalidate();
        }

        void OnStateChange(ConnectionState state)
        {
            switch (state)
            {
                case ConnectionState.Disconnected:
                    this.ConnectPanel.Visibility = Visibility.Visible;
                    this.Connecting.Visibility = Visibility.Collapsed;
                    this.Server.IsEnabled = true;
                    this.Port.IsEnabled = true;
                    this.Password.IsEnabled = true;
                    Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Arrow, 1);
                    this.isConnected = false;
                    this.hasRectangle = false;
                    break;
                case ConnectionState.Handshaking:
                case ConnectionState.SendingPassword:
                case ConnectionState.Initializing:
                    this.Server.IsEnabled = false;
                    this.Port.IsEnabled = false;
                    this.Password.IsEnabled = false;
                    this.Connecting.Visibility = Visibility.Visible;
                    break;
                case ConnectionState.Connected:
                    this.isConnected = true;
                    this.FinishConnecting();
                    break;
                default:
                    break;
            }
        }

        void FinishConnecting()
        {
            if (this.hasRectangle && this.isConnected)
            {
                this.ConnectPanel.Visibility = Visibility.Collapsed;
                Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Cross, 1);
            }
        }

        void OnException(Exception exception)
        {
        }

        void StartFrameBuffer(ConnectionInfo connectionInfo)
        {
            var frameWidth = connectionInfo.Width;
            var frameHeight = connectionInfo.Height;

            this.frameBufferBitmap = new WriteableBitmap(frameWidth, frameHeight);
            this.zoomBufferBitmap = new WriteableBitmap(50, 50);

            this.FrameBuffer.Source = this.frameBufferBitmap;
            this.ZoomBrush.ImageSource = this.zoomBufferBitmap;

            this.connection.Start();
            this.connection.Update(true);

            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += (s,e) => this.OnTimer();
            timer.Start();

            this.updateTimer = timer;
        }

        void OnTimer()
        {
            if (this.connection == null)
            {
                this.updateTimer.Stop();
            }
            else
            {
                this.connection.Update(false);
            }
        }

        int lastTouchX;
        int lastTouchY;
        bool isLeftButtonSimulated;
        bool isRightButtonSimulated;

        private void FrameBufferPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (this.connection == null) 
            {
                return;
            }

            var point = e.GetCurrentPoint(this.FrameBuffer);
            var ox = point.Position.X;
            var oy = point.Position.Y;
            var sx = this.frameBufferBitmap.PixelWidth * ox / this.FrameBuffer.ActualWidth;
            var sy = this.frameBufferBitmap.PixelHeight * oy / this.FrameBuffer.ActualHeight;

            if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse)
            {
                var buttons = (point.Properties.IsLeftButtonPressed ? 1 : 0) |
                    (point.Properties.IsMiddleButtonPressed ? 2 : 0) |
                    (point.Properties.IsRightButtonPressed ? 4 : 0);

                this.MovePointer(buttons, (int)sx, (int)sy, ox, oy, isHighPriority: point.Properties.PointerUpdateKind != PointerUpdateKind.Other);
            }
            else
            {
                if (point.Properties.IsPrimary)
                {
                    lastTouchX = (int)sx;
                    lastTouchY = (int)sy;
                    var buttons = (isLeftButtonSimulated ? 1 : 0) |
                        (isRightButtonSimulated? 4 : 0);
                    this.MovePointer(buttons, (int)sx, (int)sy, ox, oy, isHighPriority: point.Properties.PointerUpdateKind != PointerUpdateKind.Other);

                    if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
                    {
                        this.Zoomer.Visibility = Visibility.Visible;
                    }
                    else if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
                    {
                        this.Zoomer.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
                    {
                        isLeftButtonSimulated = true;
                        var buttons = (isLeftButtonSimulated ? 1 : 0) |
                            (isRightButtonSimulated ? 4 : 0);
                        this.MovePointer(buttons, lastTouchX, lastTouchY, ox, oy, isHighPriority: true);
                    }
                    else if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
                    {
                        isLeftButtonSimulated = false;
                        var buttons = (isLeftButtonSimulated ? 1 : 0) |
                            (isRightButtonSimulated ? 4 : 0);
                        this.MovePointer(buttons, lastTouchX, lastTouchY, ox, oy, isHighPriority: true);
                    }
                }
            }

            e.Handled = true;
        }

        private void MovePointer(int buttons, int sx, int sy, double x, double y, bool isHighPriority)
        {
            this.connection.SetPointer(buttons, sx, sy, isHighPriority);

            var cx = this.FrameBuffer.ActualWidth / 2;
            var cy = this.FrameBuffer.ActualHeight / 2;

            var dx = cx - x;
            var dy = cy - y;

            var len = Math.Sqrt(dx * dx + dy * dy);
            var scale = 100 / len;

            var ox = x + dx * scale - 50;
            var oy = y + dy * scale - 50;

            Canvas.SetLeft(this.Zoomer, ox);
            Canvas.SetTop(this.Zoomer, oy);

            var width = this.zoomBufferBitmap.PixelWidth;
            var height = this.zoomBufferBitmap.PixelHeight;
            var left = (int)sx - width / 2;
            if (left < 0)
            {
                left = 0;
            }
            else if (left + width >= this.frameBufferBitmap.PixelWidth)
            {
                left = this.frameBufferBitmap.PixelWidth - width;
            }

            var top = (int)sy - height / 2;
            if (top < 0)
            {
                top = 0;
            }
            else if (top + height >= this.frameBufferBitmap.PixelHeight)
            {
                top = this.frameBufferBitmap.PixelHeight - height;
            }

            var scan = width * 4;
            var srcWidth = this.frameBufferBitmap.PixelWidth;
            var src = this.frameBufferBitmap.PixelBuffer;
            var dest = this.zoomBufferBitmap.PixelBuffer;
            for (int i = 0; i < height; i++)
            {
                var srcOffset = ((top + i) * srcWidth + left) * 4;
                var destOffset = width * i * 4;
                src.CopyTo((uint)srcOffset, dest, (uint)destOffset, (uint)scan);
            }
            this.zoomBufferBitmap.Invalidate();
        }

        private void HandleKey(object sender, KeyRoutedEventArgs e)
        {
            if (this.connection == null)
            {
                return;
            }
            
            uint key = 0;
            switch (e.Key)
            {
                case VirtualKey.Back:
                    key = (uint)VncKey.BackSpace;
                    break;
                case VirtualKey.Tab:
                    key = (uint)VncKey.Tab;
                    break;
                case VirtualKey.Enter:
                    key = (uint)VncKey.Enter;
                    break;
                case VirtualKey.Escape:
                    key = (uint)VncKey.Escape;
                    break;
                case VirtualKey.Insert:
                    key = (uint)VncKey.Insert;
                    break;
                case VirtualKey.Delete:
                    key = (uint)VncKey.Delete;
                    break;
                case VirtualKey.Home:
                    key = (uint)VncKey.Home;
                    break;
                case VirtualKey.End:
                    key = (uint)VncKey.End;
                    break;
                case VirtualKey.PageUp:
                    key = (uint)VncKey.PageUp;
                    break;
                case VirtualKey.PageDown:
                    key = (uint)VncKey.PageDown;
                    break;
                case VirtualKey.Left:
                    key = (uint)VncKey.Left;
                    break;
                case VirtualKey.Up:
                    key = (uint)VncKey.Up;
                    break;
                case VirtualKey.Right:
                    key = (uint)VncKey.Right;
                    break;
                case VirtualKey.Down:
                    key = (uint)VncKey.Down;
                    break;
                case VirtualKey.F1:
                    key = (uint)VncKey.F1;
                    break;
                case VirtualKey.F2:
                    key = (uint)VncKey.F2;
                    break;
                case VirtualKey.F3:
                    key = (uint)VncKey.F3;
                    break;
                case VirtualKey.F4:
                    key = (uint)VncKey.F4;
                    break;
                case VirtualKey.F5:
                    key = (uint)VncKey.F5;
                    break;
                case VirtualKey.F6:
                    key = (uint)VncKey.F6;
                    break;
                case VirtualKey.F7:
                    key = (uint)VncKey.F7;
                    break;
                case VirtualKey.F8:
                    key = (uint)VncKey.F8;
                    break;
                case VirtualKey.F9:
                    key = (uint)VncKey.F9;
                    break;
                case VirtualKey.F10:
                    key = (uint)VncKey.F10;
                    break;
                case VirtualKey.F11:
                    key = (uint)VncKey.F11;
                    break;
                case VirtualKey.F12:
                    key = (uint)VncKey.F12;
                    break;
                case VirtualKey.Shift:
                case VirtualKey.LeftShift:
                    key = (uint)VncKey.ShiftLeft;
                    this.isLeftShiftDown = !e.KeyStatus.IsKeyReleased;
                    break;
                case VirtualKey.RightShift:
                    key = (uint)VncKey.ShiftRight;
                    this.isRightShiftDown = !e.KeyStatus.IsKeyReleased;
                    break;
                case VirtualKey.Control:
                case VirtualKey.LeftControl:
                    key = (uint)VncKey.ControlLeft;
                    break;
                case VirtualKey.RightControl:
                    key = (uint)VncKey.ControlRight;
                    break;
                case VirtualKey.LeftWindows:
                    key = (uint)VncKey.MetaLeft;
                    break;
                case VirtualKey.RightWindows:
                case VirtualKey.Application:
                    key = (uint)VncKey.MetaRight;
                    break;
                case VirtualKey.Menu:
                case VirtualKey.LeftMenu:
                    key = (uint)VncKey.AltLeft;
                    break;
                case VirtualKey.RightMenu:
                    key = (uint)VncKey.AltRight;
                    break;
                default:
                    //var modifiers = Keyboard.Modifiers;
                    //if (!modifiers.HasFlag(ModifierKeys.Control) &&
                    //    !modifiers.HasFlag(ModifierKeys.Alt))
                    //{
                    //    return;
                    //}

                    //key = (uint)TranslateKey(modifiers.HasFlag(ModifierKeys.Shift), e.Key);
                    key = (uint)TranslateKey(this.isLeftShiftDown || this.isRightShiftDown, e.Key);
                    break;
            }

            e.Handled = true;
            this.connection.SendKey(!e.KeyStatus.IsKeyReleased, key, true);
            this.connection.Update(false);        
        }

        private char TranslateKey(bool isShifted, VirtualKey key)
        {
            switch (key)
            {
                case (VirtualKey)0xC0: return isShifted ? '~' : '`';
                case (VirtualKey)0xBD: return isShifted ? '_' : '-';
                case (VirtualKey)0xBB: return isShifted ? '+' : '=';
                case (VirtualKey)0xDB: return isShifted ? '{' : '[';
                case (VirtualKey)0xDD: return isShifted ? '}' : ']';
                case (VirtualKey)0xDC: return isShifted ? '|' : '\\';
                case (VirtualKey)0xBA: return isShifted ? ':' : ';';
                case (VirtualKey)0xDE: return isShifted ? '"' : '\'';
                case (VirtualKey)0xBC: return isShifted ? '<' : ',';
                case (VirtualKey)0xBE: return isShifted ? '>' : '.';
                case (VirtualKey)0xBF: return isShifted ? '?' : '/';

                case VirtualKey.Number0: return isShifted ? ')' : '0';
                case VirtualKey.Number1: return isShifted ? '!' : '1';
                case VirtualKey.Number2: return isShifted ? '@' : '2';
                case VirtualKey.Number3: return isShifted ? '#' : '3';
                case VirtualKey.Number4: return isShifted ? '$' : '4';
                case VirtualKey.Number5: return isShifted ? '%' : '5';
                case VirtualKey.Number6: return isShifted ? '^' : '6';
                case VirtualKey.Number7: return isShifted ? '&' : '7';
                case VirtualKey.Number8: return isShifted ? '*' : '8';
                case VirtualKey.Number9: return isShifted ? '(' : '9';

                case VirtualKey.NumberPad0: return '0';
                case VirtualKey.NumberPad1: return '1';
                case VirtualKey.NumberPad2: return '2';
                case VirtualKey.NumberPad3: return '3';
                case VirtualKey.NumberPad4: return '4';
                case VirtualKey.NumberPad5: return '5';
                case VirtualKey.NumberPad6: return '6';
                case VirtualKey.NumberPad7: return '7';
                case VirtualKey.NumberPad8: return '8';
                case VirtualKey.NumberPad9: return '9';

                case VirtualKey.Decimal: return isShifted ? '>' : '.';
                case VirtualKey.Divide: return isShifted ? '?' : '/';
                case VirtualKey.Space: return ' ';
                case VirtualKey.Subtract: return isShifted ? '_' : '-';

                case VirtualKey.A: return isShifted ? 'A' : 'a';
                case VirtualKey.B: return isShifted ? 'B' : 'b';
                case VirtualKey.C: return isShifted ? 'C' : 'c';
                case VirtualKey.D: return isShifted ? 'D' : 'd';
                case VirtualKey.E: return isShifted ? 'E' : 'e';
                case VirtualKey.F: return isShifted ? 'F' : 'f';
                case VirtualKey.G: return isShifted ? 'G' : 'g';
                case VirtualKey.H: return isShifted ? 'H' : 'h';
                case VirtualKey.I: return isShifted ? 'I' : 'i';
                case VirtualKey.J: return isShifted ? 'J' : 'j';
                case VirtualKey.K: return isShifted ? 'K' : 'k';
                case VirtualKey.L: return isShifted ? 'L' : 'l';
                case VirtualKey.M: return isShifted ? 'M' : 'm';
                case VirtualKey.N: return isShifted ? 'N' : 'n';
                case VirtualKey.O: return isShifted ? 'O' : 'o';
                case VirtualKey.P: return isShifted ? 'P' : 'p';
                case VirtualKey.Q: return isShifted ? 'Q' : 'q';
                case VirtualKey.R: return isShifted ? 'R' : 'r';
                case VirtualKey.S: return isShifted ? 'S' : 's';
                case VirtualKey.T: return isShifted ? 'T' : 't';
                case VirtualKey.U: return isShifted ? 'U' : 'u';
                case VirtualKey.V: return isShifted ? 'V' : 'v';
                case VirtualKey.W: return isShifted ? 'W' : 'w';
                case VirtualKey.X: return isShifted ? 'X' : 'x';
                case VirtualKey.Y: return isShifted ? 'Y' : 'y';
                case VirtualKey.Z: return isShifted ? 'Z' : 'z';
                default:
                    return '?';
            }
        }

        private void KeyboardExpandoTapped(object sender, TappedRoutedEventArgs e)
        {
            Storyboard storyboard;
            if (this.isKeyboardVisible)
            {
                this.isKeyboardVisible = false;
                storyboard = this.Resources["CollapseKeyboard"] as Storyboard;
            }
            else
            {
                this.isKeyboardVisible = true;
                storyboard = this.Resources["ExpandKeyboard"] as Storyboard;
            }

            if (storyboard != null)
            {
                storyboard.Begin();
            }

            this.FocusCarrier.Focus(FocusState.Programmatic);
        }

        void Keyboard_KeyChange(object sender, KeyEventArgs e)
        {
            if (this.connection == null)
            {
                return;
            }

            this.connection.SendKey(e.IsPressed, (uint)e.Key, true);
            this.FocusCarrier.Focus(FocusState.Programmatic);
        }

        private void ConnectPanelKeyUp(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                if (e.OriginalSource == this.Server ||
                    e.OriginalSource == this.Port ||
                    e.OriginalSource == this.Password)
                {
                    this.ClickConnect(sender, e);
                }
            }
        }
    }
}
