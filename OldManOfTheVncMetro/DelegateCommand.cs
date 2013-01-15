// -----------------------------------------------------------------------------
// <copyright file="DelegateCommand.cs" company="Paul C. Roberts">
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
    using System.Windows.Input;

    /// <summary>Encapsulates a delegate as an ICommand</summary>
    /// <remarks>Often used to bind an action to a button.</remarks>
    internal sealed class DelegateCommand : ICommand
    {
        /// <summary>The delegate for the command's execute action.</summary>
        private readonly Action execute;

        /// <summary>The delegate for the predicate used to determine if the command can execute.</summary>
        private readonly Func<bool> canExecute;

        /// <summary>Initializes a new instance of the <see cref="DelegateCommand"/> class.</summary>
        /// <param name="action">The delegate for the command's execute action.</param>
        /// <param name="predicate">The delegate for the predicate used to determine if the command can execute.</param>
        public DelegateCommand(Action action, Func<bool> predicate)
        {
            this.execute = action;
            this.canExecute = predicate;
        }

        /// <summary>Initializes a new instance of the <see cref="DelegateCommand"/> class.</summary>
        /// <param name="action">The delegate for the command's execute action.</param>
        public DelegateCommand(Action action) :
            this(action, () => true)
        {
        }

        /// <summary>Raised when the predicate condition for the command may have changed.</summary>
        public event EventHandler CanExecuteChanged;

        /// <summary>Determines whether the command can be executed.</summary>
        /// <param name="parameter">The parameter is ignored.</param>
        /// <returns><c>true</c> if the command can be executed; <c>false</c> otherwise.</returns>
        public bool CanExecute(object parameter)
        {
            return this.canExecute();
        }

        /// <summary>Executes the command.</summary>
        /// <param name="parameter">The parameter is ignored.</param>
        public void Execute(object parameter)
        {
            this.execute();
        }

        /// <summary>Raises the <see cref="CanExecuteChanged"/> event to inform consumers that
        /// conditions on the command may have changed.</summary>
        public void RaiseCanExecuteChanged()
        {
            var handler = this.CanExecuteChanged;

            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
