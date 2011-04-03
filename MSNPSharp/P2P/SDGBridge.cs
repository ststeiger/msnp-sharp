#region
/*
Copyright (c) 2002-2011, Bas Geertsema, Xih Solutions
(http://www.xihsolutions.net), Thiago.Sayao, Pang Wu, Ethem Evlice, Andy Phan.
All rights reserved. http://code.google.com/p/msnp-sharp/

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice,
  this list of conditions and the following disclaimer.
* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.
* Neither the names of Bas Geertsema or Xih Solutions nor the names of its
  contributors may be used to endorse or promote products derived from this
  software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS 'AS IS'
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF
THE POSSIBILITY OF SUCH DAMAGE. 
*/
#endregion

using System;
using System.Text;
using System.Collections.Generic;

namespace MSNPSharp.P2P
{
    using MSNPSharp.Core;

    /// <summary>
    /// SDG Bridge that wraps P2P messages by PipeNo and sends over nameserver.
    /// </summary>
    /// <remarks>
    /// SDG bridge manages switchboards by pipe no. Pipe no indicates open switchboards and
    /// begins with 1 and increments by 1 (per user). You can create 3 pipes for each end point.
    /// 
    /// Each P2PMessage is about 1KB and can be sent multiple packets in SDG bridge.
    /// Bridging-Offsets indicates multiple fragmented p2p packets. For example:
    /// 
    /// Content-Length: 2404
    /// Bridging-Offsets: 0,52,1352
    /// 
    /// This packets contains 3 p2p messages.
    /// The first is 52-0=52 bytes. It is probably ACK/RAK or SYN.
    /// The second is 1352-52=1300 bytes. It is p2p data packet.
    /// The last is 2404-1352=1052 bytes. It can be p2p data packet or BYE packet.
    /// </remarks>
    public class SDGBridge : P2PBridge
    {
        private NSMessageHandler nsHandler;
        private Dictionary<int, P2PMessageSessionEventArgs> p2pAckMessages = new Dictionary<int, P2PMessageSessionEventArgs>();

        public override bool IsOpen
        {
            get
            {
                return ((nsHandler != null) && nsHandler.IsSignedIn);
            }
        }

        public override int MaxDataSize
        {
            get
            {
                return 1024;
            }
        }

        public override bool Synced
        {
            get
            {
                return false;
            }
        }

        public override bool SuitableFor(P2PSession session)
        {
            return true;
        }

        [Obsolete("This is not valid for SDGBridge. See SuitableFor() method.", true)]
        public override Contact Remote
        {
            get
            {
                throw new InvalidOperationException("This is not valid for SDGBridge. See SuitableFor() method.");
            }
        }

        public override void Dispose()
        {
            p2pAckMessages.Clear();

            base.Dispose();
        }

        public SDGBridge(NSMessageHandler nsHandler)
            : base(8)
        {
            this.nsHandler = nsHandler;
        }

        protected override void SendOnePacket(P2PSession session, Contact remote, Guid remoteGuid, P2PMessage p2pMessage)
        {
            string to = ((int)remote.ClientType).ToString() + ":" + remote.Account;
            string from = ((int)nsHandler.Owner.ClientType).ToString() + ":" + nsHandler.Owner.Account;

            MultiMimeMessage mmMessage = new MultiMimeMessage(to, from);
            mmMessage.RoutingHeaders[MIMERoutingHeaders.From][MIMERoutingHeaders.EPID] = NSMessageHandler.MachineGuid.ToString("B").ToLowerInvariant();
            mmMessage.RoutingHeaders[MIMERoutingHeaders.To][MIMERoutingHeaders.EPID] = remoteGuid.ToString("B").ToLowerInvariant();

            mmMessage.RoutingHeaders[MIMERoutingHeaders.ServiceChannel] = "PE";
            mmMessage.RoutingHeaders[MIMERoutingHeaders.Options] = "0";
            mmMessage.ContentKeyVersion = "2.0";

            SLPMessage slpMessage = p2pMessage.IsSLPData ? p2pMessage.InnerMessage as SLPMessage : null;
            if (slpMessage != null &&
                ((slpMessage.ContentType == "application/x-msnmsgr-transreqbody" ||
                  slpMessage.ContentType == "application/x-msnmsgr-transrespbody" ||
                  slpMessage.ContentType == "application/x-msnmsgr-transdestaddrupdate")))
            {
                mmMessage.ContentHeaders[MIMEContentHeaders.MessageType] = MessageTypes.SignalP2P;
                mmMessage.InnerBody = slpMessage.GetBytes(false);
                mmMessage.InnerMessage = slpMessage;
            }
            else
            {
                mmMessage.ContentHeaders[MIMEContentHeaders.ContentType] = "application/x-msnmsgrp2p";
                mmMessage.ContentHeaders[MIMEContentHeaders.ContentTransferEncoding] = "binary";
                mmMessage.ContentHeaders[MIMEContentHeaders.MessageType] = MessageTypes.Data;

                mmMessage.ContentHeaders[MIMEContentHeaders.Pipe] = packageNumber.ToString();
                mmMessage.ContentHeaders[MIMEContentHeaders.BridgingOffsets] = "0";
                mmMessage.InnerBody = p2pMessage.GetBytes(true);
                mmMessage.InnerMessage = p2pMessage;
            }

            NSMessageProcessor nsmp = (NSMessageProcessor)nsHandler.MessageProcessor;
            int transId = nsmp.IncreaseTransactionID();

            p2pAckMessages[transId] = new P2PMessageSessionEventArgs(p2pMessage, session);

            NSMessage sdgPayload = new NSMessage("SDG");
            sdgPayload.TransactionID = transId;
            sdgPayload.InnerMessage = mmMessage;
            nsmp.SendMessage(sdgPayload, sdgPayload.TransactionID);
        }

        internal void FireSendCompleted(int transid)
        {
            if (p2pAckMessages.ContainsKey(transid))
            {
                P2PMessageSessionEventArgs p2pe = p2pAckMessages[transid];
                p2pAckMessages.Remove(transid);
                OnBridgeSent(p2pe);
            }
        }
    }
};
