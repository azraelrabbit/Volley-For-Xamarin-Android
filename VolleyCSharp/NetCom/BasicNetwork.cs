using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System.IO;
using System.Net;
using VolleyCSharp.Utility;
using VolleyCSharp.MainCom;

/*
 * 原作者Github（java）：https://github.com/mcxiaoke/android-volley
 * 
 * C#作者：Y-Z-F
 * 博客地址：http://www.cnblogs.com/yaozhenfa/
 * Github地址：https://github.com/yaozhenfa/
 * 
 * 15.4.15 审核通过
 */

namespace VolleyCSharp.NetCom
{
    /// <summary>
    /// 基础网络
    /// 原本的版本利用该类来实现在不同系统版本
    /// 情况下实例化不同的网络实现类，但在该版
    /// 本下均采用.NET提供的网络库。
    /// 
    /// 当然你可以通过实现IHttpStack接口实现Socket
    /// 方式的网络连接或其他。
    /// </summary>
    public class BasicNetwork : INetwork
    {
        protected static bool DEBUG = VolleyLog.DEBUG;
        private static int SLOW_REQUEST_THRESHOLD_MS = 3000;
        private static int DEFAULT_POOL_SIZE = 4096;
        protected IHttpStack mHttpStack;

        public BasicNetwork(IHttpStack httpStack)
        {
            this.mHttpStack = httpStack;
        }

        #region INetwork

        /// <summary>
        /// 处理请求的核心
        /// 不包含底层请求的创建
        /// </summary>
        public NetworkResponse PerformRequest(Request request)
        {
            long requestStart = SystemClock.ElapsedRealtime();
            while (true)
            {
                HttpWebResponse httpResponse = null;
                byte[] responseContents = null;
                Dictionary<String,String> responseHeaders = null;
                try
                {
                    Dictionary<String, String> headers = new Dictionary<string, string>();
                    AddCacheHeaders(headers, request.CacheEntry);

                    //处理请求
                    httpResponse = mHttpStack.PerformRequest(request, headers);

                    var statusCode = httpResponse.StatusCode;
                    responseHeaders = ConvertHeaders(httpResponse.Headers);

                    if (statusCode == HttpStatusCode.MovedPermanently || statusCode == HttpStatusCode.Moved)
                    {
                        String newUrl = responseHeaders["Location"];
                        request.SetRedirectUrl(newUrl);
                    }

                    //获取请求到的内容
                    Stream output = httpResponse.GetResponseStream();
                    if (output != null)
                    {
                        responseContents = EntityToBytes(output);
                    }
                    else
                    {
                        responseContents = new byte[0];
                    }

                    long requestLifetime = SystemClock.ElapsedRealtime() - requestStart;
                    LogSlowRequests(requestLifetime, request, responseContents, statusCode);

                    if (statusCode < HttpStatusCode.OK || (int)statusCode > 299)
                    {
                        throw new IOException();
                    }
                    return new NetworkResponse(statusCode, responseContents, responseHeaders, false,
                        SystemClock.ElapsedRealtime() - requestStart);
                }
                catch (WebException ex)
                {
                    if (ex.Response != null)
                    {
                        var result = ex.Response as HttpWebResponse;
                        if (result.StatusCode == HttpStatusCode.NotModified)
                        {
                            Entry entry = request.CacheEntry;
                            if (entry == null)
                            {
                                return new NetworkResponse(HttpStatusCode.NotModified, null,
                                    responseHeaders, true,
                                    SystemClock.ElapsedRealtime() - requestStart);
                            }
                            //entry.ResponseHeaders = entry.ResponseHeaders.Intersect(responseHeaders).ToDictionary(x => x.Key, x => x.Value);
                            return new NetworkResponse(HttpStatusCode.NotModified, entry.Data,
                                entry.ResponseHeaders, true,
                                SystemClock.ElapsedRealtime() - requestStart);
                        }
                    }
                    throw new NetworkError(ex);
                }
                catch (TimeoutException)
                {
                    AttempRetryOnException("connection", request, new TimeoutError());
                }
                catch (IOException e)
                {
                    HttpStatusCode statusCode = 0;
                    NetworkResponse networkResponse = null;
                    if (httpResponse != null)
                    {
                        statusCode = httpResponse.StatusCode;
                    }
                    else
                    {
                        throw new NoConnectionError(e);
                    }
                    if (statusCode == HttpStatusCode.MovedPermanently)
                    {
                        VolleyLog.E("Request at {0} has been redirected to {1}", request.OriginUrl, request.Url);
                    }
                    else
                    {
                        VolleyLog.E("Unexpected response code {0} for {1}", statusCode, request.Url);
                    }
                    if (responseContents != null)
                    {
                        networkResponse = new NetworkResponse(statusCode, responseContents,
                            responseHeaders, false, SystemClock.ElapsedRealtime() - requestStart);
                        if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
                        {
                            AttempRetryOnException("auth", request, new AuthFailureError());
                        }
                        else if (statusCode == HttpStatusCode.MovedPermanently || statusCode == HttpStatusCode.Moved)
                        {
                            AttempRetryOnException("redirect", request, new AuthFailureError(networkResponse));
                        }
                        else
                        {
                            throw new ServerError(networkResponse);
                        }
                    }
                    else
                    {
                        throw new NetworkError(networkResponse);
                    }
                }
            }
        }

        #endregion

        /// <summary>
        /// 输出请求完成的信息（仅限调试）
        /// </summary>
        private void LogSlowRequests(long requestLifetime, Request request, byte[] responseContents, HttpStatusCode statusCode)
        {
            if (DEBUG || requestLifetime > SLOW_REQUEST_THRESHOLD_MS)
            {
                VolleyLog.D("HTTP response for request=<{0}> [lifetime={1}],[size={2}], [rc={3}],[retryCount={4}]", requestLifetime, requestLifetime,
                    responseContents != null ? responseContents.Length.ToString() : "null",
                    statusCode, request.GetRetryPolicy().CurrentRetryCount);
            }
        }

        private static void AttempRetryOnException(String logPrefix, Request request, VolleyError exception)
        {
            IRetryPolicy retryPolicy = request.GetRetryPolicy();
            int oldTimeout = request.GetTimeoutMs();

            try
            {
                retryPolicy.Retry(exception);
            }
            catch (VolleyError e)
            {
                request.AddMarker(String.Format("{0}-timeout-giveup[timeout={1}]", logPrefix, oldTimeout));
                throw e;
            }
            request.AddMarker(String.Format("{0}-retry [timeout-{1}]", logPrefix, oldTimeout));
        }

        /// <summary>
        /// 创建请求头部需要添加的信息
        /// 主要用来判断请求是否过期
        /// </summary>
        private void AddCacheHeaders(Dictionary<String, String> headers, Entry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (entry.ETag != null)
            {
                headers.Add("If-None-Match", entry.ETag);
            }

            if (entry.LastModified > 0)
            {
                var refTime = new DateTime(entry.LastModified);
                headers.Add("If-Modified-Since", refTime.ToString());
            }
        }

        protected void LogError(String what, String url, long start)
        {
            long now = SystemClock.ElapsedRealtime();
            VolleyLog.V("HTTP ERROR({0}) {1} ms to fetch {2}", what, (now - start), url);
        }

        /// <summary>
        /// 将流转换成字节
        /// </summary>
        private byte[] EntityToBytes(Stream entity)
        {
            StreamReader sr = new StreamReader(entity);
            byte[] buffer = Encoding.UTF8.GetBytes(sr.ReadToEnd());
            entity.Read(buffer, 0, buffer.Length);
            return buffer;
        }

        /// <summary>
        /// 将请求头转换成字典
        /// </summary>
        private Dictionary<String, String> ConvertHeaders(WebHeaderCollection headers)
        {
            Dictionary<String, String> dic = new Dictionary<string, string>();
            foreach (var item in headers.AllKeys)
            {
                dic.Add(item, headers[item]);
            }
            return dic;
        }
    }
}