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
using Nini.Config;
using Universe.Framework.Servers.HttpServer;
using Universe.Server.Base;
using Universe.Server.Handlers.Base;
using Universe.Services.Interfaces;

namespace Universe.Server.Handlers.Authorization
{
    public class AuthorizationServerConnector : ServiceConnector
    {
        private IAuthorizationService m_AuthorizationService;
        private string m_ConfigName = "AuthorizationService";

        public AuthorizationServerConnector(IConfigSource config, IHttpServer server, string configName) : base(config, server, configName)
        {
            if (configName != String.Empty)
            {
                m_ConfigName = configName;
            }

            IConfig serverConfig = config.Configs[m_ConfigName];

            if (serverConfig == null)
            {
                throw new Exception(String.Format("No section '{0}' in config file", m_ConfigName));
            }

            string authorizationService = serverConfig.GetString("LocalServiceModule", String.Empty);

            if (authorizationService == String.Empty)
            {
                throw new Exception("No AuthorizationService in config file");
            }

            Object[] args = new Object[] { config };
            m_AuthorizationService = ServerUtils.LoadPlugin<IAuthorizationService>(authorizationService, args);

            server.AddStreamHandler(new AuthorizationServerPostHandler(m_AuthorizationService));
        }
    }
}