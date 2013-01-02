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
                        if (string.IsNullOrEmpty(password)) {
                            password = await this.GetPassword();
                        }
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

        Task<string> GetPassword()
        {
            TaskCompletionSource<string> source = new TaskCompletionSource<string>();

            Invoke(() => {
                this.PasswordRequired.Visibility = Visibility.Visible;
                this.Password.IsEnabled = true;
                this.Password.Focus(FocusState.Programmatic);
                this.ConnectButton.Visibility = Visibility.Collapsed;
                this.SendPassword.Visibility = Visibility.Visible;
                this.SendPassword.Command = new DelegateCommand(() =>
                    {
                        source.SetResult(this.Password.Password);
                        this.SendPassword.Command = null;
                        this.PasswordRequired.Visibility = Visibility.Collapsed;
                        this.Password.IsEnabled = false;
                        this.ConnectButton.Visibility = Visibility.Visible;
                        this.SendPassword.Visibility = Visibility.Collapsed;
                    });
            });

            return source.Task;
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
                    this.PasswordRequired.Visibility = Visibility.Collapsed;
                    this.ConnectPanel.Visibility = Visibility.Visible;
                    this.Connecting.Visibility = Visibility.Collapsed;
                    this.Server.IsEnabled = true;
                    this.Port.IsEnabled = true;
                    this.Password.IsEnabled = true;
                    this.ConnectButton.IsEnabled = true;
                    this.FocusCarrier.IsEnabled = false;
                    this.ConnectButton.Visibility = Visibility.Visible;
                    this.SendPassword.Visibility = Visibility.Collapsed;
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
                    this.ConnectButton.IsEnabled = false;
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
                this.FocusCarrier.IsEnabled = true;
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
            if (this.connection == null || !this.isConnected) 
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
            if (this.connection == null || !this.isConnected)
            {
                return;
            }

            int scancode = (e.KeyStatus.IsExtendedKey ? 0xE000 : 0) | (int)e.KeyStatus.ScanCode;
            var pressed = !e.KeyStatus.IsKeyReleased;

            VncKey vncKey = this.Keyboard.LookupKey(isLeftShiftDown || isRightShiftDown, scancode);

            if (vncKey == VncKey.Unknown)
            {
                return;
            }
            else if (vncKey == VncKey.ShiftLeft) {
                this.isLeftShiftDown = pressed;
            }
            else if (vncKey == VncKey.ShiftRight)
            {
                this.isRightShiftDown = pressed;
            }

            e.Handled = true;
            this.connection.SendKey(pressed, (uint)vncKey, update: true);
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
            if (this.connection == null || !this.isConnected)
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
                    if (this.ConnectButton.IsEnabled)
                    {
                        this.ClickConnect(sender, e);
                    }
                    else if (this.SendPassword.IsEnabled &&
                        this.SendPassword.Command != null)
                    {
                        this.SendPassword.Command.Execute(this.SendPassword.CommandParameter);
                    }
                }
            }
        }
    }
}
