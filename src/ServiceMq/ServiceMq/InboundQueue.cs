﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceMq
{
    internal class InboundQueue 
    {
        private const string DtFormat = "yyyyMMddHHmmssfff";
        private readonly CachingQueue<Message> mq;
        private readonly string msgDir;
        private readonly string inDir;
        private readonly string readDir;
        private readonly string name;
        private readonly double hoursReadSentLogsToLive;
        private readonly bool persistMessagesReadLogs;
        private volatile bool continueProcessing = true;
        private ManualResetEvent incomingMessageWaitHandle = new ManualResetEvent(false);
        private int tcount = 0;
        private DateTime lastCleaned = DateTime.Now.AddDays(-10);
        private Exception stateException = null;
        private QueueState state = QueueState.Running;

        public InboundQueue(string name, string msgDir,
            double hoursReadSentLogsToLive, bool persistMessagesReadLogs)
        {
            this.name = name;
            this.msgDir = msgDir;
            this.hoursReadSentLogsToLive = hoursReadSentLogsToLive;
            this.persistMessagesReadLogs = persistMessagesReadLogs;
            this.inDir = Path.Combine(msgDir, "in");
            this.readDir = Path.Combine(msgDir, "read");
            Directory.CreateDirectory(this.inDir);
            Directory.CreateDirectory(this.readDir);

            try
            {
                this.mq = new CachingQueue<Message>(msgDir, Message.ReadFromFile, "*.imq");
            }
            catch (Exception e)
            {
                this.stateException = e;
                this.state = QueueState.Failed;
                throw;
            }
            if (mq.Count > 0) incomingMessageWaitHandle.Set();
        }

        public int Count
        {
            get
            {
                return mq.Count;
            }
        }

        public Exception StateException
        {
            get { return stateException; }
        }

        public QueueState State
        {
            get { return state; }
        }

        public void ClearState()
        {
            stateException = null;
            state = QueueState.Running;
        }

        public void Stop()
        {
            try
            {
                continueProcessing = false;
                incomingMessageWaitHandle.Set();
                incomingMessageWaitHandle.Dispose();
            }
            catch (Exception e)
            {
                this.stateException = e;
                this.state = QueueState.Failed;
                throw;
            }
        }

        public void Enqueue(Message msg)
        {
            //write to q file for that address
            //yyyyMMddHHmmssfff-16bit-from-to-ipport-or-pipename
            //20140324142165412-0415-010-042-024-155-08746-pipename.omq

            //assure no possibility of a duplicate
            var loc = Interlocked.Increment(ref tcount);

            var fileName = string.Format("{0}-{1}-{2}.imq",
                msg.Sent.ToString(DtFormat),
                loc.ToString("0000"),
                msg.From.ToFileNameString());

            msg.Filename = Path.Combine(this.inDir, fileName);
            mq.Enqueue(msg.Filename, msg);

            //increment and roll tcount - max 9000
            if (loc > 9000) Interlocked.Exchange(ref tcount, 0);

            //signal received
            if (continueProcessing) incomingMessageWaitHandle.Set();
        }

        public void ReEnqueue(Message message)
        {
            mq.ReEnqueue(message.Filename, message);
        }

        public Message Receive(int timeoutMs, bool logRead = true)
        {
            while (continueProcessing)
            {
                if (incomingMessageWaitHandle.WaitOne(timeoutMs))
                {
                    if (!continueProcessing) break;
                    Message message = mq.Dequeue();
                    if (null == message)
                    {
                        //set to nonsignaled and block on WaitOne again
                        incomingMessageWaitHandle.Reset();
                        continue; //loop again
                    }
                    if (logRead)
                    {
                        LogRead(message);
                    }
                    return message;
                }
                else
                {
                    break; //timedout
                }
            }
            return null;
        }

        private const string DtLogFormat = "yyyyMMdd-HH-mm";

        private void LogRead(Message message)
        {
            try
            {
                if (persistMessagesReadLogs)
                {
                    var fileName = string.Format("read-{0}.log", DateTime.Now.ToString(DtLogFormat));
                    var logFile = Path.Combine(this.readDir, fileName);
                    var line = message.ToString().ToFlatLine();
                    File.AppendAllLines(logFile, new string[] { line });
                }
                File.Delete(message.Filename);
            }
            catch (Exception e)
            {
                this.stateException = e;
                this.state = QueueState.Cautioned;
            }

            //cleanup every two hours
            if (persistMessagesReadLogs && (DateTime.Now - lastCleaned).TotalHours > 2.0)
            {
                lastCleaned = DateTime.Now;
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        var files = Directory.GetFiles(this.readDir);
                        foreach (var file in files)
                        {
                            var info = new FileInfo(file);
                            if ((DateTime.Now - info.LastWriteTime).TotalHours > this.hoursReadSentLogsToLive)
                            {
                                File.Delete(file);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        this.stateException = e;
                        this.state = QueueState.Cautioned;
                    }
                }, TaskCreationOptions.LongRunning);
            }
        }

        public void Acknowledge(Message message)
        {
            LogRead(message);
        }
    }
}
