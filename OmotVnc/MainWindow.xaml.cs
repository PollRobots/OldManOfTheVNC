// -----------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Paul C. Roberts">
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

namespace OmotVnc
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System.Windows.Media;
    using System.Windows.Media.Animation;
    using System.Windows.Media.Imaging;
    using System.Windows.Threading;
    using PollRobots.OmotVncProtocol;

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        /// <summary>Backing store for the Status property</summary>
        private string status;

        /// <summary>Backing store for the ShowStatus property</summary>
        private Visibility showStatus = Visibility.Collapsed;

        /// <summary>Backing store for the FrameWidth property</summary>
        private int frameWidth = 320;

        /// <summary>Backing store for the FrameHeight property</summary>
        private int frameHeight = 240;

        /// <summary>Backing store for the FrameBuffer property</summary>
        private WriteableBitmap framebuffer;

        /// <summary>Backing store for the ScaleX property</summary>
        private double scaleX = 1.0;

        /// <summary>Backing store for the ScaleY property</summary>
        private double scaleY = 1.0;

        /// <summary>Backing store for the IsConnected property</summary>
        private bool isConnected;

        /// <summary>Backing store for the ScaleToFit property</summary>
        private bool scaleToFit;

        /// <summary>Dialog used to establish connections ettings</summary>
        private ConnectionDialog connectionDialog;
        
        /// <summary>Port used to communicate with protocol service</summary>
        private ConnectionOperations connectionPort;

        /// <summary>The Tcp Client connection to the VNC server</summary>
        private TcpClient client;
        
        /// <summary>The timer used for background frame update requests</summary>
        private DispatcherTimer timer;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        /// <param name="taskQueue">The CCR task queue to use.</param>
        public MainWindow()
        {
            this.isConnected = false;
            
            InitializeComponent();
            
            DataContext = this;
        }

        /// <summary>The property changed event.</summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets or sets the current Status text.
        /// </summary>
        public string Status
        {
            get
            {
                return this.status;
            }
            
            set
            {
                this.ShowStatus = Visibility.Visible;
                this.status = value;
                this.RaisePropertyChanged("Status");
            }
        }

        /// <summary>
        /// Gets or sets the current Frame Width.
        /// </summary>
        public int FrameWidth
        {
            get
            {
                return this.frameWidth;
            }
            
            set
            {
                this.frameWidth = value;
                this.RaisePropertyChanged("FrameWidth");
            }
        }

        /// <summary>
        /// Gets or sets the current Frame Height.
        /// </summary>
        public int FrameHeight
        {
            get
            {
                return this.frameHeight;
            }
            
            set
            {
                this.frameHeight = value;
                this.RaisePropertyChanged("FrameHeight");
            }
        }

        /// <summary>
        /// Gets or sets the Framebuffer.
        /// </summary>
        public WriteableBitmap Framebuffer
        {
            get
            {
                return this.framebuffer;
            }
            
            set
            {
                this.framebuffer = value;
                this.RaisePropertyChanged("Framebuffer");
            }
        }

        /// <summary>
        /// Gets or sets the visibility of the status.
        /// </summary>
        public Visibility ShowStatus
        {
            get
            {
                return this.showStatus;
            }

            set
            {
                this.showStatus = value;
                this.RaisePropertyChanged("ShowStatus");
            }
        }

        /// <summary>
        /// Gets or sets the scale factor in the x-dimension
        /// </summary>
        public double ScaleX
        {
            get
            {
                return this.scaleX;
            }

            set
            {
                this.scaleX = value;
                this.RaisePropertyChanged("ScaleX");
            }
        }

        /// <summary>
        /// Gets or sets the scale factor in the y-dimension
        /// </summary>
        public double ScaleY
        {
            get
            {
                return this.scaleY;
            }
            
            set
            {
                this.scaleY = value;
                this.RaisePropertyChanged("ScaleY");
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether there is a current server
        /// connection.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                return this.isConnected;
            }

            set
            {
                this.isConnected = value;
                this.RaisePropertyChanged("IsConnected");
            }
        }

        /// <summary>Handles the window being closed.</summary>
        /// <param name="e">The parameter is not used.</param>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            this.Disconnect();
        }

        /// <summary>
        /// Handles mouse movement; used to determine when to make the menu 
        /// visible if in full screen mode.
        /// </summary>
        /// <param name="e">The mouse move event arguemtns</param>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (this.WindowStyle != WindowStyle.None)
            {
                return;
            }

            var position = e.GetPosition(this);

            if (position.Y < 5 && this.MainMenu.Visibility == Visibility.Collapsed)
            {
                this.MainMenu.Visibility = Visibility.Visible;
            }
            else if (position.Y > 100 &&
                     this.MainMenu.Visibility == Visibility.Visible &&
                     !this.IsInMenu())
            {
                this.MainMenu.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Handles the user selecting the Connect menu item</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void ConnectClick(object sender, RoutedEventArgs e)
        {
            var server = string.Empty;
            var port = 5900;
            var password = string.Empty;
            
            if (this.connectionDialog != null)
            {
                server = this.connectionDialog.Server;
                port = this.connectionDialog.Port;
                password = this.connectionDialog.Password;
            }
            
            this.connectionDialog = new ConnectionDialog
            {
                Server = server,
                Port = port,
                Password = password
            };
            
            var dialog = this.connectionDialog;
            
            if (false == dialog.ShowDialog())
            {
                return;
            }
            
            this.ShowStatus = Visibility.Visible;
            this.Connect(dialog.Server, dialog.Port, dialog.Password);
        }

        /// <summary>Handles the user selecting the Disconnect menu item.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void DisconnectClick(object sender, RoutedEventArgs e)
        {
            this.Disconnect();
        }

        /// <summary>Handles the user selecting the Exit menu item.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void ExitClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        /// <summary>Handles the user selecting one of the Scale menu items.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The event arguments, used to identify the scale 
        /// required.</param>
        private void SetScaleClick(object sender, RoutedEventArgs e)
        {
            var item = e.Source as MenuItem;
            
            if (item == null)
            {
                return;
            }
            
            var text = item.Header.ToString();
            
            if (text.EndsWith("%"))
            {
                text = text.Substring(0, text.Length - 1);
                
                var percentScale = 0;
                
                if (int.TryParse(text, out percentScale))
                {
                    this.ScaleX = percentScale / 100.0;
                    this.ScaleY = percentScale / 100.0;
                    this.scaleToFit = false;
                    
                    this.Scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    this.Scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                }
            }
        }

        /// <summary>Handles the user selecting the Scale-to-fit menu item.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void SetScaleToFit(object sender, RoutedEventArgs e)
        {
            if (this.FrameHeight == 0 || this.FrameWidth == 0)
            {
                this.ScaleX = 1;
                this.ScaleY = 1;
                this.scaleToFit = false;
                
                Scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                Scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
            else
            {
                this.ScaleX = this.DisplayArea.ActualWidth / this.FrameWidth;
                this.ScaleY = this.DisplayArea.ActualHeight / this.FrameHeight;
                this.scaleToFit = true;
                
                this.Scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                this.Scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
            }
        }

        /// <summary>Handles the user selecting the Scale-to-fit menu item.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void ToggleLocalCursor(object sender, RoutedEventArgs e)
        {
            var current = this.DisplaySurface.Cursor;
            if (current == Cursors.None)
            {
                this.DisplaySurface.Cursor = Cursors.Cross;
            }
            else if (current == Cursors.Cross)
            {
                this.DisplaySurface.Cursor = null;
            }
            else
            {
                this.DisplaySurface.Cursor = Cursors.None;
            }
        }

        /// <summary>Handles the display size changing.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void DisplayAreaSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (this.scaleToFit)
            {
                this.ScaleX = this.DisplayArea.ActualWidth / this.FrameWidth;
                this.ScaleY = this.DisplayArea.ActualHeight / this.FrameHeight;
            }
        }

        /// <summary>Handles the user selecting the FullScreen menu item.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void FullScreenClick(object sender, RoutedEventArgs e)
        {
            if (this.WindowStyle == WindowStyle.None)
            {
                this.WindowStyle = WindowStyle.ThreeDBorderWindow;
                this.WindowState = WindowState.Normal;
                
                this.MainMenu.Visibility = Visibility.Visible;
            }
            else
            {
                this.WindowState = WindowState.Normal;
                this.WindowStyle = WindowStyle.None;
                this.WindowState = WindowState.Maximized;

                this.MainMenu.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Handles the user selecting the Refresh menu item.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void RefreshClick(object sender, RoutedEventArgs e)
        {
            if (this.connectionPort != null)
            {
                this.connectionPort.UpdateAsync(true);
            }
        }

        /// <summary>Disconnect from the server if possible</summary>
        private void Disconnect()
        {
            Task.Run(
                () =>
                {
                    if (this.connectionPort != null)
                    {
                        this.connectionPort.ShutdownAsync().Wait();
                        this.connectionPort = null;
                    }
                });
            
            if (this.client != null &&
                this.client.Connected)
            {
                this.client.Client.Disconnect(true);
                this.client.Close();
            }

            this.IsConnected = false;
        }

        /// <summary>Start the process of connecting to the server.</summary>
        /// <param name="server">The server address</param>
        /// <param name="port">The TCP port number to connect on</param>
        /// <param name="password">The password to use for the connection.</param>
        private void Connect(string server, int port, string password)
        {
            this.client = new TcpClient();

            this.SetStatusText("Connecting to " + server);

            Task.Run(async () =>
                {
                    try
                    {
                        using (var cancellation = new CancellationTokenSource())
                        {
                            cancellation.CancelAfter(TimeSpan.FromSeconds(30));
                            await Task.Run(() => this.client.ConnectAsync(server, port), cancellation.Token);
                        }

                        var stream = this.client.GetStream();

                        this.connectionPort = Connection.CreateFromStream(stream, this.HandleRectangle, this.HandleConnectionState, this.HandleException);
                    }
                    catch (Exception e)
                    {
                        if (e.InnerException != null)
                        {
                            this.SetStatusText("Error connecting: " + e.InnerException.Message);
                        }
                        else
                        {
                            this.SetStatusText("Error connecting: " + e.Message);
                        }
                    }

                    await DoConnect(password);
                });
        }

        /// <summary>Run the VNC protocol connection process</summary>
        /// <param name="password">The password.</param>
        /// <returns>Standard CCR task enumerator</returns>
        private async Task DoConnect(string password)
        {
            bool requiresPassword;
            
            SetStatusText("Handshaking...");

            try
            {
                requiresPassword = await this.connectionPort.HandshakeAsync();
            }
            catch (Exception exception)
            {
                this.SetStatusText("Error handshaking: ", exception);
                return;
            }
            
            if (requiresPassword)
            {
                this.SetStatusText("Sending password...");

                try
                {
                    await this.connectionPort.SendPasswordAsync(password);
                }
                catch (Exception exception)
                {
                    this.SetStatusText("Error sending password: ", exception);
                    return;
                }
            }

            SetStatusText("Initializing...");
            
            var name = default(string);

            try
            {
                name = await this.connectionPort.InitializeAsync(true);
                this.connectionPort.BellEvent += (s, e) => this.DoInvoke(this.HandleBell);
            }
            catch (Exception exception)
            {
                this.SetStatusText("Error initializing: ", exception);
                return;
            }

            try
            {

                var connectionInfo = this.connectionPort.GetConnectionInfo();
                this.StartFramebuffer(connectionInfo);
            }
            catch(Exception exception)
            {
                this.SetStatusText("Error getting Connection info: ", exception);
                return;
            }
            
            this.DoInvoke(() => this.IsConnected = true);
        }

        /// <summary>Handles a change in the connection state as reported by 
        /// the protocol service.</summary>
        /// <param name="state">The new state.</param>
        private void HandleConnectionState(ConnectionState state)
        {
            if (state == ConnectionState.Disconnected)
            {
                this.DoInvoke(
                    () =>
                    {
                        this.SetStatusText("Disconnected (no reason given)");
                
                        this.Disconnect();
                        
                        this.Title = "Old Man of the VNC";
                        if (this.timer != null)
                        {
                            this.timer.Stop();
                            this.timer = null;
                        }
                    });
            }
        }

        /// <summary>Invoke an action on the window dispatcher</summary>
        /// <param name="action">The action to invoke.</param>
        private void DoInvoke(Action action)
        {
            this.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                action);
        }

        /// <summary>Initialize the frame buffer with the reported width and 
        /// height from the server.</summary>
        /// <param name="info">The connection info.</param>
        private void StartFramebuffer(ConnectionInfo info)
        {
            this.DoInvoke(
                () =>
                {
                     this.ShowStatus = Visibility.Collapsed;
                     
                     this.FrameWidth = Math.Max(info.Width, 320);
                     this.FrameHeight = Math.Max(info.Height, 240);
                     
                     this.Framebuffer = new WriteableBitmap(
                         this.FrameWidth,
                         this.FrameHeight,
                         96,
                         96,
                         PixelFormats.Bgr32,
                         null);
                     
                     this.connectionPort.StartAsync();
                     this.connectionPort.UpdateAsync(true);
                     
                     this.Title = info.Name + " - Old Man of the VNC";
                     
                     this.timer = new DispatcherTimer(
                         TimeSpan.FromSeconds(1.0 / 4),
                         DispatcherPriority.Background,
                         OnTimer,
                         Dispatcher);
                     this.timer.Start();
                });
        }

        /// <summary>Handles ticks on the frame update timer.</summary>
        /// <param name="sender">The paramter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void OnTimer(object sender, EventArgs e)
        {
            if (this.connectionPort == null)
            {
                this.timer.Stop();
            }
            else
            {
                this.connectionPort.UpdateAsync(false);
            }
        }

        /// <summary>Handles an update to a rectangle in the frame buffer</summary>
        /// <remarks>This assumes that the rectangle is completely within the
        /// frame buffer dimensions and that the pixel format is BGR32</remarks>
        /// <param name="update">The update information</param>
        private void HandleRectangle(Rectangle update)
        {
            var rect = new Int32Rect(
                update.Left,
                update.Top,
                update.Width,
                update.Height);
            var array = update.Pixels;
                
            this.DoInvoke(() => this.Framebuffer.WritePixels(
                rect,
                array,
                rect.Width * 4,
                0));
        }

        /// <summary>Displays any error message from the protocol</summary>
        /// <param name="exception">The exception raised by the protocol 
        /// service.</param>
        private void HandleException(Exception exception)
        {
            this.SetStatusText("Error from protocol: ", exception);
        }

        private void HandleBell()
        {
            var storyboard = new Storyboard();
            var anim = new DoubleAnimation(0.8, 0.0, new Duration(TimeSpan.FromSeconds(0.5)));
            anim.EasingFunction = new BackEase { EasingMode = EasingMode.EaseIn };
            Storyboard.SetTarget(anim, this.Bell);
            Storyboard.SetTargetProperty(anim, new PropertyPath(FrameworkElement.OpacityProperty));
            storyboard.Children.Add(anim);
            storyboard.Begin();
        }

        /// <summary>Sets the status text with exception information</summary>
        /// <param name="message">The status message prefix</param>
        /// <param name="exception">The exception causing the status update..</param>
        private void SetStatusText(string message, Exception exception)
        {
            if (exception.InnerException != null)
            {
                this.SetStatusText(message, exception.InnerException);
            }
            else
            {
                this.SetStatusText(message + exception.Message);
            }

            this.DoInvoke(this.Disconnect);
        }

        /// <summary>Sets the status text</summary>
        /// <param name="message">The status text to set.</param>
        private void SetStatusText(string message)
        {
            this.DoInvoke(() => this.Status = message);
        }

        /// <summary>Notifies binding system that a property value has changed/</summary>
        /// <param name="propertyName">The property name of the property that
        /// has changed.</param>
        private void RaisePropertyChanged(string propertyName)
        {
            var handler = this.PropertyChanged;

            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>
        /// If the mouse is near the edge of the visible region and scroll bars
        /// are visible, then automatically scroll to the obvious direction
        /// </summary>
        /// <param name="x">The x-coordinate of the mouse</param>
        /// <param name="y">The y-coordinate of the moust</param>
        private void AutoScroll(double x, double y)
        {
            if (Scroller.ComputedHorizontalScrollBarVisibility == Visibility.Visible)
            {
                if (x < Scroller.HorizontalOffset + 10)
                {
                    Scroller.ScrollToHorizontalOffset(
                        Math.Max(0, Scroller.HorizontalOffset - 1));
                }
                else if (x > Scroller.HorizontalOffset + (Scroller.ExtentWidth - Scroller.ScrollableWidth) - 10)
                {
                    Scroller.ScrollToHorizontalOffset(Math.Min(
                        Scroller.HorizontalOffset + 1,
                        Scroller.ScrollableWidth));
                }
            }

            if (Scroller.ComputedVerticalScrollBarVisibility == Visibility.Visible)
            {
                if (y < Scroller.VerticalOffset + 10)
                {
                    Scroller.ScrollToVerticalOffset(
                        Math.Max(0, Scroller.VerticalOffset - 1));
                }
                else if (y > Scroller.VerticalOffset + (Scroller.ExtentHeight - Scroller.ScrollableHeight) - 10)
                {
                    Scroller.ScrollToVerticalOffset(Math.Min(
                        Scroller.VerticalOffset + 1,
                        Scroller.ScrollableHeight));
                }
            }
        }

        /// <summary>Checks if the keyboard focus is on the menu</summary>
        /// <returns><c>true</c> if the menu is focussed.</returns>
        private bool IsInMenu()
        {
            return this.MainMenu.IsKeyboardFocusWithin;
        }

        /// <summary>Handles mouse move events in the display area.</summary>
        /// <remarks>This sends pointer events to the server if there is a 
        /// connection, and the display service is focussed</remarks>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The mouse move event arguments</param>
        private void HandleMouseMove(object sender, MouseEventArgs e)
        {
            if (this.connectionPort == null)
            {
                return;
            }
            
            var point = e.GetPosition(this.DisplaySurface);
            
            this.AutoScroll(point.X, point.Y);
            
            if (this.DisplaySurface.IsFocused == false)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    this.DisplaySurface.Focus();
                    return;
                }
            }
            
            var buttons = (e.LeftButton == MouseButtonState.Pressed ? 1 : 0) |
                (e.MiddleButton == MouseButtonState.Pressed ? 2 : 0) |
                (e.RightButton == MouseButtonState.Pressed ? 4 : 0);
            
            this.connectionPort.SetPointerAsync(buttons, (int)point.X, (int)point.Y);
        }

        /// <summary>Handles key up and down events<summary>
        /// <remarks>This translates the key-code from windows to VNC and
        /// sends the key operation to the server. The Left and Right windows
        /// keys are mapped to left and right meta respectively.</remarks>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The key event arguments.</param>
        private void HandleKey(object sender, KeyEventArgs e)
        {
            if (this.connectionPort == null ||
                e.IsRepeat)
            {
                return;
            }
            
            uint key = 0;
            switch (e.Key)
            {
                case Key.Back:
                    key = (uint)VncKey.BackSpace;
                    break;
                case Key.Tab:
                    key = (uint)VncKey.Tab;
                    break;
                case Key.Return:
                    key = (uint)VncKey.Return;
                    break;
                case Key.Escape:
                    key = (uint)VncKey.Escape;
                    break;
                case Key.Insert:
                    key = (uint)VncKey.Insert;
                    break;
                case Key.Delete:
                    key = (uint)VncKey.Delete;
                    break;
                case Key.Home:
                    key = (uint)VncKey.Home;
                    break;
                case Key.End:
                    key = (uint)VncKey.End;
                    break;
                case Key.PageUp:
                    key = (uint)VncKey.PageUp;
                    break;
                case Key.PageDown:
                    key = (uint)VncKey.PageDown;
                    break;
                case Key.Left:
                    key = (uint)VncKey.Left;
                    break;
                case Key.Up:
                    key = (uint)VncKey.Up;
                    break;
                case Key.Right:
                    key = (uint)VncKey.Right;
                    break;
                case Key.Down:
                    key = (uint)VncKey.Down;
                    break;
                case Key.F1:
                    key = (uint)VncKey.F1;
                    break;
                case Key.F2:
                    key = (uint)VncKey.F2;
                    break;
                case Key.F3:
                    key = (uint)VncKey.F3;
                    break;
                case Key.F4:
                    key = (uint)VncKey.F4;
                    break;
                case Key.F5:
                    key = (uint)VncKey.F5;
                    break;
                case Key.F6:
                    key = (uint)VncKey.F6;
                    break;
                case Key.F7:
                    key = (uint)VncKey.F7;
                    break;
                case Key.F8:
                    key = (uint)VncKey.F8;
                    break;
                case Key.F9:
                    key = (uint)VncKey.F9;
                    break;
                case Key.F10:
                    key = (uint)VncKey.F10;
                    break;
                case Key.F11:
                    key = (uint)VncKey.F11;
                    break;
                case Key.F12:
                    key = (uint)VncKey.F12;
                    break;
                case Key.LeftShift:
                    key = (uint)VncKey.ShiftLeft;
                    break;
                case Key.RightShift:
                    key = (uint)VncKey.ShiftRight;
                    break;
                case Key.LeftCtrl:
                    key = (uint)VncKey.ControlLeft;
                    break;
                case Key.RightCtrl:
                    key = (uint)VncKey.ControlRight;
                    break;
                case Key.LWin:
                    key = (uint)VncKey.MetaLeft;
                    break;
                case Key.RWin:
                    key = (uint)VncKey.MetaRight;
                    break;
                case Key.LeftAlt:
                    key = (uint)VncKey.AltLeft;
                    break;
                case Key.RightAlt:
                    key = (uint)VncKey.AltRight;
                    break;
                default:
                    var modifiers = Keyboard.Modifiers;
                    if (!modifiers.HasFlag(ModifierKeys.Control) &&
                        !modifiers.HasFlag(ModifierKeys.Alt))
                    {
                        return;
                    }

                    key = (uint)TranslateKey(modifiers.HasFlag(ModifierKeys.Shift), e.Key);
                    break;
            }

            e.Handled = true;
            this.connectionPort.SendKeyAsync(e.IsDown, key, true);
            this.connectionPort.UpdateAsync(false);        
        }

        private char TranslateKey(bool isShifted, Key key)
        {
            switch (key)
            {
                case Key.D0: return isShifted ? ')' : '0';
                case Key.D1: return isShifted ? '!' : '1';
                case Key.D2: return isShifted ? '@' : '2';
                case Key.D3: return isShifted ? '#' : '3';
                case Key.D4: return isShifted ? '$' : '4';
                case Key.D5: return isShifted ? '%' : '5';
                case Key.D6: return isShifted ? '^' : '6';
                case Key.D7: return isShifted ? '&' : '7';
                case Key.D8: return isShifted ? '*' : '8';
                case Key.D9: return isShifted ? '(' : '9';

                case Key.NumPad0: return '0';
                case Key.NumPad1: return '1';
                case Key.NumPad2: return '2';
                case Key.NumPad3: return '3';
                case Key.NumPad4: return '4';
                case Key.NumPad5: return '5';
                case Key.NumPad6: return '6';
                case Key.NumPad7: return '7';
                case Key.NumPad8: return '8';
                case Key.NumPad9: return '9';

                case Key.Decimal: return isShifted ? '>' : '.';
                case Key.Divide: return isShifted ? '?' : '/';
                case Key.Space: return ' ';
                case Key.Subtract: return isShifted ? '_' : '-';

                case Key.OemBackslash: return isShifted ? '|' : '\\';
                case Key.OemCloseBrackets: return isShifted ? '}' : ']';
                case Key.OemComma: return isShifted ? '<' : ',';
                case Key.OemMinus: return isShifted ? '_' : '-';
                case Key.OemOpenBrackets: return isShifted ? '{' : '[';
                case Key.OemPeriod:     return isShifted ? '>' : '.';
                case Key.OemPipe:       return isShifted ? '|' : '\\';
                case Key.OemPlus:       return isShifted ? '+' : '=';
                case Key.OemQuestion:   return isShifted ? '?' : '/';
                case Key.OemQuotes:     return isShifted ? '"' : '\'';
                case Key.OemSemicolon:  return isShifted ? ':' : ';';
                case Key.OemTilde:      return isShifted ? '~' : '`';

                case Key.A: return isShifted ? 'A' : 'a';
                case Key.B: return isShifted ? 'B' : 'b';
                case Key.C: return isShifted ? 'C' : 'c';
                case Key.D: return isShifted ? 'D' : 'd';
                case Key.E: return isShifted ? 'E' : 'e';
                case Key.F: return isShifted ? 'F' : 'f';
                case Key.G: return isShifted ? 'G' : 'g';
                case Key.H: return isShifted ? 'H' : 'h';
                case Key.I: return isShifted ? 'I' : 'i';
                case Key.J: return isShifted ? 'J' : 'j';
                case Key.K: return isShifted ? 'K' : 'k';
                case Key.L: return isShifted ? 'L' : 'l';
                case Key.M: return isShifted ? 'M' : 'm';
                case Key.N: return isShifted ? 'N' : 'n';
                case Key.O: return isShifted ? 'O' : 'o';
                case Key.P: return isShifted ? 'P' : 'p';
                case Key.Q: return isShifted ? 'Q' : 'q';
                case Key.R: return isShifted ? 'R' : 'r';
                case Key.S: return isShifted ? 'S' : 's';
                case Key.T: return isShifted ? 'T' : 't';
                case Key.U: return isShifted ? 'U' : 'u';
                case Key.V: return isShifted ? 'V' : 'v';
                case Key.W: return isShifted ? 'W' : 'w';
                case Key.X: return isShifted ? 'X' : 'x';
                case Key.Y: return isShifted ? 'Y' : 'y';
                case Key.Z: return isShifted ? 'Z' : 'z';
                default:
                    return '?';
            }
        }

        /// <summary>Handles text input events, sending a sequence of down, up
        /// messages to the server, followed by a single update.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The text input event.</param>
        private void HandleTextInput(object sender, TextCompositionEventArgs e)
        {
            if (this.connectionPort == null)
            {
                return;
            }

            foreach (var character in e.Text)
            {
                this.connectionPort.SendKeyAsync(true, (uint)character, false);
                this.connectionPort.SendKeyAsync(false, (uint)character, false);
            }

            this.connectionPort.UpdateAsync(false);
        }
    }
}