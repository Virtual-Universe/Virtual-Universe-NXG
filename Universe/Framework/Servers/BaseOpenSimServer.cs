/// <license>
///     Copyright (c) Contributors, https://virtual-planets.org/
///     See CONTRIBUTORS.TXT for a full list of copyright holders.
///     For an explanation of the license of each contributor and the content it
///     covers please see the Licenses directory.
///
///     Redistribution and use in source and binary forms, with or without
///     modification, are permitted provided that the following conditions are met:
///         * Redistributions of source code must retain the above copyright
///         notice, this list of conditions and the following disclaimer.
///         * Redistributions in binary form must reproduce the above copyright
///         notice, this list of conditions and the following disclaimer in the
///         documentation and/or other materials provided with the distribution.
///         * Neither the name of the Virtual Universe Project nor the
///         names of its contributors may be used to endorse or promote products
///         derived from this software without specific prior written permission.
///
///     THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
///     EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
///     WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
///     DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
///     DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
///     (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
///     LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
///     ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
///     (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
///     SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
/// </license>

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Repository;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Universe.Framework;
using Universe.Framework.Console;
using Universe.Framework.Monitoring;
using Universe.Framework.Servers;
using Universe.Framework.Servers.HttpServer;
using Timer = System.Timers.Timer;

namespace Universe.Framework.Servers
{
    /// <summary>
    /// Common base for the main UniverseServers (user, grid, inventory, region, etc)
    /// </summary>
    public abstract class BaseUniverseServer : ServerBase
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Used by tests to suppress Environment.Exit(0) 
        /// so that post-run operations are possible.
        /// </summary>
        public bool SuppressExit { get; set; }

        /// <summary>
        /// This will control a periodic log printout of the 
        /// current 'show stats' (if they are active) for this
        /// server.
        /// </summary>
        private int m_periodDiagnosticTimerMS = 60 * 60 * 1000;
        private Timer m_periodicDiagnosticsTimer = new Timer(60 * 60 * 1000);

        /// <summary>
        /// Random uuid for private data
        /// </summary>
        protected string m_osSecret = String.Empty;

        protected BaseHttpServer m_httpServer;

        public BaseHttpServer HttpServer
        {
            get { return m_httpServer; }
        }

        public BaseUniverseServer() : base()
        {
            // Random uuid for private data
            m_osSecret = UUID.Random().ToString();
        }

        private static bool m_NoVerifyCertChain = false;
        private static bool m_NoVerifyCertHostname = false;

        public static bool ValidateServerCertificate(object sender, X509Certificate certificate,
            X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (m_NoVerifyCertChain)
            {
                sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateChainErrors;
            }

            if (m_NoVerifyCertHostname)
            {
                sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateNameMismatch;
            }

            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Must be overriden by child classes for 
        /// their own server specific startup behaviour.
        /// </summary>
        protected virtual void StartupSpecific()
        {
            StatsManager.SimExtraStats = new SimExtraStatsCollector();
            RegisterCommonCommands();
            RegisterCommonComponents(Config);

            IConfig startupConfig = Config.Configs["Startup"];

            m_NoVerifyCertChain = startupConfig.GetBoolean("NoVerifyCertChain", m_NoVerifyCertChain);
            m_NoVerifyCertHostname = startupConfig.GetBoolean("NoVerifyCertHostname", m_NoVerifyCertHostname);
            ServicePointManager.ServerCertificateValidationCallback = ValidateServerCertificate;

            int logShowStatsSeconds = startupConfig.GetInt("LogShowStatsSeconds", m_periodDiagnosticTimerMS / 1000);
            m_periodDiagnosticTimerMS = logShowStatsSeconds * 1000;
            m_periodicDiagnosticsTimer.Elapsed += new ElapsedEventHandler(LogDiagnostics);

            if (m_periodDiagnosticTimerMS != 0)
            {
                m_periodicDiagnosticsTimer.Interval = m_periodDiagnosticTimerMS;
                m_periodicDiagnosticsTimer.Enabled = true;
            }
        }

        protected override void ShutdownSpecific()
        {
            Watchdog.Enabled = false;
            base.ShutdownSpecific();

            MainServer.Stop();

            Thread.Sleep(500);
            Util.StopThreadPool();
            WorkManager.Stop();

            RemovePIDFile();

            m_log.Info("[Virtual Universe Shut Down]: Shutdown processing on main thread complete.  Exiting...");

            if (!SuppressExit)
            {
                Environment.Exit(0);
            }
        }

        /// <summary>
        /// Provides a list of help topics that are available. 
        /// Overriding classes should append their topics to the
        /// information returned when the base method is called.
        /// </summary>
        /// <returns>
        /// A list of strings that represent different help 
        /// topics on which more information is available
        /// </returns>
        protected virtual List<string> GetHelpTopics() { return new List<string>(); }

        /// <summary>
        /// Print statistics to the logfile, if they are active
        /// </summary>
        protected void LogDiagnostics(object source, ElapsedEventArgs e)
        {
            StringBuilder sb = new StringBuilder("DIAGNOSTICS\n\n");
            sb.Append(GetUptimeReport());
            sb.Append(StatsManager.SimExtraStats.Report());
            sb.Append(Environment.NewLine);
            sb.Append(GetThreadsReport());

            m_log.Debug(sb);
        }

        /// <summary>
        /// Performs initialisation of the scene, 
        /// such as loading configuration from disk.
        /// </summary>
        public virtual void Startup()
        {
            m_log.Info("[Virtual Universe Startup]: Beginning startup processing");

            m_log.Info("[Virtual Universe Startup]: version: " + m_version + Environment.NewLine);

            m_log.InfoFormat(
                "[Virtual Universe Startup]: Operating system version: {0}, .NET platform {1}, {2}-bit\n",
                Environment.OSVersion, Environment.OSVersion.Platform, Util.Is64BitProcess() ? "64" : "32");

            try
            {
                StartupSpecific();
            }
            catch (Exception e)
            {
                m_log.Fatal("Fatal error: " + e.ToString());
                Environment.Exit(1);
            }

            TimeSpan timeTaken = DateTime.Now - m_startuptime;
        }

        public string osSecret
        {
            // Secret uuid for the simulator
            get { return m_osSecret; }
        }

        public string StatReport(IOSHttpRequest httpRequest)
        {
            // If we catch a request for "callback", wrap the response in the value for jsonp
            if (httpRequest.Query.ContainsKey("callback"))
            {
                return httpRequest.Query["callback"].ToString() + "(" + StatsManager.SimExtraStats.XReport((DateTime.Now - m_startuptime).ToString(), m_version) + ");";
            }
            else
            {
                return StatsManager.SimExtraStats.XReport((DateTime.Now - m_startuptime).ToString(), m_version);
            }
        }
    }
}