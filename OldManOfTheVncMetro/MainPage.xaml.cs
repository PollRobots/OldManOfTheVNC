// -----------------------------------------------------------------------------
// <copyright file="MainPage.xaml.cs" company="Paul C. Roberts">
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
// </copyright>
// -----------------------------------------------------------------------------

namespace OldManOfTheVncMetro
{
    using System;
    using System.Collections.Concurrent;
    using System.Runtime.InteropServices.WindowsRuntime;
    using System.Threading;
    using System.Threading.Tasks;
    using PollRobots.OmotVncProtocol;
    using Windows.Devices.Input;
    using Windows.Networking;
    using Windows.Networking.Sockets;
    using Windows.System;
    using Windows.UI.ApplicationSettings;
    using Windows.UI.Core;
    using Windows.UI.Input;
    using Windows.UI.Popups;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Controls.Primitives;
    using Windows.UI.Xaml.Input;
    using Windows.UI.Xaml.Media.Animation;
    using Windows.UI.Xaml.Media.Imaging;
    using Windows.UI.Xaml.Navigation;

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        /// <summary>The visual height of the keyboard.</summary>
        private const int KeyboardHeight = 350;
        
        /// <summary>Queue used to pass update actions to the UI thread.</summary>
        private readonly ConcurrentQueue<Action> updateQueue = new ConcurrentQueue<Action>();

        /// <summary>The protocol connection to the server.</summary>
        private ConnectionOperations connection;

        /// <summary>The frame buffer for the remote display.</summary>
        private WriteableBitmap frameBufferBitmap;

        /// <summary>A bitmap used to provide a zoomed in view of the portion of the screen covered by a finger.</summary>
        private WriteableBitmap zoomBufferBitmap;

        /// <summary>The timer used to ask for periodic UI updates.</summary>
        private DispatcherTimer updateTimer;

        /// <summary>Indicates whether the keyboard is visible.</summary>
        private bool isKeyboardVisible;

        /// <summary>Indicates whether a rectangle has been received since the connection was started.</summary>
        private bool hasRectangle;

        /// <summary>Indicates whether the protocol has reported itself as connected.</summary>
        private bool isConnected;

        /// <summary>Indicates whether the left shift key is pressed.</summary>
        private bool isLeftShiftDown;

        /// <summary>Indicates whether the right shift key is pressed.</summary>
        private bool isRightShiftDown;

        /// <summary>The current settings popup.</summary>
        private Popup settingsPopup;

        /// <summary>If there is currently an operation on the UI thread that will drain the update queue then this
        /// is 1, otherwise it is 0.</summary>
        private int updateInvokePending;

        /// <summary>The last touch position, x-coordinate.</summary>
        private int lastTouchX;

        /// <summary>The last touch position, y-coordinate.</summary>
        private int lastTouchY;

        /// <summary>Is the left button currently simulated as pressed.</summary>
        private bool isLeftButtonSimulated;

        /// <summary>Is the right button currently simulated as pressed.</summary>
        private bool isRightButtonSimulated;

        /// <summary>Initializes a new instance of the <see cref="MainPage"/> class.</summary>
        public MainPage()
        {
            this.InitializeComponent();
            this.Keyboard.KeyChange += this.KeyboardKeyChange;

            Task.Run(async () =>
            {
                var server = await Settings.GetLocalSettingAsync("Server");
                var port = await Settings.GetLocalSettingAsync("Port", "5900");
                var password = await Settings.GetLocalSettingAsync("Password", defaultValue: string.Empty, isEncrypted: true);
                var isSecure = await Settings.GetLocalSettingAsync("IsSecure", defaultValue: "False");

                this.Invoke(() =>
                {
                    this.Server.Text = server;
                    this.Port.Text = port;
                    this.Password.Password = password;
                    this.IsSecure.IsChecked = isSecure == "True";

                    if (!string.IsNullOrEmpty(server))
                    {
                        this.ConnectButton.Focus(FocusState.Programmatic);
                    }
                });
            });

            var settingsPane = SettingsPane.GetForCurrentView();
            settingsPane.CommandsRequested += this.SettingsPaneCommandsRequested;
        }

        /// <summary>Invoked when this page is about to be displayed in a Frame.</summary>
        /// <param name="e">Event data that describes how this page was reached.  The Parameter
        /// property is typically used to configure the page.</param>
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
        }

        /// <summary>Handles the CommandsRequested event from the Settings pane.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="args">The event arguments.</param>
        private void SettingsPaneCommandsRequested(SettingsPane sender, SettingsPaneCommandsRequestedEventArgs args)
        {
            var commands = args.Request.ApplicationCommands;
            commands.Add(new SettingsCommand("KayboardSettings", "Keyboard Settings", new UICommandInvokedHandler(this.ShowKeyboardSettings)));
        }

        /// <summary>Show the keyboard layout settings popup.</summary>
        /// <param name="command">The parameter is not used.</param>
        private void ShowKeyboardSettings(IUICommand command)
        {
            this.ShowSettingsPopup(346, () => new KeyboardLayoutSettings(this.Keyboard));
        }

        /// <summary>Show a settings popup.</summary>
        /// <param name="width">The width to set for the popup.</param>
        /// <param name="getSettingsPane">A function that will return the control used for the settings pane contents.</param>
        private void ShowSettingsPopup(int width, Func<Control> getSettingsPane)
        {
            this.settingsPopup = new Popup();
            this.settingsPopup.Closed += this.SettingsPopupClosed;
            Window.Current.Activated += this.OnActivatedBySettings;
            this.settingsPopup.IsLightDismissEnabled = true;

            this.settingsPopup.Width = width;
            this.settingsPopup.Height = Window.Current.Bounds.Height;

            this.settingsPopup.ChildTransitions = new TransitionCollection();
            this.settingsPopup.ChildTransitions.Add(new PaneThemeTransition
            {
                Edge = SettingsPane.Edge == SettingsEdgeLocation.Right ?
                EdgeTransitionLocation.Right :
                EdgeTransitionLocation.Left
            });

            var settings = getSettingsPane();
            settings.Width = width;
            settings.Height = Window.Current.Bounds.Height;
            this.settingsPopup.Child = settings;

            this.settingsPopup.SetValue(Canvas.LeftProperty, SettingsPane.Edge == SettingsEdgeLocation.Right ? Window.Current.Bounds.Width - width : 0);
            this.settingsPopup.SetValue(Canvas.TopProperty, 0);

            this.settingsPopup.IsOpen = true;
        }

        /// <summary>Handles a settings pane closing, ensures that focus is correctly captured.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void SettingsPopupClosed(object sender, object e)
        {
            Window.Current.Activated -= this.OnActivatedBySettings;
            FocusCarrier.Focus(FocusState.Programmatic);
        }

        /// <summary>Used to close a settings pane on window deactivation.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void OnActivatedBySettings(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == CoreWindowActivationState.Deactivated)
            {
                this.settingsPopup.IsOpen = false;
            }
        }

        /// <summary>Handles the connect button being tapped.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        /// <remarks>This manages the connect, handshake, initialize flow of the VNC protocol.</remarks>
        private void ClickConnect(object sender, RoutedEventArgs e)
        {
            var server = this.Server.Text;
            var port = this.Port.Text;
            var password = this.Password.Password;
            var isSecure = (bool)this.IsSecure.IsChecked;

            var client = new StreamSocket();

            Task.Run(async () =>
            {
                try
                {
                    var host = new HostName(server);

                    await client.ConnectAsync(host, port, isSecure ? SocketProtectionLevel.Ssl : SocketProtectionLevel.PlainSocket);

                    this.connection = Connection.CreateFromStreamSocket(
                        client,
                        r => this.EnqueueUpdate(() => this.OnRectangle(r)),
                        s => this.EnqueueUpdate(() => OnStateChange(s)),
                        f => this.EnqueueUpdate(() => OnException(f)));

                    var requiresPassword = await this.connection.HandshakeAsync();

                    if (requiresPassword)
                    {
                        if (string.IsNullOrEmpty(password))
                        {
                            password = await this.GetPassword();
                        }

                        await this.connection.SendPasswordAsync(password);
                    }

                    var name = await this.connection.InitializeAsync(shareDesktop: true);

                    var connectionInfo = this.connection.GetConnectionInfo();

                    this.Invoke(() =>
                    {
                        Settings.SetLocalSettingAsync("Server", server);
                        Settings.SetLocalSettingAsync("Port", port);
                        Settings.SetLocalSettingAsync("Password", password, isEncrypted: true);
                        Settings.SetLocalSettingAsync("IsSecure", isSecure ? "True" : "False");

                        this.StartFrameBuffer(connectionInfo);
                    });
                }
                catch (Exception exception)
                {
                    this.Invoke(() =>
                    {
                        this.OnException(exception);
                    });
                }
            });
        }

        /// <summary>If a password is needed and wasn't provided, then this prompts the user to enter a password.</summary>
        /// <returns>The password string entered by the user.</returns>
        private Task<string> GetPassword()
        {
            TaskCompletionSource<string> source = new TaskCompletionSource<string>();

            this.Invoke(() => 
            {
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

        /// <summary>Enqueues an action to be executed on the UI thread.</summary>
        /// <param name="action">The action to be queued.</param>
        private void EnqueueUpdate(Action action)
        {
            this.updateQueue.Enqueue(action);
            if (0 == Interlocked.CompareExchange(ref this.updateInvokePending, 1, 0))
            {
                this.Invoke(this.DrainUpdates);
            }
        }

        /// <summary>Executes the actions currently enqueued for the UI thread.</summary>
        private void DrainUpdates()
        {
            try
            {
                Action action;
                while (this.updateQueue.TryDequeue(out action))
                {
                    action();
                }
            }
            finally
            {
                Interlocked.CompareExchange(ref this.updateInvokePending, 0, 1);
            }
        }

        /// <summary>Dispatches an action to be executed on the UI thread.</summary>
        /// <param name="action">The action to invoke on the UI thread.</param>
        private void Invoke(Action action)
        {
            var ignored = this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => action());
        }

        /// <summary>Draws an update rectangle into the current frame buffer.</summary>
        /// <param name="rectangle">The rectangle to update.</param>
        private void OnRectangle(Rectangle rectangle)
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
                var destOffset = ((y + rectangle.Top) * width * 4) + leftOffset;
                var srcOffset = y * scan;

                src.CopyTo((uint)srcOffset, dest, (uint)destOffset, (uint)scan);
            }

            this.frameBufferBitmap.Invalidate();
        }

        /// <summary>Handles a connection state change. This is mostly concerned with keeping the UI in sync with the current connection state.</summary>
        /// <param name="state">The reported connection state.</param>
        private void OnStateChange(ConnectionState state)
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
                    if (this.isKeyboardVisible)
                    {
                        this.KeyboardExpandoTapped(this, new TappedRoutedEventArgs());
                    }

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

        /// <summary>Logically joins the protocol report of connection with the delivery of the first graphics update
        /// data from the server, this is the best time to update the UI to reflect complete connection.</summary>
        private void FinishConnecting()
        {
            if (this.hasRectangle && this.isConnected)
            {
                this.ConnectPanel.Visibility = Visibility.Collapsed;
                Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Cross, 1);
                this.FocusCarrier.IsEnabled = true;
            }
        }

        /// <summary>Handles an exception raised by the protocol. Causes exception information to be displayed in the UI.</summary>
        /// <param name="exception">The exception in question.</param>
        private void OnException(Exception exception)
        {
            this.ExceptionMessage.Text = exception.Message;
            if (exception.InnerException != null)
            {
                this.InnerExceptionMessage.Text = exception.InnerException.Message;
                this.InnerExceptionMessage.Visibility = Visibility.Visible;
            }
            else
            {
                this.InnerExceptionMessage.Visibility = Visibility.Collapsed;
            }

            this.ExceptionPanel.Visibility = Visibility.Visible;
        }

        /// <summary>Gets the UI ready for receiving image data from the server and asks for the first update.</summary>
        /// <param name="connectionInfo">Information about the connection.</param>
        private void StartFrameBuffer(ConnectionInfo connectionInfo)
        {
            var frameWidth = connectionInfo.Width;
            var frameHeight = connectionInfo.Height;

            if (this.frameBufferBitmap == null ||
                this.frameBufferBitmap.PixelWidth != frameWidth ||
                this.frameBufferBitmap.PixelHeight != frameHeight)
            {
                this.frameBufferBitmap = new WriteableBitmap(frameWidth, frameHeight);
            }

            if (this.zoomBufferBitmap == null)
            {
                this.zoomBufferBitmap = new WriteableBitmap(50, 50);
            }

            this.FrameBuffer.Source = this.frameBufferBitmap;
            this.ZoomBrush.ImageSource = this.zoomBufferBitmap;

            this.connection.StartAsync();
            this.connection.UpdateAsync(true);

            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += (s, e) => this.OnTimer();
            timer.Start();

            this.updateTimer = timer;
        }

        /// <summary>Handles the update timer ticking, if there is a current connection, this asks for an update from the server.</summary>
        private void OnTimer()
        {
            if (this.connection == null)
            {
                this.updateTimer.Stop();
            }
            else
            {
                this.connection.UpdateAsync(false);
            }
        }

        /// <summary>Handles a pointer movement event, currently this handles mouse and touch events.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The event argument containing the information about the pointer.</param>
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
                    this.lastTouchX = (int)sx;
                    this.lastTouchY = (int)sy;
                    var buttons = (this.isLeftButtonSimulated ? 1 : 0) | (this.isRightButtonSimulated ? 4 : 0);
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
                        this.isLeftButtonSimulated = true;
                        var buttons = (this.isLeftButtonSimulated ? 1 : 0) |
                            (this.isRightButtonSimulated ? 4 : 0);
                        this.MovePointer(buttons, this.lastTouchX, this.lastTouchY, ox, oy, isHighPriority: true);
                    }
                    else if (point.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
                    {
                        this.isLeftButtonSimulated = false;
                        var buttons = (this.isLeftButtonSimulated ? 1 : 0) |
                            (this.isRightButtonSimulated ? 4 : 0);
                        this.MovePointer(buttons, this.lastTouchX, this.lastTouchY, ox, oy, isHighPriority: true);
                    }
                }
            }

            e.Handled = true;
        }

        /// <summary>Moves the pointing device, this communicates the movement to the server and updates the zoomed pointer
        /// UI.</summary>
        /// <param name="buttons">The current mouse button state.</param>
        /// <param name="sx">The server x-coordinate.</param>
        /// <param name="sy">The server y-coordinate.</param>
        /// <param name="x">The client x-coordinate.</param>
        /// <param name="y">The client y-coordinate.</param>
        /// <param name="isHighPriority">Indicates whether this is a high priority update (used for mouse up and down)</param>
        private void MovePointer(int buttons, int sx, int sy, double x, double y, bool isHighPriority)
        {
            this.connection.SetPointerAsync(buttons, sx, sy, isHighPriority);

            var cx = this.FrameBuffer.ActualWidth / 2;
            var cy = this.FrameBuffer.ActualHeight / 2;

            var dx = cx - x;
            var dy = cy - y;

            var len = Math.Sqrt((dx * dx) + (dy * dy));
            var scale = 100 / len;

            var ox = x + (dx * scale) - 50;
            var oy = y + (dy * scale) - 50;

            Canvas.SetLeft(this.Zoomer, ox);
            Canvas.SetTop(this.Zoomer, oy);

            var width = this.zoomBufferBitmap.PixelWidth;
            var height = this.zoomBufferBitmap.PixelHeight;
            var left = (int)sx - (width / 2);
            if (left < 0)
            {
                left = 0;
            }
            else if (left + width >= this.frameBufferBitmap.PixelWidth)
            {
                left = this.frameBufferBitmap.PixelWidth - width;
            }

            var top = (int)sy - (height / 2);
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
                var srcOffset = (((top + i) * srcWidth) + left) * 4;
                var destOffset = width * i * 4;
                src.CopyTo((uint)srcOffset, dest, (uint)destOffset, (uint)scan);
            }

            this.zoomBufferBitmap.Invalidate();
        }

        /// <summary>Handles a physical keyboard key event.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The key event data.</param>
        private void HandleKey(object sender, KeyRoutedEventArgs e)
        {
            if (this.connection == null || !this.isConnected)
            {
                return;
            }

            int scancode = (e.KeyStatus.IsExtendedKey ? 0xE000 : 0) | (int)e.KeyStatus.ScanCode;
            
            // for some reason the tab key comes through with a scancode of 0??
            if (e.KeyStatus.ScanCode == 0 &&
                e.Key == VirtualKey.Tab)
            {
                scancode = 0x0F;
            }

            var pressed = !e.KeyStatus.IsKeyReleased;

            VncKey vncKey = this.Keyboard.LookupKey(this.isLeftShiftDown || this.isRightShiftDown, scancode);

            if (vncKey == VncKey.Unknown)
            {
                return;
            }
            else if (vncKey == VncKey.ShiftLeft) 
            {
                this.isLeftShiftDown = pressed;
            }
            else if (vncKey == VncKey.ShiftRight)
            {
                this.isRightShiftDown = pressed;
            }

            e.Handled = true;
            this.connection.SendKeyAsync(pressed, (uint)vncKey, update: true);
        }

        /// <summary>Toggles touch keyboard visibility.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
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

        /// <summary>Handles a key event from the touch keyboard.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The key event data.</param>
        private void KeyboardKeyChange(object sender, KeyEventArgs e)
        {
            if (this.connection == null || !this.isConnected)
            {
                return;
            }

            this.connection.SendKeyAsync(e.IsPressed, (uint)e.Key, true);
            this.FocusCarrier.Focus(FocusState.Programmatic);
        }

        /// <summary>Handles the enter key in the connect panel, used to implement default button behavior.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The key event arguments.</param>
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

        /// <summary>Handles the refresh button being touched.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void ClickRefresh(object sender, RoutedEventArgs e)
        {
            if (this.connection != null && this.isConnected)
            {
                this.connection.UpdateAsync(true);
            }
        }

        /// <summary>Handles the hang-up button being touched.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void ClickHangup(object sender, RoutedEventArgs e)
        {
            if (this.connection != null && this.isConnected)
            {
                this.connection.ShutdownAsync();
            }
        }

        /// <summary>Handles the ok button being touched when an exception is displayed.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void ExceptionOkClicked(object sender, RoutedEventArgs e)
        {
            this.ExceptionPanel.Visibility = Visibility.Collapsed;
            this.OnStateChange(ConnectionState.Disconnected);
            this.connection = null;
        }
    }
}
