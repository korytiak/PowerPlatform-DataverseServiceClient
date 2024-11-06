using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client.Utils;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.ServiceModel;
using System.Linq;
using System.Text;
using Microsoft.PowerPlatform.Dataverse.Client.Exceptions;

namespace Microsoft.PowerPlatform.Dataverse.Client
{
    /// <summary>
    /// Log Entry
    /// </summary>
    [LocalizableAttribute(false)]
    internal sealed class DataverseTraceLogger : TraceLoggerBase
    {
        internal ILogger _logger;

        #region Properties
        /// <summary>
        /// Last Error from Dataverse
        /// </summary>
        public new string LastError
        {
            get { return base.LastError; }
        }

        /// <summary>
        /// Default TraceSource Name
        /// </summary>
        public string DefaultTraceSourceName
        {
            get { return "Microsoft.PowerPlatform.Dataverse.Client.ServiceClient"; }
        }

        /// <summary>
        /// Collection of logs captured to date.
        /// </summary>
        public ConcurrentQueue<Tuple<DateTime, string>> Logs { get; private set; } = new ConcurrentQueue<Tuple<DateTime, string>>();

        /// <summary>
        /// Defines to the maximum amount of time in Minuets that logs will be kept in memory before being purged
        /// </summary>
        public TimeSpan LogRetentionDuration { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Enables or disabled in-memory log capture.
        /// Default is false.
        /// </summary>
        public bool EnabledInMemoryLogCapture { get; set; } = false;

        #endregion

        #region Public Methods

        /// <summary>
        /// Constructs the CdsTraceLogger class.
        /// </summary>
        /// <param name="traceSourceName"></param>
        public DataverseTraceLogger(string traceSourceName = "")
            : base()
        {
            if (string.IsNullOrWhiteSpace(traceSourceName))
            {
                TraceSourceName = DefaultTraceSourceName;
            }
            else
            {
                TraceSourceName = traceSourceName;
            }

            base.Initialize();
        }

        public DataverseTraceLogger(ILogger logger)
        {
            _logger = logger;
            TraceSourceName = DefaultTraceSourceName;
            base.Initialize();
        }


        public override void ResetLastError()
        {
            if (base.LastError.Length > 0)
                base.LastError = base.LastError.Remove(0, LastError.Length - 1);
            LastException = null;
        }

        /// <summary>
        /// Clears log cache.
        /// </summary>
        public void ClearLogCache()
        {
            if (Logs != null)
            {
                Logs = new ConcurrentQueue<Tuple<DateTime, string>>();
            }
        }

        /// <summary>
        /// Log a Message
        /// </summary>
        /// <param name="message"></param>
        public override void Log(string message)
        {
            TraceEvent(TraceEventType.Information, (int)TraceEventType.Information, message, null);
        }

        /// <summary>
        /// Log a Trace event
        /// </summary>
        /// <param name="message"></param>
        /// <param name="eventType"></param>
        public override void Log(string message, TraceEventType eventType)
        {
            if (eventType == TraceEventType.Error)
            {
                Log(message, eventType, new Exception(message));
            }
            else
            {
                TraceEvent(eventType, (int)eventType, message, null);
            }
        }

        /// <summary>
        /// Log a Trace event
        /// </summary>
        /// <param name="message"></param>
        /// <param name="eventType"></param>
        /// <param name="exception"></param>
        public override void Log(string message, TraceEventType eventType, Exception exception)
        {
            if (exception == null && !String.IsNullOrEmpty(message))
            {
                exception = new Exception(message);
            }

            StringBuilder detailedDump = new StringBuilder(4096);
            StringBuilder lastMessage = new StringBuilder(2048);

            lastMessage.AppendLine(message); // Added to fix missing last error line.
            detailedDump.AppendLine(message); // Added to fix missing error line.

            GetExceptionDetail(exception, detailedDump, 0, lastMessage);

            TraceEvent(eventType, (int)eventType, detailedDump.ToString(), exception);
            if (eventType == TraceEventType.Error)
            {
                base.LastError += lastMessage.ToString();
                // check and or alter the exception is its and HTTPOperationExecption.
                if (exception is HttpOperationException httpOperationException)
                {
                    string errorMessage = "Not Provided";
                    if (!string.IsNullOrWhiteSpace(httpOperationException.Response.Content))
                    {
                        JObject contentBody = JObject.Parse(httpOperationException.Response.Content);
                        errorMessage = string.IsNullOrEmpty(contentBody["error"]["message"]?.ToString()) ? "Not Provided" : GetFirstLineFromString(contentBody["error"]["message"]?.ToString()).Trim();
                    }

                    Utils.DataverseOperationException webApiExcept = new Utils.DataverseOperationException(errorMessage, httpOperationException);
                    LastException = webApiExcept;
                }
                else
                    LastException = exception;
            }

            detailedDump.Clear();
            lastMessage.Clear();

        }

        /// <summary>
        /// Log an error with an Exception
        /// </summary>
        /// <param name="exception"></param>
        public override void Log(Exception exception)
        {
            StringBuilder detailedDump = new StringBuilder(4096);
            StringBuilder lastMessage = new StringBuilder(2048);
            GetExceptionDetail(exception, detailedDump, 0, lastMessage);
            TraceEvent(TraceEventType.Error, (int)TraceEventType.Error, detailedDump.ToString(), exception);
            base.LastError += lastMessage.ToString();
            LastException = exception;

            detailedDump.Clear();
            lastMessage.Clear();
        }

        /// <summary>
        /// log retry message
        /// </summary>
        /// <param name="retryCount">retryCount</param>
        /// <param name="req">request</param>
        /// <param name="retryPauseTimeRunning">Value used by the retry system while the code is running, this value can scale up and down based on throttling limits.</param>
        /// <param name="isTerminalFailure">represents if it is final retry failure</param>
        /// <param name="isThrottled">If set, indicates that this was caused by a throttle</param>
        /// <param name="webUriMessageReq"></param>
        public void LogRetry(int retryCount, OrganizationRequest req, TimeSpan retryPauseTimeRunning, bool isTerminalFailure = false, bool isThrottled = false, string webUriMessageReq = "")
        {
            string reqName = req != null ? req.RequestName : webUriMessageReq;

            if (retryCount == 0)
            {
                Log($"No retry attempted for Command {reqName}", TraceEventType.Verbose);
            }
            else if (isTerminalFailure == true)
            {
                Log($"Retry Completed at Retry No={retryCount} for Command {reqName}", TraceEventType.Verbose);
            }
            else
            {
                Log($"Retry No={retryCount} Retry=Started IsThrottle={isThrottled} Delay={retryPauseTimeRunning} for Command {reqName}", TraceEventType.Warning);
            }
        }


        /// <summary>
        /// log exception message
        /// </summary>
        /// <param name="req">request</param>
        /// <param name="ex">exception</param>
        /// <param name="errorStringCheck">errorStringCheck</param>
        /// <param name="webUriMessageReq"></param>
        public void LogException(OrganizationRequest req, Exception ex, string errorStringCheck, string webUriMessageReq = "")
        {
            if (req != null)
            {
                Log(string.Format(CultureInfo.InvariantCulture, "************ {3} - {2} : {0} |=> {1}", errorStringCheck, ex.Message, req.RequestName, ex.GetType().Name), TraceEventType.Error, ex);
            }
            else if (ex is HttpOperationException httpOperationException)
            {
                string errorMessage;
                if (!string.IsNullOrWhiteSpace(httpOperationException.Response.Content))
                {
                    JObject contentBody = JObject.Parse(httpOperationException.Response.Content);
                    errorMessage = DataverseTraceLogger.GetFirstLineFromString(contentBody["error"]["message"].ToString()).Trim();
                }
                else
                {
                    errorMessage = httpOperationException.Response.StatusCode.ToString();
                }

                DataverseOperationException ex01 = DataverseOperationException.GenerateClientOperationException(httpOperationException);
                Log(string.Format(CultureInfo.InvariantCulture, "************ {3} - {2} : {0} |=> {1}", errorStringCheck, errorMessage, webUriMessageReq, ex.GetType().Name), TraceEventType.Error, ex01);
            }
            else
            {
                Log(string.Format(CultureInfo.InvariantCulture, "************ {3} - {2} : {0} |=> {1}", errorStringCheck, ex.Message, "UNKNOWN", ex.GetType().Name), TraceEventType.Error, ex);
            }
        }

        /// <summary>
        /// log failure message
        /// </summary>
        /// <param name="req">request</param>
        /// <param name="requestTrackingId">requestTrackingId</param>
        /// <param name="sessionTrackingId">This ID is used to support Dataverse Telemetry</param>
        /// <param name="disableConnectionLocking">Connection locking disabled</param>
        /// <param name="LockWait">LockWait</param>
        /// <param name="logDt">logDt</param>
        /// <param name="ex">ex</param>
        /// <param name="errorStringCheck">errorStringCheck</param>
        /// <param name="isTerminalFailure">represents if it is final retry failure</param>
        /// <param name="webUriMessageReq">.</param>
        public void LogFailure(OrganizationRequest req, Guid requestTrackingId, Guid? sessionTrackingId, bool disableConnectionLocking, TimeSpan LockWait, Stopwatch logDt, Exception ex, string errorStringCheck, bool isTerminalFailure = false, string webUriMessageReq = "")
        {
            if (req != null)
            {
                Log(string.Format(CultureInfo.InvariantCulture, "{6}Failed to Execute Command - {0}{1} : {5}RequestID={2} {3}: {8} duration={4} ExceptionMessage = {7}",
                    req.RequestName,
                    disableConnectionLocking ? " : DisableCrossThreadSafeties=true :" : string.Empty,
                    requestTrackingId.ToString(),
                    LockWait == TimeSpan.Zero ? string.Empty : string.Format(": LockWaitDuration={0} ", LockWait.ToString()),
                    logDt.Elapsed.ToString(),
                    sessionTrackingId.HasValue && sessionTrackingId.Value != Guid.Empty ? $"SessionID={sessionTrackingId} : " : "",
                    isTerminalFailure ? "[TerminalFailure] " : "",
                    ex.Message,
                    errorStringCheck),
                    TraceEventType.Error, ex);
            }
            else if (ex is HttpOperationException httpOperationException)
            {
                string errorMessage = "SERVER ";
                DataverseOperationException ex01 = DataverseOperationException.GenerateClientOperationException(httpOperationException);
                try
                {
                    if (!string.IsNullOrWhiteSpace(httpOperationException.Response.Content))
                    {
                        JObject contentBody = JObject.Parse(httpOperationException.Response.Content);
                        errorMessage = DataverseTraceLogger.GetFirstLineFromString(contentBody["error"]["message"].ToString()).Trim();
                    }
                    else
                    {
                        errorMessage = httpOperationException.Response.StatusCode.ToString();
                    }
                }
                catch (Exception)
                {
                    // unable to parse server response
                }

                Log(string.Format(CultureInfo.InvariantCulture, "{6}Failed to Execute Command - {0}{1} : {5}RequestID={2} {3}: {8} duration={4} ExceptionMessage = {7}",
                    webUriMessageReq,
                    disableConnectionLocking ? " : DisableCrossThreadSafeties=true :" : string.Empty,
                    requestTrackingId.ToString(),
                    string.Empty,
                    logDt.Elapsed.ToString(),
                    sessionTrackingId.HasValue && sessionTrackingId.Value != Guid.Empty ? $"SessionID={sessionTrackingId.Value} : " : "",
                    isTerminalFailure ? "[TerminalFailure] " : "",
                    errorMessage,
                    errorStringCheck),
                    TraceEventType.Error, ex);
            }
        }

        internal string GetFormatedRequestSessionIdString( Guid requestId, Guid? sessionId )
        {
            return string.Format("RequestID={0}{1}",
                        requestId.ToString(),
                        sessionId.HasValue && sessionId.Value != Guid.Empty ? $" : SessionID={sessionId.Value.ToString()} : " : "");
        }

        /// <summary>
        /// Logs data to memory.
        /// </summary>
        /// <param name="eventType"></param>
        /// <param name="id"></param>
        /// <param name="message"></param>
        /// <param name="ex"></param>
        private void TraceEvent(TraceEventType eventType, int id, string message, Exception ex)
        {
            Source.TraceEvent(eventType, id, message);

            LogLevel logLevel = TranslateTraceEventType(eventType);
            if (_logger != null && _logger.IsEnabled(logLevel))
            {
                _logger.Log(logLevel, id, ex, message);
            }

            if (EnabledInMemoryLogCapture)
            {
                Logs.Enqueue(Tuple.Create<DateTime, string>(DateTime.UtcNow,
                    string.Format(CultureInfo.InvariantCulture, "[{0}][{1}] {2}", eventType, id, message)));

                DateTime expireDateTime = DateTime.UtcNow.Subtract(LogRetentionDuration);
                bool CleanUpLog = true;
                while (CleanUpLog)
                {
                    Tuple<DateTime, string> peekOut = null;
                    if (Logs.TryPeek(out peekOut))
                    {
                        if (peekOut.Item1 <= expireDateTime)
                        {
                            Tuple<DateTime, string> tos;
                            Logs.TryDequeue(out tos);
                            Debug.WriteLine($"Flushing LogEntry from memory: {tos.Item2}");  // Write flush events out to debug log.
                            tos = null;
                        }
                        else
                        {
                            CleanUpLog = false;
                        }
                    }
                    else
                    {
                        CleanUpLog = false;
                    }
                }
            }
        }

        #endregion

        /// <summary>
        /// Disassembles the Exception into a readable block
        /// </summary>
        /// <param name="objException">Exception to work with</param>
        /// <param name="sw">Writer to write too</param>
        /// <param name="level">depth</param>
        /// <param name="lastErrorMsg">Last Writer to write too</param>
        private void GetExceptionDetail(object objException, StringBuilder sw, int level, StringBuilder lastErrorMsg)
        {
            if (objException == null)
                return;

            if (objException is FaultException<OrganizationServiceFault>)
            {
                FaultException<OrganizationServiceFault> OrgFault = (FaultException<OrganizationServiceFault>)objException;
                string ErrorDetail = GenerateOrgErrorDetailsInfo(OrgFault.Detail.ErrorDetails);
                FormatExceptionMessage(
                OrgFault.Source != null ? OrgFault.Source.ToString().Trim() : "Not Provided",
                OrgFault.TargetSite != null ? OrgFault.TargetSite.Name.ToString() : "Not Provided",
                OrgFault.Detail != null ? string.Format(CultureInfo.InvariantCulture, "Message: {0}\nErrorCode: {1}{4}\nTrace: {2}{3}", OrgFault.Detail.Message, OrgFault.Detail.ErrorCode, OrgFault.Detail.TraceText, string.IsNullOrEmpty(ErrorDetail) ? "" : $"\n{ErrorDetail}", OrgFault.Detail.ActivityId != Guid.Empty ? "" : $"\nActivityId: {OrgFault.Detail.ActivityId}") :
                string.IsNullOrEmpty(OrgFault.Message) ? "Not Provided" : OrgFault.Message.ToString().Trim(),
                string.IsNullOrEmpty(OrgFault.HelpLink) ? "Not Provided" : OrgFault.HelpLink.ToString().Trim(),
                string.IsNullOrEmpty(OrgFault.StackTrace) ? "Not Provided" : OrgFault.StackTrace.ToString().Trim()
                , sw, level);

                lastErrorMsg.Append(OrgFault.Detail != null ? OrgFault.Detail.Message :
                string.IsNullOrEmpty(OrgFault.Message) ? string.Empty : OrgFault.Message.ToString().Trim());

                if (lastErrorMsg.Length > 0 && (OrgFault.InnerException != null || OrgFault.Detail != null && OrgFault.Detail.InnerFault != null))
                    lastErrorMsg.Append(" => ");

                level++;
                if ((OrgFault.InnerException != null || OrgFault.Detail != null && OrgFault.Detail.InnerFault != null))
                    GetExceptionDetail(OrgFault.Detail != null && OrgFault.Detail.InnerFault != null ? OrgFault.Detail.InnerFault : (object)OrgFault.InnerException,
                    sw, level, lastErrorMsg);

                return;

            }
            else
            {
                if (objException is OrganizationServiceFault)
                {
                    OrganizationServiceFault oFault = (OrganizationServiceFault)objException;
                    string ErrorDetail = GenerateOrgErrorDetailsInfo(oFault.ErrorDetails);
                    FormatOrgFaultMessage(
                            string.Format(CultureInfo.InvariantCulture, "Message: {0}\nErrorCode: {1}{4}\nTrace: {2}{3}", oFault.Message, oFault.ErrorCode, oFault.TraceText, string.IsNullOrEmpty(ErrorDetail) ? "" : $"\n{ErrorDetail}", oFault.ActivityId != Guid.Empty ? "" : $"\nActivityId: {oFault.ActivityId}"),
                            oFault.Timestamp.ToString(),
                            oFault.ErrorCode.ToString(),
                            string.IsNullOrEmpty(oFault.HelpLink) ? "Not Provided" : oFault.HelpLink.ToString().Trim(),
                            string.IsNullOrEmpty(oFault.TraceText) ? "Not Provided" : oFault.TraceText.ToString().Trim(), sw, level);

                    level++;

                    lastErrorMsg.Append(oFault.Message);
                    if (lastErrorMsg.Length > 0 && oFault.InnerFault != null)
                        lastErrorMsg.Append(" => ");

                    if (oFault.InnerFault != null)
                        GetExceptionDetail(oFault.InnerFault, sw, level, lastErrorMsg);

                    return;

                }
                else
                {
                    if (objException is HttpOperationException httpOperationException)
                    {
                        JObject contentBody = null;
                        if (!string.IsNullOrEmpty(httpOperationException.Response.Content))
                            contentBody = JObject.Parse(httpOperationException.Response.Content);

                        var ErrorBlock = contentBody?["error"];
                        FormatExceptionMessage(
                        httpOperationException.Source != null ? httpOperationException.Source.ToString().Trim() : "Not Provided",
                        httpOperationException.TargetSite != null ? httpOperationException.TargetSite.Name?.ToString() : "Not Provided",
                        string.IsNullOrEmpty(ErrorBlock?["message"]?.ToString()) ? "Not Provided" : string.Format("Message: {0}{1}\n", GetFirstLineFromString(ErrorBlock?["message"]?.ToString()).Trim(), httpOperationException.Response != null && httpOperationException.Response.Headers.ContainsKey("REQ_ID") ? $"\nActivityId: {ExtractString(httpOperationException.Response.Headers["REQ_ID"])}" : ""),
                        string.IsNullOrEmpty(httpOperationException.HelpLink) ? "Not Provided" : httpOperationException.HelpLink.ToString().Trim(),
                        string.IsNullOrEmpty(ErrorBlock?["stacktrace"]?.ToString()) ? "Not Provided" : ErrorBlock["stacktrace"]?.ToString().Trim()
                        , sw, level);

                        lastErrorMsg.Append(string.IsNullOrEmpty(httpOperationException.Message) ? "Not Provided" : httpOperationException.Message.ToString().Trim());

                        // WebEx currently only returns 1 level of error.
                        var InnerError = contentBody?["error"]["innererror"];
                        if (lastErrorMsg.Length > 0 && InnerError != null)
                        {
                            level++;
                            lastErrorMsg.Append(" => ");
                            FormatExceptionMessage(
                                httpOperationException.Source != null ? httpOperationException.Source.ToString().Trim() : "Not Provided",
                                httpOperationException.TargetSite != null ? httpOperationException.TargetSite.Name?.ToString() : "Not Provided",
                                string.IsNullOrEmpty(InnerError?["message"]?.ToString()) ? "Not Provided" : GetFirstLineFromString(InnerError?["message"]?.ToString()).Trim(),
                                string.IsNullOrEmpty(InnerError?["@Microsoft.PowerApps.CDS.HelpLink"]?.ToString()) ? "Not Provided" : GetFirstLineFromString(InnerError?["@Microsoft.PowerApps.CDS.HelpLink"]?.ToString()).Trim(),
                                string.IsNullOrEmpty(InnerError?["stacktrace"]?.ToString()) ? "Not Provided" : InnerError?["stacktrace"]?.ToString().Trim()
                                , sw, level);
                        }
                    }
                    else
                    {
                        if (objException is DataverseOperationException cdsOpExecp)
                        {
                            FormatSvcFaultMessage(
                                string.IsNullOrEmpty(cdsOpExecp.Message) ? "Not Provided" : cdsOpExecp.Message.ToString().Trim(),
                                string.IsNullOrEmpty(cdsOpExecp.Source) ? "Not Provided" : cdsOpExecp.Source.ToString().Trim(),
                                cdsOpExecp.HResult == -1 ? "Not Provided" : cdsOpExecp.HResult.ToString().Trim(),
                                cdsOpExecp.Data,
                                string.IsNullOrEmpty(cdsOpExecp.HelpLink) ? "Not Provided" : cdsOpExecp.HelpLink.ToString().Trim(),
                                sw,
                                level);

                            lastErrorMsg.Append(string.IsNullOrEmpty(cdsOpExecp.Message) ? "Not Provided" : cdsOpExecp.Message.ToString().Trim());

                            if (lastErrorMsg.Length > 0 && cdsOpExecp.InnerException != null)
                                lastErrorMsg.Append(" => ");

                            level++;
                            if (cdsOpExecp.InnerException != null)
                                GetExceptionDetail(cdsOpExecp.InnerException, sw, level, lastErrorMsg);

                        }
                        else
                        {
                            if (objException is Exception)
                            {
                                Exception generalEx = (Exception)objException;
                                FormatExceptionMessage(
                                generalEx.Source != null ? generalEx.Source.ToString().Trim() : "Not Provided",
                                generalEx.TargetSite != null ? generalEx.TargetSite.Name.ToString() : "Not Provided",
                                string.IsNullOrEmpty(generalEx.Message) ? "Not Provided" : generalEx.Message.ToString().Trim(),
                                string.IsNullOrEmpty(generalEx.HelpLink) ? "Not Provided" : generalEx.HelpLink.ToString().Trim(),
                                string.IsNullOrEmpty(generalEx.StackTrace) ? "Not Provided" : generalEx.StackTrace.ToString().Trim()
                                , sw, level);

                                lastErrorMsg.Append(string.IsNullOrEmpty(generalEx.Message) ? "Not Provided" : generalEx.Message.ToString().Trim());

                                if (lastErrorMsg.Length > 0 && generalEx.InnerException != null)
                                    lastErrorMsg.Append(" => ");

                                level++;
                                if (generalEx.InnerException != null)
                                    GetExceptionDetail(generalEx.InnerException, sw, level, lastErrorMsg);
                            }
                        }
                    }
                }
            }
            return;
        }

        private static string ExtractString(IEnumerable<string> enumerable)
        {
            string sOut = string.Empty;
            if (enumerable != null)
            {
                List<string> lst = new List<string>(enumerable);

                foreach (var itm in lst.Distinct())
                {
                    if (string.IsNullOrEmpty(sOut))
                        sOut += $"{itm}";
                    else
                        sOut += $"|{itm}";
                }
            }
            return sOut;
        }

        /// <summary>
        /// returns the first line from the text block.
        /// </summary>
        /// <param name="textBlock"></param>
        /// <returns></returns>
        internal static string GetFirstLineFromString(string textBlock)
        {
            if (!string.IsNullOrEmpty(textBlock))
            {
                try
                {
                    int iCopyToo = textBlock.IndexOf(Environment.NewLine);
                    if (iCopyToo > 0)
                        return textBlock.Substring(0, textBlock.IndexOf(Environment.NewLine));
                }
                catch { } // No Op let it fall though
            }
            return textBlock;
        }

        /// <summary>
        /// Formats the detail collection from a service exception.
        /// </summary>
        /// <param name="errorDetails"></param>
        /// <returns></returns>
        private static string GenerateOrgErrorDetailsInfo(ErrorDetailCollection errorDetails)
        {
            if (errorDetails != null && errorDetails.Count > 0)
            {
                StringBuilder sw = new StringBuilder(2048);
                sw.AppendLine("Error Details\t:");
                foreach (var itm in errorDetails)
                {
                    string valueText = itm.Value != null ? itm.Value.ToString() : "Not Set";
                    sw.AppendLine($"{itm.Key}\t: {valueText}");
                }
                return sw.ToString();
            }
            return string.Empty;
        }

        /// <summary>
        /// Creates the exception message.
        /// </summary>
        /// <param name="source">Source of Exception</param>
        /// <param name="targetSite">Target of Exception</param>
        /// <param name="message">Exception Message</param>
        /// <param name="stackTrace">StackTrace</param>
        /// <param name="helpLink">Url for help. </param>
        /// <param name="sw">Writer to write too</param>
        /// <param name="level">Depth of Exception</param>
        private static void FormatExceptionMessage(string source, string targetSite, string message, string helpLink, string stackTrace, StringBuilder sw, int level)
        {
            if (level != 0)
                sw.AppendLine($"Inner Exception Level {level}\t: ");
            sw.AppendLine("Source: " + source);
            sw.AppendLine("Method: " + targetSite);
            sw.AppendLine("DateUTC: " + DateTime.UtcNow.ToShortDateString());
            sw.AppendLine("TimeUTC: " + DateTime.UtcNow.ToLongTimeString());
            sw.AppendLine("Error: " + message);
            sw.AppendLine($"HelpLink Url: {helpLink}");
            sw.AppendLine("Stack Trace: " + stackTrace);
            sw.AppendLine("======================================================================================================================");
        }

        /// <summary>
        /// Formats an Exception specific to an organization fault.
        /// </summary>
        /// <param name="message">Exception Message</param>
        /// <param name="timeOfEvent">Time occurred</param>
        /// <param name="errorCode">Error code of message</param>
        /// <param name="traceText">Message Text</param>
        /// <param name="helpLink">Help Link URL</param>
        /// <param name="sw">Writer to write too</param>
        /// <param name="level">Depth</param>
        private static void FormatOrgFaultMessage(string message, string timeOfEvent, string errorCode, string traceText, string helpLink, StringBuilder sw, int level)
        {
            if (level != 0)
                sw.AppendLine($"Inner Exception Level {level}\t: ");
            sw.AppendLine("==OrganizationServiceFault Info=======================================================================================");
            sw.AppendLine("Error: " + message);
            sw.AppendLine("Time: " + timeOfEvent);
            sw.AppendLine("ErrorCode: " + errorCode);
            sw.AppendLine("DateUTC: " + DateTime.UtcNow.ToShortDateString());
            sw.AppendLine("TimeUTC: " + DateTime.UtcNow.ToLongTimeString());
            sw.AppendLine($"HelpLink Url: {helpLink}");
            sw.AppendLine("Trace: " + traceText);
            sw.AppendLine("======================================================================================================================");
        }

        /// <summary>
        /// Formats an Exception specific to an organization fault.
        /// </summary>
        /// <param name="message">Exception Message</param>
        /// <param name="source">Source of error.</param>
        /// <param name="errorCode">Error code of message</param>
        /// <param name="dataItems">Data Items</param>
        /// <param name="helpLink">Help Link</param>
        /// <param name="sw">Writer to write too</param>
        /// <param name="level">Depth</param>
        private static void FormatSvcFaultMessage(string message, string source, string errorCode, System.Collections.IDictionary dataItems, string helpLink, StringBuilder sw, int level)
        {
            if (level != 0)
                sw.AppendLine($"Inner Exception Level {level}\t: ");
            sw.AppendLine("==DataverseOperationException Info=======================================================================================");
            sw.AppendLine($"Source: {source}");
            sw.AppendLine("Error: " + message);
            sw.AppendLine("ErrorCode: " + errorCode);
            sw.AppendLine("DateUTC: " + DateTime.UtcNow.ToShortDateString());
            sw.AppendLine("TimeUTC: " + DateTime.UtcNow.ToLongTimeString());
            sw.AppendLine($"HelpLink Url: {helpLink}");
            if (dataItems != null && dataItems.Count > 0)
            {
                sw.AppendLine("DataverseErrorDetail:");
                foreach (System.Collections.DictionaryEntry itm in dataItems)
                {
                    sw.AppendLine($"\t{itm.Key}: {itm.Value}");
                }
            }
            sw.AppendLine("======================================================================================================================");
        }

        private static LogLevel TranslateTraceEventType(TraceEventType traceLevel)
        {
            switch (traceLevel)
            {
                case TraceEventType.Critical:
                    return LogLevel.Critical;
                case TraceEventType.Error:
                    return LogLevel.Error;
                case TraceEventType.Warning:
                    return LogLevel.Warning;
                case TraceEventType.Information:
                    return LogLevel.Information;
                case TraceEventType.Verbose:
                    return LogLevel.Debug;
                default:
                    return LogLevel.None;
            }
        }

    }

    /// <summary>
    /// This class provides an override for the default trace settings.
    /// These settings must be set before the components in the control are used for them to be effective.
    /// </summary>
    public class TraceControlSettings
    {
        private static string _traceSourceName = "Microsoft.PowerPlatform.Dataverse.Client.ServiceClient";

        /// <summary>
        /// Returns the Registered Trace Listeners in the override object.
        /// </summary>
        internal static Dictionary<string, TraceListener> RegisterdTraceListeners
        {
            get
            {
                return TraceSourceSettingStore.GetTraceSourceSettings(_traceSourceName) != null ?
                    TraceSourceSettingStore.GetTraceSourceSettings(_traceSourceName).TraceListeners : null;
            }
        }
        /// <summary>
        /// Override Trace Level setting.
        /// </summary>
        public static SourceLevels TraceLevel { get; set; }
        /// <summary>
        /// Builds the base trace settings
        /// </summary>

        static TraceControlSettings()
        {
            TraceLevel = SourceLevels.Off;
        }

        /// <summary>
        /// Closes any trace listeners that were configured
        /// </summary>
        public static void CloseListeners()
        {
            if (RegisterdTraceListeners != null && RegisterdTraceListeners.Count > 0)
                foreach (TraceListener itm in RegisterdTraceListeners.Values)
                {
                    itm.Close();
                }
        }
        /// <summary>
        /// Adds a listener to the trace listen array
        /// </summary>
        /// <param name="listenerToAdd">Trace Listener you wish to add</param>
        /// <returns>true on success, false on fail.</returns>
        public static bool AddTraceListener(TraceListener listenerToAdd)
        {
            try
            {
                Trace.AutoFlush = true;
                var traceSourceSetting = TraceSourceSettingStore.GetTraceSourceSettings(_traceSourceName);
                if (traceSourceSetting == null)
                {
                    traceSourceSetting = new TraceSourceSetting(_traceSourceName, TraceLevel);
                }
                if (traceSourceSetting.TraceListeners == null)
                    traceSourceSetting.TraceListeners = new Dictionary<string, TraceListener>();

                if (traceSourceSetting.TraceListeners.ContainsKey(listenerToAdd.Name))
                    return true;

                traceSourceSetting.TraceListeners.Add(listenerToAdd.Name, listenerToAdd);
                TraceSourceSettingStore.AddTraceSettingsToStore(traceSourceSetting);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

}