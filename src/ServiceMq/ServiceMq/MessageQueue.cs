﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ServiceWire;
using ServiceWire.NamedPipes;
using ServiceWire.TcpIp;

namespace ServiceMq
{
    public class MessageQueue : IDisposable
    {
        private readonly Address address;
        private readonly NpEndPoint npEndPoint = null;
        private readonly IPEndPoint ipEndPoint = null;
        private readonly string msgDir = null;
        private readonly string name = null;
        private readonly OutboundQueue outboundQueue = null;
        private readonly InboundQueue inboundQueue = null;
        private readonly IMessageService messageService = null;
        private readonly NpHost npHost = null;
        private readonly TcpHost tcpHost = null;

        public MessageQueue(string name, Address address, string msgDir = null, ILog log = null, IStats stats = null)
        {
            this.name = name;
            this.address = address;
            this.msgDir = msgDir ?? GetExecutablePathDirectory();
            Directory.CreateDirectory(this.msgDir);

            //create inbound and outbound queues
            this.outboundQueue = new OutboundQueue(this.name, this.msgDir);
            this.inboundQueue = new InboundQueue(this.name, this.msgDir);

            //create message service singleton
            this.messageService = new MessageService(this.inboundQueue);

            //create and open hosts
            if (this.address.Transport == Transport.Both || this.address.Transport == Transport.Tcp)
            {
                this.ipEndPoint = new IPEndPoint(IPAddress.Parse(this.address.IpAddress), this.address.Port);
                this.tcpHost = new TcpHost(this.ipEndPoint, log, stats);
                this.tcpHost.AddService<IMessageService>(this.messageService);
                this.tcpHost.Open();
            }

            if (this.address.Transport == Transport.Both || this.address.Transport == Transport.Np)
            {
                this.npEndPoint = new NpEndPoint(this.address.ServerName, this.address.PipeName);
                this.npHost = new NpHost(this.npEndPoint.PipeName, log, stats);
                this.npHost.AddService<IMessageService>(this.messageService);
                this.npHost.Open();
            }
        }

        public Guid Send<T>(Address dest, T message)
        {
            var addr = GetOptimalAddress(dest);
            string msg = SvcStkTxt.TypeSerializer.SerializeToString(message);
            return SendMsg(msg, typeof(T).FullName, addr);
        }

        public Guid Send(Address dest, string messageType, string message)
        {
            var addr = GetOptimalAddress(dest);
            return SendMsg(message, messageType, addr);
        }

        public Guid SendBytes(Address dest, byte[] message, string messageType)
        {
            var addr = GetOptimalAddress(dest);
            return SendMsg(message, messageType, addr);
        }

        private Guid SendMsg(string msg, string messageType, Address dest)
        {
            var message = new OutboundMessage()
            {
                From = this.address,
                To = dest,
                Id = Guid.NewGuid(),
                MessageString = msg,
                MessageTypeName = messageType,
                Sent = DateTime.Now
            };
            this.outboundQueue.Enqueue(message);
            return message.Id;
        }

        private Guid SendMsg(byte[] msg, string messageType, Address dest)
        {
            var message = new OutboundMessage()
            {
                From = this.address,
                To = dest,
                Id = Guid.NewGuid(),
                MessageBytes = msg,
                MessageTypeName = messageType,
                Sent = DateTime.Now
            };
            this.outboundQueue.Enqueue(message);
            return message.Id;
        } 

        /// <summary>
        /// Get one message in order received and removes it from the inbox and logs it to the read log. 
        /// Blocking if timeoutMs = -1.
        /// </summary>
        /// <param name="timeoutMs">Specify milliseconds timeout. Returns null if timed out.</param>
        /// <returns></returns>
        public Message Receive(int timeoutMs = -1)
        {
            return this.inboundQueue.Receive(timeoutMs);
        }

        /// <summary>
        /// Get one message in order received without removing it from the inbox. Blocking if timeoutMs = -1.
        /// If Acknowledge is not called, the message will remain in the inbox and be queued again when
        /// the MessageQueue is next constructed.
        /// </summary>
        /// <param name="timeoutMs">Specify milliseconds timeout. Returns null if timed out.</param>
        /// <returns></returns>
        public Message Accept(int timeoutMs = -1)
        {
            return this.inboundQueue.Receive(timeoutMs, logRead: false);
        }

        /// <summary>
        /// Signal message handled to be deleted from inbox and logged to read log.
        /// </summary>
        /// <param name="message"></param>
        public void Acknowledge(Message message)
        {
            this.inboundQueue.Acknowledge(message);
        }

        /// <summary>
        /// Signal message could not be handled at this time. Adds it back into the 
        /// in-process queue out of order. This allows reprocessing after processing
        /// the current queued messages.
        /// </summary>
        /// <param name="message"></param>
        public void ReEnqueue(Message message)
        {
            this.inboundQueue.ReEnqueue(message);
        }

        private Address GetOptimalAddress(Address dest)
        {
            bool chooseTcp = (dest.Transport == Transport.Both 
                                && this.address.ServerName != dest.ServerName);
            if (chooseTcp || dest.Transport == Transport.Tcp)
            {
                if (null == this.ipEndPoint) throw new ArgumentException("Cannot send to a IP endpoint if queue does not have an IP endpoint.", "destEndPoint");
                return new Address(dest.ServerName, dest.Port);
            }
            else
            {
                if (null == this.npEndPoint) throw new ArgumentException("Cannot send to a named pipe endpoint if queue does not have named pipe endpoint.", "destEndPoint");
                return new Address(dest.PipeName);
            }
        }
        
        private string GetExecutablePathDirectory()
        {
            return Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "msg");
        }


        #region IDisposable 

        private bool _disposed = false;

        public void Dispose()
        {
            //MS recommended dispose pattern - prevents GC from disposing again
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true; //prevent second cleanup
                if (disposing)
                {
                    //cleanup here
                    this.outboundQueue.Stop();
                    if (null != npHost) npHost.Dispose();
                    if (null != tcpHost) tcpHost.Dispose();
                }
            }
        }

        #endregion
    }
}
