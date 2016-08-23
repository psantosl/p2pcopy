using System;
using System.Net.Sockets;
using System.Net;
using System.Text;

namespace p2pcopy
{
    class Program
    {
        static void Main(string[] args)
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));

            // https://gist.github.com/zziuni/3741933

            STUN_Result result = STUN_Client.Query("stun.l.google.com", 19302, socket);
            Console.WriteLine(result.NetType.ToString());

            Console.WriteLine(socket.LocalEndPoint.ToString());

            if (result.NetType != STUN_NetType.UdpBlocked)
            {
                Console.WriteLine("Public endpoint: {0}. Local port: {1}",
                    result.PublicEndPoint.ToString(),
                    socket.LocalEndPoint.ToString());
            }
            else
            {
                Console.WriteLine("No public end point");
            }
        }

        /// <summary>
        /// This enum specifies STUN message type.
        /// </summary>
        public enum STUN_MessageType
        {
            /// <summary>
            /// STUN message is binding request.
            /// </summary>
            BindingRequest = 0x0001,

            /// <summary>
            /// STUN message is binding request response.
            /// </summary>
            BindingResponse = 0x0101,

            /// <summary>
            /// STUN message is binding requesr error response.
            /// </summary>
            BindingErrorResponse = 0x0111,

            /// <summary>
            /// STUN message is "shared secret" request.
            /// </summary>
            SharedSecretRequest = 0x0002,

            /// <summary>
            /// STUN message is "shared secret" request response.
            /// </summary>
            SharedSecretResponse = 0x0102,

            /// <summary>
            /// STUN message is "shared secret" request error response.
            /// </summary>
            SharedSecretErrorResponse = 0x0112,
        }

        /// <summary>
        /// This class implements STUN ERROR-CODE. Defined in RFC 3489 11.2.9.
        /// </summary>
        public class STUN_t_ErrorCode
        {
            private int m_Code = 0;
            private string m_ReasonText = "";

            /// <summary>
            /// Default constructor.
            /// </summary>
            /// <param name="code">Error code.</param>
            /// <param name="reasonText">Reason text.</param>
            public STUN_t_ErrorCode(int code, string reasonText)
            {
                m_Code = code;
                m_ReasonText = reasonText;
            }


            #region Properties Implementation

            /// <summary>
            /// Gets or sets error code.
            /// </summary>
            public int Code
            {
                get { return m_Code; }

                set { m_Code = value; }
            }

            /// <summary>
            /// Gets reason text.
            /// </summary>
            public string ReasonText
            {
                get { return m_ReasonText; }

                set { m_ReasonText = value; }
            }

            #endregion

        }

        /// <summary>
        /// This class implements STUN CHANGE-REQUEST attribute. Defined in RFC 3489 11.2.4.
        /// </summary>
        public class STUN_t_ChangeRequest
        {
            private bool m_ChangeIP = true;
            private bool m_ChangePort = true;

            /// <summary>
            /// Default constructor.
            /// </summary>
            public STUN_t_ChangeRequest()
            {
            }

            /// <summary>
            /// Default constructor.
            /// </summary>
            /// <param name="changeIP">Specifies if STUN server must send response to different IP than request was received.</param>
            /// <param name="changePort">Specifies if STUN server must send response to different port than request was received.</param>
            public STUN_t_ChangeRequest(bool changeIP, bool changePort)
            {
                m_ChangeIP = changeIP;
                m_ChangePort = changePort;
            }


            #region Properties Implementation

            /// <summary>
            /// Gets or sets if STUN server must send response to different IP than request was received.
            /// </summary>
            public bool ChangeIP
            {
                get { return m_ChangeIP; }

                set { m_ChangeIP = value; }
            }

            /// <summary>
            /// Gets or sets if STUN server must send response to different port than request was received.
            /// </summary>
            public bool ChangePort
            {
                get { return m_ChangePort; }

                set { m_ChangePort = value; }
            }

            #endregion

        }

        /// <summary>
        /// Implements STUN message. Defined in RFC 3489.
        /// </summary>
        public class STUN_Message
        {
            #region enum AttributeType

            /// <summary>
            /// Specifies STUN attribute type.
            /// </summary>
            private enum AttributeType
            {
                MappedAddress = 0x0001,
                ResponseAddress = 0x0002,
                ChangeRequest = 0x0003,
                SourceAddress = 0x0004,
                ChangedAddress = 0x0005,
                Username = 0x0006,
                Password = 0x0007,
                MessageIntegrity = 0x0008,
                ErrorCode = 0x0009,
                UnknownAttribute = 0x000A,
                ReflectedFrom = 0x000B,
                XorMappedAddress = 0x8020,
                XorOnly = 0x0021,
                ServerName = 0x8022,
            }

            #endregion

            #region enum IPFamily

            /// <summary>
            /// Specifies IP address family.
            /// </summary>
            private enum IPFamily
            {
                IPv4 = 0x01,
                IPv6 = 0x02,
            }

            #endregion

            private STUN_MessageType m_Type = STUN_MessageType.BindingRequest;
            private Guid m_pTransactionID = Guid.Empty;
            private IPEndPoint m_pMappedAddress = null;
            private IPEndPoint m_pResponseAddress = null;
            private STUN_t_ChangeRequest m_pChangeRequest = null;
            private IPEndPoint m_pSourceAddress = null;
            private IPEndPoint m_pChangedAddress = null;
            private string m_UserName = null;
            private string m_Password = null;
            private STUN_t_ErrorCode m_pErrorCode = null;
            private IPEndPoint m_pReflectedFrom = null;
            private string m_ServerName = null;

            /// <summary>
            /// Default constructor.
            /// </summary>
            public STUN_Message()
            {
                m_pTransactionID = Guid.NewGuid();
            }


            #region method Parse

            /// <summary>
            /// Parses STUN message from raw data packet.
            /// </summary>
            /// <param name="data">Raw STUN message.</param>
            public void Parse(byte[] data)
            {
                /* RFC 3489 11.1.             
                    All STUN messages consist of a 20 byte header:

                    0                   1                   2                   3
                    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                   |      STUN Message Type        |         Message Length        |
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                   |
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                                            Transaction ID
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                                                                                   |
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
              
                   The message length is the count, in bytes, of the size of the
                   message, not including the 20 byte header.
                */

                if (data.Length < 20)
                {
                    throw new ArgumentException("Invalid STUN message value !");
                }

                int offset = 0;

                //--- message header --------------------------------------------------

                // STUN Message Type
                int messageType = (data[offset++] << 8 | data[offset++]);
                if (messageType == (int)STUN_MessageType.BindingErrorResponse)
                {
                    m_Type = STUN_MessageType.BindingErrorResponse;
                }
                else if (messageType == (int)STUN_MessageType.BindingRequest)
                {
                    m_Type = STUN_MessageType.BindingRequest;
                }
                else if (messageType == (int)STUN_MessageType.BindingResponse)
                {
                    m_Type = STUN_MessageType.BindingResponse;
                }
                else if (messageType == (int)STUN_MessageType.SharedSecretErrorResponse)
                {
                    m_Type = STUN_MessageType.SharedSecretErrorResponse;
                }
                else if (messageType == (int)STUN_MessageType.SharedSecretRequest)
                {
                    m_Type = STUN_MessageType.SharedSecretRequest;
                }
                else if (messageType == (int)STUN_MessageType.SharedSecretResponse)
                {
                    m_Type = STUN_MessageType.SharedSecretResponse;
                }
                else
                {
                    throw new ArgumentException("Invalid STUN message type value !");
                }

                // Message Length
                int messageLength = (data[offset++] << 8 | data[offset++]);

                // Transaction ID
                byte[] guid = new byte[16];
                Array.Copy(data, offset, guid, 0, 16);
                m_pTransactionID = new Guid(guid);
                offset += 16;

                //--- Message attributes ---------------------------------------------
                while ((offset - 20) < messageLength)
                {
                    ParseAttribute(data, ref offset);
                }
            }

            #endregion

            #region method ToByteData

            /// <summary>
            /// Converts this to raw STUN packet.
            /// </summary>
            /// <returns>Returns raw STUN packet.</returns>
            public byte[] ToByteData()
            {
                /* RFC 3489 11.1.             
                    All STUN messages consist of a 20 byte header:

                    0                   1                   2                   3
                    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                   |      STUN Message Type        |         Message Length        |
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                   |
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                                            Transaction ID
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                                                                                   |
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
             
                   The message length is the count, in bytes, of the size of the
                   message, not including the 20 byte header.

                */

                // We allocate 512 for header, that should be more than enough.
                byte[] msg = new byte[512];

                int offset = 0;

                //--- message header -------------------------------------

                // STUN Message Type (2 bytes)
                msg[offset++] = (byte)((int)this.Type >> 8);
                msg[offset++] = (byte)((int)this.Type & 0xFF);

                // Message Length (2 bytes) will be assigned at last.
                msg[offset++] = 0;
                msg[offset++] = 0;

                // Transaction ID (16 bytes)
                Array.Copy(m_pTransactionID.ToByteArray(), 0, msg, offset, 16);
                offset += 16;

                //--- Message attributes ------------------------------------

                /* RFC 3489 11.2.
                    After the header are 0 or more attributes.  Each attribute is TLV
                    encoded, with a 16 bit type, 16 bit length, and variable value:

                    0                   1                   2                   3
                    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                   |         Type                  |            Length             |
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                   |                             Value                             ....
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                */

                if (this.MappedAddress != null)
                {
                    StoreEndPoint(AttributeType.MappedAddress, this.MappedAddress, msg, ref offset);
                }
                else if (this.ResponseAddress != null)
                {
                    StoreEndPoint(AttributeType.ResponseAddress, this.ResponseAddress, msg, ref offset);
                }
                else if (this.ChangeRequest != null)
                {
                    /*
                        The CHANGE-REQUEST attribute is used by the client to request that
                        the server use a different address and/or port when sending the
                        response.  The attribute is 32 bits long, although only two bits (A
                        and B) are used:

                         0                   1                   2                   3
                         0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                        |0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 A B 0|
                        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

                        The meaning of the flags is:

                        A: This is the "change IP" flag.  If true, it requests the server
                           to send the Binding Response with a different IP address than the
                           one the Binding Request was received on.

                        B: This is the "change port" flag.  If true, it requests the
                           server to send the Binding Response with a different port than the
                           one the Binding Request was received on.
                    */

                    // Attribute header
                    msg[offset++] = (int)AttributeType.ChangeRequest >> 8;
                    msg[offset++] = (int)AttributeType.ChangeRequest & 0xFF;
                    msg[offset++] = 0;
                    msg[offset++] = 4;

                    msg[offset++] = 0;
                    msg[offset++] = 0;
                    msg[offset++] = 0;
                    msg[offset++] = (byte)(Convert.ToInt32(this.ChangeRequest.ChangeIP) << 2 | Convert.ToInt32(this.ChangeRequest.ChangePort) << 1);
                }
                else if (this.SourceAddress != null)
                {
                    StoreEndPoint(AttributeType.SourceAddress, this.SourceAddress, msg, ref offset);
                }
                else if (this.ChangedAddress != null)
                {
                    StoreEndPoint(AttributeType.ChangedAddress, this.ChangedAddress, msg, ref offset);
                }
                else if (this.UserName != null)
                {
                    byte[] userBytes = Encoding.ASCII.GetBytes(this.UserName);

                    // Attribute header
                    msg[offset++] = (int)AttributeType.Username >> 8;
                    msg[offset++] = (int)AttributeType.Username & 0xFF;
                    msg[offset++] = (byte)(userBytes.Length >> 8);
                    msg[offset++] = (byte)(userBytes.Length & 0xFF);

                    Array.Copy(userBytes, 0, msg, offset, userBytes.Length);
                    offset += userBytes.Length;
                }
                else if (this.Password != null)
                {
                    byte[] userBytes = Encoding.ASCII.GetBytes(this.UserName);

                    // Attribute header
                    msg[offset++] = (int)AttributeType.Password >> 8;
                    msg[offset++] = (int)AttributeType.Password & 0xFF;
                    msg[offset++] = (byte)(userBytes.Length >> 8);
                    msg[offset++] = (byte)(userBytes.Length & 0xFF);

                    Array.Copy(userBytes, 0, msg, offset, userBytes.Length);
                    offset += userBytes.Length;
                }
                else if (this.ErrorCode != null)
                {
                    /* 3489 11.2.9.
                        0                   1                   2                   3
                        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                        |                   0                     |Class|     Number    |
                        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                        |      Reason Phrase (variable)                                ..
                        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    */

                    byte[] reasonBytes = Encoding.ASCII.GetBytes(this.ErrorCode.ReasonText);

                    // Header
                    msg[offset++] = 0;
                    msg[offset++] = (int)AttributeType.ErrorCode;
                    msg[offset++] = 0;
                    msg[offset++] = (byte)(4 + reasonBytes.Length);

                    // Empty
                    msg[offset++] = 0;
                    msg[offset++] = 0;
                    // Class
                    msg[offset++] = (byte)Math.Floor((double)(this.ErrorCode.Code / 100));
                    // Number
                    msg[offset++] = (byte)(this.ErrorCode.Code & 0xFF);
                    // ReasonPhrase
                    Array.Copy(reasonBytes, msg, reasonBytes.Length);
                    offset += reasonBytes.Length;
                }
                else if (this.ReflectedFrom != null)
                {
                    StoreEndPoint(AttributeType.ReflectedFrom, this.ReflectedFrom, msg, ref offset);
                }

                // Update Message Length. NOTE: 20 bytes header not included.
                msg[2] = (byte)((offset - 20) >> 8);
                msg[3] = (byte)((offset - 20) & 0xFF);

                // Make reatval with actual size.
                byte[] retVal = new byte[offset];
                Array.Copy(msg, retVal, retVal.Length);

                return retVal;
            }

            #endregion


            #region method ParseAttribute

            /// <summary>
            /// Parses attribute from data.
            /// </summary>
            /// <param name="data">SIP message data.</param>
            /// <param name="offset">Offset in data.</param>
            private void ParseAttribute(byte[] data, ref int offset)
            {
                /* RFC 3489 11.2.
                    Each attribute is TLV encoded, with a 16 bit type, 16 bit length, and variable value:

                    0                   1                   2                   3
                    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                   |         Type                  |            Length             |
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                   |                             Value                             ....
                   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+                            
                */

                // Type
                AttributeType type = (AttributeType)(data[offset++] << 8 | data[offset++]);

                // Length
                int length = (data[offset++] << 8 | data[offset++]);

                // MAPPED-ADDRESS
                if (type == AttributeType.MappedAddress)
                {
                    m_pMappedAddress = ParseEndPoint(data, ref offset);
                }
                // RESPONSE-ADDRESS
                else if (type == AttributeType.ResponseAddress)
                {
                    m_pResponseAddress = ParseEndPoint(data, ref offset);
                }
                // CHANGE-REQUEST
                else if (type == AttributeType.ChangeRequest)
                {
                    /*
                        The CHANGE-REQUEST attribute is used by the client to request that
                        the server use a different address and/or port when sending the
                        response.  The attribute is 32 bits long, although only two bits (A
                        and B) are used:

                         0                   1                   2                   3
                         0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                        |0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 A B 0|
                        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

                        The meaning of the flags is:

                        A: This is the "change IP" flag.  If true, it requests the server
                           to send the Binding Response with a different IP address than the
                           one the Binding Request was received on.

                        B: This is the "change port" flag.  If true, it requests the
                           server to send the Binding Response with a different port than the
                           one the Binding Request was received on.
                    */

                    // Skip 3 bytes
                    offset += 3;

                    m_pChangeRequest = new STUN_t_ChangeRequest((data[offset] & 4) != 0, (data[offset] & 2) != 0);
                    offset++;
                }
                // SOURCE-ADDRESS
                else if (type == AttributeType.SourceAddress)
                {
                    m_pSourceAddress = ParseEndPoint(data, ref offset);
                }
                // CHANGED-ADDRESS
                else if (type == AttributeType.ChangedAddress)
                {
                    m_pChangedAddress = ParseEndPoint(data, ref offset);
                }
                // USERNAME
                else if (type == AttributeType.Username)
                {
                    m_UserName = Encoding.Default.GetString(data, offset, length);
                    offset += length;
                }
                // PASSWORD
                else if (type == AttributeType.Password)
                {
                    m_Password = Encoding.Default.GetString(data, offset, length);
                    offset += length;
                }
                // MESSAGE-INTEGRITY
                else if (type == AttributeType.MessageIntegrity)
                {
                    offset += length;
                }
                // ERROR-CODE
                else if (type == AttributeType.ErrorCode)
                {
                    /* 3489 11.2.9.
                        0                   1                   2                   3
                        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                        |                   0                     |Class|     Number    |
                        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                        |      Reason Phrase (variable)                                ..
                        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    */

                    int errorCode = (data[offset + 2] & 0x7) * 100 + (data[offset + 3] & 0xFF);

                    m_pErrorCode = new STUN_t_ErrorCode(errorCode, Encoding.Default.GetString(data, offset + 4, length - 4));
                    offset += length;
                }
                // UNKNOWN-ATTRIBUTES
                else if (type == AttributeType.UnknownAttribute)
                {
                    offset += length;
                }
                // REFLECTED-FROM
                else if (type == AttributeType.ReflectedFrom)
                {
                    m_pReflectedFrom = ParseEndPoint(data, ref offset);
                }
                // XorMappedAddress
                // XorOnly
                // ServerName
                else if (type == AttributeType.ServerName)
                {
                    m_ServerName = Encoding.Default.GetString(data, offset, length);
                    offset += length;
                }
                // Unknown
                else
                {
                    offset += length;
                }
            }

            #endregion

            #region method ParseEndPoint

            /// <summary>
            /// Pasrses IP endpoint attribute.
            /// </summary>
            /// <param name="data">STUN message data.</param>
            /// <param name="offset">Offset in data.</param>
            /// <returns>Returns parsed IP end point.</returns>
            private IPEndPoint ParseEndPoint(byte[] data, ref int offset)
            {
                /*
                    It consists of an eight bit address family, and a sixteen bit
                    port, followed by a fixed length value representing the IP address.

                    0                   1                   2                   3
                    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    |x x x x x x x x|    Family     |           Port                |
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    |                             Address                           |
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                */

                // Skip family
                offset++;
                offset++;

                // Port
                int port = (data[offset++] << 8 | data[offset++]);

                // Address
                byte[] ip = new byte[4];
                ip[0] = data[offset++];
                ip[1] = data[offset++];
                ip[2] = data[offset++];
                ip[3] = data[offset++];

                return new IPEndPoint(new IPAddress(ip), port);
            }

            #endregion

            #region method StoreEndPoint

            /// <summary>
            /// Stores ip end point attribute to buffer.
            /// </summary>
            /// <param name="type">Attribute type.</param>
            /// <param name="endPoint">IP end point.</param>
            /// <param name="message">Buffer where to store.</param>
            /// <param name="offset">Offset in buffer.</param>
            private void StoreEndPoint(AttributeType type, IPEndPoint endPoint, byte[] message, ref int offset)
            {
                /*
                    It consists of an eight bit address family, and a sixteen bit
                    port, followed by a fixed length value representing the IP address.

                    0                   1                   2                   3
                    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    |x x x x x x x x|    Family     |           Port                |
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
                    |                             Address                           |
                    +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+             
                */

                // Header
                message[offset++] = (byte)((int)type >> 8);
                message[offset++] = (byte)((int)type & 0xFF);
                message[offset++] = 0;
                message[offset++] = 8;

                // Unused
                message[offset++] = 0;
                // Family
                message[offset++] = (byte)IPFamily.IPv4;
                // Port
                message[offset++] = (byte)(endPoint.Port >> 8);
                message[offset++] = (byte)(endPoint.Port & 0xFF);
                // Address
                byte[] ipBytes = endPoint.Address.GetAddressBytes();
                message[offset++] = ipBytes[0];
                message[offset++] = ipBytes[0];
                message[offset++] = ipBytes[0];
                message[offset++] = ipBytes[0];
            }

            #endregion


            #region Properties Implementation

            /// <summary>
            /// Gets STUN message type.
            /// </summary>
            public STUN_MessageType Type
            {
                get { return m_Type; }

                set { m_Type = value; }
            }

            /// <summary>
            /// Gets transaction ID.
            /// </summary>
            public Guid TransactionID
            {
                get { return m_pTransactionID; }
            }

            /// <summary>
            /// Gets or sets IP end point what was actually connected to STUN server. Returns null if not specified.
            /// </summary>
            public IPEndPoint MappedAddress
            {
                get { return m_pMappedAddress; }

                set { m_pMappedAddress = value; }
            }

            /// <summary>
            /// Gets or sets IP end point where to STUN client likes to receive response.
            /// Value null means not specified.
            /// </summary>
            public IPEndPoint ResponseAddress
            {
                get { return m_pResponseAddress; }

                set { m_pResponseAddress = value; }
            }

            /// <summary>
            /// Gets or sets how and where STUN server must send response back to STUN client.
            /// Value null means not specified.
            /// </summary>
            public STUN_t_ChangeRequest ChangeRequest
            {
                get { return m_pChangeRequest; }

                set { m_pChangeRequest = value; }
            }

            /// <summary>
            /// Gets or sets STUN server IP end point what sent response to STUN client. Value null
            /// means not specified.
            /// </summary>
            public IPEndPoint SourceAddress
            {
                get { return m_pSourceAddress; }

                set { m_pSourceAddress = value; }
            }

            /// <summary>
            /// Gets or sets IP end point where STUN server will send response back to STUN client 
            /// if the "change IP" and "change port" flags had been set in the ChangeRequest.
            /// </summary>
            public IPEndPoint ChangedAddress
            {
                get { return m_pChangedAddress; }

                set { m_pChangedAddress = value; }
            }

            /// <summary>
            /// Gets or sets user name. Value null means not specified.
            /// </summary>          
            public string UserName
            {
                get { return m_UserName; }

                set { m_UserName = value; }
            }

            /// <summary>
            /// Gets or sets password. Value null means not specified.
            /// </summary>
            public string Password
            {
                get { return m_Password; }

                set { m_Password = value; }
            }

            //public MessageIntegrity

            /// <summary>
            /// Gets or sets error info. Returns null if not specified.
            /// </summary>
            public STUN_t_ErrorCode ErrorCode
            {
                get { return m_pErrorCode; }

                set { m_pErrorCode = value; }
            }


            /// <summary>
            /// Gets or sets IP endpoint from which IP end point STUN server got STUN client request.
            /// Value null means not specified.
            /// </summary>
            public IPEndPoint ReflectedFrom
            {
                get { return m_pReflectedFrom; }

                set { m_pReflectedFrom = value; }
            }

            /// <summary>
            /// Gets or sets server name.
            /// </summary>
            public string ServerName
            {
                get { return m_ServerName; }

                set { m_ServerName = value; }
            }

            #endregion

        }

        /// <summary>
        /// This class implements STUN client. Defined in RFC 3489.
        /// </summary>
        /// <example>
        /// <code>
        /// // Create new socket for STUN client.
        /// Socket socket = new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
        /// socket.Bind(new IPEndPoint(IPAddress.Any,0));
        /// 
        /// // Query STUN server
        /// STUN_Result result = STUN_Client.Query("stunserver.org",3478,socket);
        /// if(result.NetType != STUN_NetType.UdpBlocked){
        ///     // UDP blocked or !!!! bad STUN server
        /// }
        /// else{
        ///     IPEndPoint publicEP = result.PublicEndPoint;
        ///     // Do your stuff
        /// }
        /// </code>
        /// </example>
        public class STUN_Client
        {
            #region static method Query

            /// <summary>
            /// Gets NAT info from STUN server.
            /// </summary>
            /// <param name="host">STUN server name or IP.</param>
            /// <param name="port">STUN server port. Default port is 3478.</param>
            /// <param name="socket">UDP socket to use.</param>
            /// <returns>Returns UDP netwrok info.</returns>
            /// <exception cref="Exception">Throws exception if unexpected error happens.</exception>
            public static STUN_Result Query(string host, int port, Socket socket)
            {
                if (host == null)
                {
                    throw new ArgumentNullException("host");
                }
                if (socket == null)
                {
                    throw new ArgumentNullException("socket");
                }
                if (port < 1)
                {
                    throw new ArgumentException("Port value must be >= 1 !");
                }
                if (socket.ProtocolType != ProtocolType.Udp)
                {
                    throw new ArgumentException("Socket must be UDP socket !");
                }

                IPEndPoint remoteEndPoint = new IPEndPoint(System.Net.Dns.GetHostAddresses(host)[0], port);

                socket.ReceiveTimeout = 3000;
                socket.SendTimeout = 3000;

                /*
                    In test I, the client sends a STUN Binding Request to a server, without any flags set in the
                    CHANGE-REQUEST attribute, and without the RESPONSE-ADDRESS attribute. This causes the server 
                    to send the response back to the address and port that the request came from.
            
                    In test II, the client sends a Binding Request with both the "change IP" and "change port" flags
                    from the CHANGE-REQUEST attribute set.  
              
                    In test III, the client sends a Binding Request with only the "change port" flag set.
                          
                                        +--------+
                                        |  Test  |
                                        |   I    |
                                        +--------+
                                             |
                                             |
                                             V
                                            /\              /\
                                         N /  \ Y          /  \ Y             +--------+
                          UDP     <-------/Resp\--------->/ IP \------------->|  Test  |
                          Blocked         \ ?  /          \Same/              |   II   |
                                           \  /            \? /               +--------+
                                            \/              \/                    |
                                                             | N                  |
                                                             |                    V
                                                             V                    /\
                                                         +--------+  Sym.      N /  \
                                                         |  Test  |  UDP    <---/Resp\
                                                         |   II   |  Firewall   \ ?  /
                                                         +--------+              \  /
                                                             |                    \/
                                                             V                     |Y
                                  /\                         /\                    |
                   Symmetric  N  /  \       +--------+   N  /  \                   V
                      NAT  <--- / IP \<-----|  Test  |<--- /Resp\               Open
                                \Same/      |   I    |     \ ?  /               Internet
                                 \? /       +--------+      \  /
                                  \/                         \/
                                  |                           |Y
                                  |                           |
                                  |                           V
                                  |                           Full
                                  |                           Cone
                                  V              /\
                              +--------+        /  \ Y
                              |  Test  |------>/Resp\---->Restricted
                              |   III  |       \ ?  /
                              +--------+        \  /
                                                 \/
                                                  |N
                                                  |       Port
                                                  +------>Restricted

                */

                // Test I
                STUN_Message test1 = new STUN_Message();
                test1.Type = STUN_MessageType.BindingRequest;
                STUN_Message test1response = DoTransaction(test1, socket, remoteEndPoint);

                // UDP blocked.
                if (test1response == null)
                {
                    return new STUN_Result(STUN_NetType.UdpBlocked, null);
                }
                else
                {
                    // Test II
                    STUN_Message test2 = new STUN_Message();
                    test2.Type = STUN_MessageType.BindingRequest;
                    test2.ChangeRequest = new STUN_t_ChangeRequest(true, true);

                    // No NAT.
                    if (socket.LocalEndPoint.Equals(test1response.MappedAddress))
                    {
                        STUN_Message test2Response = DoTransaction(test2, socket, remoteEndPoint);
                        // Open Internet.
                        if (test2Response != null)
                        {
                            return new STUN_Result(STUN_NetType.OpenInternet, test1response.MappedAddress);
                        }
                        // Symmetric UDP firewall.
                        else
                        {
                            return new STUN_Result(STUN_NetType.SymmetricUdpFirewall, test1response.MappedAddress);
                        }
                    }
                    // NAT
                    else
                    {
                        STUN_Message test2Response = DoTransaction(test2, socket, remoteEndPoint);
                        // Full cone NAT.
                        if (test2Response != null)
                        {
                            return new STUN_Result(STUN_NetType.FullCone, test1response.MappedAddress);
                        }
                        else
                        {
                            /*
                                If no response is received, it performs test I again, but this time, does so to 
                                the address and port from the CHANGED-ADDRESS attribute from the response to test I.
                            */

                            // Test I(II)
                            STUN_Message test12 = new STUN_Message();
                            test12.Type = STUN_MessageType.BindingRequest;

                            STUN_Message test12Response = DoTransaction(test12, socket, test1response.ChangedAddress);
                            if (test12Response == null)
                            {
                                throw new Exception("STUN Test I(II) dind't get resonse !");
                            }
                            else
                            {
                                // Symmetric NAT
                                if (!test12Response.MappedAddress.Equals(test1response.MappedAddress))
                                {
                                    return new STUN_Result(STUN_NetType.Symmetric, test1response.MappedAddress);
                                }
                                else
                                {
                                    // Test III
                                    STUN_Message test3 = new STUN_Message();
                                    test3.Type = STUN_MessageType.BindingRequest;
                                    test3.ChangeRequest = new STUN_t_ChangeRequest(false, true);

                                    STUN_Message test3Response = DoTransaction(test3, socket, test1response.ChangedAddress);
                                    // Restricted
                                    if (test3Response != null)
                                    {
                                        return new STUN_Result(STUN_NetType.RestrictedCone, test1response.MappedAddress);
                                    }
                                    // Port restricted
                                    else
                                    {
                                        return new STUN_Result(STUN_NetType.PortRestrictedCone, test1response.MappedAddress);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            #endregion


            #region method GetSharedSecret

            private void GetSharedSecret()
            {
                /*
                    *) Open TLS connection to STUN server.
                    *) Send Shared Secret request.
                */

                /*
                using(SocketEx socket = new SocketEx()){
                    socket.RawSocket.ReceiveTimeout = 5000;
                    socket.RawSocket.SendTimeout = 5000;

                    socket.Connect(host,port);
                    socket.SwitchToSSL_AsClient();                

                    // Send Shared Secret request.
                    STUN_Message sharedSecretRequest = new STUN_Message();
                    sharedSecretRequest.Type = STUN_MessageType.SharedSecretRequest;
                    socket.Write(sharedSecretRequest.ToByteData());
                
                    // TODO: Parse message

                    // We must get  "Shared Secret" or "Shared Secret Error" response.

                    byte[] receiveBuffer = new byte[256];
                    socket.RawSocket.Receive(receiveBuffer);

                    STUN_Message sharedSecretRequestResponse = new STUN_Message();
                    if(sharedSecretRequestResponse.Type == STUN_MessageType.SharedSecretResponse){
                    }
                    // Shared Secret Error or Unknown response, just try again.
                    else{
                        // TODO: Unknown response
                    }
                }*/
            }

            #endregion

            #region method DoTransaction

            /// <summary>
            /// Does STUN transaction. Returns transaction response or null if transaction failed.
            /// </summary>
            /// <param name="request">STUN message.</param>
            /// <param name="socket">Socket to use for send/receive.</param>
            /// <param name="remoteEndPoint">Remote end point.</param>
            /// <returns>Returns transaction response or null if transaction failed.</returns>
            private static STUN_Message DoTransaction(STUN_Message request, Socket socket, IPEndPoint remoteEndPoint)
            {
                byte[] requestBytes = request.ToByteData();
                DateTime startTime = DateTime.Now;
                // We do it only 2 sec and retransmit with 100 ms.
                while (startTime.AddSeconds(2) > DateTime.Now)
                {
                    try
                    {
                        socket.SendTo(requestBytes, remoteEndPoint);

                        // We got response.
                        if (socket.Poll(100, SelectMode.SelectRead))
                        {
                            byte[] receiveBuffer = new byte[512];
                            socket.Receive(receiveBuffer);

                            // Parse message
                            STUN_Message response = new STUN_Message();
                            response.Parse(receiveBuffer);

                            // Check that transaction ID matches or not response what we want.
                            if (request.TransactionID.Equals(response.TransactionID))
                            {
                                return response;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                return null;
            }

            #endregion

        }

        public class STUN_Result
        {
            private STUN_NetType m_NetType = STUN_NetType.OpenInternet;
            private IPEndPoint m_pPublicEndPoint = null;

            /// <summary>
            /// Default constructor.
            /// </summary>
            /// <param name="netType">Specifies UDP network type.</param>
            /// <param name="publicEndPoint">Public IP end point.</param>
            public STUN_Result(STUN_NetType netType, IPEndPoint publicEndPoint)
            {
                m_NetType = netType;
                m_pPublicEndPoint = publicEndPoint;
            }


            #region Properties Implementation

            /// <summary>
            /// Gets UDP network type.
            /// </summary>
            public STUN_NetType NetType
            {
                get { return m_NetType; }
            }

            /// <summary>
            /// Gets public IP end point. This value is null if failed to get network type.
            /// </summary>
            public IPEndPoint PublicEndPoint
            {
                get { return m_pPublicEndPoint; }
            }

            #endregion

        }

        /// <summary>
        /// Specifies UDP network type.
        /// </summary>
        public enum STUN_NetType
        {
            /// <summary>
            /// UDP is always blocked.
            /// </summary>
            UdpBlocked,

            /// <summary>
            /// No NAT, public IP, no firewall.
            /// </summary>
            OpenInternet,

            /// <summary>
            /// No NAT, public IP, but symmetric UDP firewall.
            /// </summary>
            SymmetricUdpFirewall,

            /// <summary>
            /// A full cone NAT is one where all requests from the same internal IP address and port are 
            /// mapped to the same external IP address and port. Furthermore, any external host can send 
            /// a packet to the internal host, by sending a packet to the mapped external address.
            /// </summary>
            FullCone,

            /// <summary>
            /// A restricted cone NAT is one where all requests from the same internal IP address and 
            /// port are mapped to the same external IP address and port. Unlike a full cone NAT, an external
            /// host (with IP address X) can send a packet to the internal host only if the internal host 
            /// had previously sent a packet to IP address X.
            /// </summary>
            RestrictedCone,

            /// <summary>
            /// A port restricted cone NAT is like a restricted cone NAT, but the restriction 
            /// includes port numbers. Specifically, an external host can send a packet, with source IP
            /// address X and source port P, to the internal host only if the internal host had previously 
            /// sent a packet to IP address X and port P.
            /// </summary>
            PortRestrictedCone,

            /// <summary>
            /// A symmetric NAT is one where all requests from the same internal IP address and port, 
            /// to a specific destination IP address and port, are mapped to the same external IP address and
            /// port.  If the same host sends a packet with the same source address and port, but to 
            /// a different destination, a different mapping is used. Furthermore, only the external host that
            /// receives a packet can send a UDP packet back to the internal host.
            /// </summary>
            Symmetric
        }
    }
}
