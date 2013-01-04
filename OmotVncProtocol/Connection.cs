// -----------------------------------------------------------------------------
// <copyright file="Connection.cs" company="Paul C. Roberts">
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
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
#if !NETFX_CORE
    using System.Security.Cryptography;
#endif
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
#if NETFX_CORE
    using Windows.Security.Cryptography;
    using Windows.Security.Cryptography.Core;
    using Windows.Networking.Sockets;
#endif

    /// <summary>The service that manages a connection to a VNC server.</summary>
    public sealed class Connection : AConnectionOperations, IDisposable
    {
        /// <summary>The length of the protocol version packet.</summary>
        private const int ProtocolVersionLength = 12;

        /// <summary>The first 4 characters of the protocol versiopn packet.</summary>
        private const string ProtocolVersionStart = "RFB ";

        /// <summary>The protocoal major version supported.</summary>
        private const int ProtocolMajorVersion = 3;

        /// <summary>The protocol minor version supported.</summary>
        private const int ProtocolMinorVersion = 8;

        /// <summary>The client protocol version packet.</summary>
        private const string ClientProtocolVersion = "RFB 003.008\n";

        /// <summary>The supported security protocols</summary>
        /// <remarks>2 means requires password, 1 means no password required</remarks>
        private static readonly byte[] SecurityProtocols = { 2, 1 };

        /// <summary>The length of the security nonce.</summary>
        private const int SecurityNonceLength = 16;

        /// <summary>The default timeout when talking to a server</summary>
        /// <remarks>THis presupposes that the service is close in network 
        /// terms.</remarks>
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

        /// <summary>Lookup table for reversing nibble values (used as part of
        /// the password encryption)</summary>
        private static readonly byte[] ReverseNibble = { 0x0, 0x8, 0x4, 0xC, 0x2, 0xA, 0x6, 0xE, 0x1, 0x9, 0x5, 0xD, 0x3 , 0xB, 0x7, 0xF };

        /// <summary>Length of the server init header.</summary>
        private const int ServerInitHeaderLength = 24;

        /// <summary>Lock used for exclusive async access to the stream.</summary>
        private ExclusiveLock exclusiveLock = new ExclusiveLock();

        /// <summary>The connection input stream.</summary>
        private Stream readStream;

        /// <summary>The connection output stream.</summary>
        private Stream writeStream;

        /// <summary>The security nonce for the connection.</summary>
        private byte[] nonce;

        /// <summary>The pixel format being used.</summary>
        private PixelFormat pixelFormat;

        /// <summary>The current connection state.</summary>
        private ConnectionState state;

        private Action<Rectangle> onRectangle;

        private Action<ConnectionState> onConnectionStateChange;

        private Action<Exception> onException;

        /// <summary>
        /// Initializes a new instance of the <see cref="Connection"/> class.
        /// </summary>
        /// <param name="stream">The connection stream.</param>
        /// <param name="taskQueue">The CCR task queue for this service</param>
        private Connection(Stream readStream, Stream writeStream, Action<Rectangle> rectangleAction, Action<ConnectionState> connectionStateChangeAction, Action<Exception> exceptionAction)
        {
            this.readStream = readStream;
            this.writeStream = writeStream;
            this.onRectangle = rectangleAction ?? (_ => { });
            this.onConnectionStateChange = connectionStateChangeAction ?? (_ => { });
            this.onException = exceptionAction ?? (_ => { });
        }

        /// <summary>The supported server messages.</summary>
        private enum ServerMessage
        {
            /// <summary>The frame buffer is updated.</summary>
            FramebufferUpdate = 0,

            /// <summary>Update the palette</summary>
            SetColourMapEntries = 1,

            /// <summary>Sound the bell</summary>
            Bell = 2,

            /// <summary>Cut text on the server.</summary>
            ServerCutText = 3
        }

        /// <summary>
        /// Gets a value indicating whether a password is required.
        /// </summary>
        public bool RequiresPassword { get; private set; }

        /// <summary>
        /// Gets the current connection state of this service.
        /// </summary>
        public ConnectionState ConnectionState
        {
            get
            {
                return this.state;
            }

            private set
            {
                if (value != this.state)
                {
                    this.state = value;
                    this.onConnectionStateChange(this.state);
                }
            }
        }

        /// <summary>
        /// Gets the screen Width.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Gets the screen Height.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Gets the connection Name.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the connection is initialized.
        /// </summary>
        public bool Initialized { get; private set; }

#if NETFX_CORE
        /// <summary>Create an instance of the <see cref="Connection"/> service.
        /// </summary>
        /// <param name="stream">The connection stream.</param>
        /// <returns>The port used to communicate with the service.</returns>
        public static AConnectionOperations CreateFromStreamSocket(StreamSocket streamSocket, Action<Rectangle> onRectangle, Action<ConnectionState> onStateChange, Action<Exception> onException)
        {
            var readStream = streamSocket.InputStream.AsStreamForRead();
            var writeStream =streamSocket.OutputStream.AsStreamForWrite();
            var connection = new Connection(readStream, writeStream, onRectangle, onStateChange, onException);

            connection.Init();

            return connection;
        }
#else
        /// <summary>Create an instance of the <see cref="Connection"/> service.
        /// </summary>
        /// <param name="stream">The connection stream.</param>
        /// <returns>The port used to communicate with the service.</returns>
        public static AConnectionOperations CreateFromStream(Stream stream, Action<Rectangle> onRectangle, Action<ConnectionState> onStateChange, Action<Exception> onException)
        {
            var connection = new Connection(stream, stream, onRectangle, onStateChange, onException);

            connection.Init();

            return connection;
        }
#endif

        /// <summary>Implements the Dispose method.</summary>
        public void Dispose()
        {
            using (var old = this.readStream)
            {
                this.readStream = null;
                this.ConnectionState = OmotVncProtocol.ConnectionState.Disconnected;
            }
        }

        /// <summary>Handles the shutdown message.</summary>
        public override async Task Shutdown()
        {
            try
            {
                using (await this.exclusiveLock.Enter())
                {
                    this.Dispose();
                    await Task.Yield();
                }
            }
            catch (Exception e)
            {
                onException(e);
            }
        }

        /// <summary>Handles the start message; this starts the process of
        /// waiting for server packets.</summary>
        public override async Task Start()
        {
            try
            {
                using (await this.exclusiveLock.Enter())
                {
                    if (!this.Initialized)
                    {
                        throw new InvalidOperationException("Unable to start");
                    }
                }
            }
            catch (Exception e)
            {
                onException(e);
                throw;
            }

            await Task.Yield();
            Task.Run(() => this.WaitForServerPacket());
        }

        int pendingUpdateResponse;

        /// <summary>Handles the update message; this sends an update request
        /// to the server.</summary>
        /// <param name="update">The update request.</param>
        public override async Task Update(bool refresh)
        {
            using (await this.exclusiveLock.Enter())
            {
                if (pendingUpdateResponse > 0 && !refresh)
                {
                    return;
                }

                try
                {
                    var packet = new byte[10];

                    packet[0] = 3;
                    packet[1] = (byte)(refresh ? 0 : 1);

                    packet[6] = (byte)((this.Width >> 8) & 0xFF);
                    packet[7] = (byte)(this.Width & 0xFF);
                    packet[8] = (byte)((this.Height >> 8) & 0xFF);
                    packet[9] = (byte)(this.Height & 0xFF);

                    using (var cancellation = new CancellationTokenSource())
                    {
                        cancellation.CancelAfter(DefaultTimeout);
                        Interlocked.Increment(ref pendingUpdateResponse);
                        await this.writeStream.WriteAsync(packet, 0, packet.Length, cancellation.Token);
                        await this.writeStream.FlushAsync();
                    }
                }
                catch (Exception e)
                {
                    this.Disconnected(e);
                }
            }
        }

        DateTime last;

        /// <summary>Handles the set pointer message, this sends the pointer 
        /// position and button state to the server.
        /// </summary>
        /// <remarks>Setting the pointer position also causes an update.
        /// </remarks>
        /// <param name="buttons">The current button state.</param>
        /// <param name="x">The pointer x coordinate.</param>
        /// <param name="y">The pointer y coordinate.</param>
        public override async Task SetPointer(int buttons, int x, int y, bool isHighPriority)
        {
            var now = DateTime.UtcNow;
            if (!isHighPriority && (now - last).TotalMilliseconds < 50)
            {
                return;
            }

            using (await this.exclusiveLock.Enter())
            {
                try
                {
                    var packet = new byte[6];

                    packet[0] = 5;
                    packet[1] = (byte)buttons;
                    packet[2] = (byte)((x >> 8) & 0xFF);
                    packet[3] = (byte)(x & 0xFF);
                    packet[4] = (byte)((y >> 8) & 0xFF);
                    packet[5] = (byte)(y & 0xFF);

                    using (var cancellation = new CancellationTokenSource())
                    {
                        cancellation.CancelAfter(DefaultTimeout);
                        last = now;
                        await this.writeStream.WriteAsync(packet, 0, packet.Length, cancellation.Token);
                        await this.writeStream.FlushAsync();
                    }

                    Task.Run(() => this.Update(false));
                }
                catch (Exception e)
                {
                    this.Disconnected(e);
                }
            }
        }

        /// <summary>Handles the send key message, this sends key state change
        /// data to the server.</summary>
        /// <remarks>If the update flag is set in the message, this triggers an
        /// update.</remarks>
        /// <param name="isDown">Indicates whether the key is down.</param>
        /// <param name="key">The key that changed.</param>
        /// <param name="update">Indicates whether this should trigger an 
        public override async Task SendKey(bool isDown, uint key, bool update)
        {
            using (await this.exclusiveLock.Enter())
            {
                try
                {
                    var packet = new byte[8];

                    packet[0] = 4;
                    packet[1] = (byte)(isDown ? 1 : 0);

                    packet[4] = (byte)((key >> 24) & 0xFF);
                    packet[5] = (byte)((key >> 16) & 0xFF);
                    packet[6] = (byte)((key >> 8) & 0xFF);
                    packet[7] = (byte)(key & 0xFF);

                    await this.writeStream.WriteAsync(packet, 0, packet.Length);
                    await this.writeStream.FlushAsync();
                    if (update)
                    {
                        Task.Run(() => this.Update(false));
                    }
                }
                catch (Exception e)
                {
                    this.Disconnected(e);
                }
            }
        }

        /// <summary>Handles a get connection info message.</summary>
        /// <param name="get">The get connection info message.</param>
        public override ConnectionInfo GetConnectionInfo()
        {
            return new ConnectionInfo
            {
                Width = this.Width,
                Height = this.Height,
                Name = this.Name
            };
        }

        /// <summary>Performs the handshaking processs.</summary>
        /// <param name="resultPort">The port the result is posted to.</param>
        /// <returns>A CCR task enumerator</returns>
        public override async Task<bool> Handshake()
        {
            using (await this.exclusiveLock.Enter())
            {
                var packet = new byte[ProtocolVersionLength];

                if (this.ConnectionState != ConnectionState.Disconnected)
                {
                    throw new InvalidOperationException();
                }

                this.ConnectionState = ConnectionState.Handshaking;

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.CancelAfter(DefaultTimeout);
                    await this.ReadPacketAsync(packet, cancellation.Token);
                }

                var versionString = Encoding.UTF8.GetString(packet, 0, packet.Length);
                if (!versionString.StartsWith(ProtocolVersionStart, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Expecting: " + ProtocolVersionStart);
                }

                int major;
                int minor;
                if (!int.TryParse(versionString.Substring(4, 3), out major)
                    || !int.TryParse(versionString.Substring(8, 3), out minor))
                {
                    throw new InvalidDataException("Cannot parse protocol version");
                }

                if (major < ProtocolMajorVersion || minor < ProtocolMinorVersion)
                {
                    throw new InvalidDataException("Server protocol version is not supported");
                }

                packet = Encoding.UTF8.GetBytes(ClientProtocolVersion);
                await this.writeStream.WriteAsync(packet, 0, packet.Length);
                await this.writeStream.FlushAsync();

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.CancelAfter(DefaultTimeout);
                    await this.ReadPacketAsync(packet, 0, 1, cancellation.Token);
                }

                var numberOfSecurityTypes = packet[0];
                if (numberOfSecurityTypes == 0)
                {
                    await this.ProcessConnectionError("Protocol version not supported");
                    return true;
                }

                Debug.Assert(numberOfSecurityTypes > 0, "Number of security types:" + numberOfSecurityTypes);

                packet = new byte[numberOfSecurityTypes];

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.CancelAfter(DefaultTimeout);
                    await this.ReadPacketAsync(packet, cancellation.Token);
                }

                var found = 0;
                for (var i = 0; i < SecurityProtocols.Length && found == 0; i++)
                {
                    var supported = SecurityProtocols[i];

                    if (packet.Any(suggested => supported == suggested))
                    {
                        found = supported;
                    }
                }

                if (found == 0)
                {
                    throw new InvalidDataException("Unable to negotiate a security protocol");
                }

                this.writeStream.WriteByte((byte)found);
                await this.writeStream.FlushAsync();

                switch (found)
                {
                    case 1:
                        this.RequiresPassword = false;
                        break;
                    case 2:
                        this.RequiresPassword = true;
                        break;
                    default:
                        throw new InvalidOperationException();
                }

                return this.RequiresPassword;
            }
        }

        /// <summary>Send password to the server</summary>
        /// <param name="password">The password to send</param>
        /// <param name="resultPort">The port that results are posted back to.</param>
        /// <returns>A CCR task enumerator</returns>
        public override async Task SendPassword(string password)
        {
            using (await this.exclusiveLock.Enter())
            {
                if (this.ConnectionState != ConnectionState.Handshaking || this.RequiresPassword == false)
                {
                    throw new InvalidOperationException();
                }

                if (string.IsNullOrEmpty(password))
                {
                    throw new ArgumentNullException("password");
                }

                this.ConnectionState = ConnectionState.SendingPassword;

                this.nonce = new byte[SecurityNonceLength];

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.CancelAfter(DefaultTimeout);
                    await this.ReadPacketAsync(this.nonce, cancellation.Token);
                }

#if NETFX_CORE
                var des = SymmetricKeyAlgorithmProvider.OpenAlgorithm("DES_ECB");
                var key = new byte[des.BlockLength];

                Encoding.UTF8.GetBytes(password, 0, Math.Min(password.Length, key.Length), key, 0);
                ReverseByteArrayElements(key);

                var cryptoKey = des.CreateSymmetricKey(CryptographicBuffer.CreateFromByteArray(key));
                var encryptedResponse = CryptographicEngine.Encrypt(cryptoKey, CryptographicBuffer.CreateFromByteArray(this.nonce), null);

                byte[] response;
                CryptographicBuffer.CopyToByteArray(encryptedResponse, out response);
#else
                var des = DES.Create();
                var key = new byte[des.KeySize / 8];

                Encoding.ASCII.GetBytes(password, 0, Math.Min(password.Length, key.Length), key, 0);
                ReverseByteArrayElements(key);

                des.Key = key;

                des.Mode = CipherMode.ECB;
                var encryptor = des.CreateEncryptor();

                var response = new byte[this.nonce.Length];

                encryptor.TransformBlock(this.nonce, 0, this.nonce.Length, response, 0);
#endif
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.CancelAfter(DefaultTimeout);
                    await this.writeStream.WriteAsync(response, 0, response.Length, cancellation.Token);
                    await this.writeStream.FlushAsync();
                }

                var packet = new byte[4];

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.CancelAfter(DefaultTimeout);
                    await this.ReadPacketAsync(packet, cancellation.Token);
                }

                var securityResult = ConvertBigEndianU32(packet);

                if (securityResult != 0)
                {
                    await this.ProcessConnectionError("Invalid password");
                    return;
                }
            }
        }

        /// <summary>Initialize the connection.</summary>
        /// <param name="shareDesktop">Indicates whether the desktop is shared.</param>
        /// <param name="resultPort">The port that results are posted back to.</param>
        /// <returns>A CCR task enumerator</returns>
        public override async Task<string> Initialize(bool shareDesktop)
        {
            using (await this.exclusiveLock.Enter())
            {
                if ((this.RequiresPassword && this.ConnectionState != ConnectionState.SendingPassword)
                    || (this.RequiresPassword == false && this.ConnectionState != ConnectionState.Handshaking))
                {
                    throw new InvalidOperationException();
                }

                this.ConnectionState = ConnectionState.Initializing;

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.CancelAfter(DefaultTimeout);
                    var share = new byte[] { (byte)(shareDesktop ? 1 : 0) };
                    await this.writeStream.WriteAsync(share, 0, 1, cancellation.Token);
                    await this.writeStream.FlushAsync();
                }

                var packet = new byte[ServerInitHeaderLength];

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.CancelAfter(DefaultTimeout);
                    await this.ReadPacketAsync(packet, cancellation.Token);
                }

                this.Width = (packet[0] << 8) | packet[1];
                this.Height = (packet[2] << 8) | packet[3];

                this.pixelFormat = PixelFormat.FromServerInit(packet);

                var nameLength = ConvertBigEndianU32(packet, 20);

                packet = new byte[nameLength];

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.CancelAfter(DefaultTimeout);
                    this.ReadPacketAsync(packet, cancellation.Token);
                }

                this.Name = Encoding.UTF8.GetString(packet, 0, packet.Length);

                this.Initialized = true;

                this.ConnectionState = ConnectionState.Connected;
                return this.Name;
            }
        }

        /// <summary>Reverse the order of the bits in the elements of a byte 
        /// array</summary>
        /// <param name="input">The byte array to modify, this is modified in 
        /// place</param>
        private static void ReverseByteArrayElements(byte[] input)
        {
            for (var i = 0; i < input.Length; i++)
            {
                input[i] = ReverseBits(input[i]);
            }
        }

        /// <summary>Reverse the bits in a byte</summary>
        /// <param name="input">The input byte</param>
        /// <returns>A byte with bits in the reverse order to the input</returns>
        private static byte ReverseBits(byte input)
        {
            var high = (input >> 4) & 0x0F;
            var low = input & 0x0F;

            return (byte)((ReverseNibble[low] << 4) | ReverseNibble[high]);
        }

        /// <summary>Converts a big endian byte sequence to a 32-bit 
        /// integer.</summary>
        /// <param name="buffer">The byte array containing the sequence</param>
        /// <param name="offset">The offset at which the integer starts.</param>
        /// <returns>The integer representation</returns>
        private static int ConvertBigEndianU32(byte[] buffer, int offset = 0)
        {
            return buffer[offset] << 24 | buffer[offset + 1] << 16 | buffer[offset + 2] << 8 | buffer[offset + 3];
        }

        /// <summary>Initializes the message receivers</summary>
        private void Init()
        {
        }

        /// <summary>Waits for a server packet, reads the packet and dispatches
        /// it appropriately.</summary>
        /// <returns>A CCR task enumerator</returns>
        private async Task WaitForServerPacket()
        {
            for (;;)
            {
                var packet = new byte[1];

                var read = await this.readStream.ReadAsync(packet, 0, packet.Length);
                if (read != packet.Length)
                {
                    this.RaiseProtocolException("Unable to read packet id", new InvalidDataException());
                }

                var message = (ServerMessage)packet[0];

                switch (message)
                {
                    case ServerMessage.FramebufferUpdate:
                        await this.ReadFramebufferUpdate();
                        Interlocked.Decrement(ref pendingUpdateResponse);
                        break;

                    case ServerMessage.SetColourMapEntries:
                        this.SetColourMapEntries();
                        break;

                    case ServerMessage.Bell:
                        this.Bell();
                        break;

                    case ServerMessage.ServerCutText:
                        this.ServerCutText();
                        break;
                }
            }
        }

        /// <summary>Reads an update to the frame buffer from the server.</summary>
        /// <returns>A CCR task enumerator</returns>
        private async Task ReadFramebufferUpdate()
        {
            try
            {
                var packet = new byte[3];

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.CancelAfter(DefaultTimeout);
                    await this.ReadPacketAsync(packet, cancellation.Token);
                }

                var count = (packet[1] << 8) | packet[2];

                for (var i = 0; i < count; i++)
                {
                    await this.ReadRectangle();
                }
            }
            catch (Exception e)
            {
                Disconnected(e);
            }
        }

        private async Task ReadPacketAsync(byte[] buffer, CancellationToken cancelToken)
        {
            await this.ReadPacketAsync(buffer, 0, buffer.Length, cancelToken);
        }

        private async Task ReadPacketAsync(byte[] buffer, int offset, int length,CancellationToken cancelToken)
        {
            var remaining = length;
            while (remaining > 0)
            {
                var read = await this.readStream.ReadAsync(buffer, offset, remaining, cancelToken);
                if (read <= 0)
                {
                    throw new InvalidDataException();
                }

                remaining -= read;
                offset += read;
            }
        }

        /// <summary>Read an update rectangle from the server.</summary>
        /// <returns>A CCR task enumerator</returns>
        private async Task ReadRectangle()
        {
            var packet = new byte[12];

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.CancelAfter(DefaultTimeout);
                await this.ReadPacketAsync(packet, cancellation.Token);
            }

            var left = (packet[0] << 8) | packet[1];
            var top = (packet[2] << 8) | packet[3];
            var width = (packet[4] << 8) | packet[5];
            var height = (packet[6] << 8) | packet[7];

            var encoding = ConvertBigEndianU32(packet, 8);

            if (encoding != 0)
            {
                throw new InvalidDataException("Unexpected encoding");
            }

            byte[] pixels;
            var size = width * height;

            switch (this.pixelFormat.BitsPerPixel)
            {
                case 8:
                    pixels = new byte[size];
                    break;
                case 16:
                    pixels = new byte[size * 2];
                    break;
                case 32:
                    pixels = new byte[size * 4];
                    break;
                default:
                    throw new InvalidDataException();
            }

            var rectangle = new Rectangle(left, top, width, height, pixels);

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.CancelAfter(DefaultTimeout);
                await this.ReadPacketAsync(pixels, cancellation.Token);
            }

            this.onRectangle(rectangle);
        }

        /// <summary>Setting the palette is not implemented.</summary>
        private void SetColourMapEntries()
        {
            this.RaiseProtocolException("SetColourMapEntries", new NotImplementedException());
        }

        /// <summary>Sounding the bell is not implemented.</summary>
        private void Bell()
        {
            this.FireBell();
        }

        /// <summary>Handling server cut text operations is not implemented</summary>
        private void ServerCutText()
        {
            this.RaiseProtocolException("ServerCutText", new NotImplementedException());
        }

        /// <summary>Called when an exception occurs that disconnects the 
        /// stream.</summary>
        /// <param name="exception">The exception that reports the 
        /// disconnection.</param>
        private void Disconnected(Exception exception)
        {
            this.state = ConnectionState.Disconnected;
            this.onException(exception);
        }

        /// <summary>Handles a connection error reported from the server.</summary>
        /// <param name="reason">The error reason.</param>
        /// <returns>A CCR task enumerator</returns>
        private async Task ProcessConnectionError(string reason)
        {
            var packet = new byte[4];

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.CancelAfter(DefaultTimeout);
                await this.ReadPacketAsync(packet, cancellation.Token);
            }

            var length = ConvertBigEndianU32(packet);

            length = Math.Min(Math.Max(1024, length), 0);

            Exception exception;
            if (length > 0)
            {
                packet = new byte[length];

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.CancelAfter(DefaultTimeout);
                    await this.ReadPacketAsync(packet, cancellation.Token);
                }

                exception = new Exception(reason + Encoding.UTF8.GetString(packet, 0, packet.Length));
            }
            else
            {
                exception = new Exception(reason);
            }

            this.Disconnected(exception);
            throw exception;
        }

        /// <summary>Called to process unhandled exception in the protocol.</summary>
        /// <param name="message">The reason for the exception.</param>
        /// <param name="exception">The exception causing the disconnection.</param>
        private void RaiseProtocolException(string message, Exception exception)
        {
            this.Disconnected(new Exception(message, exception));
        }
    }
}