// -----------------------------------------------------------------------------
// <copyright file="ServerInfo.cs" company="Paul C. Roberts">
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
    using System.Net;

    /// <summary>Represents a known VNC server on the network</summary>
    public class ServerInfo
    {
        /// <summary>
        /// Gets or sets the servers browser index.
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets the servers full name.
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// Gets or sets the host name of the server.
        /// </summary>
        public string HostName { get; set; }

        /// <summary>
        /// Gets or sets the name of the server.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the IP-Address of the server.
        /// </summary>
        public IPAddress Address { get; set; }

        /// <summary>
        /// Gets or sets the port to use for VNC
        /// </summary>
        public int Port { get; set; }

        /// <summary>Generates a simple string representation of the server
        /// </summary>
        /// <returns>A string with the servers name and an indicator of if
        /// the address is not resolved.</returns>
        public override string ToString()
        {
            if (this.Address == null)
            {
                return this.Name + "*";
            }
            else
            {
                return this.Name;
            }
        }
    }
}