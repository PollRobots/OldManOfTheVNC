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
        private const string KeyboardLayouts = "ms-appx:///Assets/keyboards.txt";

        private Dictionary<string, Uri> knownKeyboardLayouts = new Dictionary<string, Uri>();

        private bool isCapsLockEngaged;
        private bool isShiftDown;
        private bool isAltGrDown;

        private bool usesAltGr;

        private readonly List<Button> toggledButtons = new List<Button>();

        private Dictionary<int, KeyInfo> allKeys;

        public Keyboard()
        {
            this.InitializeComponent();

            Task.Run(async () => await FindKeyboards());
        }

        private string currentLayout;

        public string CurrentLayout
        {
            get { return this.currentLayout; }
            set 
            {
                Uri uri;

                if (this.knownKeyboardLayouts.TryGetValue(value, out uri))
                {
                    this.currentLayout = value;
                    Style style = this.Resources["KeyButton"] as Style;
                    this.MainGrid.Children.Clear();
                    Task.Run(async () => await LoadKeyboard(uri, style));
                }
            }
        }

        public IEnumerable<string> AvailableLayouts
        {
            get
            {
                return this.knownKeyboardLayouts.Keys;
            }
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
                return info[SymbolIndex.Shifted].Code;
            }

            return info[SymbolIndex.Normal].Code;
        }

        private async Task FindKeyboards()
        {
            this.ToggleModifierKeys = "Toggle" == await Settings.GetLocalSetting("KeyboardToggleModifierKeys");
            var opacityString = await Settings.GetLocalSetting("KeyboardOpacity", "50");
            double opacity;
            if (!double.TryParse(opacityString, NumberStyles.Float, CultureInfo.InvariantCulture, out opacity))
            {
                opacity = 50;
            }

            var defaultName = await Settings.GetLocalSetting("KeyboardLayout");
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(KeyboardLayouts));
            using (var stream = await file.OpenStreamForReadAsync())
            {
                var textReader = (TextReader)new StreamReader(stream);
                string line;
                var first = true;
                Dictionary<string, Uri> layouts = new Dictionary<string, Uri>();
                while (null != (line = await textReader.ReadLineAsync()))
                {
                    if (string.IsNullOrWhiteSpace(line) ||
                        line.StartsWith("#"))
                    {
                        continue;
                    }

                    var elements = line.Split(new[] { ',' }, 2);
                    if (elements.Length != 2)
                    {
                        continue;
                    }

                    if (first == true)
                    {
                        if (string.IsNullOrEmpty(defaultName))
                        {
                            defaultName = elements[0];
                        }

                        first = false;
                    }

                    layouts[elements[0]] = new Uri(elements[1]);
                }

                this.knownKeyboardLayouts = layouts;
            }

            this.Invoke(() =>
                {
                    this.CurrentLayout = defaultName;
                    this.Opacity = opacity / 100;
                });
        }

        private async Task LoadKeyboard(Uri uri, Style style)
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
            using (var stream = await file.OpenStreamForReadAsync())
            {
                var textReader = (TextReader)new StreamReader(stream);
                string line;
                Dictionary<int, KeyInfo> allkeys = new Dictionary<int, KeyInfo>();
                var hasAltGrKey = false;
                while (null != (line = await textReader.ReadLineAsync()))
                {
                    if (string.IsNullOrWhiteSpace(line) ||
                        line.StartsWith("#"))
                    {
                        continue;
                    }

                    var elements = line.Split(',');
                    int scancode;
                    int row, col, span;

                    if (!int.TryParse(elements[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out scancode) ||
                        !int.TryParse(elements[1], out row) ||
                        !int.TryParse(elements[2], out col) ||
                        !int.TryParse(elements[3], out span))
                    {
                        continue;
                    }

                    var offset = 4;
                    var symbols = new List<KeySymbol>();
                    while (elements.Length > offset + 1)
                    {
                        int code;
                        if (!int.TryParse(elements[offset + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code) ||
                            string.IsNullOrEmpty(elements[offset]))
                        {
                            break;
                        }
                        var label = elements[offset];
                        var isDead = false;
                        if (label.StartsWith("#dead#"))
                        {
                            isDead = true;
                            label = label.Substring(6);
                        }
                        if (label == "#comma#")
                        {
                            label = ",";
                        }

                        symbols.Add(new KeySymbol(label, code, isDead));
                        offset += 2;
                    }

                    var info = new KeyInfo(scancode, symbols.ToArray());

                    hasAltGrKey |= info.HasAltGrCode;
                    allkeys[scancode] = info;

                    Invoke(() =>
                    {
                        var button = new Button();
                        button.Content = info[SymbolIndex.Normal].Label;
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
                this.usesAltGr = hasAltGrKey;
            }
        }

        private sealed class KeySymbol
        {
            public KeySymbol(string label, int code, bool isDead = false)
            {
                this.IsDead = isDead;
                this.Label = label;
                this.Code = (VncKey)code;
            }

            public bool IsDead { get; private set; }
            public string Label { get; private set; }
            public VncKey Code { get; private set; }
        }

        private enum SymbolIndex
        {
            Normal = 0,
            Shifted = 1,
            AltGr = 2,
            AltGrShifted = 3
        }

        private sealed class KeyInfo
        {
            public KeyInfo(int scancode, params KeySymbol[] symbols)
            {
                this.Scancode = scancode;
                this.Symbols = symbols;
                this.IsPressed = false;

                var code = symbols[0].Code;
                this.IsModifier = 
                    code == VncKey.ShiftLeft || code == VncKey.ShiftRight ||
                    code == VncKey.ControlLeft || code == VncKey.ControlRight ||
                    code == VncKey.AltLeft || code == VncKey.AltRight ||
                    code == VncKey.MetaLeft || code == VncKey.MetaRight;
            }

            public int Scancode { get; private set; }
            public KeySymbol[] Symbols{ get; private set; }

            public bool IsModifier { get; private set; }
            public bool IsPressed { get; set; }
            public VncKey PressedCode { get; set; }

            public KeySymbol this[SymbolIndex index]
            {
                get
                {
                    return this.Symbols[(int)index];
                }
            }

            public bool HasShiftCode
            {
                get
                {
                    return this.Symbols.Length > (int)SymbolIndex.Shifted;
                }
            }

            public bool HasAltGrCode
            {
                get
                {
                    return this.Symbols.Length > (int)SymbolIndex.AltGr;
                }
            }

            public bool HasAltGrShiftCode
            {
                get
                {
                    return this.Symbols.Length > (int)SymbolIndex.AltGrShifted;
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

            if (this.toggledButtons.Contains(button))
            {
                this.toggledButtons.Remove(button);
                return;
            }

            var info = button.Tag as KeyInfo;
            if (info == null)
            {
                return;
            }

            KeySymbol key;

            if (!info.HasShiftCode)
            {
                key = info[SymbolIndex.Normal];
            }
            else if (this.isAltGrDown && this.isShiftDown)
            {
                if (!info.HasAltGrShiftCode)
                {
                    return;
                }
                key = info[SymbolIndex.AltGrShifted];
            }
            else if (this.isAltGrDown)
            {
                if (!info.HasAltGrCode)
                {
                    return;
                }

                key = info[SymbolIndex.AltGr];
            }
            else if ((this.isShiftDown ^ this.isCapsLockEngaged)) 
            {
                key = info[SymbolIndex.Shifted];
            }
            else
            {
                key = info[SymbolIndex.Normal];
            }

            info.PressedCode = key.Code;
            info.IsPressed = true;
            if (!key.IsDead)
            {
                this.RaiseKeyChange(key.Code, info.IsPressed);
            }

            if (key.Code == VncKey.ShiftLeft ||
                key.Code == VncKey.ShiftRight)
            {
                isShiftDown = true;
                ShiftButtons(isShifted: this.isShiftDown ^ this.isCapsLockEngaged, isAltGr: this.isAltGrDown & this.usesAltGr);
            }
            else if (key.Code == VncKey.CapsLock)
            {
                this.isCapsLockEngaged = !this.isCapsLockEngaged;
                ShiftButtons(isShifted: this.isShiftDown ^ this.isCapsLockEngaged, isAltGr: this.isAltGrDown & this.usesAltGr);
            }
            else if (key.Code == VncKey.AltRight && this.usesAltGr)
            {
                this.isAltGrDown = true;
                ShiftButtons(isShifted: this.isShiftDown ^ this.isCapsLockEngaged, isAltGr: this.isAltGrDown & this.usesAltGr);
            }

            if (this.ToggleModifierKeys && info.IsModifier)
            {
                this.toggledButtons.Add(button);
            }
            else
            {
                while (this.toggledButtons.Count > 0)
                {
                    var end = this.toggledButtons.Count - 1;
                    var untoggle = this.toggledButtons[end];
                    this.toggledButtons.RemoveAt(end);
                    this.ButtonReleased(untoggle, e);
                }
            }
        }

        private void ShiftButtons(bool isShifted, bool isAltGr)
        {
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

                if (isShifted && isAltGr)
                {
                    button.Content = info.HasAltGrShiftCode ? info[SymbolIndex.AltGrShifted].Label : string.Empty;
                }
                else if (isShifted)
                {
                    button.Content = info[SymbolIndex.Shifted].Label;
                }
                else if (isAltGr)
                {
                    button.Content = info.HasAltGrCode ? info[SymbolIndex.AltGr].Label : string.Empty;
                }
                else
                {
                    button.Content = info[SymbolIndex.Normal].Label;
                }
            }
        }

        private void ButtonReleased(object sender, PointerRoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null)
            {
                return;
            }

            if (this.toggledButtons.Contains(button))
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
            if (!this.usesAltGr || info[SymbolIndex.Normal].Code != VncKey.AltRight)
            {
                this.RaiseKeyChange((VncKey)info.PressedCode, false);
            }

            if (info[SymbolIndex.Normal].Code == VncKey.ShiftLeft ||
                info[SymbolIndex.Normal].Code == VncKey.ShiftRight)
            {
                isShiftDown = false;
                ShiftButtons(isShifted: this.isShiftDown ^ this.isCapsLockEngaged, isAltGr: this.isAltGrDown & this.usesAltGr);
            }
            else if (info[SymbolIndex.Normal].Code == VncKey.AltRight && this.usesAltGr)
            {
                this.isAltGrDown = false;
                ShiftButtons(isShifted: this.isShiftDown ^ this.isCapsLockEngaged, isAltGr: this.isAltGrDown & this.usesAltGr);
            }
        }

        public bool ToggleModifierKeys { get; set; }
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
