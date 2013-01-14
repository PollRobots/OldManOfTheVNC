// -----------------------------------------------------------------------------
// <copyright file="KeyEventArgs.cs" company="Paul C. Roberts">
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
    using PollRobots.OmotVncProtocol;

    /// <summary>The event argument used by <see cref="Keyboard"/> to raise keyboard events.</summary>
    public sealed class KeyEventArgs : EventArgs
    {
        /// <summary>Initializes a new instance of the <see cref="KeyEventArgs"/> class.</summary>
        /// <param name="key">The key code of the key.</param>
        /// <param name="isPressed">Indicates whether the key is currently pressed.</param>
        public KeyEventArgs(VncKey key, bool isPressed)
        {
            this.Key = key;
            this.IsPressed = isPressed;
        }

        /// <summary>Gets the key code for this key event.</summary>
        public VncKey Key { get; private set; }

        /// <summary>Gets a value indicating whether the key is pressed for this event.</summary>
        public bool IsPressed { get; private set; }
    }
}