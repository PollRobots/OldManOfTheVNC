// -----------------------------------------------------------------------------
// <copyright file="ConnectionDialog.xaml.cs" company="Paul C. Roberts">
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
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Windows;
    using System.Windows.Threading;

#if USING_BONJOUR
    using Bonjour;
#endif

    /// <summary>The dialog used to get connection information.</summary>
    public partial class ConnectionDialog : Window, INotifyPropertyChanged
    {
        /// <summary>The backing store for the server property</summary>
        private string server;

        /// <summary>The backing store for the servers property</summary>
        private ObservableCollection<ServerInfo> servers = new ObservableCollection<ServerInfo>();

        /// <summary>The backing store for the current server property</summary>
        private ServerInfo currentServer;

        /// <summary>The backing store for the port property</summary>
        private int port = 5900;

        /// <summary>The backing store for the password property</summary>
        private string password;

#if USING_BONJOUR
        private DNSSDEventManager eventManager;
        private DNSSDService service;
        private DNSSDService browser;
        private Dictionary<DNSSDService, string> inProgress = new Dictionary<DNSSDService, string>();
#endif

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionDialog"/> class.
        /// </summary>
        public ConnectionDialog()
        {
            this.InitializeComponent();
            
            this.DataContext = this;
            
#if USING_BONJOUR
            this.eventManager = new DNSSDEventManager();

            this.eventManager.ServiceFound += this.ServiceFound;
            this.eventManager.ServiceLost += this.ServiceLost;
            this.eventManager.ServiceResolved += this.ServiceResolved;
            this.eventManager.AddressFound += this.AddressFound;
            
            this.service = new DNSSDService();
            
            this.browser = this.service.Browse(0, 0, RfbType, null, this.eventManager);
#endif
        }

        /// <summary>The property changed event.</summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets or sets the server address.
        /// </summary>
        public string Server
        {
            get
            {
                return this.server;
            }

            set
            {
                this.server = value;
                this.RaisePropertyChanged("Server");
            }
        }

        /// <summary>
        /// Gets or sets the list of servers.
        /// </summary>
        public ObservableCollection<ServerInfo> Servers
        {
            get
            {
                return this.servers;
            }

            set
            {
                this.servers = value;
                this.RaisePropertyChanged("Servers");
            }
        }

        /// <summary>
        /// Gets or sets the currently selected server.
        /// </summary>
        public ServerInfo CurrentServer
        {
            get
            {
                return this.currentServer;
            }

            set
            {
                this.currentServer = value;
                if (value != null)
                {
                    this.Server = value.Address.ToString();
                    this.Port = value.Port;
                }

                this.RaisePropertyChanged("CurrentServer");
            }
        }

        /// <summary>
        /// Gets or sets the Port.
        /// </summary>
        public int Port
        {
            get
            {
                return this.port;
            }

            set
            {
                this.port = value;
                this.RaisePropertyChanged("Port");
            }
        }

        /// <summary>
        /// Gets or sets the password.
        /// </summary>
        public string Password
        {
            get
            {
                return this.password;
            }

            set
            {
                this.password = value;
                this.RaisePropertyChanged("Password");
            }
        }

        /// <summary>Handles the user clicking the refresh button</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void RefreshClick(object sender, RoutedEventArgs e)
        {
#if USING_BONJOUR
            if (this.browser != null)
            {
                this.browser.Stop();
            }
            Servers.Clear();
            this.browser = this.service.Browse(0, 0, RfbType, null, this.eventManager); 
#endif
        }

#if USING_BONJOUR
        void AddressFound(DNSSDService service, DNSSDFlags flags, uint ifIndex, string hostname, DNSSDAddressFamily addressFamily, string address, uint ttl)
        {
            service.Stop();

            DoInvoke(() => SetServerAddress(hostname, address));
        }

        void SetServerAddress(string hostname, string address)
        {
            var matching = default(ServerInfo);

            foreach (var item in Servers)
            {
                if (string.Equals(item.HostName, hostname, StringComparison.InvariantCultureIgnoreCase))
                {
                    matching = item;
                    break;
                }
            }

            if (matching != null)
            {
                Servers.Remove(matching);

                matching.Address = IPAddress.Parse(address);
                Servers.Add(matching);
            }
        }

        void ServiceResolved(DNSSDService resolver, DNSSDFlags flags, uint ifIndex, string fullname, string hostname, ushort port, TXTRecord record)
        {
            var info = new ServerInfo
            {
                Index = (int)ifIndex,
                FullName = fullname,
                HostName = hostname,
                Port = port
            };

            if (this.inProgress.ContainsKey(resolver))
            {
                info.Name = this.inProgress[resolver];

                this.inProgress.Remove(resolver);
            }
            resolver.Stop();

            this.service.GetAddrInfo(
                flags,
                ifIndex,
                DNSSDAddressFamily.kDNSSDAddressFamily_IPv4,
                hostname,
                this.eventManager);

            DoInvoke(() => Servers.Add(info));
        }

        void DoInvoke(Action action)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                action);
        }

        void ServiceLost(DNSSDService browser, DNSSDFlags flags, uint ifIndex, string serviceName, string regtype, string domain)
        {
            DoInvoke(() => RemoveServer(serviceName));
        }

        void RemoveServer(string name)
        {

        }

        void ServiceFound(DNSSDService browser, DNSSDFlags flags, uint ifIndex, string serviceName, string regtype, string domain)
        {
            var resolver = service.Resolve(flags, ifIndex, serviceName, regtype, domain, this.eventManager);

            this.inProgress.Add(resolver, serviceName);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            if (this.browser != null)
            {
                this.browser.Stop();
                this.browser = null;
            }

            if (this.service != null)
            {
                this.service.Stop();
                this.service = null;
            }

            if (this.eventManager != null)
            {
                this.eventManager.ServiceFound -= ServiceFound;
                this.eventManager.ServiceLost -= ServiceLost;
                this.eventManager.ServiceResolved -= ServiceResolved;
            }
        }
#endif

        /// <summary>Notifies the binding system that a property has changed</summary>
        /// <param name="propertyName">The property that has changed.</param>
        private void RaisePropertyChanged(string propertyName)
        {
            var handler = this.PropertyChanged;

            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>Handles the OK button being selected</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void OkClick(object sender, RoutedEventArgs e)
        {
            this.Password = this.PasswordBox.Password;
            DialogResult = true;
        }

        /// <summary>Handles the Cancel button being selected</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void CancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}