// -----------------------------------------------------------------------------
// <copyright file="ConnectionOperations.cs" company="Paul C. Roberts">
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

namespace PollRobots.OmotVncProtocol
{
    using System;
    using System.Threading.Tasks;

    /// <summary>The operations port for the <see cref="Connection"/> service.</summary>
    public abstract class AConnectionOperations
    {
        /// <summary>Gets the connection info.</summary>
        /// <returns>The result port for the request.</returns>
        public abstract ConnectionInfo GetConnectionInfo();

        /// <summary>Shutdown the connection.</summary>
        /// <returns>The task.</returns>
        public abstract Task Shutdown();

        /// <summary>Posts a handshake message.</summary>
        /// <returns><c>true</c> if a password is required.</returns>
        public abstract Task<bool> Handshake();

        /// <summary>Sends a password to the server.</summary>
        /// <param name="password">The password to send.</param>
        /// <returns>The result port for the request.</returns>
        public abstract Task SendPassword(string password);

        /// <summary>Initializes the connection</summary>
        /// <param name="shareDesktop">Indicates whether the desktop is being
        /// shared.</param>
        /// <returns>Remote name for the connection.</returns>
        public abstract Task<string> Initialize(bool shareDesktop);

        /// <summary>Starts the active protocol</summary>
        public abstract Task Start();

        /// <summary>Sends an update request.</summary>
        /// <param name="refresh">Indicates whther the entire screen should be
        /// refreshed.</param>
        public abstract Task Update(bool refresh);

        /// <summary>Sends the current pointer position and button state.</summary>
        /// <param name="buttons">The current button state.</param>
        /// <param name="x">The pointer x coordinate.</param>
        /// <param name="y">The pointer y coordinate.</param>
        public abstract Task SetPointer(int buttons, int x, int y, bool isHighPriority = true);

        /// <summary>Sends a keyboard key state change</summary>
        /// <param name="down">Indicates whether the key is down.</param>
        /// <param name="key">The key that changed.</param>
        /// <param name="update">Indicates whether this should trigger an 
        /// update.</param>
        public abstract Task SendKey(bool down, uint key, bool update);

        public event EventHandler BellEvent;

        protected void FireBell()
        {
            var handler = this.BellEvent;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}