﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace ServiceMq
{
    public class Message
    {
        private const string DtFormat = "yyyyMMddHHmmssfff";

        internal string Filename { get; set; }
        public Guid Id { get; set; }
        public Address From { get; set; }
        public DateTime Sent { get; set; }
        public DateTime Received { get; set; }
        public int SendAttempt { get; set; }
        public string MessageTypeName { get; set; }
        public byte[] MessageBytes { get; set; }
        public string MessageString { get; set; }

        private JsonSerializerSettings settings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore
        };

        public T To<T>()
        {
#if (!NET35)
            if (string.IsNullOrWhiteSpace(MessageString)) return default(T);
#else
            if (string.IsNullOrEmpty(MessageString)) return default(T);
#endif
            return JsonConvert.DeserializeObject<T>(MessageString, settings);
        }

        //id   from   sentts   receivedts   sentattempts   msgtypename   bin/str   message(binbase64)
        public override string ToString()
        {
            return string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}",
                Id,
                From,
                Sent.ToString(DtFormat),
                Received.ToString(DtFormat),
                SendAttempt,
                MessageTypeName,
                MessageBytes == null ? "str" : "bin",
                MessageBytes == null ? MessageString : Convert.ToBase64String(MessageBytes));
        }

        public static Message ReadFromFile(string fileName, FastFile fastFile)
        {
            //id   from   sentts   receivedts   sentattempts   msgtypename   bin/str   message(binbase64)
            var text = fastFile.ReadAllText(fileName);
            var parts = text.Split('\t');
            if (parts.Length == 8)
            {
                var msg = new Message()
                {
                    Filename = fileName,
#if (!NET35)
                    Id = Guid.Parse(parts[0]),
#else
                    Id = new Guid(parts[0]),
#endif
                    From = Address.FromString(parts[1]),
                    Sent = DateTime.ParseExact(parts[2], DtFormat, DateTimeFormatInfo.InvariantInfo),
                    Received = DateTime.ParseExact(parts[3], DtFormat, DateTimeFormatInfo.InvariantInfo),
                    SendAttempt = Convert.ToInt32(parts[4]),
                    MessageTypeName = parts[5],
                    MessageString = parts[6] == "bin" ? null : parts[7],
                    MessageBytes = parts[6] != "bin" ? null : Convert.FromBase64String(parts[7])
                };
                return msg;
            }
            return null;
        }


    }
}
