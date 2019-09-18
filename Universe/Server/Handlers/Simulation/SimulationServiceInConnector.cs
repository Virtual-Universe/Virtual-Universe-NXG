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
using Universe.Framework;
using Universe.Framework.Servers.HttpServer;
using Universe.Server.Base;
using Universe.Server.Handlers.Base;
using Universe.Services.Interfaces;

namespace Universe.Server.Handlers.Simulation
{
    public class SimulationServiceInConnector : ServiceConnector
    {
        private ISimulationService m_LocalSimulationService;

        public SimulationServiceInConnector(IConfigSource config, IHttpServer server, IScene scene) : base(config, server, String.Empty)
        {
            m_LocalSimulationService = scene.RequestModuleInterface<ISimulationService>();
            m_LocalSimulationService = m_LocalSimulationService.GetInnerService();

            // This one MUST be a stream handler because compressed fatpacks
            // are pure binary and shoehorning that into a string with UTF-8
            // encoding breaks it
            server.AddStreamHandler(new AgentPostHandler(m_LocalSimulationService));
            server.AddStreamHandler(new AgentPutHandler(m_LocalSimulationService));
            server.AddHTTPHandler("/agent/", new AgentHandler(m_LocalSimulationService).Handler);
            server.AddHTTPHandler("/object/", new ObjectHandler(m_LocalSimulationService).Handler);
        }
    }
}