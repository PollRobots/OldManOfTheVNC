// -----------------------------------------------------------------------------
// <copyright file="KeyboardLayoutSettings.xaml.cs" company="Paul C. Roberts">
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
    using System.Globalization;
    using System.Threading.Tasks;
    using Windows.UI.ApplicationSettings;
    using Windows.UI.ViewManagement;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Controls.Primitives;

    /// <summary>The keyboard settings widget</summary>
    public sealed partial class KeyboardLayoutSettings : UserControl
    {
        /// <summary>The keyboard control being configured.</summary>
        private Keyboard keyboard;

        /// <summary>Initializes a new instance of the <see cref="KeyboardLayoutSettings"/> class.</summary>
        /// <param name="appKeyboard">The keyboard control being configured.</param>
        public KeyboardLayoutSettings(Keyboard appKeyboard)
        {
            this.InitializeComponent();
            this.keyboard = appKeyboard;

            Task.Run(async () =>
            {
                string selectedLayout = await Settings.GetLocalSettingAsync("KeyboardLayout");
                string opacityString = await Settings.GetLocalSettingAsync("KeyboardOpacity", "50");
                string toggle = await Settings.GetLocalSettingAsync("KeyboardToggleModifierKeys");
                double opacity;
                if (!double.TryParse(opacityString, NumberStyles.Float, CultureInfo.InvariantCulture, out opacity))
                {
                    opacity = 50;
                }

                this.Invoke(() =>
                {
                    if (string.IsNullOrEmpty(selectedLayout))
                    {
                        selectedLayout = this.keyboard.CurrentLayout;
                    }

                    foreach (var item in this.keyboard.AvailableLayouts)
                    {
                        this.CurrentLayout.Items.Add(item);
                        if (item == selectedLayout)
                        {
                            this.CurrentLayout.SelectedItem = item;
                        }
                    }

                    this.KeyboardOpacity.Value = opacity;
                    this.ToggleModifierKeys.IsOn = toggle == "Toggle";
                });
            });
        }

        /// <summary>Runs an action on the UI thread.</summary>
        /// <param name="action">The action to run on the UI thread.</param>
        private void Invoke(Action action)
        {
            var ignored = this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => action());
        }

        /// <summary>Handles the back button being clicked by closing the popup and showing the settings pane.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void BackClicked(object sender, RoutedEventArgs e)
        {
            var popup = this.Parent as Popup;
            if (popup != null)
            {
                popup.IsOpen = false;
            }

            if (ApplicationView.Value != ApplicationViewState.Snapped)
            {
                SettingsPane.Show();
            }
        }

        /// <summary>Handles a change in keyboard layout.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void CurrentLayoutSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var layout = this.CurrentLayout.SelectedItem as string;
            if (layout == null)
            {
                return;
            }

            if (layout != this.keyboard.CurrentLayout)
            {
                this.keyboard.CurrentLayout = layout;
                Settings.SetLocalSettingAsync("KeyboardLayout", layout);
            }
        }

        /// <summary>Handles a change in the keyboard opacity setting.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void KeyboardOpacityValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (this.keyboard != null)
            {
                this.keyboard.Opacity = this.KeyboardOpacity.Value / 100;
                Settings.SetLocalSettingAsync("KeyboardOpacity", this.KeyboardOpacity.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>Handles the toggling of the toggle modifier keys setting.</summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void ToggleModifierKeysToggled(object sender, RoutedEventArgs e)
        {
            if (this.keyboard != null)
            {
                this.keyboard.ToggleModifierKeys = this.ToggleModifierKeys.IsOn;
                Settings.SetLocalSettingAsync("KeyboardToggleModifierKeys", this.ToggleModifierKeys.IsOn ? "Toggle" : "Off");
            }
        }
    }
}
