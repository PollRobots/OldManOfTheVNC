using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.ApplicationSettings;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The User Control item template is documented at http://go.microsoft.com/fwlink/?LinkId=234236

namespace OldManOfTheVncMetro
{
    public sealed partial class KeyboardLayoutSettings : UserControl
    {
        Keyboard keyboard;

        public KeyboardLayoutSettings(Keyboard appKeyboard)
        {
            this.InitializeComponent();
            this.keyboard = appKeyboard;

            Task.Run(async () =>
            {
                string selectedLayout = await Settings.GetLocalSetting("KeyboardLayout");

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
                });
            });
        }

        private void Invoke(Action action)
        {
            this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => action());
        }

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
                Settings.SetLocalSetting("KeyboardLayout", layout);
            }
        }
    }
}
