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
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Threading.Tasks;
    using PollRobots.OmotVncProtocol;
    using Windows.Storage;
    using Windows.UI.Core;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;

    public sealed partial class Keyboard : UserControl
    {
        private bool isShifted;
        private Dictionary<int, KeyInfo> allKeys;

        public Keyboard()
        {
            this.InitializeComponent();

            Style style = this.Resources["KeyButton"] as Style;
            Task.Run(async () => await LoadKeyboard(new Uri("ms-appx:///Assets/us.csv"), style));
        }

        public VncKey LookupKey(bool isShifted, int scancode)
        {
            KeyInfo info;

            if (!this.allKeys.TryGetValue(scancode, out info))
            {
                return VncKey.Unknown;
            }

            if (isShifted && info.HasShiftCode)
            {
                return info.ShiftCode;
            }

            return info.Code;
        }

        private async Task LoadKeyboard(Uri uri, Style style)
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
            using (var stream = await file.OpenStreamForReadAsync())
            {
                var textReader = (TextReader)new StreamReader(stream);
                string line;
                Dictionary<int, KeyInfo> allkeys = new Dictionary<int, KeyInfo>();
                while (null != (line = await textReader.ReadLineAsync()))
                {
                    if (line.StartsWith("#"))
                    {
                        continue;
                    }

                    var elements = line.Split(',');
                    int scancode;
                    int row, col, span;
                    string label, shiftLabel;
                    int code, shiftCode;

                    if (!int.TryParse(elements[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out scancode) ||
                        !int.TryParse(elements[1], out row) ||
                        !int.TryParse(elements[2], out col) ||
                        !int.TryParse(elements[3], out span) ||
                        !int.TryParse(elements[5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                    {
                        continue;
                    }
                    label = elements[4];
                    if (label == "#comma#")
                    {
                        label = ",";
                    }

                    KeyInfo info;

                    if (elements.Length >= 8 &&
                        !string.IsNullOrWhiteSpace(elements[6]) &&
                        int.TryParse(elements[7], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out shiftCode))
                    {
                        shiftLabel = elements[6];
                        info = new KeyInfo(scancode, label, code, shiftLabel, shiftCode);
                    }
                    else
                    {
                        info = new KeyInfo(scancode, label, code);
                    }

                    allkeys[scancode] = info;

                    Invoke(() =>
                    {
                        var button = new Button();
                        button.Content = label;
                        Grid.SetColumn(button, col);
                        Grid.SetRow(button, row);
                        Grid.SetColumnSpan(button, span);
                        button.Style = style;
                        button.Tag = info;
                        button.IsTabStop = false;

                        button.AddHandler(UIElement.PointerPressedEvent, (PointerEventHandler)this.ButtonPressed, true);
                        button.AddHandler(UIElement.PointerReleasedEvent, (PointerEventHandler)this.ButtonReleased, true);
                        button.AddHandler(UIElement.PointerExitedEvent, (PointerEventHandler)this.ButtonReleased, true);

                        this.MainGrid.Children.Add(button);
                    });
                }

                this.allKeys = allkeys;
            }
        }

        private sealed class KeyInfo
        {
            public KeyInfo(int scancode, string label, int code, string shiftLabel = null, int shiftCode = -1)
            {
                this.Scancode = scancode;
                this.Label = label;
                this.Code = (VncKey)code;
                this.ShiftLabel = shiftLabel;
                this.ShiftCode = (VncKey)shiftCode;
                this.IsPressed = false;
            }

            public int Scancode { get; private set; }
            public string Label { get; private set; }
            public VncKey Code { get; private set; }
            public string ShiftLabel { get; private set; }
            public VncKey ShiftCode { get; private set; }

            public bool IsPressed { get; set; }
            public VncKey PressedCode { get; set; }

            public bool HasShiftCode
            {
                get
                {
                    return this.ShiftCode > 0 && this.ShiftLabel != null;
                }
            }
        }

        private void Invoke(Action action) {
            this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => action());
        }

        public event EventHandler<KeyEventArgs> KeyChange;

        private void RaiseKeyChange(VncKey key, bool isPressed)
        {
            var handler = this.KeyChange;
            if (handler != null)
            {
                handler(this, new KeyEventArgs(key, isPressed));
            }
        }

        private void ButtonPressed(object sender, PointerRoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null)
            {
                return;
            }

            var info = button.Tag as KeyInfo;
            if (info == null)
            {
                return;
            }
            VncKey key;

            if (this.isShifted && info.HasShiftCode) {
                key = info.ShiftCode;
            }
            else{
                key = info.Code;
            }
            this.RaiseKeyChange(key, true);
            info.PressedCode = key;
            info.IsPressed = true;

            if (info.Code == VncKey.ShiftLeft ||
                info.Code == VncKey.ShiftRight)
            {
                ShiftButtons(isShifted: true);
            }
        }

        private void ShiftButtons(bool isShifted)
        {
            this.isShifted = isShifted;
            foreach (var item in this.MainGrid.Children)
            {
                var button = item as Button;
                if (button == null)
                {
                    continue;
                }

                var info = button.Tag as KeyInfo;
                if (info == null || !info.HasShiftCode)
                {
                    continue;
                }

                button.Content = isShifted ? info.ShiftLabel : info.Label;
            }
        }

        private void ButtonReleased(object sender, PointerRoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null)
            {
                return;
            }

            var info = button.Tag as KeyInfo;
            if (info == null ||
                info.IsPressed == false)
            {
                return;
            }

            info.IsPressed = false;
            this.RaiseKeyChange((VncKey)info.PressedCode, false);

            if (info.Code == VncKey.ShiftLeft ||
                info.Code == VncKey.ShiftRight)
            {
                ShiftButtons(isShifted: false);
            }
        }
    }

    public sealed class KeyEventArgs : EventArgs
    {
        public KeyEventArgs(VncKey key, bool isPressed)
        {
            this.Key = key;
            this.IsPressed = isPressed;
        }

        public VncKey Key { get; private set; }
        public bool IsPressed { get; private set; }
    }
}
