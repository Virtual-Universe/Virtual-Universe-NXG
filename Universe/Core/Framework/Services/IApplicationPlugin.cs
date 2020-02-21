﻿/// <license>
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
using System.Data;
using System.Linq;
using Nini.Config;
using Universe.Core.Framework.Modules;

namespace Universe.Core.Framework.Services
{
	/// <summary>
	/// Application Plugin Framework Interface
	/// 
	///		This is the framework interface for
	///		the application plugins that run the 
	///		server
	/// </summary>
	public interface IApplicationPlugin
	{
		/// <summary>
		/// Returns the name of the plugin
		/// </summary>
		string Name { get; }

		/// <summary>
		/// This is called before any other
		/// calls are made by the servers and
		/// the console is setup
		/// </summary>
		/// <param name="simBase"></param>
		void PreStartup(ISimulationBase simBase);

		/// <summary>
		/// Now we initialize the plugin
		/// </summary>
		/// <param name="simBase">The application instance</param>
		void Initialize(ISimulationBase simBase);

		/// <summary>
		/// We call this when the application
		/// has complted its initialization
		/// </summary>
		void PostIntialize();

		/// <summary>
		/// We call this when the application
		/// has completed loading
		/// </summary>
		void Start();

		/// <summary>
		/// This is also called when the 
		/// application has completed loading
		/// </summary>
		void PostStart();

		/// <summary>
		/// We call this to close out a module
		/// </summary>
		void Close();

		/// <summary>
		/// When the configuration of one of the
		/// servers has changed, we can be sure the
		/// server has the new updated information
		/// </summary>
		/// <param name="m_config"></param>
		void ReloadConfiguration(IConfigSource m_config);
	}
}