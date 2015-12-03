/*
 * Copyright (c) 2015, InWorldz Halcyon Developers
 * All rights reserved.
 * 
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 * 
 *   * Redistributions of source code must retain the above copyright notice, this
 *     list of conditions and the following disclaimer.
 * 
 *   * Redistributions in binary form must reproduce the above copyright notice,
 *     this list of conditions and the following disclaimer in the documentation
 *     and/or other materials provided with the distribution.
 * 
 *   * Neither the name of halcyon nor the names of its
 *     contributors may be used to endorse or promote products derived from
 *     this software without specific prior written permission.
 * 
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
 * FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
 * DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
 * SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 * CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
 * OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
 * OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using log4net;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;

namespace OpenSim.Region.CoreModules.Scripting.HttpRequest
{
    public class HttpRequestObject : IServiceRequest
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private bool _finished = false;
        public bool Finished
        {
            get { return _finished; }
        }

        private int _HttpBodyMaxLength = 2048;
        public int HttpBodyMaxLength
        {
            get
            {
                return _HttpBodyMaxLength;
            }
            set
            {
                if ((value > 0) && (value <= 16384))
                    _HttpBodyMaxLength = value;
            }
        }

        // Parameter members and default values
        public string HttpMethod = "GET";
        public string HttpMIMEType = "text/plain;charset=utf-8";
        public int HttpTimeout;
        public bool HttpVerifyCert = true;
        public bool HttpVerboseThrottle = true;

        // Request info
        private UUID _itemID;
        public UUID ItemID
        {
            get { return _itemID; }
            set { _itemID = value; }
        }
        private uint _localID;
        public uint LocalID
        {
            get { return _localID; }
            set { _localID = value; }
        }

        public UUID SogID { get; set; }

        public DateTime Next;
        public string proxyurl;
        public string proxyexcepts;
        public string OutboundBody;
        private UUID _reqID;
        public UUID ReqID
        {
            get { return _reqID; }
            set { _reqID = value; }
        }


        public HttpWebRequest Request;
        public string ResponseBody;
        public List<string> ResponseMetadata;
        public Dictionary<string, string> ResponseHeaders;
        public int Status;
        public string Url;

        public ulong RequestDuration {get; set;}

        public bool IsLowPriority { get; set; }

        public void Process()
        {
            _finished = false;
            SendRequest();
        }
        /*
         * TODO: More work on the response codes.  Right now
         * returning 200 for success or 499 for exception
         */

        public void SendRequest()
        {
            HttpWebResponse response = null;
            ulong requestStart = Util.GetLongTickCount();

            try
            {
                Request = (HttpWebRequest)WebRequest.Create(Url);
                Request.Method = HttpMethod;
                Request.ContentType = HttpMIMEType;
                Request.Timeout = HttpTimeout;
                Request.ReadWriteTimeout = HttpTimeout;

                if (!HttpVerifyCert)
                {
                    Request.Headers.Add("NoVerifyCert", "true");
                }

                if (!string.IsNullOrEmpty(proxyurl))
                {
                    if (!string.IsNullOrEmpty(proxyexcepts))
                    {
                        string[] elist = proxyexcepts.Split(';');
                        Request.Proxy = new WebProxy(proxyurl, true, elist);
                    }
                    else
                    {
                        Request.Proxy = new WebProxy(proxyurl, true);
                    }
                }

                foreach (KeyValuePair<string, string> entry in ResponseHeaders)
                {
                    if (entry.Key.ToLower().Equals("user-agent"))
                        Request.UserAgent = entry.Value;
                    else
                        Request.Headers[entry.Key] = entry.Value;
                }

                // Encode outbound data
                if (!String.IsNullOrEmpty(OutboundBody))
                {
                    byte[] data = Encoding.UTF8.GetBytes(OutboundBody);

                    Request.ContentLength = data.Length;
                    using (Stream requestStream = Request.GetRequestStream())
                    {
                        requestStream.Write(data, 0, data.Length);
                        requestStream.Close();
                    }
                }

                response = (HttpWebResponse)Request.GetResponse();
                Status = (int)response.StatusCode;

                using (Stream responseStream = response.GetResponseStream())
                {
                    int readSoFar = 0;
                    int count = 0;
                    byte[] buf = new byte[HttpBodyMaxLength];

                    do
                    {
                        count = responseStream.Read(buf, readSoFar, HttpBodyMaxLength - readSoFar);
                        if (count > 0) readSoFar += count;
                    }
                    while (count > 0 && readSoFar < HttpBodyMaxLength);

                    // translate from bytes to ASCII text
                    ResponseBody = Encoding.UTF8.GetString(buf, 0, readSoFar);
                }
            }
            catch (WebException e)
            {
                if (e.Status == WebExceptionStatus.ProtocolError)
                {
                    HttpWebResponse webRsp = (HttpWebResponse)e.Response;
                    Status = (int)webRsp.StatusCode;
                    ResponseBody = webRsp.StatusDescription;
                }
                else
                {
                    Status = (int)OSHttpStatusCode.ClientErrorJoker;
                    ResponseBody = e.Message;
                }
            }
            catch (Exception e)
            {
                Status = (int)OSHttpStatusCode.ClientErrorJoker;
                ResponseBody = e.Message;
                m_log.ErrorFormat("[HTTPREQUEST]: 499 - Exception on httprequest: {0}", e.ToString());
            }
            finally
            {
                if (response != null)
                    response.Close();

                RequestDuration = Util.GetLongTickCount() - requestStart;
            }

            _finished = true;
        }

        public void Stop()
        {
        }
    }
}
