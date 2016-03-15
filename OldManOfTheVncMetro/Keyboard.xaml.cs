// -----------------------------------------------------------------------------
// <copyright file="Keyboard.xaml.cs" company="Paul C. Roberts">
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
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Threading.Tasks;
    using Windows.Storage;
    using Windows.UI.Core;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;

    /// <summary>Implements a touch screen keyboard</summary>
    public sealed partial class Keyboard : UserControl
    {
        /// <summary>The uri of the asset that contains the list of built-in keyboard layouts.</summary>
        private const string KeyboardLayouts = "ms-appx:///Assets/keyboards.txt";

        /// <summary>The known keyboard layouts.</summary>
        private readonly Dictionary<string, Uri> knownKeyboardLayouts = new Dictionary<string, Uri>();

        /// <summary>The list of currently toggled modifier keys</summary>
        private readonly List<Button> toggledButtons = new List<Button>();

        /// <summary>Indicates whether CapsLock is engaged.</summary>
        private bool isCapsLockEngaged;

        /// <summary>Indicates whether a Shift key is down.</summary>
        private bool isShiftDown;

        /// <summary>Indicates whether an AltGr key is down.</summary>
        private bool isAltGrDown;

        /// <summary>Indicates whether the current keyboard layout uses AltGr logic.</summary>
        private bool usesAltGr;

        /// <summary>The current lookup dictionary for a dead key, this is only populated if the last
        /// key pressed was a dead key - typically used to create accented characters.</summary>
        private Dictionary<char, char> deadKeyLookup = null;

        /// <summary>Used to lookup scan-codes to keys.</summary>
        private Dictionary<int, KeyInfo> allKeys;

        /// <summary>The current keyboard layout name.</summary>
        private string currentLayout;

        /// <summary>Initializes a new instance of the <see cref="Keyboard"/> class.</summary>
        public Keyboard()
        {
            this.InitializeComponent();

            Task.Run(async () => await this.FindKeyboards());
        }

        /// <summary>Raised when a key state changes.</summary>
        public event EventHandler<KeyEventArgs> KeyChange;

        /// <summary>Used to index key definitions for the Shift and AltGr modifier keys.</summary>
        private enum SymbolIndex
        {
            /// <summary>The key's default symbol. Every key has this defined.</summary>
            Normal = 0,

            /// <summary>The key's shifted symbol.</summary>
            Shifted = 1,

            /// <summary>The key's AltGr symbol.</summary>
            AltGr = 2,

            /// <summary>The key's Shifted AltGr symbol.</summary>
            AltGrShifted = 3
        }

        /// <summary>Gets or sets a value indicating whether modifier keys are toggled when pressed.</summary>
        public bool ToggleModifierKeys { get; set; }

        /// <summary>Gets or sets the name of the current keyboard layout.</summary>
        public string CurrentLayout
        {
            get 
            { 
                return this.currentLayout; 
            }

            set 
            {
                Uri uri;

                if (this.knownKeyboardLayouts.TryGetValue(value, out uri))
                {
                    this.currentLayout = value;
                    Style style = this.Resources["KeyButton"] as Style;
                    this.MainGrid.Children.Clear();
                    Task.Run(async () => await this.LoadKeyboard(uri, style));
                }
            }
        }

        /// <summary>Gets the set of available keyboard layouts.</summary>
        public IEnumerable<string> AvailableLayouts
        {
            get
            {
                return this.knownKeyboardLayouts.Keys;
            }
        }
        
        /// <summary>Lookup the key code associated withe a scan-code.</summary>
        /// <param name="isShifted">Indicates whether the shift key is currently pressed.</param>
        /// <param name="scancode">The scan-code to look up.</param>
        /// <returns>The character associated with the scan-code for the current keyboard layout.</returns>
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

        /// <summary>Enumerates all the available keyboards.</summary>
        /// <returns>An async task.</returns>
        private async Task FindKeyboards()
        {
            this.ToggleModifierKeys = "Toggle" == await Settings.GetLocalSettingAsync("KeyboardToggleModifierKeys");
            var opacityString = await Settings.GetLocalSettingAsync("KeyboardOpacity", "50");
            double opacity;
            if (!double.TryParse(opacityString, NumberStyles.Float, CultureInfo.InvariantCulture, out opacity))
            {
                opacity = 50;
            }

            var defaultName = await Settings.GetLocalSettingAsync("KeyboardLayout");
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(KeyboardLayouts));
            using (var stream = await file.OpenStreamForReadAsync())
            {
                var textReader = (TextReader)new StreamReader(stream);
                string line;
                var first = true;
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

                    this.knownKeyboardLayouts[elements[0]] = new Uri(elements[1]);
                }
            }

            this.Invoke(() =>
                {
                    this.CurrentLayout = defaultName;
                    this.Opacity = opacity / 100;
                });
        }

        /// <summary>Load a specific keyboard from its definition file.</summary>
        /// <param name="uri">The location of the keyboard definition file.</param>
        /// <param name="style">The style to use for a keyboard key.</param>
        /// <returns>An async task,</returns>
        private async Task LoadKeyboard(Uri uri, Style style)
        {
            var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
            using (var stream = await file.OpenStreamForReadAsync())
            {
                var textReader = (TextReader)new StreamReader(stream);
                string line;
                Dictionary<int, KeyInfo> allkeys = new Dictionary<int, KeyInfo>();
                var hasAltGrKey = false;
                var readKeys = false;
                var readDeadKeys = false;
                while (null != (line = await textReader.ReadLineAsync()))
                {
                    if (string.IsNullOrWhiteSpace(line) ||
                        line.StartsWith("#"))
                    {
                        continue;
                    }

                    if (line == "[Keys]")
                    {
                        readKeys = true;
                        readDeadKeys = false;
                    }
                    else if (line == "[DeadKeys]")
                    {
                        readKeys = false;
                        readDeadKeys = true;
                    }
                    else if (readKeys)
                    {
                        int row, col, span;
                        var info = this.ReadKeyDefinition(line, out row, out col, out span);
                        if (info == null)
                        {
                            continue;
                        }

                        hasAltGrKey |= info.HasAltGrCode;
                        allkeys[info.Scancode] = info;

                        this.Invoke(() =>
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
                    else if (readDeadKeys)
                    {
                        this.ReadDeadKeyDefinition(line, allkeys);
                    }
                }

                this.allKeys = allkeys;
                this.usesAltGr = hasAltGrKey;
            }
        }

        /// <summary>Reads a key definition from a line in the INI file that defines a keyboard.</summary>
        /// <param name="line">The line in the file to be read.</param>
        /// <param name="row">On success contains the grid row of the keyboard key.</param>
        /// <param name="col">On success contains the grid column of the keyboard key.</param>
        /// <param name="span">On success contains the grid span of the keyboard key.</param>
        /// <returns>The <see cref="KeyInfo"/> with the key symbol definitions.</returns>
        private KeyInfo ReadKeyDefinition(string line, out int row, out int col, out int span)
        {
            row = col = span = 0;
            int equals = line.IndexOf('=');
            if (equals < 1)
            {
                return null;
            }

            var first = line.Substring(0, equals);
            int scancode;
            var elements = line.Substring(equals + 1).Split(',');

            if (!int.TryParse(first, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out scancode) ||
                !int.TryParse(elements[0], out row) ||
                !int.TryParse(elements[1], out col) ||
                !int.TryParse(elements[2], out span))
            {
                return null;
            }

            var offset = 3;
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

            return new KeyInfo(scancode, symbols.ToArray());
        }

        /// <summary>Reads a dead key definition from a keyboard layout INI file.</summary>
        /// <param name="line">The line containing the definition.</param>
        /// <param name="keys">The keys in the current layout</param>
        private void ReadDeadKeyDefinition(string line, Dictionary<int, KeyInfo> keys)
        {
            int equals = line.IndexOf('=');
            if (equals < 1)
            {
                return;
            }

            var first = line.Substring(0, equals);
            var elements = line.Substring(equals + 1).Split(',');

            int scancode;
            int keycode;
            KeyInfo keyInfo;
            if (!int.TryParse(first, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out scancode) ||
                !int.TryParse(elements[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out keycode) ||
                !keys.TryGetValue(scancode, out keyInfo))
            {
                return;
            }

            var offset = 1;
            var lookup = new Dictionary<char, char>();
            while (elements.Length > offset + 1)
            {
                var from = elements[offset++];
                var to = elements[offset++];
                if (from.Length != 1 || to.Length != 1)
                {
                    continue;
                }

                lookup[from[0]] = to[0];
            }

            foreach (var symbol in keyInfo.Symbols)
            {
                if (symbol.Code == (VncKey)keycode)
                {
                    symbol.DeadKeyLookup = lookup;
                    return;
                }
            }
        }

        /// <summary>Invokes an action asynchronously on the UI thread.</summary>
        /// <param name="action">The action to invoke.</param>
        private void Invoke(Action action) 
        {
            var ignored = this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => action());
        }

        /// <summary>Raises the <see cref="KeyChange"/> event.</summary>
        /// <param name="key">The character code of the key whose state has changed.</param>
        /// <param name="isPressed">Indicates if the key is pressed.</param>
        private void RaiseKeyChange(VncKey key, bool isPressed)
        {
            var handler = this.KeyChange;
            if (handler != null)
            {
                handler(this, new KeyEventArgs(key, isPressed));
            }
        }

        /// <summary>Handles a button being pressed.</summary>
        /// <param name="sender">The button that was pressed.</param>
        /// <param name="e">The parameter is not used.</param>
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
            else if (this.isShiftDown ^ this.isCapsLockEngaged)
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
                if (this.deadKeyLookup != null)
                {
                    var from = char.ConvertFromUtf32((int)key.Code)[0];
                    char to;
                    if (this.deadKeyLookup.TryGetValue(from, out to))
                    {
                        info.PressedCode = (VncKey)to;
                    }

                    if (!info.IsModifier)
                    {
                        this.deadKeyLookup = null;
                    }
                }

                this.RaiseKeyChange(info.PressedCode, info.IsPressed);
            }
            else if (key.DeadKeyLookup != null)
            {
                this.deadKeyLookup = key.DeadKeyLookup;
            }

            if (key.Code == VncKey.ShiftLeft ||
                key.Code == VncKey.ShiftRight)
            {
                this.isShiftDown = true;
                this.ShiftButtons(isShifted: this.isShiftDown ^ this.isCapsLockEngaged, isAltGr: this.isAltGrDown & this.usesAltGr);
            }
            else if (key.Code == VncKey.CapsLock)
            {
                this.isCapsLockEngaged = !this.isCapsLockEngaged;
                this.ShiftButtons(isShifted: this.isShiftDown ^ this.isCapsLockEngaged, isAltGr: this.isAltGrDown & this.usesAltGr);
            }
            else if (key.Code == VncKey.AltRight && this.usesAltGr)
            {
                this.isAltGrDown = true;
                this.ShiftButtons(isShifted: this.isShiftDown ^ this.isCapsLockEngaged, isAltGr: this.isAltGrDown & this.usesAltGr);
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

        /// <summary>Sets the button labels to reflect the current status of the Shift and AltGr modifier keys.</summary>
        /// <param name="isShifted">Indicates if the Shift key is pressed.</param>
        /// <param name="isAltGr">Indicates if the AltGr key is pressed.</param>
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

        /// <summary>Handles a button being released or the mouse leaving the button area.</summary>
        /// <param name="sender">The button in question.</param>
        /// <param name="e">The parameter is not used.</param>
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
                this.isShiftDown = false;
                this.ShiftButtons(isShifted: this.isShiftDown ^ this.isCapsLockEngaged, isAltGr: this.isAltGrDown & this.usesAltGr);
            }
            else if (info[SymbolIndex.Normal].Code == VncKey.AltRight && this.usesAltGr)
            {
                this.isAltGrDown = false;
                this.ShiftButtons(isShifted: this.isShiftDown ^ this.isCapsLockEngaged, isAltGr: this.isAltGrDown & this.usesAltGr);
            }
        }

        /// <summary>Used to represent a key definition. Each key may have between 1 and 4 of these.</summary>
        private sealed class KeySymbol
        {
            /// <summary>Initializes a new instance of the <see cref="KeySymbol"/> class.</summary>
            /// <param name="label">The label for this key.</param>
            /// <param name="code">The character code for this key.</param>
            /// <param name="isDead">Indicates whether this is a dead key.</param>
            public KeySymbol(string label, int code, bool isDead = false)
            {
                this.IsDead = isDead;
                this.Label = label;
                this.Code = (VncKey)code;
            }

            /// <summary>Gets a value indicating whether this is a dead key.</summary>
            public bool IsDead { get; private set; }

            /// <summary>Gets the label for this key.</summary>
            public string Label { get; private set; }

            /// <summary>Gets the character code for this key.</summary>
            public VncKey Code { get; private set; }

            /// <summary>Gets or sets the dead key lookup table for this key; if any.</summary>
            public Dictionary<char, char> DeadKeyLookup { get; set; }
        }

        /// <summary>Represents the information about a key.</summary>
        private sealed class KeyInfo
        {
            /// <summary>Initializes a new instance of the <see cref="KeyInfo"/> class.</summary>
            /// <param name="scancode">The scan-code for this key.</param>
            /// <param name="symbols">The symbols associated with this key.</param>
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

            /// <summary>Gets the scan-code for this key.</summary>
            public int Scancode { get; private set; }

            /// <summary>Gets the symbols for this key</summary>
            public KeySymbol[] Symbols { get; private set; }

            /// <summary>Gets a value indicating whether this is a modifier key.</summary>
            public bool IsModifier { get; private set; }

            /// <summary>Gets or sets a value indicating whether this key is currently pressed.</summary>
            public bool IsPressed { get; set; }

            /// <summary>Gets or sets the code that this key generated when it was pressed.</summary>
            public VncKey PressedCode { get; set; }

            /// <summary>Gets a value indicating whether this key has a symbol associated with the Shift modifier being pressed.</summary>
            public bool HasShiftCode
            {
                get
                {
                    return this.Symbols.Length > (int)SymbolIndex.Shifted;
                }
            }

            /// <summary>Gets a value indicating whether this key has a symbol associated with the AltGr modifier being pressed.</summary>
            public bool HasAltGrCode
            {
                get
                {
                    return this.Symbols.Length > (int)SymbolIndex.AltGr;
                }
            }

            /// <summary>Gets a value indicating whether this key has a symbol associated with the AltGr and Shift modifiers being pressed.</summary>
            public bool HasAltGrShiftCode
            {
                get
                {
                    return this.Symbols.Length > (int)SymbolIndex.AltGrShifted;
                }
            }

            /// <summary>Gets the key symbol associated with a key modifier state.</summary>
            /// <param name="index">The key modifier state.</param>
            /// <returns>The associated key symbol.</returns>
            public KeySymbol this[SymbolIndex index]
            {
                get
                {
                    return this.Symbols[(int)index];
                }
            }
        }
    }
}
